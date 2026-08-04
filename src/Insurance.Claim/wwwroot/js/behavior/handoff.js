/* Handoff between the submit-stage and evidence-stage drains — plain JS
 * port of (legacy)/behavior/handoff.ts. Same localStorage key
 * ('beh_claim_registered') and same shape { claimId, at }. */
(function (root) {
    'use strict';

    var b = root.__behavior;
    if (!b) throw new Error('behavior/types.js must load before handoff.js');

    var KEY = 'beh_claim_registered';

    b.recordClaimSubmittedAt = function (claimId) {
        try {
            localStorage.setItem(KEY, JSON.stringify({ claimId: claimId, at: Date.now() }));
        } catch (e) { /* private mode / quota — best effort */ }
    };

    b.readClaimSubmittedAt = function (claimId) {
        try {
            var raw = localStorage.getItem(KEY);
            if (!raw) return null;
            var v = JSON.parse(raw);
            return v.claimId === claimId ? v.at : null;
        } catch (e) { return null; }
    };
})(window);
