(function () {
    const STORAGE_KEY_TOKEN = "unimap360.accessToken";
    const STORAGE_KEY_ACCOUNT = "unimap360.account";

    function parseBootstrapAccount() {
        const bootstrap = window.__UNIMAP360_AUTH_BOOTSTRAP__;
        if (!bootstrap || bootstrap.isAuthenticated !== true || !bootstrap.email) {
            return null;
        }

        const accountId = Number(bootstrap.accountId);
        return {
            accountId: Number.isFinite(accountId) && accountId > 0 ? accountId : null,
            email: String(bootstrap.email || "").trim(),
            role: String(bootstrap.role || "").trim() || null
        };
    }

    // In-memory auth state only (token stays in HttpOnly cookie).
    let _currentAccount = parseBootstrapAccount();

    function emitAuthChanged(account) {
        try {
            window.dispatchEvent(new CustomEvent("unimap360:auth-changed", {
                detail: { account: account || null }
            }));
        } catch {
            // ignore
        }
    }

    function setTokenCookie() {
        // Server sets HttpOnly cookie on login/google/refresh.
    }

    function clearTokenCookie() {
        // Use /api/auth/logout so server removes HttpOnly cookie.
    }

    function getStoredToken() {
        // Compatibility sentinel for legacy pages that still gate on token presence.
        return _currentAccount ? "cookie-auth" : null;
    }

    function getStoredAccount() {
        return _currentAccount;
    }

    function saveAccount(account) {
        if (!account || !account.email) {
            _currentAccount = null;
            emitAuthChanged(null);
            return;
        }

        _currentAccount = Object.assign({}, _currentAccount || {}, account);
        emitAuthChanged(_currentAccount);
    }

    function clearStoredAuth() {
        _currentAccount = null;
        emitAuthChanged(null);

        // Cleanup old client-side traces if any remain from older versions.
        localStorage.removeItem(STORAGE_KEY_TOKEN);
        localStorage.removeItem(STORAGE_KEY_ACCOUNT);
        sessionStorage.removeItem(STORAGE_KEY_TOKEN);
        sessionStorage.removeItem(STORAGE_KEY_ACCOUNT);

        // Ask server to remove HttpOnly cookie.
        fetch("/api/auth/logout", { method: "POST", credentials: "same-origin" }).catch(() => { });
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
