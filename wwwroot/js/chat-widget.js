(function () {
    const POLL_CONVERSATIONS_MS = 2500;
    const POLL_MESSAGES_MS = 1800;

    const state = {
        initialized: false,
        open: false,
        loadingConversations: false,
        activeConversationId: null,
        conversations: [],
        messages: [],
        conversationPollTimer: null,
        messagePollTimer: null,
        viewMode: "list"
    };

    function getToken() {
        return window.UniMap360AuthStore?.getStoredToken?.() || null;
    }

    function getAccount() {
        return window.UniMap360AuthStore?.getStoredAccount?.() || null;
    }

    function isAuthenticated() {
        return !!getToken();
    }

    function withNoCacheUrl(url) {
        const separator = url.includes("?") ? "&" : "?";
        return `${url}${separator}_chatTs=${Date.now()}`;
    }

    async function fetchJson(url, options) {
        const requestOptions = Object.assign({}, options || {});
        const method = (requestOptions.method || "GET").toUpperCase();
        const headers = Object.assign({}, requestOptions.headers || {});
        let finalUrl = url;

        if (method === "GET") {
            finalUrl = withNoCacheUrl(url);
            requestOptions.cache = "no-store";
            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }

        requestOptions.headers = headers;

        const response = await fetch(finalUrl, requestOptions);
        const contentType = response.headers.get("content-type") || "";
        let payload = null;
        if (contentType.includes("application/json")) {
            payload = await response.json();
            if (payload && typeof payload === 'object' && 'success' in payload) {
                payload = payload.success ? payload.data : (payload.error || payload);
            }
        }
        return { response, payload };
    }

    function escapeHtml(value) {
        return (value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function formatTime(isoString) {
        if (!isoString) return "";
        const date = new Date(isoString);
        if (Number.isNaN(date.getTime())) return "";
        return date.toLocaleString("vi-VN", {
            hour12: false,
            day: "2-digit",
            month: "2-digit",
            hour: "2-digit",
            minute: "2-digit"
        });
    }

    function truncate(text, max) {
        const value = text || "";
        if (value.length <= max) return value;
        return value.slice(0, max - 1) + "...";
    }

    function ensureWidget() {
        if (state.initialized) return;

        const wrapper = document.createElement("div");
        wrapper.id = "um-chat-widget-root";
        wrapper.innerHTML = `
            <button id="um-chat-launcher" class="um-chat-launcher" type="button" title="Trò chuyện">
                <i class="fas fa-comments"></i>
                <span id="um-chat-launcher-badge" class="um-chat-launcher-badge"></span>
            </button>
            <section id="um-chat-panel" class="um-chat-panel" aria-label="Chat panel">
                <div class="um-chat-header">
                    <strong><i class="fas fa-comments me-2"></i>Trò chuyện</strong>
                    <button id="um-chat-close-btn" class="um-chat-close-btn" type="button" aria-label="Close chat">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
                <div class="um-chat-body">
                    <aside class="um-chat-sidebar">
                        <div id="um-chat-list" class="um-chat-list"></div>
                    </aside>
                    <section class="um-chat-thread">
                        <div id="um-chat-thread-header" class="um-chat-thread-header">
                            <button id="um-chat-back-btn" class="um-chat-back-btn" type="button" aria-label="Back to conversations">
                                <i class="fas fa-arrow-left"></i>
                            </button>
                            <span id="um-chat-thread-title">Tin nhắn</span>
                        </div>
                        <div id="um-chat-thread-messages" class="um-chat-thread-messages">
                            <div class="um-chat-empty"> </div>
                        </div>
                        <div class="um-chat-thread-input-wrap">
                            <textarea id="um-chat-message-input" placeholder="Nhập tin nhắn..."></textarea>
                            <button id="um-chat-send-btn" type="button" aria-label="Send message"><i class="fas fa-paper-plane"></i></button>
                        </div>
                    </section>
                </div>
            </section>
        `;

        document.body.appendChild(wrapper);

        const launcher = document.getElementById("um-chat-launcher");
        const closeButton = document.getElementById("um-chat-close-btn");
        const sendButton = document.getElementById("um-chat-send-btn");
        const messageInput = document.getElementById("um-chat-message-input");
        const backButton = document.getElementById("um-chat-back-btn");

        launcher?.addEventListener("click", togglePanel);
        closeButton?.addEventListener("click", togglePanel);
        sendButton?.addEventListener("click", onSendMessage);
        backButton?.addEventListener("click", showListView);
        messageInput?.addEventListener("keydown", function (event) {
            if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                onSendMessage();
            }
        });

        state.initialized = true;
    }

    function setPanelOpen(open) {
        state.open = !!open;
        const panel = document.getElementById("um-chat-panel");
        if (panel) panel.classList.toggle("open", state.open);

        if (state.open) {
            showListView();
            loadConversations(true);
            startPolling();
            
            // Tự động đóng panel AI Chat để tránh chồng chéo giao diện
            const aiPanel = document.getElementById("ai-chat-panel");
            if (aiPanel && aiPanel.classList.contains("open")) {
                aiPanel.classList.remove("open");
            }
        } else {
            stopMessagePolling();
        }
    }

    function applyViewMode() {
        const panel = document.getElementById("um-chat-panel");
        if (!panel) return;
        panel.classList.toggle("list-mode", state.viewMode === "list");
        panel.classList.toggle("thread-mode", state.viewMode === "thread");
    }

    function showListView() {
        state.viewMode = "list";
        applyViewMode();
        stopMessagePolling();
    }

    function showThreadView() {
        state.viewMode = "thread";
        applyViewMode();
        startMessagePolling();
    }

    function togglePanel() {
        if (!isAuthenticated()) return;
        setPanelOpen(!state.open);
    }

    function updateWidgetVisibility() {
        const root = document.getElementById("um-chat-widget-root");
        if (!root) return;

        const authenticated = isAuthenticated();
        root.style.display = authenticated ? "" : "none";

        if (!authenticated) {
            setPanelOpen(false);
            stopConversationPolling();
            stopMessagePolling();
            state.activeConversationId = null;
            state.conversations = [];
            state.messages = [];
            updateLauncherBadge(0);
            return;
        }

        loadConversations(true);
        if (!state.conversationPollTimer) startPolling();
    }

    function hasConversationListChanged(oldList, newList) {
        if (oldList.length !== newList.length) return true;
        for (let i = 0; i < oldList.length; i++) {
            const o = oldList[i];
            const n = newList[i];
            if (o.conversationId !== n.conversationId) return true;
            if (o.unreadCount !== n.unreadCount) return true;
            if ((o.lastMessage?.messageId || 0) !== (n.lastMessage?.messageId || 0)) return true;
            if ((o.lastMessage?.content || "") !== (n.lastMessage?.content || "")) return true;
        }
        return false;
    }

    async function loadConversations(forceRefresh) {
        if (state.loadingConversations && !forceRefresh) return;

        const token = getToken();
        const account = getAccount();
        if (!token || !account) {
            renderUnauthenticated();
            updateLauncherBadge(0);
            return;
        }

        state.loadingConversations = true;
        try {
            const { response, payload } = await fetchJson("/api/chat/conversations?limit=80");

            if (!response.ok) {
                renderConversationError(payload?.message || "Không tải được danh sách chat.");
                return;
            }

            const newConversations = Array.isArray(payload?.items) ? payload.items : [];
            if (forceRefresh || hasConversationListChanged(state.conversations, newConversations)) {
                state.conversations = newConversations;
                renderConversationList();
            }
            updateLauncherBadge(payload?.totalUnread || 0);

            if (!state.activeConversationId && state.conversations.length > 0) {
                await openConversation(state.conversations[0].conversationId);
            }
        } catch {
            renderConversationError("Không kết nối được hệ thống chat.");
        } finally {
            state.loadingConversations = false;
        }
    }

    function renderConversationError(message) {
        const listEl = document.getElementById("um-chat-list");
        if (!listEl) return;
        listEl.innerHTML = `<div class="um-chat-empty">${escapeHtml(message)}</div>`;
    }

    function renderUnauthenticated() {
        const listEl = document.getElementById("um-chat-list");
        const threadHeader = document.getElementById("um-chat-thread-title");
        const messagesEl = document.getElementById("um-chat-thread-messages");
        if (listEl) listEl.innerHTML = '<div class="um-chat-empty">Đăng nhập để sử dụng chat.</div>';
        if (threadHeader) threadHeader.textContent = "Tin nhắn";
        if (messagesEl) messagesEl.innerHTML = '<div class="um-chat-empty">Đăng nhập để nhận và gửi tin nhắn.</div>';
    }

    function renderConversationList() {
        const listEl = document.getElementById("um-chat-list");
        const account = getAccount();
        if (!listEl) return;

        if (state.conversations.length === 0) {
            listEl.innerHTML = '<div class="um-chat-empty">Chưa có cuộc trò chuyện nào.</div>';
            return;
        }

        const html = state.conversations.map(function (conversation) {
            const isActive = conversation.conversationId === state.activeConversationId;
            
            let preview = "Chưa có tin nhắn.";
            if (conversation.lastMessage?.content) {
                const isMine = account && Number(conversation.lastMessage.senderAccountId) === Number(account.accountId);
                preview = isMine ? `Bạn: ${conversation.lastMessage.content}` : conversation.lastMessage.content;
            }

            const time = conversation.lastMessage?.createdAt ? formatTime(conversation.lastMessage.createdAt) : "";
            const unread = Number(conversation.unreadCount || 0);
            return `
                <div class="um-chat-list-item ${isActive ? "active" : ""}" data-conversation-id="${conversation.conversationId}">
                    <div class="um-chat-item-click-area">
                        <div class="um-chat-item-title">${escapeHtml(conversation.title || "Cuộc trò chuyện")}</div>
                        <div class="um-chat-item-preview">${escapeHtml(truncate(preview, 80))}</div>
                        <div class="um-chat-item-meta">
                            <span class="um-chat-item-time">${escapeHtml(time)}</span>
                            ${unread > 0 ? `<span class="um-chat-item-unread">${unread}</span>` : ""}
                        </div>
                    </div>
                    <div class="um-chat-item-action">
                        <button class="um-chat-item-more-btn" type="button" title="Thao tác" data-more-id="${conversation.conversationId}">
                            <i class="fas fa-ellipsis-v"></i>
                        </button>
                        <div class="um-chat-item-dropdown" id="um-dropdown-${conversation.conversationId}">
                            <button class="um-dropdown-action-btn" type="button" data-delete-id="${conversation.conversationId}">
                                <i class="fas fa-trash-alt"></i> Xóa
                            </button>
                        </div>
                    </div>
                </div>
            `;
        }).join("");

        listEl.innerHTML = html;

        // Gắn sự kiện click mở chat
        listEl.querySelectorAll(".um-chat-item-click-area").forEach(function (clickArea) {
            clickArea.addEventListener("click", async function () {
                const item = clickArea.closest("[data-conversation-id]");
                const conversationId = Number(item.getAttribute("data-conversation-id"));
                if (conversationId > 0) await openConversation(conversationId);
            });
        });

        // Gắn sự kiện click nút 3 chấm toggle dropdown
        listEl.querySelectorAll(".um-chat-item-more-btn").forEach(function (btn) {
            btn.addEventListener("click", function (event) {
                event.stopPropagation(); // Ngăn kích hoạt click area mở chat
                const conversationId = btn.getAttribute("data-more-id");
                
                // Đóng tất cả dropdown khác và xoá class active của các nút khác
                listEl.querySelectorAll(".um-chat-item-dropdown").forEach(function (drop) {
                    if (drop.id !== `um-dropdown-${conversationId}`) {
                        drop.classList.remove("show");
                    }
                });
                listEl.querySelectorAll(".um-chat-item-more-btn").forEach(function (otherBtn) {
                    if (otherBtn !== btn) {
                        otherBtn.classList.remove("active");
                    }
                });
 
                const dropdown = document.getElementById(`um-dropdown-${conversationId}`);
                if (dropdown) {
                    const isShowing = dropdown.classList.toggle("show");
                    btn.classList.toggle("active", isShowing);
                }
            });
        });

        // Gắn sự kiện click nút Xóa cuộc trò chuyện
        listEl.querySelectorAll("[data-delete-id]").forEach(function (btn) {
            btn.addEventListener("click", async function (event) {
                event.stopPropagation(); // Ngăn kích hoạt click area mở chat
                const conversationId = Number(btn.getAttribute("data-delete-id"));
                
                // Đóng dropdown trước
                const dropdown = btn.closest(".um-chat-item-dropdown");
                if (dropdown) dropdown.classList.remove("show");

                if (conversationId > 0) {
                    if (window.confirm("Bạn có chắc chắn muốn xóa cuộc trò chuyện này?")) {
                        await deleteConversation(conversationId);
                    }
                }
            });
        });
    }

    async function openConversation(conversationId) {
        state.activeConversationId = conversationId;
        renderConversationList();

        const conversation = state.conversations.find(x => x.conversationId === conversationId);
        const threadHeader = document.getElementById("um-chat-thread-title");
        if (threadHeader) threadHeader.textContent = conversation?.title || "Tin nhắn";

        const messagesEl = document.getElementById("um-chat-thread-messages");
        if (messagesEl) {
            messagesEl.innerHTML = '<div class="um-chat-empty">Đang tải tin nhắn...</div>';
        }

        showThreadView();
        await loadMessages(true, true);
        await markConversationRead();
        await loadConversations(true);
    }

    async function deleteConversation(conversationId) {
        const token = getToken();
        if (!token) return;

        try {
            const { response, payload } = await fetchJson(`/api/chat/conversations/${conversationId}/archive`, {
                method: "POST"
            });

            if (!response.ok) {
                window.alert(payload?.message || "Không thể xóa cuộc trò chuyện.");
                return;
            }

            // Nếu cuộc trò chuyện bị xóa đang là cuộc trò chuyện active, đóng khung chat thread
            if (state.activeConversationId === conversationId) {
                state.activeConversationId = null;
                showListView();
            }

            // Load lại danh sách chat
            await loadConversations(true);
        } catch {
            window.alert("Lỗi kết nối khi xóa cuộc trò chuyện.");
        }
    }

    async function loadMessages(forceScrollToBottom = false, forceRebuild = false) {
        const token = getToken();
        if (!token || !state.activeConversationId) return;

        const messagesEl = document.getElementById("um-chat-thread-messages");
        if (!messagesEl) return;

        try {
            const { response, payload } = await fetchJson(`/api/chat/conversations/${state.activeConversationId}/messages?take=80`);

            if (!response.ok) {
                messagesEl.innerHTML = '<div class="um-chat-empty">Không tải được tin nhắn.</div>';
                return;
            }

            const newMessages = Array.isArray(payload?.items) ? payload.items : [];
            const currentIds = state.messages.map(m => m.messageId).join(",");
            const newIds = newMessages.map(m => m.messageId).join(",");

            if (currentIds !== newIds || forceScrollToBottom || forceRebuild) {
                state.messages = newMessages;
                renderMessages(forceScrollToBottom, forceRebuild);
            }
        } catch {
            messagesEl.innerHTML = '<div class="um-chat-empty">Lỗi kết nối khi tải tin nhắn.</div>';
        }
    }

    function renderMessages(forceScrollToBottom = false, forceRebuild = false) {
        const messagesEl = document.getElementById("um-chat-thread-messages");
        const account = getAccount();
        if (!messagesEl || !account) return;

        if (state.messages.length === 0) {
            messagesEl.innerHTML = '<div class="um-chat-empty">Chưa có tin nhắn. Bạn có thể gửi lời chào đầu tiên.</div>';
            return;
        }

        const hasPlaceholder = messagesEl.querySelector(".um-chat-empty") !== null;

        if (forceRebuild || hasPlaceholder || messagesEl.children.length === 0) {
            const html = state.messages.map(function (message) {
                const mine = Number(message.senderAccountId) === Number(account.accountId);
                return `
                    <div class="um-chat-bubble ${mine ? "mine" : "other"}" data-message-id="${message.messageId}">
                        <div>${escapeHtml(message.content || "")}</div>
                        <div class="um-chat-bubble-time">${escapeHtml(formatTime(message.createdAt))}</div>
                    </div>
                `;
            }).join("");
            messagesEl.innerHTML = html;
            messagesEl.scrollTop = messagesEl.scrollHeight;
            return;
        }

        const wasNearBottom = messagesEl.scrollHeight - messagesEl.scrollTop - messagesEl.clientHeight < 120;

        state.messages.forEach(function (message) {
            const existing = messagesEl.querySelector(`[data-message-id="${message.messageId}"]`);
            if (!existing) {
                const mine = Number(message.senderAccountId) === Number(account.accountId);
                const tempDiv = document.createElement("div");
                tempDiv.className = `um-chat-bubble ${mine ? "mine" : "other"} new-message`;
                tempDiv.setAttribute("data-message-id", message.messageId);
                tempDiv.innerHTML = `
                    <div>${escapeHtml(message.content || "")}</div>
                    <div class="um-chat-bubble-time">${escapeHtml(formatTime(message.createdAt))}</div>
                `;
                messagesEl.appendChild(tempDiv);
            }
        });

        if (wasNearBottom || forceScrollToBottom) {
            messagesEl.scrollTop = messagesEl.scrollHeight;
        }
    }

    async function onSendMessage() {
        if (!state.activeConversationId) return;

        const token = getToken();
        if (!token) return;

        const inputEl = document.getElementById("um-chat-message-input");
        const sendBtn = document.getElementById("um-chat-send-btn");
        const content = (inputEl?.value || "").trim();
        if (!content) return;

        if (sendBtn) sendBtn.disabled = true;
        try {
            const { response, payload } = await fetchJson(`/api/chat/conversations/${state.activeConversationId}/messages`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ content: content })
            });

            if (!response.ok) {
                window.alert(payload?.message || "Không gửi được tin nhắn.");
                return;
            }

            if (inputEl) inputEl.value = "";
            await loadMessages(true, false);
            await markConversationRead();
            await loadConversations(true);
        } catch {
            window.alert("Không kết nối được hệ thống chat.");
        } finally {
            if (sendBtn) sendBtn.disabled = false;
        }
    }

    async function markConversationRead() {
        const token = getToken();
        if (!token || !state.activeConversationId) return;

        const lastMessage = state.messages[state.messages.length - 1];
        const lastReadMessageId = lastMessage?.messageId || null;

        try {
            await fetch(`/api/chat/conversations/${state.activeConversationId}/read`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ lastReadMessageId: lastReadMessageId })
            });
        } catch {
            // ignore
        }
    }

    async function openDirectChat(targetAccountId, preferredName, targetType, targetId) {
        const token = getToken();
        const account = getAccount();
        if (!token || !account) {
            window.location.href = "/Home/Auth";
            return;
        }

        const normalizedTargetId = Number(targetAccountId || 0);
        if (!normalizedTargetId) throw new Error("Tài khoản đích không hợp lệ.");
        if (normalizedTargetId === Number(account.accountId)) throw new Error("Không thể tự nhắn tin với chính mình.");

        setPanelOpen(true);

        const { response, payload } = await fetchJson("/api/chat/conversations/direct", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ 
                targetAccountId: normalizedTargetId,
                targetType: targetType || null,
                targetId: targetId ? Number(targetId) : null
            })
        });

        if (!response.ok) {
            throw new Error(payload?.message || "Không mở được đoạn chat.");
        }

        await loadConversations(true);
        const conversationId = Number(payload?.conversationId || 0);
        if (conversationId > 0) {
            await openConversation(conversationId);
            return;
        }

        if (preferredName) {
            const threadHeader = document.getElementById("um-chat-thread-title");
            if (threadHeader) threadHeader.textContent = preferredName;
        }
        showThreadView();
    }

    function updateLauncherBadge(totalUnread) {
        const badge = document.getElementById("um-chat-launcher-badge");
        if (!badge) return;

        const count = Number(totalUnread || 0);
        if (count <= 0) {
            badge.style.display = "none";
            badge.textContent = "";
            return;
        }

        badge.style.display = "inline-flex";
        badge.textContent = count > 99 ? "99+" : String(count);
    }

    async function handleConnected(label) {
        console.log("ChatWidget: SignalR " + label + ", switching to slow polling and reloading state.");
        await loadConversations(true);
        if (state.open && state.activeConversationId) {
            await loadMessages(true, true);
            await markConversationRead();
        }
        // Re-apply polling
        if (state.open) {
            startPolling();
            if (state.viewMode === "thread") {
                startMessagePolling();
            }
        }
    }

    function registerRealtimeListeners() {
        window.addEventListener("unimap360:realtime:message", async function (event) {
            const message = event.detail;
            if (!message) return;
            
            // If this message belongs to the active conversation, append it
            if (state.activeConversationId && Number(message.conversationId) === Number(state.activeConversationId)) {
                // Ensure message not already in state.messages
                const exists = state.messages.some(m => m.messageId === message.messageId);
                if (!exists) {
                    state.messages.push(message);
                    renderMessages(true, false);
                    if (state.open) {
                        await markConversationRead();
                    }
                }
            }
            
            // Refetch conversations is handled by unimap360:realtime:conversation event
        });

        window.addEventListener("unimap360:realtime:conversation", async function (event) {
            await loadConversations(true);
        });

        window.addEventListener("unimap360:realtime:chat-unread", function (event) {
            const payload = event.detail;
            if (payload && typeof payload.totalUnread !== "undefined") {
                updateLauncherBadge(payload.totalUnread);
            }
        });

        window.addEventListener("unimap360:realtime:connected", async function () {
            await handleConnected("connected");
        });

        window.addEventListener("unimap360:realtime:reconnected", async function () {
            await handleConnected("reconnected");
        });

        window.addEventListener("unimap360:realtime:closed", function () {
            console.log("ChatWidget: SignalR closed, reverting to fast polling.");
            if (state.open) {
                startPolling();
                if (state.viewMode === "thread") {
                    startMessagePolling();
                }
            }
        });
    }

    function startPolling() {
        stopConversationPolling();
        const isSignalRConnected = window.UniMap360RealtimeClient && window.UniMap360RealtimeClient.isConnected();
        const interval = isSignalRConnected ? 60000 : POLL_CONVERSATIONS_MS;
        state.conversationPollTimer = window.setInterval(function () {
            loadConversations(false);
        }, interval);
    }

    function stopConversationPolling() {
        if (state.conversationPollTimer) {
            window.clearInterval(state.conversationPollTimer);
            state.conversationPollTimer = null;
        }
    }

    function startMessagePolling() {
        stopMessagePolling();
        const isSignalRConnected = window.UniMap360RealtimeClient && window.UniMap360RealtimeClient.isConnected();
        if (isSignalRConnected) {
            // SignalR handles message delivery in real time, so no need to poll
            return;
        }
        state.messagePollTimer = window.setInterval(async function () {
            if (!state.activeConversationId || !state.open) return;
            await loadMessages();
            await markConversationRead();
            await loadConversations(false);
        }, POLL_MESSAGES_MS);
    }

    function stopMessagePolling() {
        if (state.messagePollTimer) {
            window.clearInterval(state.messagePollTimer);
            state.messagePollTimer = null;
        }
    }

    function boot() {
        registerRealtimeListeners();
        ensureWidget();
        applyViewMode();
        updateWidgetVisibility();

        window.UniMap360ChatWidget = {
            openPanel: function () {
                if (!isAuthenticated()) return;
                setPanelOpen(true);
            },
            closePanel: function () {
                setPanelOpen(false);
            },
            openDirectChat: openDirectChat
        };

        document.addEventListener("click", function (event) {
            // Nếu click không nằm trong nút 3 chấm và dropdown, đóng hết dropdowns đang mở
            if (!event.target.closest(".um-chat-item-action")) {
                document.querySelectorAll(".um-chat-item-dropdown").forEach(function (drop) {
                    drop.classList.remove("show");
                });
                document.querySelectorAll(".um-chat-item-more-btn").forEach(function (btn) {
                    btn.classList.remove("active");
                });
            }
        });

        document.addEventListener("visibilitychange", function () {
            if (!isAuthenticated()) return;
            if (document.visibilityState === "visible") {
                loadConversations(true);
                if (state.open && state.activeConversationId) {
                    loadMessages();
                    markConversationRead();
                }
            }
        });

        window.addEventListener("unimap360:auth-changed", function () {
            updateWidgetVisibility();
        });

        window.addEventListener("storage", function (event) {
            if (!event || (event.key !== "unimap360.accessToken" && event.key !== "unimap360.account")) return;
            updateWidgetVisibility();
        });
    }

    document.addEventListener("DOMContentLoaded", boot);
})();
