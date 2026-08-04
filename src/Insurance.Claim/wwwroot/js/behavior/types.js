/* Behavior collector wire-format helpers — plain JS port of
 * (legacy)/behavior/types.ts. Kept as a global namespace
 * (window.__behavior) rather than an ES module so the file can be loaded
 * via a plain <script> tag alongside jQuery/AngularJS without a bundler.
 *
 * SCHEMA_VERSION bumps must stay in lockstep with the React version — the
 * backend expects a single wire format, so keep both trees producing the
 * same envelope.
 */
(function (root) {
    'use strict';

    var api = root.__behavior || (root.__behavior = {});

    api.SCHEMA_VERSION = '1.0.0';

    /** Bucket a length so we never ship the raw char count of a PII field. */
    api.lengthBucket = function (n) {
        if (n <= 0) return '0';
        if (n <= 5) return '1-5';
        if (n <= 20) return '6-20';
        if (n <= 100) return '21-100';
        return '100+';
    };

    /** Coarse edit-magnitude bucket from |new - prev| length delta. */
    api.editDistanceBucket = function (delta) {
        var a = Math.abs(delta);
        if (a === 0) return '0';
        if (a <= 2) return 'micro';
        if (a <= 10) return 'small';
        if (a <= 50) return 'medium';
        return 'large';
    };
})(window);
