/* ClaimEvidenceController — AngularJS 1.x port of React ClaimEvidencePage.
 *
 * Standalone evidence-upload page reached from the "action needed" email
 * link. Same SSE stream as ClaimSubmittedPage — we listen for evidence
 * requests + the incident description, then hand off to <evidence-section>.
 * When the user hits Done we drain the evidence-stage behavior log with
 * the prior submit timestamp so the email shows elapsed time. */
(function () {
    'use strict';

    angular.module('degoudseApp').controller('ClaimEvidenceController',
        ['$scope', '$window', 'behaviorSvc',
        function ($scope, $window, behaviorSvc) {

            var claimId = $window.__claimId;

            behaviorSvc.setStep('evidence');

            $scope.claimId = claimId;
            $scope.assessment = null;
            $scope.evidenceRequests = [];
            $scope.done = false;
            $scope.reference = null;

            var es = new EventSource('/api/claimwizard/claims/' + encodeURIComponent(claimId) + '/stream');
            es.onmessage = function (e) {
                try {
                    var payload = JSON.parse(e.data);
                    var data = payload.data;
                    var description = data && data.incident ? data.incident.description : undefined;
                    $scope.$apply(function () {
                        $scope.assessment = { description: description, raw: data };
                        $scope.evidenceRequests = (data && data.evidenceRequests) || [];
                        // Prefer AC's externalId (CLM-XXXXX) — fall back to a
                        // shortened UUID for legacy claims, matches React.
                        var externalId = data && typeof data.externalId === 'string' ? data.externalId : null;
                        $scope.reference = externalId
                            || (claimId ? claimId.slice(0, 4).toUpperCase() + '-' + claimId.slice(-4).toUpperCase() : null);
                    });
                } catch (err) { /* ignore parse errors */ }
            };
            es.onerror = function () { es.close(); };
            $scope.$on('$destroy', function () { es.close(); });

            $scope.onDone = function () {
                $scope.done = true;
                var events = behaviorSvc.drain();
                var submittedAt = behaviorSvc.readClaimSubmittedAt(claimId);
                behaviorSvc.reset();
                behaviorSvc.attachBehaviorToClaim(claimId, events, 'evidence', submittedAt);
            };
        }]);
})();
