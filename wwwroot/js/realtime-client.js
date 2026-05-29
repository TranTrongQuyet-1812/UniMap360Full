(function () {
    let connection = null;
    let isConnected = false;

    const DEBUG_REALTIME = false;

    function getAccount() {
        return window.UniMap360AuthStore?.getStoredAccount?.() || null;
    }

    function isAuthenticated() {
        return !!getAccount();
    }

    async function startConnection() {
        if (!isAuthenticated()) {
            return;
        }

        if (connection) {
            if (isConnected || connection.state === signalR.HubConnectionState.Connecting) {
                return;
            }
            try {
                await connection.start();
                isConnected = true;
                console.log("SignalR connected successfully.");
                window.dispatchEvent(new CustomEvent("unimap360:realtime:connected"));
                return;
            } catch (err) {
                console.error("SignalR connection error:", err);
                return;
            }
        }

        // Tạo hub connection
        connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/realtime", {
                withCredentials: true // Để tự động gửi cookie accessToken đi kèm
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();

        // Đăng ký các sự kiện từ Server
        connection.on("ReceiveMessage", function (message) {
            if (DEBUG_REALTIME) console.log("SignalR: ReceiveMessage", message);
            window.dispatchEvent(new CustomEvent("unimap360:realtime:message", { detail: message }));
        });

        connection.on("ConversationUpdated", function (conversation) {
            if (DEBUG_REALTIME) console.log("SignalR: ConversationUpdated", conversation);
            window.dispatchEvent(new CustomEvent("unimap360:realtime:conversation", { detail: conversation }));
        });

        connection.on("ChatUnreadChanged", function (payload) {
            if (DEBUG_REALTIME) console.log("SignalR: ChatUnreadChanged", payload);
            window.dispatchEvent(new CustomEvent("unimap360:realtime:chat-unread", { detail: payload }));
        });

        connection.on("NotificationCreated", function (notification) {
            if (DEBUG_REALTIME) console.log("SignalR: NotificationCreated", notification);
            window.dispatchEvent(new CustomEvent("unimap360:realtime:notification", { detail: notification }));
        });

        connection.on("NotificationUnreadChanged", function (payload) {
            if (DEBUG_REALTIME) console.log("SignalR: NotificationUnreadChanged", payload);
            window.dispatchEvent(new CustomEvent("unimap360:realtime:notif-unread", { detail: payload }));
        });

        // Xử lý các sự kiện vòng đời kết nối
        connection.onreconnecting(function (error) {
            console.warn("SignalR: Reconnecting due to error:", error);
            isConnected = false;
            window.dispatchEvent(new CustomEvent("unimap360:realtime:reconnecting"));
        });

        connection.onreconnected(function (connectionId) {
            console.log("SignalR: Reconnected. ConnectionId:", connectionId);
            isConnected = true;
            window.dispatchEvent(new CustomEvent("unimap360:realtime:reconnected"));
        });

        connection.onclose(function (error) {
            console.error("SignalR: Connection closed.", error);
            isConnected = false;
            window.dispatchEvent(new CustomEvent("unimap360:realtime:closed"));
        });

        try {
            await connection.start();
            isConnected = true;
            console.log("SignalR connected successfully.");
            window.dispatchEvent(new CustomEvent("unimap360:realtime:connected"));
        } catch (err) {
            console.error("SignalR connection error on start:", err);
            isConnected = false;
        }
    }

    async function stopConnection() {
        if (!connection) return;
        try {
            await connection.stop();
            console.log("SignalR connection stopped.");
        } catch (err) {
            console.error("SignalR error stopping connection:", err);
        } finally {
            connection = null;
            isConnected = false;
        }
    }

    function checkAuthAndToggleConnection() {
        if (isAuthenticated()) {
            startConnection();
        } else {
            stopConnection();
        }
    }

    // Khởi động kết nối ban đầu
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", checkAuthAndToggleConnection);
    } else {
        checkAuthAndToggleConnection();
    }

    // Lắng nghe sự kiện đăng nhập/đăng xuất
    window.addEventListener("unimap360:auth-changed", checkAuthAndToggleConnection);

    // Export interface ra global
    window.UniMap360RealtimeClient = {
        getConnection: () => connection,
        isConnected: () => isConnected,
        startConnection,
        stopConnection
    };
})();
