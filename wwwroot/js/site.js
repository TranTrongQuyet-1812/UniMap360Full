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
		return localStorage.getItem(STORAGE_KEY_TOKEN) || sessionStorage.getItem(STORAGE_KEY_TOKEN);
	}

	function getStoredAccount() {
		if (authStore) return authStore.getStoredAccount();
		const raw = localStorage.getItem(STORAGE_KEY_ACCOUNT) || sessionStorage.getItem(STORAGE_KEY_ACCOUNT);
		if (!raw) return null;

		try {
			return JSON.parse(raw);
		} catch {
			return null;
		}
	}

	function saveAccount(account) {
		if (authStore) {
			authStore.saveAccount(account);
			return;
		}
		const serialized = JSON.stringify(account);
		if (localStorage.getItem(STORAGE_KEY_TOKEN)) {
			localStorage.setItem(STORAGE_KEY_ACCOUNT, serialized);
			return;
		}
		if (sessionStorage.getItem(STORAGE_KEY_TOKEN)) {
			sessionStorage.setItem(STORAGE_KEY_ACCOUNT, serialized);
		}
	}

	function clearStoredAuth() {
		if (authStore) {
			authStore.clearStoredAuth();
			return;
		}
		localStorage.removeItem(STORAGE_KEY_TOKEN);
		localStorage.removeItem(STORAGE_KEY_ACCOUNT);
		sessionStorage.removeItem(STORAGE_KEY_TOKEN);
		sessionStorage.removeItem(STORAGE_KEY_ACCOUNT);
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

	async function hydrateAccountFromApi(token) {
		if (!token) return null;

		try {
			const requestResult = apiClient
				? await apiClient.requestJson('/api/auth/me', {
					method: 'GET',
					headers: {
						Authorization: `Bearer ${token}`
					}
				})
				: null;
			const response = requestResult ? requestResult.response : await fetch('/api/auth/me', {
				method: 'GET',
				headers: {
					Authorization: `Bearer ${token}`
				}
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

		const token = getStoredToken();
		const account = getStoredAccount();

		renderAuthNavbar(account);

		if (!token) {
			renderAuthNavbar(null);
			return;
		}

		if (account) {
			if (notificationCenter) notificationCenter.startPolling(token);
			return; // Đã có bộ nhớ đệm, KHÔNG gọi API nữa để chống 429
		}

		const freshAccount = await hydrateAccountFromApi(token);
		if (freshAccount) {
			renderAuthNavbar(freshAccount);
			if (notificationCenter) notificationCenter.startPolling(token);
			return;
		}

		// Keep current navbar state when API refresh fails due to transient network issues.
		renderAuthNavbar(getStoredAccount());
		if (notificationCenter) notificationCenter.startPolling(token);
	});
})();
