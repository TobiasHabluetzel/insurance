/* <chat-widget> — bottom-right FAB that opens an iframe of the bot.
 * The bot's × button posts back { type: 'widget-close' } which we
 * listen for and hide the iframe. Iframe stays in the DOM so chat
 * state survives close/reopen (mirrors the React implementation).
 *
 * Attributes:
 *   bot-url — full URL of the bot page to embed
 */
(function () {
    'use strict';

    angular.module('portalApp').directive('chatWidget', function () {
        return {
            restrict: 'E',
            scope: { botUrl: '@' },
            template:
                '<div style="position: fixed; bottom: 16px; right: 16px; z-index: 9999;">' +
                '  <div ng-style="{ display: open ? \'block\' : \'none\', marginBottom: \'12px\' }">' +
                '    <iframe ng-src="{{ botUrl }}"' +
                '            style="width: 440px; height: 700px; border: none; border-radius: 20px; box-shadow: 0 12px 48px rgba(0,0,0,0.18); display: block;"' +
                '            allow="microphone" title="Claims Chat"></iframe>' +
                '  </div>' +
                '  <div ng-if="!open" style="display: flex; justify-content: flex-end;">' +
                '    <button type="button" ng-click="openChat()" aria-label="Open chat"' +
                '            style="width: 56px; height: 56px; border-radius: 50%; background: #F5A623; border: none; cursor: pointer; box-shadow: 0 4px 16px rgba(0,0,0,0.2); display: flex; align-items: center; justify-content: center;">' +
                '      <svg width="22" height="22" viewBox="0 0 24 24" fill="white">' +
                '        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>' +
                '      </svg>' +
                '    </button>' +
                '  </div>' +
                '</div>',
            link: function (scope) {
                scope.open = false;
                scope.openChat = function () { scope.open = true; };

                function onMessage(e) {
                    if (e && e.data && e.data.type === 'widget-close') {
                        scope.$apply(function () { scope.open = false; });
                    }
                }
                window.addEventListener('message', onMessage);
                scope.$on('$destroy', function () { window.removeEventListener('message', onMessage); });
            },
        };
    });
})();
