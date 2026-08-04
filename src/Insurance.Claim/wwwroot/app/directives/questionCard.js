/* <question-card> — inner card used by <flow-walker>. Renders a single
 * yesno / text / file question. All state lives on the parent directive
 * (revealed list, text values, pending files); this card only wires the
 * user interactions back up. */
(function () {
    'use strict';

    angular.module('degoudseApp').directive('questionCard', function () {
        return {
            restrict: 'E',
            scope: {
                node: '<',
                answer: '<',
                textValue: '<',
                onYes: '&',
                onNo: '&',
                onTextSubmit: '&',
                onTextChange: '&',
                onFilesChange: '&',
                onFileAdvance: '&',
                onSkip: '&',
            },
            template:
                '<div class="bg-white rounded-2xl px-4 py-4 shadow-sm transition-opacity" ng-class="{ \'opacity-60\': answered() }">' +

                // ---- yesno ----
                '  <div ng-if="node.type === \'yesno\'" class="flex items-center justify-between gap-4">' +
                '    <span class="text-gray-700 text-sm font-medium flex-1">{{ node.question }}</span>' +
                '    <div class="flex gap-4 shrink-0">' +
                '      <button type="button" ng-click="!answered() && onYes()" ng-disabled="answered()" class="flex items-center gap-1.5 text-sm text-gray-600 disabled:cursor-default">' +
                '        <div class="w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors" ng-class="answer === \'yes\' ? \'border-brand-blue\' : \'border-gray-300\'">' +
                '          <div ng-if="answer === \'yes\'" class="w-2.5 h-2.5 rounded-full bg-brand-blue"></div>' +
                '        </div>Yes</button>' +
                '      <button type="button" ng-click="!answered() && onNo()" ng-disabled="answered()" class="flex items-center gap-1.5 text-sm text-gray-600 disabled:cursor-default">' +
                '        <div class="w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors" ng-class="answer === \'no\' ? \'border-brand-blue\' : \'border-gray-300\'">' +
                '          <div ng-if="answer === \'no\'" class="w-2.5 h-2.5 rounded-full bg-brand-blue"></div>' +
                '        </div>No</button>' +
                '    </div>' +
                '  </div>' +

                // ---- text ----
                '  <div ng-if="node.type === \'text\'" class="space-y-3">' +
                '    <p class="text-gray-700 text-sm font-medium">{{ node.question }}</p>' +
                '    <textarea ng-if="!answered()" ng-model="localText" ng-change="onTextChange({value: localText})" rows="3"' +
                '              class="w-full text-sm text-gray-700 rounded-xl border border-gray-200 px-3 py-2.5 outline-none focus:border-brand-purple resize-none"></textarea>' +
                '    <button ng-if="!answered()" type="button" ng-click="onTextSubmit()" ng-disabled="!(localText && localText.trim())"' +
                '            class="w-full bg-brand-purple text-white rounded-full py-2.5 text-sm font-semibold disabled:opacity-40">Continue</button>' +
                '    <p ng-if="answered()" class="text-sm text-gray-400 italic">{{ localText }}</p>' +
                '  </div>' +

                // ---- file ----
                '  <div ng-if="node.type === \'file\'" class="space-y-2">' +
                '    <div class="flex items-center justify-between gap-4">' +
                '      <span class="text-gray-700 text-sm font-medium flex-1">{{ node.question }}</span>' +
                '      <button ng-if="!answered()" type="button" ng-click="openPicker()" class="text-brand-blue shrink-0" aria-label="Upload file">' +
                '        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                '          <path d="M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242"/>' +
                '          <path d="M12 12v9"/><polyline points="8 17 12 12 16 17"/>' +
                '        </svg>' +
                '      </button>' +
                '      <p ng-if="answered() && answer === \'uploaded\'" class="text-sm text-brand-blue font-medium shrink-0">Uploaded</p>' +
                '      <p ng-if="answered() && answer === \'skipped\'" class="text-sm text-gray-400 italic shrink-0">Skipped</p>' +
                '    </div>' +
                '    <div ng-if="!answered() && entries.length" class="flex gap-3 overflow-x-auto pb-1">' +
                '      <div ng-repeat="entry in entries track by $index" class="flex flex-col items-center shrink-0">' +
                '        <div class="w-16 h-16 rounded-xl overflow-hidden bg-gray-100 flex items-center justify-center">' +
                '          <img ng-if="entry.previewUrl" ng-src="{{ entry.previewUrl }}" alt="{{ entry.file.name }}" class="w-full h-full object-cover" />' +
                '          <div ng-if="!entry.previewUrl" class="w-full h-full flex flex-col items-center justify-center gap-0.5" ng-class="entry.iconCls">' +
                '            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                '              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>' +
                '            </svg>' +
                '            <span class="text-[9px] font-bold uppercase">{{ entry.ext }}</span>' +
                '          </div>' +
                '        </div>' +
                '        <button type="button" ng-click="remove($index)" class="mt-1.5 w-6 h-6 rounded-full bg-white border border-gray-200 shadow-sm flex items-center justify-center text-gray-400 hover:text-red-400 transition-colors">' +
                '          <svg width="10" height="2" viewBox="0 0 10 2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="0" y1="1" x2="10" y2="1"/></svg>' +
                '        </button>' +
                '      </div>' +
                '    </div>' +
                '    <div ng-if="!answered()" class="flex gap-3">' +
                '      <button ng-if="entries.length" type="button" ng-click="onFileAdvance({hasFiles: true})" class="text-xs text-brand-purple font-semibold underline">Done</button>' +
                '      <button type="button" ng-click="onSkip()" class="text-xs text-gray-400 underline">Skip</button>' +
                '    </div>' +
                '    <input class="qc-file hidden" type="file" multiple />' +
                '  </div>' +
                '</div>',
            link: function (scope, element) {
                var fp = window.__filePreview;
                scope.entries = [];
                scope.localText = scope.textValue || '';
                scope.answered = function () { return scope.answer !== null && scope.answer !== undefined; };

                var input = element[0].querySelector('.qc-file');
                scope.openPicker = function () { if (input) input.click(); };

                if (input) {
                    input.addEventListener('change', function (e) {
                        var files = Array.prototype.slice.call(e.target.files || []);
                        input.value = '';
                        files.forEach(function (file) {
                            var ext = fp.extOf(file.name);
                            var entry = { file: file, ext: ext, iconCls: fp.iconClasses(ext), previewUrl: null };
                            scope.entries.push(entry);
                            fp.build(file).then(function (url) {
                                if (url) scope.$apply(function () { entry.previewUrl = url; });
                            });
                        });
                        scope.$apply(function () {
                            scope.onFilesChange({ files: scope.entries.map(function (en) { return en.file; }) });
                        });
                    });
                }

                scope.remove = function (idx) {
                    var entry = scope.entries[idx];
                    if (entry) fp.revoke(entry.previewUrl);
                    scope.entries.splice(idx, 1);
                    scope.onFilesChange({ files: scope.entries.map(function (en) { return en.file; }) });
                };

                scope.$on('$destroy', function () {
                    scope.entries.forEach(function (en) { fp.revoke(en.previewUrl); });
                });
            },
        };
    });
})();
