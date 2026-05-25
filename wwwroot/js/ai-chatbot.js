/* wwwroot/js/ai-chatbot.js */
/* UniMap360 - Premium Geolocation & Frosted Glass AI Chatbot Logic */

(function () {
    let chatState = {
        state: "waiting_location", // waiting_location, waiting_service, waiting_radius, ready
        userLat: null,
        userLng: null,
        selectedService: null,
        selectedRadius: null
    };

    // DOM Elements
    let launcherBtn, chatPanel, closeBtn, msgContainer, quickChipsContainer, chatInput, sendBtn;
    let chatbotInitialized = false;

    document.addEventListener("DOMContentLoaded", () => {
        initDOMElements();
        if (!launcherBtn || !chatPanel) return;

        applyAuthVisibility();

        window.addEventListener("unimap360:auth-changed", () => {
            applyAuthVisibility();
        });
    });

    function getCurrentAccount() {
        return window.UniMap360AuthStore?.getStoredAccount?.() || null;
    }

    function canUseAiChat() {
        const account = getCurrentAccount();
        if (!account) return false;
        return String(account.role || "").toLowerCase() === "student";
    }

    function ensureChatbotBootstrapped() {
        if (chatbotInitialized) return;
        checkAndHarvestGeolocation();
        registerEvents();
        renderInitialWelcome();
        chatbotInitialized = true;
    }

    function applyAuthVisibility() {
        const allowed = canUseAiChat();
        if (launcherBtn) launcherBtn.style.display = allowed ? "flex" : "none";
        if (!allowed && chatPanel) {
            chatPanel.classList.remove("open");
        }
        if (allowed) {
            ensureChatbotBootstrapped();
        }
    }

    function initDOMElements() {
        launcherBtn = document.getElementById("ai-chat-launcher");
        chatPanel = document.getElementById("ai-chat-panel");
        closeBtn = document.getElementById("ai-chat-close-btn");
        msgContainer = document.getElementById("ai-chat-messages");
        quickChipsContainer = document.getElementById("ai-quick-chips");
        chatInput = document.getElementById("ai-chat-input");
        sendBtn = document.getElementById("ai-chat-send-btn");
    }

    // 1. TỰ ĐỘNG THU THẬP TỌA ĐỘ KHI TRUY CẬP TRANG WEB
    function checkAndHarvestGeolocation() {
        // Tự động yêu cầu định vị trình duyệt ngay khi tải trang, không quan tâm vai trò hay trạng thái đăng nhập
        requestBrowserLocation(false); // Âm thầm lấy tọa độ và vẽ pulse marker
    }

    function requestBrowserLocation(openPanelOnSuccess = false) {
        if (!navigator.geolocation) {
            console.warn("Trình duyệt không hỗ trợ Geolocation API.");
            return;
        }

        navigator.geolocation.getCurrentPosition(
            (position) => {
                const lat = position.coords.latitude;
                const lng = position.coords.longitude;

                // Lưu vào localStorage
                localStorage.setItem("unimap360.userCoords", JSON.stringify({ lat, lng, timestamp: Date.now() }));

                chatState.userLat = lat;
                chatState.userLng = lng;

                if (chatState.state === "waiting_location") {
                    chatState.state = "waiting_service";

                    // Nếu ô chat đang hiển thị lời chào chờ vị trí, cập nhật ngay lập tức sang trạng thái đã nhận vị trí
                    if (msgContainer && msgContainer.children.length <= 1) {
                        msgContainer.innerHTML = "";
                        addMessage("🤖 Vị trí của bạn đã được xác định thành công bằng định vị vệ tinh GPS! 📍\n\nBạn muốn tìm **Phòng Trọ**, **Việc Làm** hay **Cả Hai** quanh khu vực của bạn?");
                        renderQuickChips();
                    }
                }

                // Vẽ dấu chấm đỏ và làn sóng lan tỏa trên Leaflet Map
                if (window.UniMap360Map && typeof window.UniMap360Map.drawUserLocationOnMap === "function") {
                    window.UniMap360Map.drawUserLocationOnMap(lat, lng);
                }

                if (openPanelOnSuccess) {
                    togglePanel(true);
                    renderInitialWelcome();
                }
            },
            (error) => {
                console.warn("Người dùng từ chối hoặc lỗi định vị:", error.message);
                // Xóa cache cũ nếu có để tránh sai lệch
                localStorage.removeItem("unimap360.userCoords");
            },
            { enableHighAccuracy: true, timeout: 10000 }
        );
    }

    // 2. SỰ KIỆN TƯƠNG TÁC GIAO DIỆN
    function registerEvents() {
        if (launcherBtn) {
            launcherBtn.addEventListener("click", () => {
                const isOpen = chatPanel.classList.contains("open");
                togglePanel(!isOpen);
            });
        }

        if (closeBtn) {
            closeBtn.addEventListener("click", () => {
                togglePanel(false);
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener("click", () => {
                handleUserMessageSubmit();
            });
        }

        if (chatInput) {
            chatInput.addEventListener("keydown", (e) => {
                if (e.key === "Enter") {
                    e.preventDefault();
                    handleUserMessageSubmit();
                }
            });
        }
    }

    function togglePanel(open) {
        if (open) {
            chatPanel.classList.add("open");

            // Tự động đóng panel hỗ trợ nếu đang mở để tránh chồng chéo
            if (window.UniMap360ChatWidget && typeof window.UniMap360ChatWidget.closePanel === "function") {
                window.UniMap360ChatWidget.closePanel();
            } else {
                const supportPanel = document.getElementById("um-chat-panel");
                if (supportPanel && supportPanel.classList.contains("open")) {
                    supportPanel.classList.remove("open");
                }
            }

            // Nếu click mở panel nhưng chưa có tọa độ trong state, thử load từ localStorage
            if (chatState.userLat === null) {
                const cached = localStorage.getItem("unimap360.userCoords");
                if (cached) {
                    try {
                        const { lat, lng } = JSON.parse(cached);
                        chatState.userLat = lat;
                        chatState.userLng = lng;
                        chatState.state = "waiting_service";

                        if (window.UniMap360Map && typeof window.UniMap360Map.drawUserLocationOnMap === "function") {
                            window.UniMap360Map.drawUserLocationOnMap(lat, lng);
                        }
                    } catch (e) {
                        localStorage.removeItem("unimap360.userCoords");
                    }
                }
            }
            renderInitialWelcome();
        } else {
            chatPanel.classList.remove("open");
        }
    }

    // 3. RENDER TIN NHẮN CHAT & CHIPS GỢI Ý NHANH
    function addMessage(text, sender = "ai") {
        if (!msgContainer) return;

        const bubble = document.createElement("div");
        bubble.className = `ai-chat-bubble ${sender}`;

        // Chống lỗ hổng XSS bằng cách escape HTML trước khi định dạng Markdown
        const escapedText = (window.escapeHtml && typeof window.escapeHtml === "function") 
            ? window.escapeHtml(text) 
            : text;

        const formattedText = escapedText
            .replace(/\n/g, "<br/>")
            .replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>")
            .replace(/\*(.*?)\*/g, "<em>$1</em>")
            .replace(/`([^`]+)`/g, "<code>$1</code>");

        bubble.innerHTML = `
            <div class="ai-chat-bubble-content">${formattedText}</div>
            <div class="ai-chat-bubble-time">${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</div>
        `;

        msgContainer.appendChild(bubble);
        msgContainer.scrollTop = msgContainer.scrollHeight;
    }

    function showTypingIndicator() {
        if (!msgContainer) return;
        const indicator = document.createElement("div");
        indicator.className = "ai-chat-bubble ai typing-indicator-bubble";
        indicator.id = "ai-chat-typing-indicator";
        indicator.innerHTML = `
            <div style="display: flex; gap: 4px; align-items: center; min-height: 20px;">
                <span style="font-style: italic; color: rgba(255,255,255,0.6)">Trợ lý đang phân tích...</span>
            </div>
        `;
        msgContainer.appendChild(indicator);
        msgContainer.scrollTop = msgContainer.scrollHeight;
    }

    function removeTypingIndicator() {
        const indicator = document.getElementById("ai-chat-typing-indicator");
        if (indicator) {
            indicator.remove();
        }
    }

    function renderInitialWelcome() {
        if (!msgContainer) return;

        // Chỉ vẽ chào mừng ban đầu nếu ô chat trống rỗng
        if (msgContainer.children.length > 0) {
            renderQuickChips();
            return;
        }

        if (chatState.userLat && chatState.userLng) {
            chatState.state = "waiting_service";
            addMessage("🤖 Xin chào! Vị trí của bạn đã được xác định thành công bằng tín hiệu định vị vệ tinh GPS.\n\nBạn muốn tìm **Phòng Trọ**, **Việc Làm** hay **Cả Hai** quanh khu vực của bạn?");
        } else {
            chatState.state = "waiting_location";
            addMessage("🤖 Xin chào! Để tôi giúp bạn quét tìm phòng trọ và việc làm gần đây nhé.\n\nTrước tiên, **bạn đang ở đâu?** Hãy chọn các gợi ý nhanh bên dưới hoặc nhập địa chỉ/tên trường cụ thể của bạn (ví dụ: *Đại học Đồng Nai*, *Làng Đại Học Thủ Đức*...).");
        }
        renderQuickChips();
    }

    function renderQuickChips() {
        if (!quickChipsContainer) return;
        quickChipsContainer.innerHTML = "";

        let chips = [];
        if (chatState.state === "waiting_location") {
            chips = [
                { label: "📍 Chia sẻ Vị trí GPS", action: () => requestBrowserLocation(true) },
                { label: "🏫 Đại học Đồng Nai", text: "Đại học Đồng Nai" },
                { label: "🏫 Đại học Lạc Hồng", text: "Đại học Lạc Hồng" },
                { label: "🏫 Làng Đại học Thủ Đức", text: "Làng Đại học Thủ Đức" }
            ];
        } else if (chatState.state === "waiting_service") {
            chips = [
                { label: "🏠 Phòng Trọ", text: "Phòng Trọ" },
                { label: "💼 Việc Làm", text: "Việc Làm" },
                { label: "✨ Cả Hai", text: "Cả Hai" },
                { label: "🔄 Đổi Vị Trí", text: "Đổi Vị Trí" }
            ];
        } else if (chatState.state === "waiting_radius") {
            chips = [
                { label: "⚡ 1 km", text: "1" },
                { label: "⚡ 2 km", text: "2" },
                { label: "⚡ 3 km", text: "3" },
                { label: "⚡ 5 km", text: "5" },
                { label: "🔄 Đổi Vị Trí", text: "Đổi Vị Trí" }
            ];
        } else if (chatState.state === "ready") {
            chips = [
                { label: "🏠 Tìm Phòng Trọ", text: "Phòng Trọ" },
                { label: "💼 Tìm Việc Làm", text: "Việc Làm" },
                { label: "✨ Quét Cả Hai", text: "Cả Hai" },
                { label: "🔄 Đổi Vị Trí", text: "Đổi Vị Trí" }
            ];
        }

        // Luôn có nút "❌ Xóa Bộ Lọc" ở mọi trạng thái
        chips.push({
            label: "❌ Xóa Bộ Lọc",
            action: () => {
                if (window.UniMap360Map && typeof window.UniMap360Map.clearProximityFilter === "function") {
                    window.UniMap360Map.clearProximityFilter();
                }
                chatState.state = "waiting_location";
                chatState.userLat = null;
                chatState.userLng = null;
                chatState.selectedService = null;
                chatState.selectedRadius = null;

                // Xóa các tin nhắn cũ để làm sạch giao diện chat
                if (msgContainer) {
                    msgContainer.innerHTML = "";
                }
                addMessage("🤖 Đã xóa bộ lọc và đưa chatbot về trạng thái ban đầu. Hãy chọn hoặc nhập vị trí mới của bạn để bắt đầu tìm kiếm nhé!", "ai");
                renderQuickChips();
            }
        });

        chips.forEach(c => {
            const chipBtn = document.createElement("div");
            chipBtn.className = "ai-chip";
            chipBtn.innerText = c.label;
            chipBtn.addEventListener("click", () => {
                if (c.action) {
                    c.action();
                } else if (c.text) {
                    submitMessage(c.text);
                }
            });
            quickChipsContainer.appendChild(chipBtn);
        });
    }

    // 4. GỬI TIN NHẮN HỘI THOẠI & ĐỒNG BỘ BẢN ĐỒ
    function handleUserMessageSubmit() {
        if (!chatInput) return;
        const text = chatInput.value.trim();
        if (!text) return;
        submitMessage(text);
    }

    function submitMessage(text) {
        addMessage(text, "user");
        chatInput.value = "";

        showTypingIndicator();

        // Gửi API lên Backend
        fetch("/api/ai-chat/query", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                message: text,
                state: chatState.state,
                userLat: chatState.userLat,
                userLng: chatState.userLng,
                selectedService: chatState.selectedService,
                selectedRadius: chatState.selectedRadius
            })
        })
            .then(res => res.json())
            .then(data => {
                removeTypingIndicator();
                if (data.success) {
                    // Cập nhật trạng thái hội thoại
                    chatState.state = data.newState;
                    chatState.userLat = data.userLat;
                    chatState.userLng = data.userLng;
                    chatState.selectedService = data.selectedService;
                    chatState.selectedRadius = data.selectedRadius;

                    // Thêm câu trả lời của AI
                    addMessage(data.response, "ai");

                    // Nếu Geocoding tìm được vị trí mới của người dùng
                    if (data.detectedLat && data.detectedLng) {
                        if (window.UniMap360Map && typeof window.UniMap360Map.drawUserLocationOnMap === "function") {
                            window.UniMap360Map.drawUserLocationOnMap(data.detectedLat, data.detectedLng);
                        }
                    }

                    // Nếu đã đủ thông tin và chạy tìm kiếm cận lộ thành công
                    if (data.newState === "ready" && chatState.userLat && chatState.userLng && chatState.selectedRadius) {
                        if (window.UniMap360Map && typeof window.UniMap360Map.applyProximityFilterOnMap === "function") {
                            window.UniMap360Map.applyProximityFilterOnMap(
                                chatState.userLat,
                                chatState.userLng,
                                chatState.selectedRadius,
                                chatState.selectedService
                            );
                        }
                    }

                    // Render lại gợi ý tương ứng với state mới
                    renderQuickChips();
                } else {
                    addMessage("🤖 Có lỗi xảy ra trong quá trình trao đổi thông tin. Vui lòng thử lại.");
                }
            })
            .catch(err => {
                removeTypingIndicator();
                console.error("Lỗi kết nối AI chatbot:", err);
                addMessage("🤖 Kết nối máy chủ AI thất bại. Vui lòng kiểm tra lại đường truyền mạng.");
            });
    }
})();
