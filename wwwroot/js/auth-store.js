(function () {
    const STORAGE_KEY_TOKEN = "unimap360.accessToken";
    const STORAGE_KEY_ACCOUNT = "unimap360.account";
    const AUTH_COOKIE_TOKEN = "unimap360.accessToken";

    function setTokenCookie(token, rememberMe) {
        if (!token) return;

        const attrs = ["path=/", "SameSite=Lax"];
        if (location.protocol === "https:") attrs.push("Secure");
        if (rememberMe) attrs.push("Max-Age=" + (60 * 60 * 24 * 7));

        document.cookie = AUTH_COOKIE_TOKEN + "=" + encodeURIComponent(token) + "; " + attrs.join("; ");
    }

    function clearTokenCookie() {
        const attrs = ["path=/", "Max-Age=0", "SameSite=Lax"];
        if (location.protocol === "https:") attrs.push("Secure");
        document.cookie = AUTH_COOKIE_TOKEN + "=; " + attrs.join("; ");
    }

    function getStoredToken() {
        return localStorage.getItem(STORAGE_KEY_TOKEN) || sessionStorage.getItem(STORAGE_KEY_TOKEN);
    }

    function getStoredAccount() {
        const raw = localStorage.getItem(STORAGE_KEY_ACCOUNT) || sessionStorage.getItem(STORAGE_KEY_ACCOUNT);
        if (!raw) return null;
        try {
            return JSON.parse(raw);
        } catch {
            return null;
        }
    }

    function saveAccount(account) {
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
        localStorage.removeItem(STORAGE_KEY_TOKEN);
        localStorage.removeItem(STORAGE_KEY_ACCOUNT);
        sessionStorage.removeItem(STORAGE_KEY_TOKEN);
        sessionStorage.removeItem(STORAGE_KEY_ACCOUNT);
        clearTokenCookie();
    }

    window.UniMap360AuthStore = {
        storageKeyToken: STORAGE_KEY_TOKEN,
        storageKeyAccount: STORAGE_KEY_ACCOUNT,
        setTokenCookie,
        clearTokenCookie,
        getStoredToken,
        getStoredAccount,
        saveAccount,
        clearStoredAuth
    };
})();

