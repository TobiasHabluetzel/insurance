/* ViewModeController — drives the customer/management toggle in the brand
 * header. Thin on purpose: all the state lives in viewModeSvc so any page can
 * read the mode without going through this controller. */
(function () {
    'use strict';

    angular.module('degoudseApp').controller('ViewModeController',
        ['$scope', 'viewModeSvc', function ($scope, viewModeSvc) {
            $scope.isManagement = viewModeSvc.isManagement;
            $scope.toggle = viewModeSvc.toggle;
        }]);
})();
