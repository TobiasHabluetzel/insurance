/* portalSvc — $http wrapper around /api/portal. Mirrors
 * (legacy repo)src/api/portal.ts. Uses authSvc's stored
 * token as the Bearer header. */
(function () {
    'use strict';

    angular.module('portalApp').factory('portalSvc', ['$http', 'authSvc', function ($http, authSvc) {
        var BASE = '/api/portal';

        return {
            getDashboard: function () {
                var token = authSvc.readToken();
                return $http.get(BASE + '/dashboard', {
                    headers: token ? { Authorization: 'Bearer ' + token } : {},
                }).then(function (r) { return r.data; });
            },
        };
    }]);
})();
