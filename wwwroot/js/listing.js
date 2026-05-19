document.addEventListener("DOMContentLoaded", function () {
    var listingMapUtils = window.UniMap360ListingMapUtils;

    if (!window.handleListingImageError && listingMapUtils) {
        window.handleListingImageError = listingMapUtils.handleImageError;
    }

    var currentFilter = "all";
    var currentKeyword = "";
    var currentProvince = "all";
    var currentPage = 1;
    var pageSize = 12;
    var debounceTimer;

    initListingFilters();
    wireListingEvents();
    loadCards();

    function showSkeleton() {
        var gridContainer = document.getElementById("grid-items-list");
        gridContainer.innerHTML = `
            <div class="text-center py-5 w-100" style="grid-column: 1 / -1;">
                <div class="spinner-border" style="color: var(--color-primary) !important;" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <p class="mt-3 text-muted fw-bold">Đang tải bảng tin...</p>
            </div>
        `;

        var paginationContainer = document.getElementById("grid-pagination-container");
        if (paginationContainer) paginationContainer.innerHTML = "";
    }

    function renderEmptyState(isError) {
        var gridContainer = document.getElementById("grid-items-list");
        if (!gridContainer) return;

        if (isError) {
            gridContainer.innerHTML = `
                <div class="empty-state-container">
                    <i class="fas fa-tools empty-state-icon" style="color: var(--color-primary);"></i>
                    <h3 class="fw-bold" style="color: var(--color-primary);">Không tải được dữ liệu</h3>
                    <p class="text-muted mt-2">Hệ thống tạm thời chưa phản hồi. Vui lòng thử tải lại sau ít phút.</p>
                </div>
            `;
            return;
        }

        gridContainer.innerHTML = `
            <div class="empty-state-container">
                <i class="fas fa-box-open empty-state-icon"></i>
                <h3 class="fw-bold text-muted">Không tìm thấy dữ liệu</h3>
                <p class="text-muted mt-2">Hãy thử đổi từ khóa hoặc khu vực tìm kiếm khác.</p>
            </div>
        `;
    }

    function loadCards() {
        showSkeleton();

        var query = new URLSearchParams({
            page: currentPage,
            pagesize: pageSize,
            type: currentFilter,
            keyword: currentKeyword,
            province: currentProvince
        }).toString();

        fetch("/api/listings/cards?" + query)
            .then(function (response) {
                if (!response.ok) {
                    throw new Error("API chưa phản hồi");
                }
                return response.json();
            })
            .then(function (json) {
                var payload = (json && json.success === true && json.data !== undefined) ? json.data : json;
                var items = payload.items || [];
                if (items.length === 0) {
                    renderEmptyState(false);
                    return;
                }

                renderGridView(items);
                renderPagination(payload.page || currentPage, payload.totalPages || 1);
            })
            .catch(function () {
                setTimeout(function () {
                    renderEmptyState(true);
                }, 500);
            });
    }

    function renderGridView(items) {
        var gridContainer = document.getElementById("grid-items-list");
        if (!gridContainer) return;

        gridContainer.innerHTML = "";

        items.forEach(function (item, index) {
            var itemType = item.type;
            var title = window.escapeHtml(item.title);
            var address = window.escapeHtml(item.address);
            var priceLabel = window.escapeHtml(item.price || "Thỏa thuận");
            var fallbackImg = itemType === "room" ? "/images/fallback-room.svg" : "/images/fallback-job.svg";
            var imgUrl = window.escapeHtml(item.thumbnail || fallbackImg);
            var itemId = window.escapeHtml(item.id);
            var priceClass = itemType === "room" ? "price-room" : "price-job";

            var cardHTML = `
                <div class="item-card mini-card shadow-sm" data-id="${itemId}" 
                     data-detail-url="/Home/Detail?id=${itemId}&type=${itemType}" 
                     style="animation-delay: ${index * 50}ms; border: 1px solid var(--color-border); border-radius: var(--radius-md); overflow: hidden; background: #fff; transition: all var(--motion-base) var(--ease-cinematic);">
                    <div class="item-img-wrapper" style="aspect-ratio: 16/10; overflow: hidden; position: relative;">
                        <img src="${imgUrl}" alt="${title}" class="item-img w-100 h-100" style="object-fit: cover; transition: transform 0.6s var(--ease-cinematic);"
                             data-item-id="${itemId}" data-item-type="${itemType}" data-img-attempt="0" data-fallback="${fallbackImg}" loading="lazy">
                        <div class="position-absolute top-0 end-0 m-3">
                            <span class="badge ${itemType === 'room' ? 'bg-maroon' : 'bg-gold'} px-3 py-2 shadow-sm" style="border-radius: 8px; font-weight: 700; font-size: 11px; letter-spacing: 0.5px; text-transform: uppercase;">
                                ${itemType === 'room' ? 'Phòng Trọ' : 'Việc Làm'}
                            </span>
                        </div>
                    </div>
                    <div class="item-info p-4 d-flex flex-column" style="min-height: 160px;">
                        <h4 class="item-title fw-800 mb-2" style="font-size: 1.15rem; color: var(--color-primary); display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; height: 3rem; line-height: 1.3;">${title}</h4>
                        <p class="item-address text-muted mb-3 d-flex align-items-center" style="font-size: 0.85rem; font-weight: 500;">
                            <i class="fas fa-map-marker-alt me-2 text-accent"></i>
                            <span class="text-truncate">${address}</span>
                        </p>
                        <div class="mt-auto d-flex align-items-center justify-content-between">
                            <div class="item-price-tag ${priceClass} fw-900" style="font-size: 1.3rem;">${priceLabel}</div>
                            <div class="btn-view-detail" style="color: var(--color-primary); font-weight: 700; font-size: 0.9rem;">
                                Xem <i class="fas fa-arrow-right ms-1" style="font-size: 0.8rem;"></i>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            gridContainer.insertAdjacentHTML("beforeend", cardHTML);
        });

        var cards = gridContainer.querySelectorAll(".item-card[data-detail-url]");
        cards.forEach(function (card) {
            card.setAttribute("tabindex", "0");
            card.setAttribute("role", "link");
            card.setAttribute("aria-label", "Xem chi tiết tin đăng");
        });
    }

    function renderPagination(current, totalPages) {
        var paginationContainer = document.getElementById("grid-pagination-container");
        if (!paginationContainer) return;

        if (totalPages <= 1) {
            paginationContainer.innerHTML = "";
            return;
        }

        var html = '<div class="academic-pagination">';
        html += `<button class="page-btn" ${current === 1 ? "disabled" : ""} data-page="${current - 1}"><i class="fas fa-chevron-left"></i></button>`;

        var startPage = Math.max(1, current - 2);
        var endPage = Math.min(totalPages, current + 2);

        if (startPage > 1) {
            html += `<button class="page-btn" data-page="1">1</button>`;
            if (startPage > 2) html += `<button class="page-btn" disabled>...</button>`;
        }

        for (var i = startPage; i <= endPage; i++) {
            html += `<button class="page-btn ${i === current ? "active" : ""}" data-page="${i}">${i}</button>`;
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) html += `<button class="page-btn" disabled>...</button>`;
            html += `<button class="page-btn" data-page="${totalPages}">${totalPages}</button>`;
        }

        html += `<button class="page-btn" ${current === totalPages ? "disabled" : ""} data-page="${current + 1}"><i class="fas fa-chevron-right"></i></button>`;
        html += "</div>";

        paginationContainer.innerHTML = html;
    }

    function wireListingEvents() {
        var gridContainer = document.getElementById("grid-items-list");
        var paginationContainer = document.getElementById("grid-pagination-container");

        if (gridContainer) {
            gridContainer.addEventListener("click", function (event) {
                var card = event.target.closest(".item-card[data-detail-url]");
                if (!card) return;

                var url = card.getAttribute("data-detail-url");
                if (url) window.location.href = url;
            });

            gridContainer.addEventListener("keydown", function (event) {
                if (event.key !== "Enter" && event.key !== " ") return;
                var card = event.target.closest(".item-card[data-detail-url]");
                if (!card) return;
                event.preventDefault();
                var url = card.getAttribute("data-detail-url");
                if (url) window.location.href = url;
            });

            gridContainer.addEventListener(
                "error",
                function (event) {
                    var img = event.target;
                    if (!img || img.tagName !== "IMG") return;
                    if (window.handleListingImageError) {
                        window.handleListingImageError(img);
                    }
                },
                true
            );
        }

        if (paginationContainer) {
            paginationContainer.addEventListener("click", function (event) {
                var button = event.target.closest(".page-btn[data-page]");
                if (!button || button.disabled) return;

                var page = parseInt(button.getAttribute("data-page") || "0", 10);
                if (page <= 0 || Number.isNaN(page)) return;

                currentPage = page;
                loadCards();
            });
        }
    }

    function initListingFilters() {
        var gridSearchInput = document.getElementById("grid-search-input");
        var gridProvinceFilter = document.getElementById("grid-province-filter");
        var gridFilterChips = document.querySelectorAll(".filter-chips .chip");

        if (gridSearchInput) {
            gridSearchInput.addEventListener("input", function (e) {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(function () {
                    currentKeyword = e.target.value;
                    currentPage = 1;
                    loadCards();
                }, 500);
            });
        }

        if (gridProvinceFilter) {
            var provinces = [
                "An Giang", "Bà Rịa - Vũng Tàu", "Bạc Liêu", "Bắc Giang", "Bắc Kạn", "Bắc Ninh", "Bến Tre", "Bình Dương",
                "Bình Định", "Bình Phước", "Bình Thuận", "Cà Mau", "Cao Bằng", "Cần Thơ", "Đà Nẵng", "Đắk Lắk", "Đắk Nông",
                "Điện Biên", "Đồng Nai", "Đồng Tháp", "Gia Lai", "Hà Giang", "Hà Nam", "Hà Nội", "Hà Tĩnh", "Hải Dương",
                "Hải Phòng", "Hậu Giang", "Hòa Bình", "Hưng Yên", "Hồ Chí Minh", "Khánh Hòa", "Kiên Giang", "Kon Tum",
                "Lai Châu", "Lạng Sơn", "Lào Cai", "Lâm Đồng", "Long An", "Nam Định", "Nghệ An", "Ninh Bình", "Ninh Thuận",
                "Phú Thọ", "Phú Yên", "Quảng Bình", "Quảng Nam", "Quảng Ngãi", "Quảng Ninh", "Quảng Trị", "Sóc Trăng",
                "Sơn La", "Tây Ninh", "Thái Bình", "Thái Nguyên", "Thanh Hóa", "Thừa Thiên Huế", "Tiền Giang", "Trà Vinh",
                "Tuyên Quang", "Vĩnh Long", "Vĩnh Phúc", "Yên Bái"
            ];

            provinces.forEach(function (pr) {
                var opt = document.createElement("option");
                opt.value = pr.toLowerCase();
                opt.textContent = pr;
                gridProvinceFilter.appendChild(opt);
            });

            gridProvinceFilter.addEventListener("change", function (e) {
                currentProvince = e.target.value;
                currentPage = 1;
                loadCards();
            });
        }

        gridFilterChips.forEach(function (chip) {
            chip.addEventListener("click", function () {
                var chipType = this.getAttribute("data-type");
                if (this.classList.contains("chip-room")) chipType = "room";
                else if (this.classList.contains("chip-job")) chipType = "job";
                else chipType = "all";

                currentFilter = chipType;

                gridFilterChips.forEach(function (c) { c.classList.remove("active"); });
                this.classList.add("active");

                currentPage = 1;
                loadCards();
            });
        });
    }
});
