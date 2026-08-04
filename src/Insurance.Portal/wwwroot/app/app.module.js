/* AngularJS 1.x root module for the policyholder portal. Same shape as
 * the degoudseclaim MVC port — feature-specific controllers and
 * directives register on 'portalApp'. */
(function () {
    'use strict';

    angular.module('portalApp', [])
        .config(['$sceDelegateProvider', function ($sceDelegateProvider) {
            // The chat widget embeds an iframe of the claim wizard's /bot
            // page — a different Railway host. Angular's default $sce
            // resourceUrlWhitelist is 'self' only, which blocks that
            // iframe with $sce:insecurl (surfaced as $interpolate:interr
            // when ng-src fails to resolve). Whitelist Railway public
            // hosts so the widget can load its src.
            $sceDelegateProvider.resourceUrlWhitelist([
                'self',
                'https://*.up.railway.app/**',
                'https://*.railway.app/**',
            ]);
        }]);
})();
