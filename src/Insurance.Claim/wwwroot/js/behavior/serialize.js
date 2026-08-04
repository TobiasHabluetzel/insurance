/* Serialization + attach helper — plain JS port of
 * (legacy)/behavior/serialize.ts. Uses fetch() so no jQuery /
 * $http dependency; the collector must stay usable outside AngularJS if
 * we ever move it back to a framework-agnostic host. */
(function (root) {
    'use strict';

    var b = root.__behavior;
    if (!b) throw new Error('behavior/types.js must load before serialize.js');

    b.serializeBehavior = function (events) {
        return JSON.stringify(events, null, 2);
    };

    /** stage: 'submit' | 'evidence' — names the post point. */
    b.attachBehaviorToClaim = function (acClaimId, events, stage, priorSubmittedAt) {
        stage = stage || 'submit';
        if ((!events || !events.length) && !priorSubmittedAt) return Promise.resolve();
        return fetch('/api/claimwizard/behavior', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                acClaimId: acClaimId,
                text: b.serializeBehavior(events || []),
                stage: stage,
                priorSubmittedAt: priorSubmittedAt || null,
            }),
        }).catch(function () { /* silent — never block the user on telemetry */ });
    };
})(window);
