/* BotController — AngularJS 1.x port of React BotPage.
 *
 * State-machine chat controller talking to /api/bot/{welcome,message,submit}.
 * The state object is the shape produced by the .NET BotConversationService
 * on the API side; the client carries it verbatim between turns.
 *
 * Firing behavior signals on every step transition + input keystrokes so
 * the analyst has the same fidelity as the React version. */
(function () {
    'use strict';

    angular.module('degoudseApp').controller('BotController',
        ['$scope', '$window', '$http', '$timeout', '$sce', 'behaviorSvc',
        function ($scope, $window, $http, $timeout, $sce, behaviorSvc) {

            $scope.messages = [];
            $scope.state = { step: 'Welcome' };
            $scope.options = null;
            $scope.awaitingUpload = false;
            $scope.files = [];
            // Nested so the ng-if="!finished()" child scope's ng-model
            // writes back through the shared reference (AngularJS "dot
            // rule").
            $scope.form = { input: '' };
            $scope.sending = false;

            // Initial greeting.
            $http.get('/api/bot/welcome').then(function (r) {
                var data = r.data;
                $scope.messages = [{ role: 'bot', text: data.reply }];
                $scope.state = data.state;
                $scope.options = data.options || null;
            }).catch(function () {
                $scope.messages = [{ role: 'bot', text: "Sorry, I couldn't start. Please refresh." }];
            });

            // Emit a step transition on every state.step change.
            $scope.$watch('state.step', function (step) {
                if (step) behaviorSvc.setStep('bot:' + step);
            });

            // Refocus the text input after the bot replies. Disabling the
            // input while sending drops focus; restoring it keeps the
            // conversation flowing without an extra click. Skip when
            // we're waiting on an upload. Mirrors the React version.
            $scope.$watchGroup(['sending', 'awaitingUpload'], function (vals) {
                if (vals[0] || vals[1]) return;
                $timeout(function () {
                    var el = document.querySelector('[ng-controller=BotController] input[type=text]');
                    if (el) el.focus();
                });
            });

            // Auto-scroll on new messages.
            var scrollArea = null;
            $scope.$watchCollection('messages', function () {
                $timeout(function () {
                    scrollArea = scrollArea || document.getElementById('bot-scroll');
                    if (scrollArea) scrollArea.scrollTo({ top: scrollArea.scrollHeight, behavior: 'smooth' });
                });
            });

            // When the state machine reaches Submitting, fire the multipart submit.
            $scope.$watch('state.step', function (step) {
                if (step !== 'Submitting' || $scope.sending) return;
                $scope.sending = true;
                var form = new FormData();
                form.append('state', JSON.stringify($scope.state));
                $scope.files.forEach(function (f) { form.append('files', f, f.name); });

                fetch('/api/bot/submit', { method: 'POST', body: form })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        $scope.$apply(function () {
                            $scope.messages.push({ role: 'bot', text: data.reply });
                            $scope.state = data.state;
                            $scope.options = data.options || null;
                            $scope.awaitingUpload = false;

                            var acClaimId = $scope.state.claimId;
                            var events = behaviorSvc.drain();
                            behaviorSvc.reset();
                            if (acClaimId) {
                                behaviorSvc.recordClaimSubmittedAt(acClaimId);
                                behaviorSvc.attachBehaviorToClaim(acClaimId, events, 'submit');
                            }
                        });
                    })
                    .catch(function () {
                        $scope.$apply(function () {
                            $scope.messages.push({ role: 'bot', text: 'Sorry, submission failed. Please try again.' });
                        });
                    })
                    .finally(function () {
                        $scope.$apply(function () { $scope.sending = false; });
                    });
            });

            $scope.sendMessage = function (text) {
                text = (text || '').trim();
                if (!text || $scope.sending) return;
                $scope.messages.push({ role: 'user', text: text });
                $scope.form.input = '';
                $scope.options = null;
                $scope.sending = true;

                $http.post('/api/bot/message', { message: text, state: $scope.state })
                    .then(function (r) {
                        var data = r.data;
                        $scope.messages.push({ role: 'bot', text: data.reply });
                        $scope.state = data.state;
                        $scope.options = data.options || null;
                        $scope.awaitingUpload = !!data.awaitingUpload;
                    }).catch(function () {
                        $scope.messages.push({ role: 'bot', text: 'Sorry, something went wrong. Try again?' });
                    }).finally(function () {
                        $scope.sending = false;
                    });
            };

            $scope.onInputChange = function () {
                behaviorSvc.fieldChange('bot-input', $scope.form.input || '', 'type');
            };
            $scope.onInputFocus = function () { behaviorSvc.fieldFocus('bot-input'); };
            $scope.onInputBlur = function () { behaviorSvc.fieldBlur('bot-input', ($scope.form.input || '').length); };
            $scope.onInputPaste = function (e) {
                var pasted = (e.originalEvent || e).clipboardData
                    ? (e.originalEvent || e).clipboardData.getData('text')
                    : '';
                behaviorSvc.fieldPaste('bot-input', (pasted || '').length);
            };
            $scope.onKey = function (e) {
                if ((e.originalEvent || e).key === 'Enter') $scope.sendMessage($scope.form.input);
            };

            $scope.pickFiles = function (fileList) {
                var picked = Array.prototype.slice.call(fileList || []);
                if (!picked.length) return;
                $scope.files = $scope.files.concat(picked);
                $scope.messages.push({ role: 'user', text: picked.map(function (f) { return '📎 ' + f.name; }).join('\n') });
                $scope.messages.push({ role: 'bot', text: "Got it. Upload more or type 'done' when you're finished." });
            };

            $scope.removeFile = function (idx) {
                $scope.files.splice(idx, 1);
            };

            $scope.close = function () {
                if ($window.parent) $window.parent.postMessage({ type: 'widget-close' }, '*');
            };

            $scope.finished = function () {
                return $scope.state.step === 'Done' || $scope.state.step === 'Blocked';
            };

            $scope.splitOption = function (opt) {
                var dash = opt.indexOf(' – ');
                return dash > 0
                    ? { primary: opt.slice(0, dash), secondary: opt.slice(dash + 3) }
                    : { primary: opt, secondary: null };
            };

            // ng-bind-html requires a trusted string. We escape the raw
            // text before applying the **bold** rewrite, so the only HTML
            // that survives is our own <strong>. $sce.trustAsHtml is safe
            // here because the escape happens first — treating the whole
            // string as trusted after that isn't the same as trusting the
            // upstream bot text.
            $scope.renderInline = function (text) {
                var escaped = (text || '')
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;');
                return $sce.trustAsHtml(escaped.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>'));
            };
        }]);
})();
