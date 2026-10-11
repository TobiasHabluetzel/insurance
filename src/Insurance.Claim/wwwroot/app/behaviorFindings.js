/* behaviorFindings — turns raw behavioural events into plain-language findings
 * for the management view.
 *
 * Lives in the frontend rather than the API on purpose: naming what a signal
 * *means* is a presentation decision, and the wording will be argued over. The
 * API returns the events as collected.
 *
 * Two rules this file holds to, because a fraud panel that overstates its case
 * is worse than no panel at all:
 *
 *   1. Findings are observations, never conclusions. "Pasted rather than typed"
 *      is a fact. "Likely fraudulent" is not, and nothing here says it.
 *   2. Nothing is invented. Every finding maps to an event the collector
 *      actually emits. The collector's privacy contract means raw field values
 *      never leave the browser — only bucketed metadata — so findings talk
 *      about magnitude ("100+ characters") and never content.
 */
(function (root) {
    'use strict';

    var LENGTH_WORDS = {
        '0': 'nothing',
        '1-5': 'a few characters',
        '6-20': 'a short entry',
        '21-100': 'a moderate entry',
        '100+': 'a long entry',
    };

    /** Field ids are dotted paths; show the leaf, spaced out. */
    function fieldLabel(fieldId) {
        if (!fieldId) return 'a field';
        var leaf = String(fieldId).split('.').pop();
        return leaf
            .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
            .replace(/[_-]+/g, ' ')
            .toLowerCase();
    }

    function humanDuration(ms) {
        if (ms == null || !isFinite(ms)) return null;
        var s = Math.round(ms / 1000);
        if (s < 60) return s + 's';
        var m = Math.round(s / 6) / 10;
        if (m < 60) return m + ' min';
        return (Math.round(m / 6) / 10) + ' h';
    }

    function count(map, key) { map[key] = (map[key] || 0) + 1; }

    /**
     * @param batches  as returned by GET /claims/{id}/behavior
     * @returns { findings: [{ title, detail, weight }], summary: {...} }
     *          weight: 'notable' | 'context' — styling only, not a risk score.
     */
    function build(batches) {
        batches = batches || [];

        var findings = [];
        var pastes = {};        // fieldId -> count
        var pasteBucket = {};   // fieldId -> last bucket seen
        var reentries = {};     // fieldId -> count
        var bigEdits = {};      // fieldId -> count
        var stepRevisits = {};  // stepId -> highest revisitCount
        var navBacks = 0;
        var resumed = false;
        var deviceId = null;
        var tzOffsetMinutes = null;
        var priorSubmittedAt = null;
        var firstTs = null, lastTs = null, total = 0;
        var stages = [];

        batches.forEach(function (batch) {
            if (batch.stage && stages.indexOf(batch.stage) === -1) stages.push(batch.stage);
            if (batch.priorSubmittedAt) priorSubmittedAt = batch.priorSubmittedAt;

            (batch.events || []).forEach(function (e) {
                total++;
                if (e.ts) {
                    if (firstTs === null || e.ts < firstTs) firstTs = e.ts;
                    if (lastTs === null || e.ts > lastTs) lastTs = e.ts;
                }
                var m = e.meta || {};

                switch (e.eventType) {
                    case 'session_start':
                        if (m.resumed) resumed = true;
                        if (m.deviceId) deviceId = m.deviceId;
                        if (m.tzOffsetMinutes != null) tzOffsetMinutes = m.tzOffsetMinutes;
                        break;
                    case 'session_resume':
                        resumed = true;
                        break;
                    case 'field_paste':
                        count(pastes, e.fieldId);
                        pasteBucket[e.fieldId] = m.pastedLengthBucket;
                        break;
                    case 'field_reentry':
                        count(reentries, e.fieldId);
                        break;
                    case 'field_value_change':
                        if (m.editDistanceBucket === 'large' || m.editDistanceBucket === 'medium') {
                            if (m.postCompletionEdits > 0) count(bigEdits, e.fieldId);
                        }
                        break;
                    case 'step_enter':
                        if (m.revisitCount > (stepRevisits[e.stepId] || 0)) {
                            stepRevisits[e.stepId] = m.revisitCount;
                        }
                        break;
                    case 'nav_back':
                        navBacks++;
                        break;
                }
            });
        });

        // ---- notable: things an assessor would want pointed out ------------

        Object.keys(pastes).forEach(function (f) {
            findings.push({
                weight: 'notable',
                title: 'Pasted rather than typed — ' + fieldLabel(f),
                detail: 'Content was pasted into this field'
                    + (pastes[f] > 1 ? ' ' + pastes[f] + ' times' : '')
                    + (LENGTH_WORDS[pasteBucket[f]] ? ' (' + LENGTH_WORDS[pasteBucket[f]] + ')' : '')
                    + '. Common when text is prepared elsewhere or reused.',
            });
        });

        Object.keys(bigEdits).forEach(function (f) {
            findings.push({
                weight: 'notable',
                title: 'Substantially revised after completion — ' + fieldLabel(f),
                detail: 'The field was finished, then changed again by a large amount.',
            });
        });

        Object.keys(reentries).forEach(function (f) {
            findings.push({
                weight: 'notable',
                title: 'Cleared and re-entered — ' + fieldLabel(f),
                detail: 'The field was completed, emptied and filled in again'
                    + (reentries[f] > 1 ? ', ' + reentries[f] + ' times' : '') + '.',
            });
        });

        if (priorSubmittedAt) {
            var since = humanDuration(Date.now() - priorSubmittedAt);
            findings.push({
                weight: 'notable',
                title: 'Earlier claim from this browser',
                detail: 'A previous claim was registered from the same browser'
                    + (since ? ' ' + since + ' ago' : '') + '.',
            });
        }

        // ---- context: useful, but unremarkable on its own ------------------

        var revisited = Object.keys(stepRevisits).filter(function (s) { return stepRevisits[s] > 0; });
        if (revisited.length) {
            findings.push({
                weight: 'context',
                title: 'Steps revisited',
                detail: revisited.map(function (s) {
                    return fieldLabel(s) + ' (' + (stepRevisits[s] + 1) + '×)';
                }).join(', ') + '.',
            });
        }

        if (navBacks > 0) {
            findings.push({
                weight: 'context',
                title: 'Navigated backwards ' + navBacks + (navBacks === 1 ? ' time' : ' times'),
                detail: 'Going back is normal; a high count alongside edits is worth a look.',
            });
        }

        if (resumed) {
            findings.push({
                weight: 'context',
                title: 'Resumed an earlier draft',
                detail: 'The claim was started in a previous session and continued later.',
            });
        }

        if (tzOffsetMinutes != null) {
            var sign = tzOffsetMinutes <= 0 ? '+' : '-';
            var hours = Math.abs(tzOffsetMinutes) / 60;
            findings.push({
                weight: 'context',
                title: 'Browser timezone UTC' + sign + hours,
                detail: 'Compare against where the incident is stated to have happened.',
            });
        }

        return {
            findings: findings,
            summary: {
                total: total,
                stages: stages,
                deviceId: deviceId,
                duration: (firstTs && lastTs && lastTs > firstTs) ? humanDuration(lastTs - firstTs) : null,
                notableCount: findings.filter(function (f) { return f.weight === 'notable'; }).length,
            },
        };
    }

    root.__behaviorFindings = { build: build, fieldLabel: fieldLabel };
})(window);
