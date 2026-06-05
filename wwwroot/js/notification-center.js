(function () {
    function createNotificationCenter(options) {
        const apiClient = options && options.apiClient ? options.apiClient : null;
        const getToken = options && options.getToken ? options.getToken : function () { return null; };

        let pollTimer = null;

        function resetUi() {
            document.querySelectorAll(".nav-notification-badge").forEach(badge => {
                badge.textContent = "0";
                badge.classList.add("d-none");
            });
            document.querySelectorAll(".nav-notification-menu").forEach(menu => {
                menu.innerHTML = `
                    <li class="px-4 py-3 d-flex justify-content-between align-items-center bg-white border-bottom sticky-top rounded-top-4">
                        <strong class="mb-0 fs-6 text-dark fw-800">Thông báo</strong>
                        <div class="d-flex gap-3">
                            <button type="button" class="mark-all-read-btn btn btn-sm btn-link text-decoration-none p-0 fw-bold text-primary" style="font-size: 0.75rem;">Đánh dấu đã đọc</button>
                            <button type="button" class="delete-all-notifications-btn btn btn-sm btn-link text-decoration-none p-0 fw-bold text-maroon" style="font-size: 0.75rem;">Xóa hết</button>
                        </div>
                    </li>
                    <div class="notif-scroll-area" style="max-height: 420px; overflow-y: auto; scrollbar-width: thin;">
                        <li class="nav-notification-empty px-4 py-5 text-center text-muted">
                            <div class="mb-3 opacity-20"><i class="fas fa-bell-slash fa-3x"></i></div>
                            <p class="mb-0 fw-600">Bạn chưa có thông báo nào mới.</p>
                        </li>
                        <div class="nav-notification-list bg-white"></div>
                    </div>
                    <li class="text-center py-2 bg-light border-top">
                        <small class="text-muted fw-bold" style="font-size: 0.65rem; text-transform: uppercase; letter-spacing: 1px;">UniMap360 Notifications</small>
                    </li>
                `;
            });
        }

        function stopPolling() {
            if (pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }

        function formatTime(value) {
            if (!value) return "";
            const d = new Date(value);
            if (Number.isNaN(d.getTime())) return "";
            return d.toLocaleString("vi-VN");
        }

        function buildLink(item) {
            const targetType = (item.targetType || "").toLowerCase();
            const targetId = item.targetId;
            if (!targetId || (targetType !== "room" && targetType !== "job")) return "#";
            return `/Home/Detail?id=${targetId}&type=${targetType}`;
        }

        function updateBadges(unreadCount) {
            document.querySelectorAll(".nav-notification-badge").forEach(badge => {
                if (unreadCount > 0) {
                    badge.classList.remove("d-none");
                    badge.textContent = unreadCount > 99 ? "99+" : String(unreadCount);
                } else {
                    badge.classList.add("d-none");
                    badge.textContent = "0";
                }
            });
        }

        function render(items, unreadCount) {
            updateBadges(unreadCount);

            // Update all lists
            document.querySelectorAll(".nav-notification-menu").forEach(menu => {
                const list = menu.querySelector(".nav-notification-list");
                const empty = menu.querySelector(".nav-notification-empty");
                if (!list || !empty) return;

                list.innerHTML = "";
                if (!items || items.length === 0) {
                    empty.classList.remove("d-none");
                    return;
                }

                empty.classList.add("d-none");
                items.forEach(item => {
                    const isUnread = !item.isRead;
                    const row = document.createElement("li");
                    row.className = "notification-row-wrap";
                    row.innerHTML = `
                        <div class="notification-row p-3 ${isUnread ? "unread" : ""}">
                            <div class="d-flex justify-content-between align-items-start gap-3">
                                <a class="text-decoration-none text-reset flex-grow-1" href="${buildLink(item)}" data-notification-id="${item.notificationId}" data-notification-action="open">
                                    <div class="notif-time mb-1">${window.escapeHtml(formatTime(item.createdAt))}</div>
                                    <div class="notif-title fw-600 mb-1" style="font-size: 0.95rem; line-height: 1.3;">${window.escapeHtml(item.title || "Thông báo")}</div>
                                    <div class="notif-msg small text-muted" style="line-height: 1.4;">${window.escapeHtml(item.message || "")}</div>
                                </a>
                                <button type="button" class="btn btn-sm btn-light text-maroon rounded-circle shadow-sm p-0 d-flex align-items-center justify-content-center" style="width: 32px; height: 32px; flex-shrink: 0;" data-notification-id="${item.notificationId}" data-notification-action="delete" title="Xóa thông báo">
                                    <i class="fas fa-trash-alt" style="font-size: 0.8rem;"></i>
                                </button>
                            </div>
                        </div>
                    `;
                    list.appendChild(row);
                });
            });
        }

        async function fetchNotifications(token) {
            if (!token) return;
            try {
                const requestResult = apiClient
                    ? await apiClient.requestJson("/api/notifications?limit=10", {
                        method: "GET"
                    })
                    : null;
                const response = requestResult ? requestResult.response : await fetch("/api/notifications?limit=10", {
                    method: "GET"
                });
                if (response.status === 401 || response.status === 403) return;
                if (!response.ok) return;
                const raw = requestResult ? requestResult.payload : await response.json();
                const payload = (raw && raw.success === true && raw.data !== undefined) ? raw.data : raw;
                render(payload.items || [], payload.unreadCount || 0);
            } catch {
                // retry on next tick
            }
        }

        function startPolling(token) {
            stopPolling();
            if (!token) return;
            fetchNotifications(token);
            
            // Dùng polling chậm (60s) nếu SignalR đang kết nối tốt, ngược lại dùng polling nhanh (15s)
            const isSignalRConnected = window.UniMap360RealtimeClient && window.UniMap360RealtimeClient.isConnected();
            const interval = isSignalRConnected ? 60000 : 15000;
            pollTimer = setInterval(() => fetchNotifications(token), interval);
        }

        function registerRealtimeListeners() {
            window.addEventListener("unimap360:realtime:notification", function (event) {
                const token = getToken();
                if (token) {
                    fetchNotifications(token);
                }
            });

            window.addEventListener("unimap360:realtime:notif-unread", function (event) {
                const payload = event.detail;
                if (payload && typeof payload.unreadCount !== "undefined") {
                    updateBadges(payload.unreadCount);
                }
            });

            window.addEventListener("unimap360:realtime:connected", function () {
                const token = getToken();
                if (token) {
                    console.log("NotificationCenter: SignalR connected, resyncing and switching to slow polling.");
                    startPolling(token);
                }
            });

            window.addEventListener("unimap360:realtime:reconnected", function () {
                const token = getToken();
                if (token) {
                    console.log("NotificationCenter: SignalR reconnected, resyncing and switching to slow polling.");
                    startPolling(token);
                }
            });

            window.addEventListener("unimap360:realtime:closed", function () {
                const token = getToken();
                if (token) {
                    console.log("NotificationCenter: SignalR disconnected, reverting to fast polling fallback.");
                    startPolling(token);
                }
            });
        }

        function wireEvents() {
            registerRealtimeListeners();
            document.querySelectorAll(".nav-notification-menu").forEach(menu => {
                menu.addEventListener("click", async function (event) {
                    const actionNode = event.target.closest("[data-notification-action]");
                    const markAllBtn = event.target.closest(".mark-all-read-btn");
                    const deleteAllBtn = event.target.closest(".delete-all-notifications-btn");
                    const token = getToken();
                    if (!token) return;

                    if (actionNode) {
                        const action = actionNode.getAttribute("data-notification-action");
                        const id = actionNode.getAttribute("data-notification-id");
                        if (!id) return;

                        if (action === "delete") {
                            event.preventDefault();
                            event.stopPropagation();
                            if (!window.confirm("Bạn có chắc muốn xóa thông báo này không?")) return;
                            try {
                                await fetch(`/api/notifications/${id}`, {
                                    method: "DELETE"
                                });
                            } finally {
                                fetchNotifications(token);
                            }
                        } else if (action === "open") {
                            try {
                                await fetch(`/api/notifications/${id}/read`, {
                                    method: "POST"
                                });
                            } catch {}
                        }
                    }

                    if (markAllBtn) {
                        try {
                            await fetch("/api/notifications/read-all", {
                                method: "POST"
                            });
                        } finally {
                            fetchNotifications(token);
                        }
                    }

                    if (deleteAllBtn) {
                        if (!window.confirm("Bạn có chắc muốn xóa toàn bộ thông báo không?")) return;
                        try {
                            await fetch("/api/notifications", {
                                method: "DELETE"
                            });
                        } finally {
                            fetchNotifications(token);
                        }
                    }
                });
            });
        }

        return {
            resetUi,
            wireEvents,
            startPolling,
            stopPolling
        };
    }

    window.UniMap360NotificationCenter = { createNotificationCenter };
})();
