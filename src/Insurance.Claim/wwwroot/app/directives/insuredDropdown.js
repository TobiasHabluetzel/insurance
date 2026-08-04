/* <insured-dropdown> — AngularJS 1.x port of React InsuredDropdown.
 *
 * Attributes:
 *   insureds     — array of { uid, name, ... } (one-way, bound via '<')
 *   selectedUid  — currently-selected uid (two-way, '=')
 *   onChange     — expression invoked as onChange({insured: ...}) when the
 *                  user picks a person; used by the parent FileClaim
 *                  controller to fire the behavior signal too.
 *
 * Kept intentionally close to the React implementation so a visual diff
 * shows only what the framework forces to differ. */
(function () {
    'use strict';

    angular.module('degoudseApp').directive('insuredDropdown', function () {
        return {
            restrict: 'E',
            scope: {
                insureds: '<',
                selectedUid: '=',
                onChange: '&',
            },
            template:
                '<div class="relative">' +
                '  <button type="button" ng-click="open = !open"' +
                '          class="w-full bg-white rounded-2xl px-4 py-4 flex items-center justify-between shadow-sm text-left">' +
                '    <span ng-class="selected() ? \'text-brand-purple font-semibold\' : \'text-gray-400\'">' +
                '      {{ selected() ? selected().name : "Please choose" }}' +
                '    </span>' +
                '    <svg ng-class="{\'rotate-180\': open}" class="text-brand-blue transition-transform"' +
                '         width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">' +
                '      <polyline points="6 9 12 15 18 9"/>' +
                '    </svg>' +
                '  </button>' +
                '  <div ng-if="open" class="mt-1 bg-white rounded-2xl shadow-md overflow-hidden">' +
                '    <button ng-repeat="p in insureds track by p.uid" type="button"' +
                '            ng-click="pick(p)"' +
                '            ng-class="p.uid === selectedUid ? \'text-brand-purple font-semibold\' : \'text-gray-500\'"' +
                '            class="w-full text-left px-4 py-4"' +
                '            ng-attr-style="{{ $index < insureds.length - 1 ? \'border-bottom: 1px solid rgb(243 244 246);\' : \'\' }}">' +
                '      {{ p.name }}' +
                '    </button>' +
                '  </div>' +
                '</div>',
            link: function (scope) {
                scope.open = false;
                scope.selected = function () {
                    if (!scope.insureds) return null;
                    for (var i = 0; i < scope.insureds.length; i++) {
                        if (scope.insureds[i].uid === scope.selectedUid) return scope.insureds[i];
                    }
                    return null;
                };
                scope.pick = function (p) {
                    scope.selectedUid = p.uid;
                    scope.open = false;
                    scope.onChange({ insured: p });
                };
            },
        };
    });
})();
