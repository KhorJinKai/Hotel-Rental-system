// Front-end behaviour for RentEasy.
// Everything below is progressive enhancement: each block bails out quietly when the
// elements it needs are not on the current page, so one script can serve every view.

// ------------------------------------------------------------------
// 1. Destination autocomplete
//    Popular cities appear the moment the box is focused (before any typing),
//    then the list narrows to matches as the tenant types.
// ------------------------------------------------------------------
(function () {
    var input = document.getElementById("Location");
    var suggestions = document.getElementById("LocationSuggestions");
    if (!input || !suggestions) {
        return;
    }

    var debounceTimer;
    var popularCache = null;

    function hide() {
        suggestions.innerHTML = "";
        suggestions.style.display = "none";
    }

    function heading(text) {
        var el = document.createElement("div");
        el.className = "suggestion-heading";
        el.textContent = text;
        return el;
    }

    function item(name, sub) {
        var row = document.createElement("div");
        row.className = "location-suggestion-item";

        var pin = document.createElement("span");
        pin.className = "pin";
        pin.textContent = "📍";
        row.appendChild(pin);

        var label = document.createElement("span");
        label.textContent = name;
        row.appendChild(label);

        if (sub) {
            var note = document.createElement("span");
            note.className = "sub";
            note.textContent = sub;
            row.appendChild(note);
        }

        row.addEventListener("mousedown", function (e) {
            // mousedown rather than click: the input's blur would otherwise close
            // the list before the click landed.
            e.preventDefault();
            input.value = name;
            hide();
        });

        return row;
    }

    function render(nodes) {
        if (!nodes.length) {
            hide();
            return;
        }
        suggestions.innerHTML = "";
        nodes.forEach(function (node) { suggestions.appendChild(node); });
        suggestions.style.display = "block";
    }

    function showPopular() {
        if (popularCache) {
            render(popularCache);
            return;
        }
        fetch("/Home/PopularLocations")
            .then(function (r) { return r.json(); })
            .then(function (cities) {
                if (!cities || !cities.length) {
                    return;
                }
                var nodes = [heading("Popular destinations")];
                cities.forEach(function (c) {
                    var count = c.count || c.Count;
                    var name = c.city || c.City;
                    nodes.push(item(name, count + (count === 1 ? " property" : " properties")));
                });
                popularCache = nodes;
                render(nodes);
            })
            .catch(hide);
    }

    function showMatches(locations) {
        if (!locations || !locations.length) {
            hide();
            return;
        }
        var nodes = [heading("Matching locations")];
        locations.forEach(function (l) { nodes.push(item(l)); });
        render(nodes);
    }

    input.addEventListener("focus", function () {
        if (!input.value.trim()) {
            showPopular();
        }
    });

    input.addEventListener("input", function () {
        var term = input.value.trim();
        clearTimeout(debounceTimer);

        if (!term) {
            showPopular();
            return;
        }

        debounceTimer = setTimeout(function () {
            fetch("/Home/SearchLocations?term=" + encodeURIComponent(term))
                .then(function (response) { return response.json(); })
                .then(showMatches)
                .catch(hide);
        }, 200);
    });

    document.addEventListener("click", function (e) {
        if (e.target !== input && !suggestions.contains(e.target)) {
            hide();
        }
    });

    input.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            hide();
        }
    });
})();

// ------------------------------------------------------------------
// 2. Price filter: slider and the min/max boxes stay in sync
// ------------------------------------------------------------------
(function () {
    var slider = document.getElementById("priceSlider");
    var maxInput = document.getElementById("maxPrice");
    var readout = document.getElementById("priceReadout");
    if (!slider || !maxInput) {
        return;
    }

    function format(value) {
        return Number(value).toLocaleString("en-MY");
    }

    function updateReadout(value) {
        if (readout) {
            readout.textContent = "Up to RM " + format(value) + " / month";
        }
    }

    slider.addEventListener("input", function () {
        maxInput.value = slider.value;
        updateReadout(slider.value);
    });

    maxInput.addEventListener("input", function () {
        var value = parseInt(maxInput.value, 10);
        if (!isNaN(value)) {
            slider.value = Math.min(value, parseInt(slider.max, 10));
            updateReadout(slider.value);
        }
    });
})();

// ------------------------------------------------------------------
// 3. AJAX search: filtering, sorting, paging and the search bar all update
//    just the #resultsPanel via fetch() instead of reloading the whole page.
//    Every control it hooks is a real <form>/<a> too, so with JS off the page
//    still works - this only upgrades the experience.
// ------------------------------------------------------------------
(function () {
    var panel = document.getElementById("resultsPanel");
    var searchBar = document.querySelector("form.searchbar");
    var filterForm = document.getElementById("filterForm");
    if (!panel) {
        return;
    }

    var baseUrl = panel.getAttribute("data-results-url") || (searchBar && searchBar.getAttribute("action")) || location.pathname;
    var priceDebounce;

    // --- Build the results URL from whatever the controls currently say ---
    function buildUrl(overrides) {
        var p = new URLSearchParams();
        var set = function (key, value) {
            if (value !== null && value !== undefined && String(value).trim() !== "") {
                p.set(key, String(value).trim());
            }
        };

        var loc = document.getElementById("Location");
        var date = document.getElementById("MoveInDate");
        var cat = document.getElementById("CategoryID");
        var beds = document.getElementById("Bedrooms");
        if (loc) { set("location", loc.value); }
        if (date) { set("moveInDate", date.value); }
        if (cat) { set("categoryID", cat.value); }
        if (beds) { set("bedrooms", beds.value); }

        if (filterForm) {
            var catRadio = filterForm.querySelector('input[name="categoryID"]:checked');
            var bedRadio = filterForm.querySelector('input[name="bedrooms"]:checked');
            if (catRadio) { p.delete("categoryID"); set("categoryID", catRadio.value); }
            if (bedRadio) { p.delete("bedrooms"); set("bedrooms", bedRadio.value); }

            var min = document.getElementById("minPrice");
            var max = document.getElementById("maxPrice");
            if (min) { set("minPrice", min.value); }
            if (max) { set("maxPrice", max.value); }

            var score = filterForm.querySelector('input[name="minScore"]');
            if (score) { set("minScore", score.value); }
            var sort = filterForm.querySelector('input[name="sortBy"]');
            if (sort && sort.value && sort.value !== "recommended") { set("sortBy", sort.value); }

            filterForm.querySelectorAll('input[name="amenityIDs"]:checked').forEach(function (cb, i) {
                p.set("amenityIDs[" + i + "]", cb.value);
            });
        }

        // Changing a filter or the sort always drops you back to page 1; only an
        // explicit override (a pagination link) sets the page.
        Object.keys(overrides || {}).forEach(function (key) {
            var value = overrides[key];
            p.delete(key);
            if (value !== null && value !== undefined) {
                p.set(key, String(value));
            }
        });

        var qs = p.toString();
        return qs ? baseUrl + "?" + qs : baseUrl;
    }

    // --- Fetch a results URL and swap it in ---
    function loadResults(url, pushState) {
        panel.classList.add("is-loading");

        fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (response) {
                if (!response.ok) { throw new Error("Bad status " + response.status); }
                return response.text();
            })
            .then(function (html) {
                panel.innerHTML = html;
                panel.classList.remove("is-loading");

                if (pushState) {
                    history.pushState({ ajaxSearch: true }, "", url);
                }
                syncControls();

                if (panel.getBoundingClientRect().top < 0) {
                    panel.scrollIntoView({ behavior: "smooth", block: "start" });
                }
            })
            .catch(function () {
                // Network / server trouble - fall back to a normal navigation.
                window.location.href = url;
            });
    }

    function applySearch(overrides) {
        loadResults(buildUrl(overrides || {}), true);
    }

    // --- Keep the filter rail and search bar matching the current URL ---
    // (needed after Back/Forward and after removing a filter chip in the results)
    function syncControls() {
        var q = new URLSearchParams(location.search);

        var setValue = function (id, value) {
            var el = document.getElementById(id);
            if (el) { el.value = value || ""; }
        };
        setValue("Location", q.get("location"));
        setValue("MoveInDate", q.get("moveInDate"));
        setValue("CategoryID", q.get("categoryID"));
        setValue("Bedrooms", q.get("bedrooms"));
        setValue("minPrice", q.get("minPrice"));
        setValue("maxPrice", q.get("maxPrice"));

        if (filterForm) {
            var cat = q.get("categoryID") || "";
            var beds = q.get("bedrooms") || "";
            filterForm.querySelectorAll('input[name="categoryID"]').forEach(function (r) {
                r.checked = (r.value === cat);
            });
            filterForm.querySelectorAll('input[name="bedrooms"]').forEach(function (r) {
                r.checked = (r.value === beds);
            });

            var scoreHidden = filterForm.querySelector('input[name="minScore"]');
            if (scoreHidden) { scoreHidden.value = q.get("minScore") || ""; }
            var sortHidden = filterForm.querySelector('input[name="sortBy"]');
            if (sortHidden) { sortHidden.value = q.get("sortBy") || "recommended"; }
            var locHidden = filterForm.querySelector('input[name="location"]');
            if (locHidden) { locHidden.value = q.get("location") || ""; }

            var chosen = q.getAll("amenityIDs").concat(
                [...q.keys()].filter(function (k) { return /^amenityIDs\[\d+\]$/.test(k); }).map(function (k) { return q.get(k); })
            );
            filterForm.querySelectorAll('input[name="amenityIDs"]').forEach(function (cb) {
                cb.checked = chosen.indexOf(cb.value) !== -1;
            });
        }

        var slider = document.getElementById("priceSlider");
        var maxBox = document.getElementById("maxPrice");
        if (slider && maxBox) {
            slider.value = maxBox.value || slider.max;
        }

        var clearAll = document.getElementById("filterClearAll");
        if (clearAll) {
            var hasFilter = ["location", "categoryID", "bedrooms", "minPrice", "maxPrice", "minScore"]
                .some(function (k) { return q.get(k); }) || [...q.keys()].some(function (k) { return k.indexOf("amenityIDs") === 0; });
            clearAll.hidden = !hasFilter;
        }
    }

    // --- Wire it up ---
    if (searchBar) {
        searchBar.addEventListener("submit", function (e) {
            e.preventDefault();
            applySearch();
        });
        ["CategoryID", "Bedrooms"].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) { el.addEventListener("change", function () { applySearch(); }); }
        });
    }

    if (filterForm) {
        filterForm.addEventListener("submit", function (e) {
            e.preventDefault();
            applySearch();
        });
        filterForm.querySelectorAll('input[type="checkbox"], input[type="radio"]').forEach(function (control) {
            control.addEventListener("change", function () { applySearch(); });
        });
        ["minPrice", "maxPrice"].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) {
                el.addEventListener("change", function () {
                    clearTimeout(priceDebounce);
                    priceDebounce = setTimeout(function () { applySearch(); }, 250);
                });
            }
        });
        var slider = document.getElementById("priceSlider");
        if (slider) {
            slider.addEventListener("change", function () { applySearch(); });
        }
    }

    // Sort tabs, score pills, filter-tag removers, "clear all", empty-state reset,
    // pagination and the popular-city chips are all real links - intercept and AJAX them.
    document.addEventListener("click", function (e) {
        var link = e.target.closest("a");
        if (!link || e.metaKey || e.ctrlKey || e.shiftKey) {
            return;
        }
        var hooked = link.matches(
            "#resultsPanel .sort-tab, #resultsPanel .filter-tag a, #resultsPanel .pagination .page-link, " +
            "#resultsPanel .empty-state .btn-outline, .filter-rail .score-pill, #filterClearAll, .chip-row .chip"
        );
        if (!hooked || link.classList.contains("is-disabled") || link.classList.contains("is-current")) {
            return;
        }
        e.preventDefault();
        loadResults(link.href, true);
    });

    window.addEventListener("popstate", function () {
        loadResults(location.href, false);
    });
})();

// ------------------------------------------------------------------
// 4. Header: drop shadow once the page scrolls, plus the account menu
// ------------------------------------------------------------------
(function () {
    var header = document.getElementById("siteHeader");
    if (header) {
        var applyShadow = function () {
            header.classList.toggle("is-stuck", window.scrollY > 4);
        };
        applyShadow();
        window.addEventListener("scroll", applyShadow, { passive: true });
    }

    var trigger = document.getElementById("userMenuTrigger");
    var panel = document.getElementById("userMenuPanel");
    if (!trigger || !panel) {
        return;
    }

    trigger.addEventListener("click", function (e) {
        e.stopPropagation();
        var open = panel.classList.toggle("open");
        trigger.setAttribute("aria-expanded", open ? "true" : "false");
    });

    document.addEventListener("click", function (e) {
        if (!panel.contains(e.target)) {
            panel.classList.remove("open");
            trigger.setAttribute("aria-expanded", "false");
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            panel.classList.remove("open");
            trigger.setAttribute("aria-expanded", "false");
        }
    });
})();

// ------------------------------------------------------------------
// 5. Property gallery lightbox
// ------------------------------------------------------------------
(function () {
    var gallery = document.getElementById("propertyGallery");
    var lightbox = document.getElementById("lightbox");
    var dataEl = document.getElementById("galleryData");
    if (!gallery || !lightbox || !dataEl) {
        return;
    }

    var photos;
    try {
        photos = JSON.parse(dataEl.textContent);
    } catch (err) {
        return;
    }
    if (!photos || !photos.length) {
        return;
    }

    var image = document.getElementById("lightboxImage");
    var counter = document.getElementById("lightboxCounter");
    var current = 0;

    function show(index) {
        current = (index + photos.length) % photos.length;
        image.src = photos[current];
        counter.textContent = (current + 1) + " / " + photos.length;
    }

    function open(index) {
        show(index);
        lightbox.classList.add("open");
        document.body.style.overflow = "hidden";
    }

    function close() {
        lightbox.classList.remove("open");
        document.body.style.overflow = "";
    }

    gallery.querySelectorAll("img").forEach(function (img) {
        img.addEventListener("click", function () {
            open(parseInt(img.getAttribute("data-index"), 10) || 0);
        });
    });

    var moreButton = document.getElementById("galleryMore");
    if (moreButton) {
        moreButton.addEventListener("click", function () { open(0); });
    }

    document.getElementById("lightboxClose").addEventListener("click", close);
    document.getElementById("lightboxPrev").addEventListener("click", function (e) {
        e.stopPropagation();
        show(current - 1);
    });
    document.getElementById("lightboxNext").addEventListener("click", function (e) {
        e.stopPropagation();
        show(current + 1);
    });

    lightbox.addEventListener("click", function (e) {
        if (e.target === lightbox) {
            close();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (!lightbox.classList.contains("open")) {
            return;
        }
        if (e.key === "Escape") { close(); }
        if (e.key === "ArrowLeft") { show(current - 1); }
        if (e.key === "ArrowRight") { show(current + 1); }
    });
})();

// ------------------------------------------------------------------
// 6. Date inputs never accept a move-in date in the past
// ------------------------------------------------------------------
(function () {
    var today = new Date().toISOString().split("T")[0];
    document.querySelectorAll('input[type="date"][name="moveInDate"], input[type="date"][name="MoveInDate"]')
        .forEach(function (input) {
            if (!input.getAttribute("min")) {
                input.setAttribute("min", today);
            }
        });
})();

// ------------------------------------------------------------------
// 7. CAPTCHA reload button: fetch a fresh challenge image without a page reload.
//    The cache-buster query string is what actually forces the new request.
// ------------------------------------------------------------------
(function () {
    var image = document.getElementById("captchaImage");
    var reload = document.getElementById("captchaReload");
    if (!image || !reload) {
        return;
    }

    reload.addEventListener("click", function () {
        var base = image.getAttribute("src").split("?")[0];
        image.setAttribute("src", base + "?t=" + Date.now());
        var field = document.getElementById("Captcha");
        if (field) {
            field.value = "";
            field.focus();
        }
    });
})();

// ------------------------------------------------------------------
// 8. Show / hide password
//    Drops a "Show" toggle inside every password field so a user can check
//    what they typed. Hooks every input[type="password"] on the page (sign in,
//    register, reset, change password), so no per-view markup is needed.
// ------------------------------------------------------------------
(function () {
    var fields = document.querySelectorAll('input[type="password"]');
    if (!fields.length) {
        return;
    }

    fields.forEach(function (input) {
        var wrapper = document.createElement("span");
        wrapper.className = "pw-field";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);

        var toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "pw-toggle";
        toggle.textContent = "Show";
        toggle.setAttribute("aria-label", "Show password");
        toggle.setAttribute("aria-pressed", "false");
        wrapper.appendChild(toggle);

        toggle.addEventListener("click", function () {
            var reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            toggle.textContent = reveal ? "Hide" : "Show";
            toggle.setAttribute("aria-label", reveal ? "Hide password" : "Show password");
            toggle.setAttribute("aria-pressed", reveal ? "true" : "false");
            input.focus();
        });
    });
})();

// ------------------------------------------------------------------
// 9. Admin "needs attention" alert bar
//    The bar is rendered server-side whenever the admin has pending booking / maintenance
//    / support / cancellation items. Dismissing it hides it only until the total changes -
//    a new request brings it straight back.
// ------------------------------------------------------------------
(function () {
    var bar = document.getElementById("adminAlertBar");
    var dismiss = document.getElementById("adminAlertDismiss");
    if (!bar || !dismiss) {
        return;
    }

    var KEY = "adminAlertDismissed";
    var total = bar.getAttribute("data-alert-total") || "";

    function read() {
        try {
            return sessionStorage.getItem(KEY);
        } catch (e) {
            return null;
        }
    }

    if (read() === total) {
        bar.hidden = true;
    }

    dismiss.addEventListener("click", function () {
        bar.hidden = true;
        try {
            sessionStorage.setItem(KEY, total);
        } catch (e) {
            /* private mode - the bar just reappears on the next page, which is fine */
        }
    });
})();
