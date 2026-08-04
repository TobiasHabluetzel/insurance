/* <policy-card> — one policy row on the dashboard. Two outbound
 * affordances: a "Claim by mail" mailto: link (uses the CloudMailin
 * inbound address) and a "File a claim" button that deep-links into
 * the claim wizard on a new tab.
 *
 * Attributes:
 *   policy       — PolicySummary object (one-way, '<')
 *   holder-name  — display name of the holder (one-way, '<')
 *   config       — { claimAppUrl, claimEmail } (one-way, '<')
 */
(function () {
    'use strict';

    function formatDate(iso) {
        return new Date(iso).toLocaleDateString('nl-NL', {
            day: 'numeric', month: 'short', year: 'numeric',
        });
    }

    function mailtoForClaim(policy, holderName, claimEmail) {
        var subject = 'New claim for policy ' + policy.displayNumber;
        var body = [
            'Hello,',
            '',
            "I'd like to file a claim on policy " + policy.displayNumber + '.',
            '',
            'Claimant',
            'Name: ' + holderName,
            '',
            'Incident',
            'What happened: ',
            'When: ',
            'Where: ',
            '',
            'Please find any related documents attached.',
            '',
            'Kind regards,',
        ].join('\n');
        return 'mailto:' + claimEmail
            + '?subject=' + encodeURIComponent(subject)
            + '&body=' + encodeURIComponent(body);
    }

    angular.module('portalApp').directive('policyCard', function () {
        return {
            restrict: 'E',
            scope: {
                policy: '<',
                holderName: '<',
                config: '<',
            },
            template:
                '<div class="bg-white rounded-2xl shadow-sm overflow-hidden">' +
                '  <div class="px-5 pt-5 pb-4 border-b border-gray-100">' +
                '    <div class="flex items-start justify-between gap-4">' +
                '      <div>' +
                '        <p class="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-0.5">{{ policy.displayNumber }}</p>' +
                '        <h3 class="text-base font-semibold text-gray-800">{{ policy.productName }}</h3>' +
                '      </div>' +
                '      <span class="mt-0.5 shrink-0 text-xs font-medium px-2.5 py-1 rounded-full"' +
                '            ng-class="isActive() ? \'bg-green-50 text-green-700\' : \'bg-gray-100 text-gray-500\'">' +
                '        {{ isActive() ? "Active" : "Expired" }}' +
                '      </span>' +
                '    </div>' +
                '  </div>' +
                '  <div class="px-5 py-4 flex items-center gap-8 flex-wrap">' +
                '    <div><p class="text-xs text-gray-400 mb-0.5">Start</p><p class="text-sm font-medium text-gray-700">{{ formatDate(policy.periodStart) }}</p></div>' +
                '    <div><p class="text-xs text-gray-400 mb-0.5">End</p><p class="text-sm font-medium text-gray-700">{{ formatDate(policy.periodEnd) }}</p></div>' +
                '    <div ng-if="policy.destination"><p class="text-xs text-gray-400 mb-0.5">Destination</p><p class="text-sm font-medium text-gray-700">{{ policy.destination }}</p></div>' +
                '    <div ng-if="policy.travelers.length" class="min-w-0">' +
                '      <p class="text-xs text-gray-400 mb-0.5">{{ policy.travelers.length === 1 ? "Insured" : "Insureds" }}</p>' +
                '      <p class="text-sm font-medium text-gray-700 truncate">{{ travelerNames() }}</p>' +
                '    </div>' +
                '    <div><p class="text-xs text-gray-400 mb-0.5">Claims</p><p class="text-sm font-medium text-gray-700">{{ policy.claimsCount }}</p></div>' +
                '    <div class="ml-auto flex items-center gap-2">' +
                '      <a ng-href="{{ mailtoUrl() }}" class="border border-gray-200 text-gray-600 text-sm font-semibold px-4 py-2 rounded-full hover:bg-gray-50 transition-colors">Claim by mail</a>' +
                '      <a ng-href="{{ claimAppUrl() }}" target="_blank" rel="noopener noreferrer"' +
                '         class="bg-brand-blue text-white text-sm font-semibold px-4 py-2 rounded-full hover:opacity-90 transition-opacity">File a claim</a>' +
                '    </div>' +
                '  </div>' +
                '</div>',
            link: function (scope) {
                scope.formatDate = formatDate;
                scope.isActive = function () {
                    return scope.policy && new Date(scope.policy.periodEnd) >= new Date();
                };
                scope.travelerNames = function () {
                    return (scope.policy.travelers || [])
                        .map(function (t) { return (t.firstName + ' ' + t.lastName).trim(); })
                        .join(', ');
                };
                scope.mailtoUrl = function () {
                    if (!scope.policy || !scope.config) return '';
                    return mailtoForClaim(scope.policy, scope.holderName || '', scope.config.claimEmail);
                };
                scope.claimAppUrl = function () {
                    if (!scope.policy || !scope.config) return '';
                    return scope.config.claimAppUrl + '/?policy=' + encodeURIComponent(scope.policy.displayNumber);
                };
            },
        };
    });
})();
