/* $http wrapper around the DeGoudse.Api claim-wizard endpoints — the
 * client-side twin of Services/PolicyApiClient.cs. AngularJS controllers
 * inject this instead of writing raw $http calls, so all API shape / error
 * handling stays in one place. Mirrors src/DeGoudse.Web/src/api/claims.ts
 * from the React project — payload shapes are 1:1. */
(function () {
    'use strict';

    angular.module('degoudseApp').factory('policySvc', ['$http', function ($http) {
        var BASE = '/api/claimwizard';

        function isoDate(d) { return d.toISOString().split('T')[0]; }

        function submitClaim(params) {
            var policy = params.policy;
            var insured = params.insured;
            var incidentAddress = params.incidentAddress || {};
            var incidentDate = params.incidentDate;

            // AC uses the root coverage's code as the product reference
            // (e.g. "salsin_v3").
            var productCode = (policy.policyCoverage
                && policy.policyCoverage.coverages
                && policy.policyCoverage.coverages[0]
                && policy.policyCoverage.coverages[0].code) || '';

            var body = {
                currency: policy.currencyCode,
                product: productCode,
                productCode: productCode,
                claimDate: isoDate(incidentDate),
                claimant: {
                    firstName: insured.firstName || (insured.name ? insured.name.split(' ')[0] : ''),
                    lastName: insured.lastName || (insured.name ? insured.name.split(' ').slice(1).join(' ') : ''),
                    email: insured.email,
                    phoneNumber: insured.phone ? insured.phone.replace(/\s+/g, '') : undefined,
                    dateOfBirth: insured.dateOfBirth,
                    address: insured.primaryAddress ? {
                        streetAddress: insured.primaryAddress.line1 || '',
                        city: insured.primaryAddress.city || '',
                        postalCode: insured.primaryAddress.postalCode || '',
                        countryIso2Code: insured.primaryAddress.countryCode,
                    } : undefined,
                },
                incident: {
                    incidentDate: isoDate(incidentDate),
                    location: {
                        streetAddress: incidentAddress.line1 || '',
                        city: incidentAddress.city || '',
                        postalCode: incidentAddress.postalCode || '',
                        countryIso2Code: incidentAddress.countryCode,
                        position: (incidentAddress.latitude != null && incidentAddress.longitude != null)
                            ? { latitude: incidentAddress.latitude, longitude: incidentAddress.longitude }
                            : undefined,
                    },
                },
                policyCoverage: policy.policyCoverage,
            };

            // POST the claim as JSON, then upload each document sequentially
            // to /claims/{id}/documents (multipart). Same two-phase flow as
            // the React version — the API expects it this way.
            return $http.post(BASE + '/submit-claim', body)
                .then(function (r) { return r.data; })
                .then(function (claim) {
                    var docs = params.documents || [];
                    var chain = Promise.resolve();
                    docs.forEach(function (file) {
                        chain = chain.then(function () {
                            var form = new FormData();
                            form.append('file', file, file.name);
                            return fetch(BASE + '/claims/' + encodeURIComponent(claim.id) + '/documents', {
                                method: 'POST',
                                body: form,
                            });
                        });
                    });
                    return chain.then(function () { return claim; });
                });
        }

        return {
            getDashboard: function (policyId) {
                return $http.get(BASE + '/dashboard/' + encodeURIComponent(policyId))
                    .then(function (r) { return r.data; });
            },
            submitClaim: submitClaim,
        };
    }]);
})();
