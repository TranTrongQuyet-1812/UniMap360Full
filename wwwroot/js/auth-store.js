(function () {
    const STORAGE_KEY_TOKEN = "unimap360.accessToken";
    const STORAGE_KEY_ACCOUNT = "unimap360.account";

    function parseBootstrapAccount() {
        const bootstrap = window.__UNIMAP360_AUTH_BOOTSTRAP__;
        if (!bootstrap || bootstrap.isAuthenticated !== true || !bootstrap.email) {
            return null;
        }

        const accountId = Number(bootstrap.accountId);
        const base = {
            accountId: Number.isFinite(accountId) && accountId > 0 ? accountId : null,
            email: String(bootstrap.email || "").trim(),
            role: String(bootstrap.role || "").trim() || null
        };

        try {
            const raw = localStorage.getItem(STORAGE_KEY_ACCOUNT);
            if (raw) {
                const stored = JSON.parse(raw);
                if (stored && stored.accountId === base.accountId) {
                    base.fullName = stored.fullName;
                    base.avatarUrl = stored.avatarUrl;
                }
            }
        } catch (_) {}

        return base;
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
            try {
                localStorage.removeItem(STORAGE_KEY_ACCOUNT);
            } catch (_) {}
            emitAuthChanged(null);
            return;
        }

        _currentAccount = Object.assign({}, _currentAccount || {}, account);
        try {
            localStorage.setItem(STORAGE_KEY_ACCOUNT, JSON.stringify(_currentAccount));
        } catch (_) {}
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
