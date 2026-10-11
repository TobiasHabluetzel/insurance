/* viewModeSvc — which lens the claim pages are being read through.
 *
 * 'customer'   what the claimant sees
 * 'management' the same claim from the insurer's side
 *
 * Kept in sessionStorage rather than a URL parameter or a cookie, for two
 * reasons: the mode survives moving between the wizard pages, and it dies with
 * the tab, so a demo cannot leave the management view switched on for whoever
 * opens the laptop next. It is never sent to the server — the server decides
 * only whether the toggle exists at all (DemoOptions.ManagementView).
 */
(function () {
    'use strict';

    angular.module('degoudseApp').factory('viewModeSvc', ['$rootScope', function ($rootScope) {
        var KEY = 'claim_view_mode';
        var CUSTOMER = 'customer';
        var MANAGEMENT = 'management';

        function read() {
            try {
                return sessionStorage.getItem(KEY) === MANAGEMENT ? MANAGEMENT : CUSTOMER;
            } catch (e) {
                // Private browsing can throw on access, not just on write.
                return CUSTOMER;
            }
        }

        var current = read();

        function set(mode) {
            current = mode === MANAGEMENT ? MANAGEMENT : CUSTOMER;
            try { sessionStorage.setItem(KEY, current); } catch (e) { /* best effort */ }
            // Controllers listen rather than poll, so a toggle anywhere on the
            // page updates every panel at once.
            $rootScope.$broadcast('viewMode:changed', current);
        }

        return {
            CUSTOMER: CUSTOMER,
            MANAGEMENT: MANAGEMENT,
            get: function () { return current; },
            isManagement: function () { return current === MANAGEMENT; },
            set: set,
            toggle: function () { set(current === MANAGEMENT ? CUSTOMER : MANAGEMENT); },
        };
    }]);
})();
