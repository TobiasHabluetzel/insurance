/* DashboardController — AngularJS 1.x port of React DashboardPage.
 *
 * Loads the dashboard payload via portalSvc; if the token is missing,
 * bounces to /. Also exposes the outbound URLs (claim-app, mailto,
 * bot) that policyCard needs, sourced from window.__portalConfig
 * which the Razor view injects from server-side config. */
(function () {
    'use strict';

    angular.module('portalApp').controller('DashboardController',
        ['$scope', '$window', 'authSvc', 'portalSvc',
        function ($scope, $window, authSvc, portalSvc) {

            if (!authSvc.readToken()) { $window.location.href = '/'; return; }

            $scope.data = null;
            $scope.error = '';
            $scope.config = $window.__portalConfig || {};

            portalSvc.getDashboard().then(function (data) {
                $scope.data = data;
            }).catch(function (err) {
                // 401 → the token is stale; force a sign-in.
                if (err && err.status === 401) {
                    authSvc.clearToken();
                    $window.location.href = '/';
                    return;
                }
                $scope.error = 'Failed to load your data. Please try again.';
            });

            $scope.holderName = function () {
                if (!$scope.data) return '';
                return ($scope.data.firstName + ' ' + $scope.data.lastName).trim();
            };

            $scope.holderInitial = function () {
                return ($scope.data && $scope.data.firstName) ? $scope.data.firstName[0] : '?';
            };

            $scope.signOut = function () {
                authSvc.clearToken();
                $window.location.href = '/';
            };
        }]);
})();
