/* ClaimSubmittedController — AngularJS 1.x port of React
 * ClaimSubmittedPage.
 *
 * - Opens a plain EventSource on /api/claimwizard/claims/{id}/stream.
 * - Faked step1/step2 timing (1.5s + 3.5s) matches the React version so
 *   the progress ring animates the same way.
 * - "Additions" form PUTs to /api/claimwizard/claims/{id} same as React.
 * - FlowWalker directive is instantiated below and calls onDone(), which
 *   drains the behavior log for the evidence stage (mirrors React).
 *
 * The category label (`It appears that this has been ...`) is looked up
 * from window.__claimFlows.
 */
(function () {
    'use strict';

    angular.module('degoudseApp').controller('ClaimSubmittedController',
        ['$scope', '$window', '$timeout', '$http', 'behaviorSvc',
        function ($scope, $window, $timeout, $http, behaviorSvc) {

            var claimId = $window.__claimId;

            $scope.step1 = 'pending';
            $scope.step2 = 'pending';
            $scope.step3 = 'pending';
            $scope.assessment = null;
            $scope.evidenceRequests = [];
            // Nested `form` so the textarea's ng-model survives the ng-if
            // child scope on the additions card. See the login controller
            // for the same fix.
            $scope.form = { editedDescription: '' };
            $scope.hasAdditions = null;
            $scope.saving = false;
            $scope.saved = false;
            $scope.flowDone = false;

            $timeout(function () { $scope.step1 = 'done'; }, 1500);
            $timeout(function () { $scope.step2 = 'done'; }, 3500);

            var es = new EventSource('/api/claimwizard/claims/' + encodeURIComponent(claimId) + '/stream');
            es.onmessage = function (e) {
                try {
                    var payload = JSON.parse(e.data);
                    var data = payload.data;
                    var description = data && data.incident ? data.incident.description : undefined;
                    var category = payload.category || 'other';
                    $scope.$apply(function () {
                        $scope.assessment = { description: description, category: category, raw: data };
                        $scope.evidenceRequests = (data && data.evidenceRequests) || [];
                        if (!$scope.form.editedDescription) {
                            $scope.form.editedDescription = description || '';
                        }
                        $scope.step3 = 'done';
                    });
                } catch (err) { /* ignore parse errors */ }
            };
            es.onerror = function () { es.close(); };
            $scope.$on('$destroy', function () { es.close(); });

            $scope.categoryLabel = function () {
                if (!$scope.assessment) return 'a claim';
                var flows = $window.__claimFlows || {};
                var flow = flows[$scope.assessment.category];
                return (flow && flow.label) || 'a claim';
            };

            $scope.allDone = function () {
                return $scope.step1 === 'done' && $scope.step2 === 'done' && $scope.step3 === 'done';
            };

            $scope.progressState = function () {
                var C = 251;
                if ($scope.allDone()) return { offset: 0, spin: false };
                var doneCount = [$scope.step1, $scope.step2, $scope.step3]
                    .filter(function (s) { return s === 'done'; }).length;
                return { offset: C - (doneCount / 3) * C * 0.75, spin: true };
            };

            $scope.pickAdditions = function (v) {
                $scope.hasAdditions = v;
                if (v === false) $scope.flowDone = false;
            };

            $scope.saveAdditions = function () {
                if (!$scope.assessment || $scope.saving || !$scope.form.editedDescription.trim()) return;
                $scope.saving = true;
                var d = $scope.assessment.raw || {};
                var body = {
                    claimDate: d.claimDate,
                    currency: d.currency,
                    externalId: d.externalId,
                    productCode: d.product,
                    claimant: d.claimant ? {
                        firstName: d.claimant.firstName,
                        lastName: d.claimant.lastName,
                        email: d.claimant.email,
                        phoneNumber: d.claimant.phoneNumber,
                        dateOfBirth: d.claimant.dateOfBirth,
                        originCountryIso2Code: d.claimant.originCountryIso2Code,
                        address: d.claimant.address,
                        bankAccount: d.claimant.bankAccount,
                    } : undefined,
                    incident: {
                        incidentDate: d.incident && d.incident.incidentDate,
                        incidentType: d.incident && d.incident.incidentType,
                        location: d.incident && d.incident.location,
                        description: $scope.form.editedDescription,
                    },
                    policyCoverage: d.policyCoverage ? {
                        reference: d.policyCoverage.reference,
                        periodFrom: d.policyCoverage.periodFrom,
                        periodTo: d.policyCoverage.periodTo,
                        currency: d.policyCoverage.currency,
                        coverages: d.policyCoverage.coverages,
                    } : undefined,
                };
                $http.put('/api/claimwizard/claims/' + encodeURIComponent(claimId), body)
                    .then(function () {
                        $scope.saved = true;
                        if ($scope.assessment) $scope.assessment.description = $scope.form.editedDescription;
                    })
                    .finally(function () { $scope.saving = false; });
            };

            $scope.onFlowDone = function () {
                $scope.flowDone = true;
                var events = behaviorSvc.drain();
                var submittedAt = behaviorSvc.readClaimSubmittedAt(claimId);
                behaviorSvc.reset();
                behaviorSvc.attachBehaviorToClaim(claimId, events, 'evidence', submittedAt);
            };

            // ------- ReviewCard helpers ---------------------------------

            function readMoney(input) {
                if (!input) return null;
                var raw = input.amount != null ? input.amount : input.value;
                var currency = input.currency;
                if (currency == null) return null;
                if (typeof raw === 'number' && isFinite(raw)) return { amount: raw, currency: currency };
                if (typeof raw === 'string' && raw.trim() !== '') {
                    var parsed = Number(raw);
                    if (isFinite(parsed)) return { amount: parsed, currency: currency };
                }
                return null;
            }

            $scope.review = function () {
                if (!$scope.assessment || !$scope.assessment.raw) return null;
                var raw = $scope.assessment.raw;
                var review = raw.review || {};
                var scenario = review.scenario || {};
                var reviewCosts = Array.isArray(review.costs) ? review.costs : [];
                var documents = Array.isArray(raw.documents) ? raw.documents : [];
                var incidentCosts = Array.isArray(raw.costs) ? raw.costs : [];

                var docTotals = documents
                    .map(function (d) { return readMoney(d && d.analysis && d.analysis.financial && d.analysis.financial.total); })
                    .filter(function (m) { return m !== null; });
                var docCurrency = docTotals[0] && docTotals[0].currency;
                var allSameCurrency = docTotals.every(function (t) { return t.currency === docCurrency; });
                var docSum = allSameCurrency
                    ? docTotals.reduce(function (s, t) { return s + t.amount; }, 0)
                    : null;

                var externalId = typeof raw.externalId === 'string' ? raw.externalId : undefined;
                var claimRef = raw.id;
                var reference = externalId
                    || (claimRef ? claimRef.slice(0, 4).toUpperCase() + '-' + claimRef.slice(-4).toUpperCase() : null);

                return {
                    documents: documents,
                    incidentCosts: incidentCosts.map(function (c) {
                        var m = readMoney(c && c.amount) || readMoney(c && c.totalAmount) || readMoney(c);
                        return {
                            description: (c && c.description) || 'Cost',
                            date: c && c.date ? new Date(c.date).toLocaleDateString() : null,
                            money: m ? formatMoney(m.amount, m.currency) : null,
                        };
                    }),
                    reviewCosts: reviewCosts.map(function (c) {
                        var status = (c && c.status) || '';
                        var palette = status === 'covered'
                            ? { icon: 'text-emerald-500', label: 'Covered', labelCls: 'text-emerald-600' }
                            : status === 'partiallyCovered'
                                ? { icon: 'text-amber-500', label: 'Partially covered', labelCls: 'text-amber-600' }
                                : { icon: 'text-rose-400', label: 'Not covered', labelCls: 'text-rose-500' };
                        return {
                            classification: (c && c.classification) || 'Cost',
                            argumentation: c && c.argumentation,
                            status: status,
                            palette: palette,
                        };
                    }),
                    docTotalText: (docSum !== null && docCurrency)
                        ? 'Total read across documents: ' + formatMoney(docSum, docCurrency)
                        : null,
                    reference: reference,
                    hasAnyExtras: reviewCosts.length > 0 || documents.length > 0 || incidentCosts.length > 0,
                };
            };

            function formatMoney(amount, currency) {
                try {
                    return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency }).format(amount);
                } catch (e) {
                    return currency + ' ' + amount.toFixed(2);
                }
            }
        }]);
})();
