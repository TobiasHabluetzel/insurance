/* <address-picker> — AngularJS 1.x port of the React AddressPicker.
 *
 * Attributes:
 *   apiKey          — Google Maps API key (passed in from Razor via
 *                     __googleMapsApiKey; kept as a one-way binding '@'
 *                     so it can be a literal in the view).
 *   initialAddress  — pre-selected Address object (one-way, '<')
 *   onChange        — expression called as onChange({address: <Address>})
 *                     when the user picks a place.
 *
 * The Google Maps JS API is loaded lazily and cached across directive
 * instances. Places autocomplete attaches to the search input when the
 * panel opens and rebuilds fresh on each close/reopen — same pattern as
 * the React version. */
(function () {
    'use strict';

    var mapsLoader = null;

    function loadMaps(apiKey) {
        if (mapsLoader) return mapsLoader;
        if (window.google && window.google.maps && window.google.maps.places) {
            mapsLoader = Promise.resolve();
            return mapsLoader;
        }
        mapsLoader = new Promise(function (resolve, reject) {
            var s = document.createElement('script');
            s.src = 'https://maps.googleapis.com/maps/api/js?libraries=places&key=' + encodeURIComponent(apiKey);
            s.async = true;
            s.defer = true;
            s.onload = function () { resolve(); };
            s.onerror = function (e) { reject(e); };
            document.head.appendChild(s);
        });
        return mapsLoader;
    }

    function extractAddress(place) {
        var comps = place.address_components || [];
        function get(type) {
            for (var i = 0; i < comps.length; i++) {
                if (comps[i].types.indexOf(type) !== -1) return comps[i];
            }
            return null;
        }
        var streetNumber = (get('street_number') || {}).long_name || '';
        var route = (get('route') || {}).long_name || '';
        var city = (get('locality') || get('postal_town') || get('administrative_area_level_2') || {}).long_name || '';
        var postalCode = (get('postal_code') || {}).long_name || '';
        var countryComp = get('country');
        var countryCode = countryComp ? countryComp.short_name : '';
        var countryName = countryComp ? countryComp.long_name : '';
        if (!countryCode) return null;

        return {
            line1: [streetNumber, route].filter(Boolean).join(' ') || undefined,
            city: city || undefined,
            postalCode: postalCode || undefined,
            countryCode: countryCode,
            countryName: countryName,
            formattedAddress: place.formatted_address || undefined,
            placeId: place.place_id || undefined,
            latitude: place.geometry && place.geometry.location ? place.geometry.location.lat() : undefined,
            longitude: place.geometry && place.geometry.location ? place.geometry.location.lng() : undefined,
        };
    }

    angular.module('degoudseApp').directive('addressPicker',
        ['$timeout', function ($timeout) {
            return {
                restrict: 'E',
                scope: {
                    apiKey: '@',
                    initialAddress: '<',
                    onChange: '&',
                },
                template:
                    '<div>' +
                    '  <button type="button" ng-click="toggle()"' +
                    '          class="w-full bg-white rounded-2xl px-4 py-4 shadow-sm flex items-center justify-between text-left">' +
                    '    <span ng-class="address ? \'text-brand-purple font-semibold\' : \'text-gray-400\'">' +
                    '      {{ address.formattedAddress || "Enter address" }}' +
                    '    </span>' +
                    '    <svg class="text-brand-blue shrink-0" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">' +
                    '      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>' +
                    '      <circle cx="12" cy="10" r="3"/>' +
                    '    </svg>' +
                    '  </button>' +
                    '  <div ng-if="open" class="mt-1 bg-white rounded-2xl overflow-hidden shadow-sm">' +
                    '    <div class="px-4 py-3 border-b border-gray-100">' +
                    '      <input class="ap-search w-full outline-none text-sm text-gray-500 bg-transparent placeholder-gray-400"' +
                    '             type="text" placeholder="{{ address.formattedAddress || \'Search address…\' }}" />' +
                    '    </div>' +
                    '    <div class="ap-map" ng-if="address.latitude"></div>' +
                    '    <div ng-if="loadError" class="px-4 py-3 text-xs text-red-500">Failed to load maps</div>' +
                    '  </div>' +
                    '</div>',
                link: function (scope, element) {
                    scope.address = scope.initialAddress || null;
                    scope.open = false;
                    scope.loadError = false;
                    var autocomplete = null;

                    scope.$watch('initialAddress', function (a) {
                        if (a && !scope.address) scope.address = a;
                    });

                    scope.toggle = function () {
                        scope.open = !scope.open;
                        if (scope.open) attach();
                        else autocomplete = null;
                    };

                    function attach() {
                        if (!scope.apiKey) { scope.loadError = true; return; }
                        loadMaps(scope.apiKey).then(function () {
                            // $timeout to let the ng-if template render the input
                            $timeout(function () {
                                var input = element[0].querySelector('.ap-search');
                                if (!input || autocomplete) return;
                                autocomplete = new google.maps.places.Autocomplete(input, {
                                    fields: ['address_components', 'formatted_address', 'geometry', 'place_id'],
                                });
                                autocomplete.addListener('place_changed', function () {
                                    var place = autocomplete.getPlace();
                                    var addr = extractAddress(place);
                                    if (!addr) return;
                                    scope.$apply(function () {
                                        scope.address = addr;
                                        scope.onChange({ address: addr });
                                        scope.open = false;
                                        autocomplete = null;
                                    });
                                });
                                input.focus();
                                renderMap();
                            });
                        }).catch(function () {
                            scope.$apply(function () { scope.loadError = true; });
                        });
                    }

                    function renderMap() {
                        if (!scope.address || !scope.address.latitude) return;
                        $timeout(function () {
                            var mapEl = element[0].querySelector('.ap-map');
                            if (!mapEl) return;
                            mapEl.style.height = '200px';
                            var center = { lat: scope.address.latitude, lng: scope.address.longitude };
                            var map = new google.maps.Map(mapEl, {
                                center: center, zoom: 15,
                                disableDefaultUI: true, gestureHandling: 'none',
                            });
                            new google.maps.Marker({ position: center, map: map });
                        });
                    }
                },
            };
        }]);
})();
