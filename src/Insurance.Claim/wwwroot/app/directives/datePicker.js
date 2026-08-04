/* <date-picker> — AngularJS 1.x port of the React DatePicker.
 *
 * Attributes:
 *   value        — currently-selected Date (two-way, '=')
 *   minDate      — earliest selectable Date (one-way, '<')
 *   maxDate      — latest selectable Date (one-way, '<')
 *   onChange     — expression called as onChange({date: <Date>}) when the
 *                  user picks a day.
 *
 * Monday-first calendar, brand-styled, matches the React implementation
 * pixel-for-pixel with tailwind classes. */
(function () {
    'use strict';

    var DAYS = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SO'];
    var MONTHS = ['January', 'February', 'March', 'April', 'May', 'June',
        'July', 'August', 'September', 'October', 'November', 'December'];

    function startOfDay(d) {
        return new Date(d.getFullYear(), d.getMonth(), d.getDate());
    }

    function isSameDay(a, b) {
        return a.getFullYear() === b.getFullYear()
            && a.getMonth() === b.getMonth()
            && a.getDate() === b.getDate();
    }

    angular.module('degoudseApp').directive('datePicker', function () {
        return {
            restrict: 'E',
            scope: {
                value: '=',
                minDate: '<',
                maxDate: '<',
                onChange: '&',
            },
            template:
                '<div>' +
                '  <button type="button" ng-click="toggle()"' +
                '          class="w-full bg-white rounded-2xl px-4 py-4 flex items-center justify-between shadow-sm text-left">' +
                '    <span ng-class="displayValue() ? \'text-brand-purple font-semibold\' : \'text-gray-400\'">' +
                '      {{ displayValue() || "Please choose" }}' +
                '    </span>' +
                '    <svg class="text-brand-blue" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">' +
                '      <rect x="3" y="4" width="18" height="18" rx="2"/>' +
                '      <line x1="16" y1="2" x2="16" y2="6"/>' +
                '      <line x1="8" y1="2" x2="8" y2="6"/>' +
                '      <line x1="3" y1="10" x2="21" y2="10"/>' +
                '      <circle cx="12" cy="16" r="1.5" fill="currentColor"/>' +
                '    </svg>' +
                '  </button>' +
                '  <div ng-if="open" class="mt-1 bg-white rounded-2xl shadow-md overflow-hidden">' +
                '    <div class="flex items-center justify-between px-4 py-3 border-b border-gray-100">' +
                '      <button type="button" ng-click="prevMonth()" class="text-brand-purple p-1">' +
                '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">' +
                '          <polyline points="15 18 9 12 15 6"/>' +
                '        </svg>' +
                '      </button>' +
                '      <span class="text-brand-purple font-bold">{{ monthName }} {{ year }}</span>' +
                '      <button type="button" ng-click="nextMonth()" class="text-brand-purple p-1">' +
                '        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">' +
                '          <polyline points="9 18 15 12 9 6"/>' +
                '        </svg>' +
                '      </button>' +
                '    </div>' +
                '    <div class="grid grid-cols-7 px-3 pt-3 pb-1">' +
                '      <div ng-repeat="d in dayLabels" class="text-center text-xs text-gray-400 font-medium py-1">{{ d }}</div>' +
                '    </div>' +
                '    <div class="px-3 pb-3">' +
                '      <div ng-repeat="week in weeks track by $index"' +
                '           ng-class="{\'bg-brand-blue-light\': week.isCurrent}"' +
                '           class="grid grid-cols-7 rounded-full overflow-hidden">' +
                '        <button ng-repeat="cell in week.cells track by $index" type="button"' +
                '                ng-if="cell"' +
                '                ng-disabled="cell.disabled"' +
                '                ng-click="pick(cell.day)"' +
                '                ng-class="cell.isSelected ? \'text-white\' : (cell.isToday ? \'text-brand-purple font-bold\' : (cell.disabled ? \'text-gray-300\' : \'text-gray-600\'))"' +
                '                class="relative text-sm py-2 text-center transition-colors">' +
                '          <span ng-if="cell.isSelected" class="absolute inset-0 flex items-center justify-center">' +
                '            <span class="w-8 h-8 rounded-full bg-brand-purple"></span>' +
                '          </span>' +
                '          <span class="relative z-10">{{ cell.day.getDate() }}</span>' +
                '        </button>' +
                '        <div ng-repeat-end ng-if="!cell"></div>' +
                '      </div>' +
                '    </div>' +
                '  </div>' +
                '</div>',
            link: function (scope) {
                scope.open = false;
                scope.dayLabels = DAYS;

                function rebuild() {
                    var viewDate = scope.viewDate || scope.value || scope.maxDate || new Date();
                    var y = viewDate.getFullYear();
                    var m = viewDate.getMonth();
                    scope.year = y;
                    scope.monthName = MONTHS[m];

                    var today = startOfDay(new Date());
                    var min = scope.minDate ? startOfDay(scope.minDate) : new Date(1900, 0, 1);
                    var max = scope.maxDate ? startOfDay(scope.maxDate) : new Date(2100, 0, 1);

                    var first = new Date(y, m, 1);
                    var startDow = first.getDay() || 7; // 1=Mon..7=Sun
                    var daysInMonth = new Date(y, m + 1, 0).getDate();
                    var cells = [];
                    for (var i = 1; i < startDow; i++) cells.push(null);
                    for (var d = 1; d <= daysInMonth; d++) {
                        var day = new Date(y, m, d);
                        cells.push({
                            day: day,
                            isToday: isSameDay(day, today),
                            isSelected: scope.value ? isSameDay(day, scope.value) : false,
                            disabled: startOfDay(day) < min || startOfDay(day) > max,
                        });
                    }
                    while (cells.length % 7 !== 0) cells.push(null);

                    var weeks = [];
                    for (var w = 0; w < cells.length / 7; w++) {
                        var slice = cells.slice(w * 7, w * 7 + 7);
                        var real = slice.filter(function (c) { return c; });
                        var isCurrent = real.some(function (c) { return c.isToday; });
                        weeks.push({ cells: slice, isCurrent: isCurrent });
                    }
                    scope.weeks = weeks;
                }

                scope.toggle = function () { scope.open = !scope.open; };
                scope.prevMonth = function () {
                    var v = scope.viewDate || scope.value || scope.maxDate || new Date();
                    scope.viewDate = new Date(v.getFullYear(), v.getMonth() - 1, 1);
                    rebuild();
                };
                scope.nextMonth = function () {
                    var v = scope.viewDate || scope.value || scope.maxDate || new Date();
                    scope.viewDate = new Date(v.getFullYear(), v.getMonth() + 1, 1);
                    rebuild();
                };
                scope.pick = function (d) {
                    var clamped = startOfDay(d);
                    scope.value = clamped;
                    scope.open = false;
                    scope.onChange({ date: clamped });
                    rebuild();
                };
                scope.displayValue = function () {
                    if (!scope.value) return null;
                    return scope.value.toLocaleDateString('en-GB', {
                        day: 'numeric', month: 'long', year: 'numeric',
                    });
                };

                scope.$watch('value', rebuild);
                scope.$watch('minDate', rebuild);
                scope.$watch('maxDate', rebuild);
                rebuild();
            },
        };
    });
})();
