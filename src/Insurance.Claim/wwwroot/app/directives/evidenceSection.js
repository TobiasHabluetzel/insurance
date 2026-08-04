/* <evidence-section> — AngularJS 1.x port of React EvidenceSection.
 *
 * Shows the residual evidence requests the AC evaluator still wants after
 * the initial submit. Each card supports upload / report-missing. Fires
 * add/remove signals per card so the analyst can see load-then-drop
 * patterns (matches the React implementation exactly).
 *
 * Attributes:
 *   requests   — evidence request array from the SSE stream
 *   claim-id   — AC claim id for upload URL
 *   on-done    — parent handler; fires the evidence-stage drain
 */
(function () {
    'use strict';

    angular.module('degoudseApp').directive('evidenceSection',
        ['behaviorSvc', function (behaviorSvc) {
            return {
                restrict: 'E',
                scope: {
                    requests: '<',
                    claimId: '<',
                    onDone: '&',
                },
                template:
                    '<div>' +
                    // Empty state — everything already provided.
                    '  <div ng-if="!pending.length" class="space-y-4 pt-2">' +
                    '    <div class="bg-white rounded-2xl px-6 py-8 text-center space-y-3">' +
                    '      <div class="w-14 h-14 mx-auto rounded-full bg-brand-blue/10 flex items-center justify-center">' +
                    '        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" class="text-brand-blue">' +
                    '          <polyline points="5 12 10 17 19 7" />' +
                    '        </svg>' +
                    '      </div>' +
                    '      <p class="text-gray-700 font-semibold text-base">All good for now…</p>' +
                    '      <p class="text-gray-500 text-sm">We have everything we need. We\'ll reach out if anything else comes up.</p>' +
                    '    </div>' +
                    '    <button type="button" ng-click="onDone()" class="w-full bg-brand-purple text-white rounded-full py-4 font-semibold text-sm">Done</button>' +
                    '  </div>' +

                    // Full state — one card per outstanding request.
                    '  <div ng-if="pending.length" class="space-y-3 pt-2">' +
                    '    <p class="text-gray-500 text-sm font-medium">Still needed from you:</p>' +
                    '    <div ng-repeat="req in pending track by $index" class="bg-white rounded-2xl px-4 py-3 shadow-sm space-y-2">' +
                    '      <div class="flex items-center justify-between gap-4">' +
                    '        <div class="flex-1">' +
                    '          <p class="text-gray-700 text-sm font-medium capitalize">{{ req.request }}</p>' +
                    '          <p class="text-gray-400 text-xs mt-0.5">{{ req.description }}</p>' +
                    '        </div>' +
                    '        <p ng-if="unavailable[$index]" class="text-xs text-gray-400 italic shrink-0">Not available</p>' +
                    '        <p ng-if="!unavailable[$index] && uploading[$index]" class="text-xs text-gray-400 shrink-0">Uploading…</p>' +
                    '        <button ng-if="!unavailable[$index] && !uploading[$index]" type="button" ng-click="pick($index)" class="text-brand-blue shrink-0" aria-label="Upload file">' +
                    '          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                    '            <path d="M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242"/><path d="M12 12v9"/><polyline points="8 17 12 12 16 17"/>' +
                    '          </svg>' +
                    '        </button>' +
                    '      </div>' +
                    '      <div ng-if="entries[$index].length" class="flex gap-3 overflow-x-auto pb-1">' +
                    '        <div ng-repeat="entry in entries[$parent.$index] track by $index" class="flex flex-col items-center shrink-0">' +
                    '          <div class="w-16 h-16 rounded-xl overflow-hidden bg-gray-100 flex items-center justify-center">' +
                    '            <img ng-if="entry.previewUrl" ng-src="{{ entry.previewUrl }}" alt="{{ entry.file.name }}" class="w-full h-full object-cover" />' +
                    '            <div ng-if="!entry.previewUrl" class="w-full h-full flex flex-col items-center justify-center gap-0.5" ng-class="entry.iconCls">' +
                    '              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                    '                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>' +
                    '              </svg>' +
                    '              <span class="text-[9px] font-bold uppercase">{{ entry.ext }}</span>' +
                    '            </div>' +
                    '          </div>' +
                    '          <button type="button" ng-click="remove($parent.$index, $index)" class="mt-1.5 w-6 h-6 rounded-full bg-white border border-gray-200 shadow-sm flex items-center justify-center text-gray-400 hover:text-red-400 transition-colors">' +
                    '            <svg width="10" height="2" viewBox="0 0 10 2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="0" y1="1" x2="10" y2="1"/></svg>' +
                    '          </button>' +
                    '        </div>' +
                    '      </div>' +
                    '      <button ng-if="!unavailable[$index] && !uploading[$index]" type="button" ng-disabled="reportingMissing[$index]"' +
                    '              ng-click="reportMissing($index)" class="text-xs text-gray-400 underline disabled:opacity-40">' +
                    '        {{ reportingMissing[$index] ? \'Reporting…\' : \'Not available\' }}' +
                    '      </button>' +
                    '      <input class="es-file hidden" type="file" multiple data-idx="{{ $index }}" />' +
                    '    </div>' +
                    '    <button type="button" ng-click="onDone()" class="w-full bg-brand-purple text-white rounded-full py-4 font-semibold text-sm">Done</button>' +
                    '  </div>' +
                    '</div>',
                link: function (scope, element) {
                    var fp = window.__filePreview;

                    function filterPending() {
                        var reqs = scope.requests || [];
                        scope.pending = reqs.filter(function (req) {
                            if (req.request === 'insurance policy') return false;
                            if (req.isMissing === true) return false;
                            if (Array.isArray(req.evidence) && req.evidence.length > 0) return false;
                            return true;
                        });
                    }

                    scope.entries = {};
                    scope.uploading = {};
                    scope.unavailable = {};
                    scope.reportingMissing = {};
                    filterPending();
                    scope.$watch('requests', filterPending);

                    scope.pick = function (idx) {
                        var inputs = element[0].querySelectorAll('.es-file');
                        for (var i = 0; i < inputs.length; i++) {
                            if (String(idx) === inputs[i].getAttribute('data-idx')) { inputs[i].click(); break; }
                        }
                    };

                    element[0].addEventListener('change', function (e) {
                        if (!e.target.classList || !e.target.classList.contains('es-file')) return;
                        var idx = Number(e.target.getAttribute('data-idx'));
                        var files = Array.prototype.slice.call(e.target.files || []);
                        e.target.value = '';
                        if (!files.length) return;

                        behaviorSvc.fieldChange('evidence-doc:' + idx, String(files.length), 'add');

                        if (!scope.entries[idx]) scope.entries[idx] = [];
                        files.forEach(function (file) {
                            var ext = fp.extOf(file.name);
                            var entry = { file: file, ext: ext, iconCls: fp.iconClasses(ext), previewUrl: null };
                            scope.entries[idx].push(entry);
                            fp.build(file).then(function (url) {
                                if (url) scope.$apply(function () { entry.previewUrl = url; });
                            });
                        });

                        scope.$apply(function () { scope.uploading[idx] = true; });
                        var uploads = files.map(function (file) {
                            var form = new FormData();
                            form.append('file', file, file.name);
                            return fetch(
                                '/api/claimwizard/claims/' + encodeURIComponent(scope.claimId) + '/documents',
                                { method: 'POST', body: form }
                            );
                        });
                        Promise.all(uploads).finally(function () {
                            scope.$apply(function () { scope.uploading[idx] = false; });
                        });
                    });

                    scope.remove = function (cardIdx, entryIdx) {
                        behaviorSvc.fieldChange('evidence-doc:' + cardIdx, '1', 'remove');
                        var arr = scope.entries[cardIdx];
                        if (arr && arr[entryIdx]) {
                            fp.revoke(arr[entryIdx].previewUrl);
                            arr.splice(entryIdx, 1);
                        }
                    };

                    scope.reportMissing = function (idx) {
                        scope.reportingMissing[idx] = true;
                        var req = scope.pending[idx];
                        fetch(
                            '/api/claimwizard/claims/' + encodeURIComponent(scope.claimId) +
                                '/evidence-requests/' + encodeURIComponent(req.id) + '/report-missing-evidence',
                            { method: 'POST' }
                        ).finally(function () {
                            scope.$apply(function () {
                                scope.unavailable[idx] = true;
                                scope.reportingMissing[idx] = false;
                            });
                        });
                    };

                    scope.$on('$destroy', function () {
                        Object.keys(scope.entries).forEach(function (k) {
                            (scope.entries[k] || []).forEach(function (e) { fp.revoke(e.previewUrl); });
                        });
                    });
                },
            };
        }]);
})();
