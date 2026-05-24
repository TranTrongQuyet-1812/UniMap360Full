/**
 * UniMap360 Core Javascript
 * Cung cấp các hàm tiện ích dùng chung cho toàn dự án để tránh lặp code.
 */
window.UniMap360Core = window.UniMap360Core || {};

// 1. Lấy Token xác thực
window.UniMap360Core.getToken = function () {
    if (window.UniMap360AuthStore && typeof window.UniMap360AuthStore.getStoredToken === 'function') {
        return window.UniMap360AuthStore.getStoredToken();
    }
    const bootstrap = window.__UNIMAP360_AUTH_BOOTSTRAP__;
    if (bootstrap && bootstrap.isAuthenticated === true && bootstrap.email) {
        return "cookie-auth";
    }
    return null;
};

// 2. Hiển thị thông báo (Toast)
window.UniMap360Core.showToast = function (message, type = 'success') {
    let t = document.getElementById('toastMsg');
    if (!t) {
        // Tự động tạo phần tử toast nếu chưa có trên giao diện
        t = document.createElement('div');
        t.id = 'toastMsg';
        document.body.appendChild(t);
        
        // Thêm CSS nội tuyến cơ bản nếu file site.css chưa load kịp
        if (!document.getElementById('toastStyles')) {
            const style = document.createElement('style');
            style.id = 'toastStyles';
            style.innerHTML = `
                .toast-msg {
                    position: fixed; top: 80px; right: 20px; padding: 12px 24px;
                    border-radius: 8px; color: white; font-weight: bold;
                    z-index: 9999; transform: translateX(120%); opacity: 0;
                    transition: transform 0.3s ease, opacity 0.3s ease; 
                    box-shadow: 0 4px 12px rgba(0,0,0,0.15); pointer-events: none;
                }
                .toast-msg.show { transform: translateX(0); opacity: 1; }
                .toast-msg.success { background: #10B981; }
                .toast-msg.error { background: #EF4444; }
                .toast-msg.warning { background: #F59E0B; }
                .toast-msg.info { background: #3B82F6; }
            `;
            document.head.appendChild(style);
        }
    }
    
    t.textContent = message;
    t.className = 'toast-msg ' + type + ' show';
    
    if (t.hideTimeout) clearTimeout(t.hideTimeout);
    t.hideTimeout = setTimeout(() => {
        t.classList.remove('show');
    }, 4000);
};

// 3. Export thành các biến global để tương thích với các file js/cshtml cũ
window.getToken = window.UniMap360Core.getToken;
window.showToast = window.UniMap360Core.showToast;

// 4. Global Fetch Interceptor để xóa header "Authorization: Bearer null/undefined"
(function () {
    const originalFetch = window.fetch;
    window.fetch = async function (resource, options) {
        options = options || {};
        if (options.headers) {
            let headersObj;
            if (options.headers instanceof Headers) {
                headersObj = options.headers;
            } else if (Array.isArray(options.headers)) {
                headersObj = new Headers(options.headers);
            } else {
                headersObj = new Headers();
                for (const [key, value] of Object.entries(options.headers)) {
                    headersObj.append(key, value);
                }
            }

            const authHeader = headersObj.get('Authorization');
            if (authHeader) {
                const cleanAuth = authHeader.trim();
                const normalized = cleanAuth.toLowerCase();
                if (cleanAuth === 'Bearer null' ||
                    cleanAuth === 'Bearer undefined' ||
                    cleanAuth === 'Bearer' ||
                    cleanAuth === 'Bearer ' ||
                    normalized === 'bearer cookie-auth') {
                    headersObj.delete('Authorization');
                }
            }
            options.headers = headersObj;
        }
        return originalFetch(resource, options);
    };
})();
