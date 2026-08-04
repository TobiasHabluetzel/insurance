/* Behavioral signal collector — plain JS port of
 * (legacy)/behavior/collector.ts. Attached to
 * window.__behavior.collector; the AngularJS behaviorSvc wraps it for
 * injection into controllers.
 *
 * Privacy contract preserved: raw field values stay in this file's
 * private closures, only bucketed metadata reaches the event stream.
 * SCHEMA_VERSION must stay in lockstep with the React version.
 */
(function (root) {
    'use strict';

    var b = root.__behavior;
    if (!b) throw new Error('behavior/types.js must load before collector.js');
    var SCHEMA_VERSION = b.SCHEMA_VERSION;
    var lengthBucket = b.lengthBucket;
    var editDistanceBucket = b.editDistanceBucket;

    var SESSION_STORAGE_KEY = 'beh_session';
    var DRAFT_STORAGE_KEY = 'beh_draft';
    var DEVICE_STORAGE_KEY = 'beh_device';
    var IDLE_THRESHOLD_MS = 30000;

    function newId() {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        return 'id-' + Math.random().toString(36).slice(2) + '-' + Date.now();
    }

    function readOrCreate(store, key) {
        var existing = store.getItem(key);
        if (existing) return existing;
        var id = newId();
        store.setItem(key, id);
        return id;
    }

    function Collector() {
        this._events = [];
        this._consent = 'none';
        this._sessionId = '';
        this._claimDraftId = '';
        this._deviceId = '';
        this._currentStep = null;
        this._fields = {};
        this._steps = {};
        this._firstCompletionOrder = 0;
        this._idleHandle = null;
        this._lastActivityAt = 0;
        this._inIdle = false;
    }

    Collector.prototype.init = function (opts) {
        if (this._consent !== 'none') return;
        if (!opts || opts.consentLevel === 'none') return;
        this._consent = opts.consentLevel;
        this._sessionId = readOrCreate(sessionStorage, SESSION_STORAGE_KEY);
        this._deviceId = readOrCreate(localStorage, DEVICE_STORAGE_KEY);

        var existingDraft = localStorage.getItem(DRAFT_STORAGE_KEY);
        var resumed = !!existingDraft;
        this._claimDraftId = existingDraft || newId();
        localStorage.setItem(DRAFT_STORAGE_KEY, this._claimDraftId);

        this._emit('session_start', null, null, {
            resumed: resumed,
            deviceId: this._deviceId,
            tzOffsetMinutes: new Date().getTimezoneOffset(),
        });
        if (resumed) this._emit('session_resume', null, null, {});
        this._kickIdle();
    };

    Collector.prototype.getDeviceId = function () { return this._deviceId; };
    Collector.prototype.getSessionId = function () { return this._sessionId; };
    Collector.prototype.getClaimDraftId = function () { return this._claimDraftId; };

    Collector.prototype.setStep = function (stepId) {
        if (!this._live()) return;
        if (this._currentStep) {
            var prev = this._steps[this._currentStep];
            var dwellMs = prev ? Date.now() - prev.enteredAt : 0;
            this._emit('step_leave', null, this._currentStep, {
                dwellMs: dwellMs,
                hadInteraction: prev ? prev.fieldInteractions > 0 : false,
            });
        }
        var existing = this._steps[stepId];
        if (existing) {
            existing.revisitCount += 1;
            existing.enteredAt = Date.now();
            existing.fieldInteractions = 0;
        } else {
            this._steps[stepId] = { enteredAt: Date.now(), fieldInteractions: 0, revisitCount: 0 };
        }
        this._emit('step_enter', null, stepId, { revisitCount: this._steps[stepId].revisitCount });
        this._currentStep = stepId;
    };

    Collector.prototype.fieldFocus = function (fieldId) {
        if (!this._live()) return;
        var f = this._ensureField(fieldId);
        f.focusStart = Date.now();
        this._touchStep();
        this._emit('field_focus', fieldId, this._currentStep, {});
    };

    Collector.prototype.fieldBlur = function (fieldId, currentValueLength) {
        if (!this._live()) return;
        var f = this._ensureField(fieldId);
        var dwellMs = f.focusStart ? Date.now() - f.focusStart : 0;
        f.focusStart = undefined;
        var isComplete = currentValueLength > 0;
        var meta = {
            dwellMs: dwellMs,
            finalLengthBucket: lengthBucket(currentValueLength),
            isComplete: isComplete,
        };
        if (isComplete && !f.completed) {
            f.completed = true;
            f.completionOrder = ++this._firstCompletionOrder;
            meta.completionOrder = f.completionOrder;
        } else if (f.completionOrder !== undefined) {
            meta.completionOrder = f.completionOrder;
        }
        this._emit('field_blur', fieldId, this._currentStep, meta);
    };

    Collector.prototype.fieldChange = function (fieldId, nextValue, mode) {
        if (!this._live()) return;
        mode = mode || 'type';
        var f = this._ensureField(fieldId);
        var prevLen = f.lastValue.length;
        var nextLen = nextValue.length;
        var lengthDelta = nextLen - prevLen;

        if (f.completed && prevLen > 0 && nextLen === 0) {
            this._emit('field_reentry', fieldId, this._currentStep, {
                previousLengthBucket: lengthBucket(prevLen),
            });
        }

        f.changeCount += 1;
        if (f.completed) f.postCompletionEdits += 1;
        f.lastValue = nextValue;
        this._touchStep();

        this._emit('field_value_change', fieldId, this._currentStep, {
            changeCount: f.changeCount,
            postCompletionEdits: f.postCompletionEdits,
            lengthDelta: lengthDelta,
            lengthBucket: lengthBucket(nextLen),
            editDistanceBucket: editDistanceBucket(lengthDelta),
            mode: mode,
        });
    };

    Collector.prototype.fieldPaste = function (fieldId, pastedLength) {
        if (!this._live()) return;
        this._touchStep();
        this._emit('field_paste', fieldId, this._currentStep, {
            pastedLengthBucket: lengthBucket(pastedLength),
        });
    };

    Collector.prototype.navBack = function (fromStep, toStep, dataChangedSinceLastVisit) {
        if (!this._live()) return;
        var target = this._steps[toStep];
        this._emit('nav_back', null, fromStep, {
            toStep: toStep,
            revisitCount: (target ? target.revisitCount : 0) + 1,
            dataChangedSinceLastVisit: !!dataChangedSinceLastVisit,
        });
    };

    Collector.prototype.navForward = function (fromStep, toStep) {
        if (!this._live()) return;
        this._emit('nav_forward', null, fromStep, { toStep: toStep });
    };

    Collector.prototype.drain = function () {
        var out = this._events;
        this._events = [];
        return out;
    };

    Collector.prototype.reset = function () {
        localStorage.removeItem(DRAFT_STORAGE_KEY);
        this._fields = {};
        this._steps = {};
        this._firstCompletionOrder = 0;
        this._currentStep = null;
        this._claimDraftId = newId();
        localStorage.setItem(DRAFT_STORAGE_KEY, this._claimDraftId);
    };

    Collector.prototype._emit = function (eventType, fieldId, stepId, meta) {
        this._events.push({
            schemaVersion: SCHEMA_VERSION,
            sessionId: this._sessionId,
            claimDraftId: this._claimDraftId,
            ts: Date.now(),
            eventType: eventType,
            fieldId: fieldId || undefined,
            stepId: stepId || undefined,
            consentLevel: this._consent,
            meta: meta,
        });
        this._lastActivityAt = Date.now();
        if (this._inIdle) {
            this._inIdle = false;
            var idleMs = Date.now() - this._lastActivityAt;
            this._events.push({
                schemaVersion: SCHEMA_VERSION,
                sessionId: this._sessionId,
                claimDraftId: this._claimDraftId,
                ts: Date.now(),
                eventType: 'idle_end',
                consentLevel: this._consent,
                meta: { idleMs: idleMs },
            });
        }
        this._kickIdle();
    };

    Collector.prototype._kickIdle = function () {
        var self = this;
        if (this._idleHandle) clearTimeout(this._idleHandle);
        this._idleHandle = setTimeout(function () {
            if (!self._live()) return;
            self._inIdle = true;
            self._events.push({
                schemaVersion: SCHEMA_VERSION,
                sessionId: self._sessionId,
                claimDraftId: self._claimDraftId,
                ts: Date.now(),
                eventType: 'idle_start',
                consentLevel: self._consent,
                meta: { thresholdMs: IDLE_THRESHOLD_MS },
            });
        }, IDLE_THRESHOLD_MS);
    };

    Collector.prototype._touchStep = function () {
        if (!this._currentStep) return;
        var s = this._steps[this._currentStep];
        if (s) s.fieldInteractions += 1;
    };

    Collector.prototype._ensureField = function (fieldId) {
        var f = this._fields[fieldId];
        if (!f) {
            f = { lastValue: '', changeCount: 0, completed: false, postCompletionEdits: 0 };
            this._fields[fieldId] = f;
        }
        return f;
    };

    Collector.prototype._live = function () {
        return this._consent !== 'none' && typeof window !== 'undefined';
    };

    b.collector = new Collector();
})(window);
