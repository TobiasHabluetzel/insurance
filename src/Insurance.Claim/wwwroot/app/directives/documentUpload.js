/* <document-upload> — AngularJS 1.x port of the React DocumentUpload.
 *
 * Attributes:
 *   files      — array of native File objects (two-way, '='). The parent
 *                controller reads this array when submitting the claim.
 *   onChange   — expression called as onChange({files: <File[]>}) after
 *                each add/remove so the parent can hook behavior signals.
 *
 * PDF previews use pdfjs (loaded lazily from a CDN, once per page). For
 * images we just make a blob URL. For everything else we show a coloured
 * file icon based on extension. Blob URLs are revoked on remove and on
 * scope destroy — same lifecycle as the React version. */
(function () {
    'use strict';

    var pdfjsLoader = null;
    var PDFJS_VERSION = '3.11.174';

    function loadPdfjs() {
        if (pdfjsLoader) return pdfjsLoader;
        pdfjsLoader = new Promise(function (resolve, reject) {
            if (window.pdfjsLib) { resolve(window.pdfjsLib); return; }
            var s = document.createElement('script');
            s.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/' + PDFJS_VERSION + '/pdf.min.js';
            s.onload = function () {
                window.pdfjsLib.GlobalWorkerOptions.workerSrc =
                    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/' + PDFJS_VERSION + '/pdf.worker.min.js';
                resolve(window.pdfjsLib);
            };
            s.onerror = reject;
            document.head.appendChild(s);
        });
        return pdfjsLoader;
    }

    /** Best-effort thumbnail generator. Returns null (no preview) for
     * unknown types — the template falls back to a coloured icon. */
    function buildPreview(file) {
        if (file.type.indexOf('image/') === 0) {
            return Promise.resolve(URL.createObjectURL(file));
        }
        if (file.type === 'application/pdf') {
            return loadPdfjs().then(function (pdfjs) {
                return file.arrayBuffer().then(function (buf) {
                    return pdfjs.getDocument({ data: buf }).promise;
                }).then(function (pdf) {
                    return pdf.getPage(1);
                }).then(function (page) {
                    var viewport = page.getViewport({ scale: 0.5 });
                    var canvas = document.createElement('canvas');
                    canvas.width = viewport.width;
                    canvas.height = viewport.height;
                    return page.render({
                        canvasContext: canvas.getContext('2d'),
                        viewport: viewport,
                    }).promise.then(function () { return canvas.toDataURL('image/png'); });
                });
            }).catch(function () { return null; });
        }
        return Promise.resolve(null);
    }

    function extOf(name) {
        var idx = name.lastIndexOf('.');
        return idx === -1 ? '' : name.slice(idx + 1).toLowerCase();
    }

    function iconClasses(ext) {
        switch (ext) {
            case 'pdf': return 'bg-red-100 text-red-500';
            case 'doc': case 'docx': return 'bg-blue-100 text-blue-500';
            case 'xls': case 'xlsx': return 'bg-green-100 text-green-500';
            default: return 'bg-gray-200 text-gray-500';
        }
    }

    // $scope isn't injectable at the directive-factory level — only in the
    // link function below. This factory has no dependencies.
    angular.module('degoudseApp').directive('documentUpload', function () {
        return {
                restrict: 'E',
                scope: {
                    files: '=',
                    onChange: '&',
                },
                template:
                    '<div class="bg-white rounded-2xl px-4 py-4 shadow-sm cursor-pointer" ng-click="openPicker()">' +
                    '  <div class="flex items-center justify-between pointer-events-none">' +
                    '    <span class="text-gray-400 text-sm">Please upload related documents</span>' +
                    '    <span class="text-brand-blue">' +
                    '      <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                    '        <path d="M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242"/>' +
                    '        <path d="M12 12v9"/>' +
                    '        <polyline points="8 17 12 12 16 17"/>' +
                    '      </svg>' +
                    '    </span>' +
                    '  </div>' +
                    '  <div ng-if="entries.length" class="flex gap-3 mt-3 overflow-x-auto pb-1">' +
                    '    <div ng-repeat="entry in entries track by $index" class="flex flex-col items-center shrink-0">' +
                    '      <div class="w-16 h-16 rounded-xl overflow-hidden bg-gray-100 flex items-center justify-center">' +
                    '        <img ng-if="entry.previewUrl" ng-src="{{ entry.previewUrl }}" alt="{{ entry.file.name }}"' +
                    '             class="w-full h-full object-cover" />' +
                    '        <div ng-if="!entry.previewUrl" class="w-full h-full flex flex-col items-center justify-center gap-0.5"' +
                    '             ng-class="entry.iconCls">' +
                    '          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                    '            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
                    '            <polyline points="14 2 14 8 20 8"/>' +
                    '          </svg>' +
                    '          <span class="text-[9px] font-bold uppercase">{{ entry.ext }}</span>' +
                    '        </div>' +
                    '      </div>' +
                    '      <button type="button" ng-click="remove($index, $event)" aria-label="Remove file"' +
                    '              class="mt-1.5 w-6 h-6 rounded-full bg-white border border-gray-200 shadow-sm flex items-center justify-center text-gray-400 hover:text-red-400 transition-colors">' +
                    '        <svg width="10" height="2" viewBox="0 0 10 2" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
                    '          <line x1="0" y1="1" x2="10" y2="1"/>' +
                    '        </svg>' +
                    '      </button>' +
                    '    </div>' +
                    '  </div>' +
                    '  <input class="du-file hidden" type="file" multiple />' +
                    '</div>',
                link: function (scope, element) {
                    scope.entries = [];
                    if (!scope.files) scope.files = [];

                    var input = element[0].querySelector('.du-file');

                    scope.openPicker = function () { input.click(); };

                    input.addEventListener('change', function (e) {
                        var added = Array.prototype.slice.call(e.target.files || []);
                        input.value = '';
                        added.forEach(function (file) {
                            var ext = extOf(file.name);
                            var entry = {
                                file: file,
                                ext: ext,
                                iconCls: iconClasses(ext),
                                previewUrl: null,
                            };
                            scope.entries.push(entry);
                            buildPreview(file).then(function (url) {
                                if (url) scope.$apply(function () { entry.previewUrl = url; });
                            });
                        });
                        scope.$apply(function () {
                            scope.files = scope.entries.map(function (en) { return en.file; });
                            scope.onChange({ files: scope.files });
                        });
                    });

                    scope.remove = function (idx, ev) {
                        if (ev) ev.stopPropagation();
                        var entry = scope.entries[idx];
                        if (entry && entry.previewUrl && entry.previewUrl.indexOf('blob:') === 0) {
                            URL.revokeObjectURL(entry.previewUrl);
                        }
                        scope.entries.splice(idx, 1);
                        scope.files = scope.entries.map(function (en) { return en.file; });
                        scope.onChange({ files: scope.files });
                    };

                    scope.$on('$destroy', function () {
                        scope.entries.forEach(function (en) {
                            if (en.previewUrl && en.previewUrl.indexOf('blob:') === 0) {
                                URL.revokeObjectURL(en.previewUrl);
                            }
                        });
                    });
                },
            };
    });
})();
