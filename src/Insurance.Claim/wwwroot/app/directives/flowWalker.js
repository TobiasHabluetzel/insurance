/* <flow-walker> — AngularJS 1.x port of the React FlowWalker.
 *
 * Attributes:
 *   category           — the AC-classified category (accident, medical…)
 *   claim-id           — the AC claim id (for file upload URL + report)
 *   evidence-requests  — array of evidence requests from the SSE stream
 *   on-done            — expression called when the whole flow (questions
 *                        + evidence uploads) finishes.
 *
 * Walks the CATEGORY_FLOWS tree, reveals one card at a time, handles
 * yesno/text/file nodes. When the flow's end node is reached, uploads any
 * pending files then hands off to the <evidence-section> directive. */
(function () {
    'use strict';

    angular.module('degoudseApp').directive('flowWalker', function () {
        return {
            restrict: 'E',
            scope: {
                category: '<',
                claimId: '<',
                evidenceRequests: '<',
                onDone: '&',
            },
            template:
                '<div class="px-4 py-6 space-y-3">' +
                '  <p ng-if="revealed.length" class="text-gray-500 text-sm mb-4">We have a few additional questions about the incident:</p>' +
                '  <question-card ng-repeat="r in revealed track by r.node.id"' +
                '                 node="r.node" answer="r.answer"' +
                '                 on-yes="advance(r.node.id, $index, \'yes\')"' +
                '                 on-no="advance(r.node.id, $index, \'no\')"' +
                '                 on-text-submit="advanceWithText(r.node.id, $index)"' +
                '                 on-text-change="setText(r.node.id, value)"' +
                '                 text-value="textValues[r.node.id]"' +
                '                 on-files-change="setFiles(r.node.id, files)"' +
                '                 on-file-advance="advance(r.node.id, $index, hasFiles ? \'uploaded\' : \'skipped\')"' +
                '                 on-skip="advance(r.node.id, $index, \'skipped\')">' +
                '  </question-card>' +
                '  <button ng-if="complete && !showEvidence" type="button" ng-click="continue_()" ng-disabled="uploading"' +
                '          class="mt-2 bg-brand-purple text-white rounded-full px-8 py-4 font-semibold flex items-center gap-3 disabled:opacity-60">' +
                '    <span ng-if="!uploading">Continue</span>' +
                '    <svg ng-if="!uploading" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">' +
                '      <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>' +
                '    </svg>' +
                '    <span ng-if="uploading">Uploading…</span>' +
                '  </button>' +
                '  <evidence-section ng-if="showEvidence" requests="evidenceRequests"' +
                '                    claim-id="claimId" on-done="onDone()"></evidence-section>' +
                '</div>',
            link: function (scope) {
                var flows = window.__claimFlows || {};

                function currentFlow() {
                    return flows[scope.category] || flows.other;
                }

                scope.revealed = [];
                scope.textValues = {};
                scope.pendingFiles = {};
                scope.complete = false;
                scope.uploading = false;
                scope.showEvidence = false;

                function init() {
                    var flow = currentFlow();
                    var firstNode = flow.nodes[flow.start];
                    var skip = !firstNode || firstNode.type === 'end';
                    scope.revealed = skip ? [] : [{ node: firstNode, answer: null }];
                    scope.complete = skip;
                    scope.showEvidence = skip;
                }

                scope.$watch('category', function (v) { if (v) init(); });

                scope.setText = function (nodeId, value) {
                    scope.textValues[nodeId] = value;
                };

                scope.setFiles = function (nodeId, files) {
                    scope.pendingFiles[nodeId] = files;
                };

                function commit(index, answer) {
                    scope.revealed[index].answer = answer;
                }

                scope.advance = function (nodeId, index, answer) {
                    commit(index, answer);
                    var flow = currentFlow();
                    // 'uploaded' / 'skipped' both route via 'done'.
                    var routing = (answer === 'uploaded' || answer === 'skipped') ? 'done' : answer;
                    var nextId = flow.nodes[nodeId] && flow.nodes[nodeId].next
                        ? flow.nodes[nodeId].next[routing]
                        : null;
                    if (!nextId || nextId === 'end') { scope.complete = true; return; }
                    var nextNode = flow.nodes[nextId];
                    if (!nextNode || nextNode.type === 'end') { scope.complete = true; return; }
                    scope.revealed.push({ node: nextNode, answer: null });
                };

                scope.advanceWithText = function (nodeId, index) {
                    if (!(scope.textValues[nodeId] || '').trim()) return;
                    scope.advance(nodeId, index, 'done');
                };

                scope.continue_ = function () {
                    scope.uploading = true;
                    var uploads = [];
                    Object.keys(scope.pendingFiles).forEach(function (nodeId) {
                        (scope.pendingFiles[nodeId] || []).forEach(function (file) {
                            var form = new FormData();
                            form.append('file', file, file.name);
                            uploads.push(fetch(
                                '/api/claimwizard/claims/' + encodeURIComponent(scope.claimId) + '/documents',
                                { method: 'POST', body: form }
                            ));
                        });
                    });
                    Promise.all(uploads).finally(function () {
                        scope.$apply(function () {
                            scope.uploading = false;
                            scope.showEvidence = true;
                        });
                    });
                };
            },
        };
    });
})();
