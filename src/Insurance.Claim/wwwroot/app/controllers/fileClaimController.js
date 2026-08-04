/* FileClaimController — AngularJS 1.x port of React FileClaimPage.
 *
 * State + submit flow mirror the React version. Behavior signals fire in
 * the same shape (trackPick for pickers, add/remove/pick modes on
 * documents, drain + attachBehaviorToClaim on successful submit).
 *
 * behaviorSvc is a thin AngularJS wrapper around window.__behavior — the
 * real collector runs framework-agnostically so it stays reusable if the
 * host framework ever changes again. */
(function () {
    'use strict';

    angular.module('degoudseApp').controller('FileClaimController',
        ['$scope', '$window', 'policySvc', 'behaviorSvc',
        function ($scope, $window, policySvc, behaviorSvc) {

            behaviorSvc.setStep('file-claim');

            $scope.policy = $window.__initialPolicy || null;
            $scope.selectedInsured = null;
            $scope.selectedInsuredUid = null;
            $scope.incidentDate = null;
            $scope.address = null;
            $scope.documents = [];
            $scope.submitting = false;
            $scope.error = null;

            $scope.googleMapsApiKey = $window.__googleMapsApiKey || '';

            if ($scope.policy && $scope.policy.periodStart) {
                $scope.periodStart = new Date($scope.policy.periodStart);
            }
            if ($scope.policy && $scope.policy.periodEnd) {
                var end = new Date($scope.policy.periodEnd);
                var today = new Date();
                $scope.maxDate = end < today ? end : today;
            }

            // trackPick equivalent from the React version: focus → change(pick)
            // → blur, all synthetic. Non-text controls (dropdown, date, address)
            // never actually gain browser focus, so we synthesize the pair so
            // the analyst can still see "insured field was picked at ts=X".
            function trackPick(fieldId, value) {
                behaviorSvc.fieldFocus(fieldId);
                behaviorSvc.fieldChange(fieldId, value || '', 'pick');
                behaviorSvc.fieldBlur(fieldId, (value || '').length);
            }

            $scope.handleInsuredChange = function (insured) {
                $scope.selectedInsured = insured;
                $scope.selectedInsuredUid = insured ? insured.uid : null;
                trackPick('insured', insured ? insured.uid : '');
            };

            $scope.handleDateChange = function (date) {
                $scope.incidentDate = date;
                trackPick('incident-date', date ? date.toISOString().slice(0, 10) : '');
            };

            $scope.handleAddressChange = function (address) {
                $scope.address = address;
                // Only country code — line1/city would leak PII.
                trackPick('incident-address', (address && address.countryCode) || '');
            };

            var prevDocCount = 0;
            $scope.handleDocumentsChange = function (files) {
                $scope.documents = files;
                var after = files.length;
                var mode = after > prevDocCount ? 'add' : (after < prevDocCount ? 'remove' : 'pick');
                behaviorSvc.fieldChange('documents', String(after), mode);
                prevDocCount = after;
            };

            $scope.canContinue = function () {
                return !!$scope.selectedInsured
                    && !!$scope.incidentDate
                    && !!$scope.address
                    && !$scope.submitting;
            };

            $scope.submit = function () {
                if (!$scope.canContinue()) return;
                $scope.submitting = true;
                $scope.error = null;
                policySvc.submitClaim({
                    policy: $scope.policy,
                    insured: $scope.selectedInsured,
                    incidentDate: $scope.incidentDate,
                    incidentAddress: $scope.address,
                    documents: $scope.documents,
                }).then(function (result) {
                    // Drain + reset + stash timestamp, then attach the log to
                    // the AC claim. Best-effort — never blocks the redirect.
                    var events = behaviorSvc.drain();
                    behaviorSvc.reset();
                    behaviorSvc.recordClaimSubmittedAt(result.id);
                    behaviorSvc.attachBehaviorToClaim(result.id, events, 'submit');
                    $window.location.href = '/claim-submitted/' + result.id;
                }).catch(function () {
                    $scope.error = 'Failed to submit claim. Please try again.';
                }).finally(function () {
                    $scope.submitting = false;
                });
            };
        }]);
})();
