// map.js
// UniMap360 - Restored Rich Marker Labels

document.addEventListener('DOMContentLoaded', function () {
    var listingMapUtils = window.UniMap360ListingMapUtils;
    if (!window.handleMapImageError && listingMapUtils) {
        window.handleMapImageError = listingMapUtils.handleImageError;
    }

    // ==========================================
    // 1. KHỞI TẠO BẢN ĐỒ VÀ MÀNG CHE PHỦ (MASK)
    // ==========================================
    var map = L.map('map', {
        center: [16.047079, 108.206230], // Miền Trung VN
        zoom: 6,
        zoomControl: false // Sẽ add lại ở vị trí khác
    });

    // Add Zoom Control xuống dưới cùng bên phải
    L.control.zoom({ position: 'bottomright' }).addTo(map);

    // Dùng cùng style với mini map để đồng bộ cảm giác nhìn.
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        maxZoom: 19
    }).addTo(map);

    // ----------------------------------------------------
    // INVERTED GEOJSON MASKING (HIỆU ỨNG SPOTLIGHT VIỆT NAM)
    // ----------------------------------------------------
    fetch('/data/vietnam.json')
        .then(response => response.json())
        .then(data => {
            var worldBounds = [
                [-90, -180], [90, -180], [90, 180], [-90, 180], [-90, -180]
            ];
            var vnPolygons = L.GeoJSON.coordsToLatLngs(data.coordinates, 2);
            var maskCoords = [worldBounds];

            vnPolygons.forEach(function (polygon) {
                maskCoords.push(polygon[0]);
            });

            L.polygon(maskCoords, {
                stroke: false,
                fillColor: '#0f172a',
                fillOpacity: 0.35
            }).addTo(map);

            L.geoJSON(data, {
                style: {
                    color: '#6366f1',
                    weight: 1.5,
                    opacity: 0.8,
                    fillOpacity: 0
                }
            }).addTo(map);
        })
        .catch(err => console.log('Không tải được mask VN:', err));

    // ==========================================
    // 2. MARKER CLUSTERING BUBBLES
    // ==========================================
    var markerClusterGroup = L.markerClusterGroup({
        iconCreateFunction: function (cluster) {
            var childCount = cluster.getChildCount();
            var children = cluster.getAllChildMarkers();
            var hasRoom = false;
            var hasJob = false;

            children.forEach(function (marker) {
                if (marker.options.itemType === 'room') hasRoom = true;
                if (marker.options.itemType === 'job') hasJob = true;
            });

            var clusterClass = 'cluster-bubble';
            if (hasRoom && !hasJob) clusterClass += ' room';
            else if (!hasRoom && hasJob) clusterClass += ' job';
            else clusterClass += ' room';

            return new L.DivIcon({
                html: '<div><span>' + childCount + '</span></div>',
                className: clusterClass,
                iconSize: new L.Point(40, 40)
            });
        },
        disableClusteringAtZoom: 16, /* Khi zoom tới 16 trở lên, ngưng gom thành nhóm vòng tròn */
        spiderfyOnMaxZoom: false, /* Tắt bung hoa */
        showCoverageOnHover: false,
        zoomToBoundsOnClick: true,
        chunkedLoading: true // GIÚP MOBILE KHÔNG BỊ TREO KHI VẼ NHIỀU MARKER
    });

    var markersMap = {};

    // ==========================================
    // 3. TẢI DỮ LIỆU TỪ API & RENDER
    // ==========================================
    fetch('/api/feed')
        .then(response => response.json())
        .then(json => {
            var items = (json && json.success === true && json.data !== undefined) ? json.data : json;
            renderItems(items);
            initSearchAndFilters(items);
            
            // Khôi phục bộ lọc AI nếu có
            var savedFilter = sessionStorage.getItem('ai_proximity_filter');
            if (savedFilter) {
                try {
                    var f = JSON.parse(savedFilter);
                    if (f.lat && f.lng && f.radiusKm && f.serviceType) {
                        setTimeout(() => {
                            if (window.UniMap360Map && typeof window.UniMap360Map.applyProximityFilterOnMap === 'function') {
                                window.UniMap360Map.applyProximityFilterOnMap(f.lat, f.lng, f.radiusKm, f.serviceType);
                            }
                        }, 500); // Đợi Leaflet và DOM ổn định
                    }
                } catch(e) {}
            }
        })
        .catch(error => {
            console.error('Error fetching feed data:', error);
            document.getElementById('items-list').innerHTML = '<div class="alert alert-danger" style="margin:20px;">Không thể tải dữ liệu. Xin thử lại sau.</div>';
        });

    // ==========================================
    // 4. RENDER UI CARDS & MARKERS (WITH FULL INFO LABELS)
    // ==========================================
    function renderItems(items) {
        var listContainer = document.getElementById('items-list');
        listContainer.innerHTML = '';
        markerClusterGroup.clearLayers();
        markersMap = {};

        if (items.length === 0) {
            listContainer.innerHTML = '<div class="text-center text-muted mt-4">Không tìm thấy kết quả nào.</div>';
            return;
        }

        items.forEach((item, index) => {
            var itemType = item.type;
            var title = window.escapeHtml(item.title);
            var address = window.escapeHtml(item.address);
            var priceLabel = window.escapeHtml(item.price || 'Thỏa thuận');
            var fallbackImg = itemType === 'room' ? '/images/fallback-room.svg' : '/images/fallback-job.svg';
            var imgUrl = window.escapeHtml(item.thumbnail || fallbackImg);
            var itemId = window.escapeHtml(item.id);

            // Icon Fa
            var iconClassFa = itemType === 'room' ? 'fa-home' : 'fa-briefcase';

            // Giá trị ngắn gọn cho cục Pin chính
            var shortPriceRaw = itemType === 'room' && item.priceStr >= 1000000
                ? (item.priceStr / 1000000).toFixed(1) + ' Tr'
                : (itemType === 'job' ? '💼' : 'Thỏa thuận');
            var shortPrice = window.escapeHtml(shortPriceRaw);

            var pinClass = itemType === 'room' ? 'room-pin' : 'job-pin';
            var priceClass = itemType === 'room' ? 'price-room' : 'price-job';
            
            var uniqueId = itemType + '_' + itemId;

            // --- TẠO CARD BÊN BẢNG ĐIỀU KHIỂN ---
            var cardHTML = `
                <div class="item-card" data-id="${uniqueId}">
                    <div class="item-img-wrapper">
                        <img src="${imgUrl}" alt="${title}" class="item-img"
                             data-item-id="${itemId}" data-item-type="${itemType}" data-img-attempt="0" data-fallback="${fallbackImg}"
                             onerror="window.handleMapImageError(this)" loading="lazy">
                    </div>
                    <div class="item-info">
                        <h4 class="item-title">${title}</h4>
                        <p class="item-address">${address}</p>
                        <div class="item-price-tag ${priceClass}">${priceLabel}</div>
                    </div>
                </div>
            `;
            listContainer.insertAdjacentHTML('beforeend', cardHTML);

            // --- TẠO MARKER TRÊN BẢN ĐỒ ---
            if (item.lat && item.lng) {
                // Khôi phục lại Label đầy đủ thông tin nằm cạnh Marker!
                var customIcon = L.divIcon({
                    html: `
                        <div class="custom-marker-wrapper">
                            <div class="marker-pin-card ${pinClass}">
                                <div class="pin-icon"><i class="fas ${iconClassFa}"></i></div>
                                <div class="pin-title" title="${title}">${title}</div>
                                <div class="pin-price">${priceLabel}</div>
                            </div>
                        </div>
                    `,
                    className: 'custom-marker-container-clear', // Dùng class rỗng để tránh Leaflet override
                    iconSize: null, // Đặt null để div co giãn tự nhiên theo nội dung
                    iconAnchor: [24, 30] // Anchor vị trí mũi nhọn marker
                });

                // Xử lý Jitter cố định: Lệch 10-30m nhưng không bị 'nhảy múa' khi tìm kiếm
                // Dùng hàm lượng giác với itemId để tạo một "random" cố định
                var seed = parseInt((itemId || '').toString().replace(/\D/g, '') || index) || index;
                var pseudoRandomLat = (Math.sin(seed * 12.9898) * 43758.5453) % 1;
                var pseudoRandomLng = (Math.cos(seed * 78.233) * 43758.5453) % 1;

                var jitterLat = (pseudoRandomLat - 0.5) * 0.0015;
                var jitterLng = (pseudoRandomLng - 0.5) * 0.0015;

                var finalLat = item.lat + jitterLat;
                var finalLng = item.lng + jitterLng;

                var marker = L.marker([finalLat, finalLng], {
                    icon: customIcon,
                    itemType: itemType,
                    itemId: itemId
                });

                // Tooltip hiện ra khi Hover vào Marker cho chi tiết Ảnh
                var tooltipHTML = `
                    <div class="text-center" style="width: 200px;">
                        <img src="${imgUrl}" style="width: 100%; height: 120px; object-fit: cover; border-radius: 8px; margin-bottom: 8px;"
                             data-item-id="${itemId}" data-item-type="${itemType}" data-img-attempt="0" data-fallback="${fallbackImg}"
                             onerror="window.handleMapImageError(this)" loading="lazy">
                        <div style="font-size: 14px; font-weight: bold; display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; white-space: normal; text-align: left;">${title}</div>
                        <div class="${priceClass}" style="font-weight: 800; font-size: 15px;">${priceLabel}</div>
                    </div>
                `;
                marker.bindTooltip(tooltipHTML, {
                    className: 'leaflet-tooltip-custom',
                    direction: 'top',
                    offset: [0, -40]
                });

                // --- TÍNH NĂNG MỚI: BẤM LÀ BAY ---
                marker.on('click', function () {
                    window.location.href = `/Home/Detail?id=${itemId}&type=${itemType}`;
                });

                markerClusterGroup.addLayer(marker);
                markersMap[uniqueId] = marker;
            }
        });

        map.addLayer(markerClusterGroup);
        initHoverEffects();
    }

    // ==========================================
    // 5. TƯƠNG TÁC HOVER & LỌC
    // ==========================================
    function initHoverEffects() {
        var cards = document.querySelectorAll('.item-card');
        cards.forEach(card => {
            card.addEventListener('mouseenter', function () {
                var id = this.getAttribute('data-id');
                var marker = markersMap[id];
                if (marker) {
                    var el = marker.getElement();
                    if (el) {
                        var wrapper = el.querySelector('.custom-marker-wrapper');
                        if (wrapper) {
                            wrapper.classList.add('hovered');
                        }
                    }
                    marker.openTooltip();
                }
            });

            card.addEventListener('mouseleave', function () {
                var id = this.getAttribute('data-id');
                var marker = markersMap[id];
                if (marker) {
                    var el = marker.getElement();
                    if (el) {
                        var wrapper = el.querySelector('.custom-marker-wrapper');
                        if (wrapper) {
                            wrapper.classList.remove('hovered');
                        }
                    }
                    marker.closeTooltip();
                }
            });

            card.addEventListener('click', function () {
                var id = this.getAttribute('data-id');
                var marker = markersMap[id];
                if (marker) {
                    var point = marker.getLatLng();
                    map.flyTo(point, 16, { animate: true, duration: 1.5 });

                    // Thu nhỏ Panel trên Mobile để người dùng thấy bản đồ "bay" đến
                    if (window.innerWidth < 768) {
                        var panel = document.getElementById('floating-panel');
                        var icon = document.getElementById('panel-toggle-icon');
                        if (panel) {
                            panel.classList.remove('expanded');
                        }
                        if (icon) {
                            icon.classList.replace('fa-chevron-down', 'fa-chevron-up');
                        }
                    }
                }
            });
        });
    }

    function initSearchAndFilters(allItems) {
        var searchInput = document.querySelector('.search-input');
        var filterChips = document.querySelectorAll('.chip');
        var currentFilter = 'all';

        function filterData() {
            var keyword = searchInput.value.toLowerCase().trim();
            var filtered = allItems.filter(item => {
                var itemType = item.type;
                var matchFilter = (currentFilter === 'all') || (currentFilter === itemType);

                var title = itemType === 'room' ? (item.title || '') : (item.jobTitle || '');
                var address = itemType === 'room' ? (item.address || '') : (item.companyName || '');
                var matchSearch = title.toLowerCase().includes(keyword) || address.toLowerCase().includes(keyword);

                return matchFilter && matchSearch;
            });
            renderItems(filtered);
        }

        searchInput.addEventListener('input', filterData);

        filterChips.forEach(chip => {
            chip.addEventListener('click', function () {
                filterChips.forEach(c => c.classList.remove('active'));
                this.classList.add('active');

                if (this.classList.contains('chip-room')) currentFilter = 'room';
                else if (this.classList.contains('chip-job')) currentFilter = 'job';
                else currentFilter = 'all';

                filterData();
            });
        });
    }

    // ==========================================
    // 6. TRỢ LÝ AI & ĐỒNG BỘ PROXIMITY SPOTLIGHT
    // ==========================================
    var userLocationMarker = null;
    var proximityCircle = null;

    window.UniMap360Map = {
        drawUserLocationOnMap: function (lat, lng) {
            if (userLocationMarker) {
                map.removeLayer(userLocationMarker);
            }

            var pulseIcon = L.divIcon({
                html: `
                    <div class="user-pulse-container">
                        <div class="user-pulse-ring"></div>
                        <div class="user-pulse-ring"></div>
                        <div class="user-pulse-ring"></div>
                        <div class="user-pulse-dot"></div>
                    </div>
                `,
                className: 'user-pulse-icon-container',
                iconSize: [20, 20],
                iconAnchor: [10, 10]
            });

            userLocationMarker = L.marker([lat, lng], { icon: pulseIcon }).addTo(map);
            map.flyTo([lat, lng], 15, { animate: true, duration: 1.5 });
        },

        applyProximityFilterOnMap: function (lat, lng, radiusKm, serviceType) {
            // Lưu state để F5 không bị mất
            sessionStorage.setItem('ai_proximity_filter', JSON.stringify({
                lat: lat,
                lng: lng,
                radiusKm: radiusKm,
                serviceType: serviceType
            }));



            // 1. Vẽ vòng tròn bán kính spotlight đỏ đô
            if (proximityCircle) {
                map.removeLayer(proximityCircle);
            }

            proximityCircle = L.circle([lat, lng], {
                radius: radiusKm * 1000,
                color: '#780115',
                fillColor: '#780115',
                fillOpacity: 0.12,
                weight: 2,
                dashArray: '6, 6'
            }).addTo(map);

            map.flyToBounds(proximityCircle.getBounds(), { padding: [40, 40], animate: true, duration: 1.5 });

            // 2. Lọc marker và card sidebar dựa theo khoảng cách Haversine
            function getHaversineDistance(lat1, lon1, lat2, lon2) {
                var R = 6371; // km
                var dLat = (lat2 - lat1) * Math.PI / 180;
                var dLon = (lon2 - lon1) * Math.PI / 180;
                var a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                        Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
                        Math.sin(dLon / 2) * Math.sin(dLon / 2);
                var c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
                return R * c;
            }

            var cards = document.querySelectorAll('.item-card');
            document.querySelectorAll('.distance-badge').forEach(el => el.remove());
            var emptyMsg = document.querySelector('.proximity-empty-msg');
            if (emptyMsg) emptyMsg.remove();

            var itemsInside = [];
            var itemsOutside = [];
            var markersToKeep = [];

            cards.forEach(card => {
                var id = card.getAttribute('data-id');
                var marker = markersMap[id];
                if (!marker) return;

                var markerLatLng = marker.getLatLng();
                var dist = getHaversineDistance(lat, lng, markerLatLng.lat, markerLatLng.lng);
                var markerType = marker.options.itemType;
                var matchType = (serviceType === 'both') || (serviceType === 'room' && markerType === 'room') || (serviceType === 'job' && markerType === 'job');
                var isInside = (dist <= radiusKm) && matchType;

                if (isInside) {
                    itemsInside.push({ card: card, marker: marker, distance: dist });
                    marker.setOpacity(1);
                    markersToKeep.push(marker);
                } else {
                    itemsOutside.push({ card: card, marker: marker });
                    if (matchType) {
                        marker.setOpacity(0.4);
                        markersToKeep.push(marker);
                    }
                }
            });

            // Xóa toàn bộ marker hiện tại và chỉ thêm lại những marker đúng loại dịch vụ
            markerClusterGroup.clearLayers();
            markerClusterGroup.addLayers(markersToKeep);

            itemsOutside.forEach(item => {
                item.card.style.display = 'none';
            });

            // Sắp xếp các card bên trong theo khoảng cách tăng dần và hiển thị Badge
            itemsInside.sort((a, b) => a.distance - b.distance);

            var listContainer = document.getElementById('items-list');
            itemsInside.forEach(item => {
                item.card.style.display = 'flex';

                var infoPanel = item.card.querySelector('.item-info');
                if (infoPanel) {
                    var distStr = item.distance >= 1 
                        ? item.distance.toFixed(1) + ' km' 
                        : Math.round(item.distance * 1000) + ' m';
                    
                    var badge = document.createElement('div');
                    badge.className = 'distance-badge';
                    badge.innerHTML = `<i class="fas fa-route"></i> Cách bạn ${distStr}`;
                    infoPanel.appendChild(badge);
                }

                listContainer.appendChild(item.card);
            });

            if (itemsInside.length === 0) {
                var emptyMsgEl = document.createElement('div');
                emptyMsgEl.className = 'text-center text-muted mt-4 proximity-empty-msg';
                emptyMsgEl.style.padding = '20px';
                emptyMsgEl.innerText = 'Không có địa điểm nào trong bán kính spotlight này.';
                listContainer.appendChild(emptyMsgEl);
            }
        },

        clearProximityFilter: function () {
            // Xóa session storage
            sessionStorage.removeItem('ai_proximity_filter');

            if (proximityCircle) {
                map.removeLayer(proximityCircle);
                proximityCircle = null;
            }
            if (userLocationMarker) {
                map.removeLayer(userLocationMarker);
                userLocationMarker = null;
            }
            


            // Khôi phục tất cả markers về mặc định
            markerClusterGroup.clearLayers();
            var allMarkers = [];
            Object.values(markersMap).forEach(marker => {
                marker.setOpacity(1);
                allMarkers.push(marker);
            });
            markerClusterGroup.addLayers(allMarkers);
            
            // Khôi phục tất cả cards
            var cards = document.querySelectorAll('.item-card');
            cards.forEach(card => {
                card.style.display = 'flex';
                // Xóa distance badge
                var badge = card.querySelector('.distance-badge');
                if (badge) badge.remove();
            });

            // Xóa câu báo trống (nếu có)
            var emptyMsg = document.querySelector('.proximity-empty-msg');
            if (emptyMsg) emptyMsg.remove();
            
            // Reset map view về VN
            map.flyTo([16.047079, 108.206230], 6, { animate: true, duration: 1.5 });
        }
    };
});
