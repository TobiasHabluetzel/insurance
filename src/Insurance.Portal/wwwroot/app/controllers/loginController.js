/* LoginController — AngularJS 1.x port of React LoginPage.
 *
 * Three-step wizard: email → (password | set-password). The React version
 * used discriminated state (`step: 'email' | 'password' | 'set-password'`);
 * we do the same via $scope.step. Errors surface in $scope.error and are
 * cleared on each new submission.
 *
 * All form-input state lives on $scope.form so ng-model writes survive
 * the ng-if child scope on each step (the "dot rule" — primitives on the
 * parent scope get shadowed by child scopes, but property writes on a
 * shared object are visible to the parent). */
(function () {
    'use strict';

    angular.module('portalApp').controller('LoginController',
        ['$scope', '$window', 'authSvc',
        function ($scope, $window, authSvc) {

            // If we're already signed in, jump straight to /dashboard.
            if (authSvc.readToken()) { $window.location.href = '/dashboard'; }

            $scope.step = 'email';
            $scope.form = { email: '', password: '', confirmPassword: '' };
            $scope.emailHash = '';
            $scope.externalProviders = [];
            $scope.insuredId = null;
            $scope.error = '';
            $scope.loading = false;

            $scope.submitEmail = function () {
                $scope.error = '';
                $scope.loading = true;
                authSvc.checkEmail($scope.form.email).then(function (res) {
                    $scope.emailHash = res.emailHash;
                    if (res.status === 'known') {
                        $scope.externalProviders = res.externalProviders || [];
                        // Both hasPassword and !hasPassword go to the same
                        // step — the password view shows provider buttons
                        // if hasPassword is false. Matches the React flow.
                        $scope.step = 'password';
                    } else {
                        if (res.policyMatch === 'single' && res.insuredId) {
                            $scope.insuredId = res.insuredId;
                            $scope.step = 'set-password';
                        } else {
                            $scope.error = 'No policy found for this email address. Please check your email or contact support.';
                        }
                    }
                }).catch(function () {
                    $scope.error = 'Something went wrong. Please try again.';
                }).finally(function () { $scope.loading = false; });
            };

            $scope.submitPassword = function () {
                $scope.error = '';
                $scope.loading = true;
                authSvc.login($scope.emailHash, $scope.form.password).then(function (res) {
                    authSvc.storeToken(res.token);
                    $window.location.href = '/dashboard';
                }).catch(function () {
                    $scope.error = 'Incorrect password.';
                }).finally(function () { $scope.loading = false; });
            };

            $scope.submitSetPassword = function () {
                $scope.error = '';
                if ($scope.form.password !== $scope.form.confirmPassword) { $scope.error = 'Passwords do not match.'; return; }
                if (($scope.form.password || '').length < 8) { $scope.error = 'Password must be at least 8 characters.'; return; }
                if (!$scope.insuredId) { $scope.error = 'Insured id missing — please restart.'; return; }
                $scope.loading = true;
                authSvc.register($scope.emailHash, $scope.insuredId, $scope.form.password).then(function (res) {
                    authSvc.storeToken(res.token);
                    $window.location.href = '/dashboard';
                }).catch(function () {
                    $scope.error = 'Registration failed. Please try again.';
                }).finally(function () { $scope.loading = false; });
            };

            $scope.startOver = function () { $scope.step = 'email'; $scope.error = ''; };

            $scope.goOAuth = function (provider) {
                $window.location.href = authSvc.oauthUrl(provider);
            };

            $scope.hasLocalPassword = function () {
                // Empty externalProviders means "local password only"
                // (either the user set one, or the account was created
                // by policy-lookup and we're on the set-password branch).
                return !$scope.externalProviders.length || $scope.externalProviders.indexOf('Local') !== -1;
            };
        }]);
})();
