(function () {
    function computeSeedLock(itemId, attempt) {
        var s = `${itemId}-${attempt}`;
        var hash = 0;
        for (var i = 0; i < s.length; i++) {
            hash = ((hash << 5) - hash) + s.charCodeAt(i);
            hash |= 0;
        }
        return Math.abs(hash % 100000);
    }

    function getFormattedPrice(price) {
        if (!price || price === 0) return "Thỏa thuận";
        if (price >= 1000000) return (price / 1000000).toFixed(1) + " Triệu";
        return price.toLocaleString("vi-VN") + " đ";
    }

    function handleImageError(imgEl) {
        if (!imgEl) return;
        var fallback = imgEl.getAttribute("data-fallback") || "/images/fallback-room.svg";
        var itemType = imgEl.getAttribute("data-item-type") || "room";
        var itemId = imgEl.getAttribute("data-item-id") || "0";
        var attempt = parseInt(imgEl.getAttribute("data-img-attempt") || "0", 10);

        if (itemType === "room" && attempt < 2) {
            var nextAttempt = attempt + 1;
            imgEl.setAttribute("data-img-attempt", String(nextAttempt));
            var lock = computeSeedLock(itemId, nextAttempt);
            imgEl.src = `https://loremflickr.com/960/640/apartment,interior,bedroom?lock=${lock}`;
            return;
        }

        imgEl.onerror = null;
        imgEl.src = fallback;
    }

    window.UniMap360ListingMapUtils = {
        computeSeedLock,
        getFormattedPrice,
        handleImageError
    };

    // Keep old inline handler compatibility.
    window.handleListingImageError = handleImageError;
    window.handleMapImageError = handleImageError;
})();

