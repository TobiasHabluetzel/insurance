/* behaviorSvc — thin AngularJS wrapper around window.__behavior so
 * controllers can inject it rather than reach through the window global.
 * All the real logic lives in wwwroot/js/behavior/collector.js; this file
 * just re-exposes the shape controllers actually use. */
(function () {
    'use strict';

    angular.module('degoudseApp').factory('behaviorSvc', [function () {
        var b = window.__behavior;
        if (!b) throw new Error('window.__behavior is missing — check script load order in _Layout');
        var c = b.collector;
        return {
            init: function (opts) { return c.init(opts); },
            setStep: function (stepId) { return c.setStep(stepId); },
            fieldFocus: function (id) { return c.fieldFocus(id); },
            fieldBlur: function (id, len) { return c.fieldBlur(id, len); },
            fieldChange: function (id, next, mode) { return c.fieldChange(id, next, mode); },
            fieldPaste: function (id, len) { return c.fieldPaste(id, len); },
            drain: function () { return c.drain(); },
            reset: function () { return c.reset(); },
            recordClaimSubmittedAt: function (id) { return b.recordClaimSubmittedAt(id); },
            readClaimSubmittedAt: function (id) { return b.readClaimSubmittedAt(id); },
            attachBehaviorToClaim: function (id, events, stage, prior) {
                return b.attachBehaviorToClaim(id, events, stage, prior);
            },
        };
    }]);
})();
