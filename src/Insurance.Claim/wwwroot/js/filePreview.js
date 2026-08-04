/* File preview helpers — shared between documentUpload, flowWalker and
 * evidenceSection directives. Builds thumbnails (image blob URL / PDF
 * first-page render / null fallback) and provides an extension icon
 * class map. Plain JS port of src/DeGoudse.Web/src/utils/filePreview.ts. */
(function (root) {
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

    root.__filePreview = {
        extOf: function (name) {
            var idx = name.lastIndexOf('.');
            return idx === -1 ? '' : name.slice(idx + 1).toLowerCase();
        },

        iconClasses: function (ext) {
            switch (ext) {
                case 'pdf': return 'bg-red-100 text-red-500';
                case 'doc': case 'docx': return 'bg-blue-100 text-blue-500';
                case 'xls': case 'xlsx': return 'bg-green-100 text-green-500';
                default: return 'bg-gray-200 text-gray-500';
            }
        },

        /** Returns a Promise<string|null> with a blob:/data: URL for the
         *  thumbnail — null means the caller should render a fallback icon. */
        build: function (file) {
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
        },

        revoke: function (previewUrl) {
            if (previewUrl && previewUrl.indexOf('blob:') === 0) {
                URL.revokeObjectURL(previewUrl);
            }
        },
    };
})(window);
