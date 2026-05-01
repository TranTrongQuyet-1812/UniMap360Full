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
    return localStorage.getItem('unimap360.accessToken') || sessionStorage.getItem('unimap360.accessToken');
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
