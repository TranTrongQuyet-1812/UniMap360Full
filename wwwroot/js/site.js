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
			authStore.clearStoredAuth();
			return;
		}
		fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => { });

		window.dispatchEvent(new CustomEvent('unimap360:auth-changed', {
			detail: { account: null }
		}));
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

			if (!response.ok) return null;

			let me = requestResult ? requestResult.payload : await response.json();
			if (me && me.success === true && me.data !== undefined) me = me.data;
			if (!me || !me.email) return null;

			const account = {
				accountId: me.accountId,
				email: me.email,
				role: me.role
			};

			saveAccount(account);
			return account;
		} catch {
			return null;
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
