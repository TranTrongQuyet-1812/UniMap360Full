(function () {
    async function requestJson(url, options) {
        const response = await fetch(url, options);
        const contentType = response.headers.get("content-type") || "";
        let payload = null;

        if (contentType.includes("application/json")) {
            try {
                let json = await response.json();
                if (json && json.success === true && json.data !== undefined) {
                    payload = json.data;
                } else {
                    payload = json;
                }
            } catch {
                payload = null;
            }
        }

        return { response, payload };
    }

    window.UniMap360ApiClient = {
        requestJson
    };
})();

