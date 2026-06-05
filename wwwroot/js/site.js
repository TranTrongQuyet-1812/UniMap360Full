// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
window.escapeHtml = function(unsafe) {
    if (unsafe === null || unsafe === undefined) return '';
    return String(unsafe)
         .replace(/&/g, "&amp;")
         .replace(/</g, "&lt;")
         .replace(/>/g, "&gt;")
         .replace(/"/g, "&quot;")
         .replace(/'/g, "&#039;");
};

window.normalizeListingType = function(value) {
    const raw = String(value || "").toLowerCase().trim();
    return raw === "job" ? "job" : "room";
};

window.safeImageUrl = function(value, fallback) {
    const raw = String(value || "").trim();
    if (!raw) return fallback;

    const allowed =
        (raw.startsWith("/") && !raw.startsWith("//") && !raw.startsWith("/\\")) ||
        raw.startsWith("https://res.cloudinary.com/") ||
        raw.startsWith("https://api.dicebear.com/") ||
        raw.startsWith("https://lh3.googleusercontent.com/");

    return allowed ? window.escapeHtml(raw) : fallback;
};

(function () {
	const authStore = window.UniMap360AuthStore;
	const apiClient = window.UniMap360ApiClient;
	const navbarAuthFactory = window.UniMap360NavbarAuth;
	const notificationCenterFactory = window.UniMap360NotificationCenter;
	const STORAGE_KEY_TOKEN = authStore ? authStore.storageKeyToken : 'unimap360.accessToken';
	const STORAGE_KEY_ACCOUNT = authStore ? authStore.storageKeyAccount : 'unimap360.account';
	const notificationCenter = notificationCenterFactory
		? notificationCenterFactory.createNotificationCenter({
			apiClient: apiClient,
			getToken: function () { return getStoredToken(); }
		})
		: null;
	const navbarAuth = navbarAuthFactory
		? navbarAuthFactory.createNavbarAuth({
			clearStoredAuth: clearStoredAuth,
			resetNotifications: function () {
				if (notificationCenter) notificationCenter.resetUi();
			},
			stopNotifications: stopNotificationPolling
		})
		: null;

	function getStoredToken() {
		if (authStore) return authStore.getStoredToken();
		return null;
	}

	function getStoredAccount() {
		if (authStore) return authStore.getStoredAccount();
		return null;
	}

	function saveAccount(account) {
		if (authStore) {
			authStore.saveAccount(account);
			return;
		}

		window.dispatchEvent(new CustomEvent('unimap360:auth-changed', {
			detail: { account: account || null }
		}));
	}

	function clearStoredAuth() {
		if (authStore) {
			return authStore.clearStoredAuth();
		}
		const promise = fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => { });

		window.dispatchEvent(new CustomEvent('unimap360:auth-changed', {
			detail: { account: null }
		}));
		return promise;
	}

	function stopNotificationPolling() {
		if (notificationCenter) notificationCenter.stopPolling();
	}

	function renderAuthNavbar(account) {
		if (navbarAuth) {
			navbarAuth.render(account);
		}
	}

	function wireNotificationEvents() {
		if (notificationCenter) notificationCenter.wireEvents();
	}

	async function hydrateAccountFromApi() {
		try {
			const requestResult = apiClient
				? await apiClient.requestJson('/api/auth/me', {
					method: 'GET'
				})
				: null;
			const response = requestResult ? requestResult.response : await fetch('/api/auth/me', {
				method: 'GET'
			});

			if (response.status === 401 || response.status === 403) {
				clearStoredAuth();
				return null;
			}

			if (!response.ok) {
				throw new Error("HTTP-ERROR-" + response.status);
			}

			let me = requestResult ? requestResult.payload : await response.json();
			if (me && me.success === true && me.data !== undefined) me = me.data;
			if (!me || !me.email) return null;

			const account = {
				accountId: me.accountId,
				email: me.email,
				role: me.role,
				fullName: me.fullName,
				avatarUrl: me.avatarUrl
			};

			saveAccount(account);
			return account;
		} catch (err) {
			// Chỉ logout nếu có bằng chứng rõ ràng là lỗi 401 hoặc 403.
			// Nếu lỗi mạng, 429 hoặc 5xx, chúng ta giữ nguyên thông tin tài khoản cũ đã lưu.
			return getStoredAccount();
		}
	}

	function wireLogoutButton() {
		if (navbarAuth) navbarAuth.wireLogout();
	}

	function wireAuthStateListeners() {
		window.addEventListener('unimap360:auth-changed', function (event) {
			const account = event && event.detail ? event.detail.account : null;
			renderAuthNavbar(account || getStoredAccount());
		});

		window.addEventListener('storage', function (event) {
			if (!event || (event.key !== STORAGE_KEY_TOKEN && event.key !== STORAGE_KEY_ACCOUNT)) return;
			renderAuthNavbar(getStoredAccount());
		});
	}

	document.addEventListener('DOMContentLoaded', async function () {
		wireLogoutButton();
		wireAuthStateListeners();
		wireNotificationEvents();

		const account = getStoredAccount();

		if (account) {
			// Hiển thị ngay trạng thái đăng nhập từ cache
			renderAuthNavbar(account);
			if (notificationCenter) notificationCenter.startPolling("cookie-auth");

			// Kiểm tra âm thầm phiên đăng nhập thực tế
			hydrateAccountFromApi().then(freshAccount => {
				if (!freshAccount) {
					renderAuthNavbar(null);
					stopNotificationPolling();
				} else {
					renderAuthNavbar(freshAccount);
				}
			});
		} else {
			renderAuthNavbar(null);
			// Kiểm tra xem có cookie đăng nhập hợp lệ không
			hydrateAccountFromApi().then(freshAccount => {
				if (freshAccount) {
					renderAuthNavbar(freshAccount);
					if (notificationCenter) notificationCenter.startPolling("cookie-auth");
				}
			});
		}
	});
})();
