/* authSvc — $http wrapper around /api/auth. Mirrors
 * (legacy repo)src/api/auth.ts. Also owns the JWT lifecycle:
 * the React version had a React context for this; we just wrap
 * localStorage so any AngularJS controller can call the same accessors.
 *
 * Token is client-side (localStorage) same as the React version — the
 * portal API is Bearer-token, not cookie-auth, so we mirror it. */
(function () {
    'use strict';

    var TOKEN_KEY = 'portal_token';

    angular.module('portalApp').factory('authSvc', ['$http', function ($http) {
        var BASE = '/api/auth';

        return {
            checkEmail: function (email) {
                return $http.post(BASE + '/check-email', { email: email }).then(function (r) { return r.data; });
            },
            login: function (emailHash, password) {
                return $http.post(BASE + '/login', { emailHash: emailHash, password: password })
                    .then(function (r) { return r.data; });
            },
            register: function (emailHash, insuredId, password) {
                return $http.post(BASE + '/register', { emailHash: emailHash, insuredId: insuredId, password: password })
                    .then(function (r) { return r.data; });
            },
            oauthUrl: function (provider) {
                return BASE + '/oauth/challenge/' + provider;
            },

            storeToken: function (token) {
                try { localStorage.setItem(TOKEN_KEY, token); } catch (e) { /* private mode */ }
            },
            readToken: function () {
                try { return localStorage.getItem(TOKEN_KEY); } catch (e) { return null; }
            },
            clearToken: function () {
                try { localStorage.removeItem(TOKEN_KEY); } catch (e) { /* private mode */ }
            },
        };
    }]);
})();
