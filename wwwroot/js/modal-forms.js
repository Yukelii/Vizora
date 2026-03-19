(function () {
    "use strict";

    const modalElement = document.getElementById("appModal");
    const modalBody = document.getElementById("appModalBody");
    const modalTitle = document.getElementById("appModalLabel");

    let loadController = null;
    let submitController = null;
    let lastTrigger = null;
    let validationScriptsPromise = null;

    const api = {
        initializeTransactionCreateForm,
        initializeBudgetCreateForm,
        initializeCategoryIconPicker
    };

    window.VizoraModalForms = api;

    const CATEGORY_ICON_LIBRARY = [
        { key: "shopping-cart", canonical: "shopping_cart" },
        { key: "coffee", canonical: "restaurant" },
        { key: "home", canonical: "home" },
        { key: "car", canonical: "directions_car" },
        { key: "wallet", canonical: "payments" },
        { key: "credit-card", canonical: "payments" },
        { key: "gift", canonical: "shopping_cart" },
        { key: "movie", canonical: "movie" },
        { key: "school", canonical: "school" },
        { key: "plane", canonical: "flight" },
        { key: "tools", canonical: "work" },
        { key: "device-mobile", canonical: "payments" },
        { key: "heart", canonical: "favorite" },
        { key: "basket", canonical: "shopping_cart" },
        { key: "receipt", canonical: "receipt_long" },
        { key: "chart-bar", canonical: "savings" },
        { key: "building", canonical: "home" },
        { key: "truck", canonical: "directions_car" },
        { key: "map-pin", canonical: "directions_car" },
        { key: "book", canonical: "school" },
        { key: "music", canonical: "movie" },
        { key: "camera", canonical: "movie" },
        { key: "pizza", canonical: "restaurant" },
        { key: "bike", canonical: "directions_car" },
        { key: "cash", canonical: "payments" },
        { key: "coin", canonical: "payments" },
        { key: "medical-cross", canonical: "local_hospital" },
        { key: "pill", canonical: "local_hospital" },
        { key: "shield", canonical: "savings" },
        { key: "lock", canonical: "savings" },
        { key: "user", canonical: "favorite" },
        { key: "briefcase", canonical: "work" },
        { key: "building-bank", canonical: "savings" },
        { key: "currency-dollar", canonical: "payments" },
        { key: "chart-pie", canonical: "savings" },
        { key: "calculator", canonical: "savings" },
        { key: "file-invoice", canonical: "receipt_long" },
        { key: "shopping-bag", canonical: "shopping_cart" },
        { key: "tag", canonical: "shopping_cart" },
        { key: "box", canonical: "shopping_cart" },
        { key: "archive", canonical: "receipt_long" },
        { key: "train", canonical: "directions_car" },
        { key: "bus", canonical: "directions_car" },
        { key: "gas-station", canonical: "directions_car" },
        { key: "lamp", canonical: "home" },
        { key: "book-2", canonical: "school" },
        { key: "beach", canonical: "flight" },
        { key: "dog", canonical: "pets" },
        { key: "cat", canonical: "pets" },
        { key: "salad", canonical: "restaurant" },
        { key: "apple", canonical: "restaurant" },
        { key: "chef-hat", canonical: "restaurant" },
        { key: "swimming", canonical: "fitness_center" },
        { key: "barbell", canonical: "fitness_center" },
        { key: "guitar-pick", canonical: "movie" },
        { key: "headphones", canonical: "movie" },
        { key: "video", canonical: "movie" },
        { key: "paint", canonical: "movie" },
        { key: "stethoscope", canonical: "local_hospital" },
        { key: "ambulance", canonical: "local_hospital" },
        { key: "baby-carriage", canonical: "home" },
        { key: "sun", canonical: "favorite" },
        { key: "moon", canonical: "favorite" },
        { key: "battery", canonical: "payments" },
        { key: "wifi", canonical: "work" },
        { key: "globe", canonical: "flight" },
        { key: "camera-plus", canonical: "movie" },
        { key: "notebook", canonical: "work" },
        { key: "language", canonical: "school" },
        { key: "trophy", canonical: "fitness_center" },
        { key: "device-gamepad-2", canonical: "sports_esports" }
    ];

    const QUICK_ICON_COUNT = 17;
    const DEFAULT_CANONICAL_ICON = "receipt_long";
    const canonicalToAliasMap = new Map();
    const aliasToCanonicalMap = new Map();

    CATEGORY_ICON_LIBRARY.forEach((icon) => {
        if (!canonicalToAliasMap.has(icon.canonical)) {
            canonicalToAliasMap.set(icon.canonical, icon.key);
        }

        if (!aliasToCanonicalMap.has(icon.key)) {
            aliasToCanonicalMap.set(icon.key, icon.canonical);
        }
    });

    initializeTransactionCreateForm(document);
    initializeBudgetCreateForm(document);
    initializeCategoryIconPicker(document);

    if (!modalElement || !modalBody || !window.bootstrap || !window.bootstrap.Modal) {
        return;
    }

    const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement, {
        backdrop: true,
        keyboard: true,
        focus: true
    });

    document.addEventListener("click", onDocumentClick);
    modalBody.addEventListener("submit", onModalFormSubmit);
    modalBody.addEventListener("click", onModalBodyClick);

    modalElement.addEventListener("hidden.bs.modal", () => {
        cancelLoadRequest();
        cancelSubmitRequest();
        modalBody.innerHTML = "";
        modalElement.removeAttribute("aria-busy");
        modalElement.classList.remove("vz-modal-size-sm", "vz-modal-size-md", "vz-modal-size-lg", "vz-modal-category");
        modalElement.classList.add("vz-modal-size-md");

        if (lastTrigger && typeof lastTrigger.focus === "function") {
            lastTrigger.focus();
        }

        lastTrigger = null;
    });

    async function onDocumentClick(event) {
        const trigger = event.target.closest("a[data-modal='true']");
        if (!trigger) {
            return;
        }

        if (!shouldInterceptClick(event, trigger)) {
            return;
        }

        const url = trigger.getAttribute("href");
        if (!url) {
            return;
        }

        event.preventDefault();
        lastTrigger = trigger;

        const defaultTitle = trigger.textContent ? trigger.textContent.trim() : "Create";
        const title = trigger.getAttribute("data-modal-title") || defaultTitle || "Create";
        await openModalFromUrl(url, title);
    }

    function onModalBodyClick(event) {
        const closeTrigger = event.target.closest("[data-modal-close='true']");
        if (!closeTrigger) {
            return;
        }

        event.preventDefault();
        modal.hide();
    }

    async function onModalFormSubmit(event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        event.preventDefault();
        await submitModalForm(form);
    }

    async function openModalFromUrl(url, title) {
        cancelLoadRequest();
        cancelSubmitRequest();

        loadController = new AbortController();
        modalTitle.textContent = title;
        modalElement.setAttribute("aria-busy", "true");
        modalBody.innerHTML = "<div class=\"py-4 text-center text-muted\">Loading form...</div>";
        modal.show();

        try {
            const response = await fetch(url, {
                method: "GET",
                credentials: "same-origin",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                },
                signal: loadController.signal
            });

            if (!response.ok) {
                throw new Error("Failed to load modal form.");
            }

            const html = await response.text();
            modalBody.innerHTML = html;
            await initializeModalContent(modalBody);
            focusFirstInput(modalBody);
        } catch (error) {
            if (isAbortError(error)) {
                return;
            }

            modalBody.innerHTML =
                "<div class=\"alert alert-danger\" role=\"alert\">Unable to load the form. Please try again.</div>";
        } finally {
            modalElement.removeAttribute("aria-busy");
            loadController = null;
        }
    }

    async function submitModalForm(form) {
        cancelSubmitRequest();
        submitController = new AbortController();

        setFormSubmitting(form, true);

        try {
            const action = form.getAttribute("action") || window.location.href;
            const method = (form.getAttribute("method") || "post").toUpperCase();
            const formData = new FormData(form);

            const response = await fetch(action, {
                method,
                credentials: "same-origin",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                },
                body: formData,
                signal: submitController.signal
            });

            if (response.redirected) {
                modal.hide();
                window.location.reload();
                return;
            }

            const html = await response.text();
            modalBody.innerHTML = html;
            await initializeModalContent(modalBody);
            focusFirstInput(modalBody);
        } catch (error) {
            if (isAbortError(error)) {
                return;
            }

            const feedback = document.createElement("div");
            feedback.className = "alert alert-danger";
            feedback.setAttribute("role", "alert");
            feedback.textContent = "Unable to submit the form. Please try again.";
            modalBody.prepend(feedback);
        } finally {
            if (form.isConnected) {
                setFormSubmitting(form, false);
            }

            submitController = null;
        }
    }

    async function initializeModalContent(container) {
        initializeTransactionCreateForm(container);
        initializeBudgetCreateForm(container);
        initializeCategoryIconPicker(container);
        synchronizeModalLayoutContext(container);
        await initializeClientValidation(container);
    }

    function synchronizeModalLayoutContext(container) {
        if (!(modalElement instanceof HTMLElement)) {
            return;
        }

        const sizeHost = container.querySelector("[data-vz-modal-size]");
        const requestedSize = (sizeHost?.getAttribute("data-vz-modal-size") || "md").trim().toLowerCase();
        const normalizedSize =
            requestedSize === "sm" || requestedSize === "md" || requestedSize === "lg"
                ? requestedSize
                : "md";

        modalElement.classList.remove("vz-modal-size-sm", "vz-modal-size-md", "vz-modal-size-lg");
        modalElement.classList.add(`vz-modal-size-${normalizedSize}`);
    }

    async function initializeClientValidation(container) {
        try {
            await ensureValidationScriptsLoaded();
        } catch {
            return;
        }

        if (!window.jQuery || !window.jQuery.validator || !window.jQuery.validator.unobtrusive) {
            return;
        }

        const form = container.querySelector("form");
        if (!form) {
            return;
        }

        const $form = window.jQuery(form);
        $form.removeData("validator");
        $form.removeData("unobtrusiveValidation");
        window.jQuery.validator.unobtrusive.parse(form);
    }

    function ensureValidationScriptsLoaded() {
        if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
            return Promise.resolve();
        }

        if (validationScriptsPromise) {
            return validationScriptsPromise;
        }

        validationScriptsPromise = loadScriptOnce("/lib/jquery-validation/dist/jquery.validate.min.js")
            .then(() => loadScriptOnce("/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js"))
            .catch((error) => {
                validationScriptsPromise = null;
                throw error;
            });

        return validationScriptsPromise;
    }

    function loadScriptOnce(src) {
        const existing = Array.from(document.querySelectorAll("script[src]"))
            .some((script) => script.getAttribute("src") === src || script.getAttribute("src")?.endsWith(src));

        if (existing) {
            return Promise.resolve();
        }

        return new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = src;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error(`Failed to load script: ${src}`));
            document.head.appendChild(script);
        });
    }

    function initializeTransactionCreateForm(root) {
        const form = root.querySelector(
            "form[data-modal-form='create-transaction'], form[data-modal-form='edit-transaction'], form[action$='/Transactions/Create'], form[action$='/Transactions/Edit']"
        );
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        if (form.dataset.vzTransactionInit === "1") {
            return;
        }

        const categorySelect = form.querySelector("#CategoryId");
        const derivedTypeContainer = form.querySelector("#derivedTypeContainer");
        const derivedTypeBadge = form.querySelector("#derivedTypeBadge");
        const categoryMapElement = form.querySelector("#CategoryTypeMapJson");
        const incomeTypeChip = form.querySelector("[data-vz-role='type-chip-income']");
        const expenseTypeChip = form.querySelector("[data-vz-role='type-chip-expense']");

        if (!(categorySelect instanceof HTMLSelectElement) ||
            !(categoryMapElement instanceof HTMLInputElement)) {
            return;
        }

        let categoryTypeMap = {};
        try {
            categoryTypeMap = JSON.parse(categoryMapElement.value || "{}");
        } catch {
            categoryTypeMap = {};
        }

        const applyTypeToggleState = (type) => {
            if (incomeTypeChip instanceof HTMLElement) {
                const isIncome = type === "Income";
                incomeTypeChip.classList.toggle("is-active", isIncome);
                incomeTypeChip.setAttribute("aria-pressed", isIncome ? "true" : "false");
            }

            if (expenseTypeChip instanceof HTMLElement) {
                const isExpense = type === "Expense";
                expenseTypeChip.classList.toggle("is-active", isExpense);
                expenseTypeChip.setAttribute("aria-pressed", isExpense ? "true" : "false");
            }
        };

        const updateTypeBadge = () => {
            const mapValue = categoryTypeMap[categorySelect.value];
            const type = mapValue === "Income" || mapValue === "Expense" ? mapValue : null;

            if (derivedTypeContainer instanceof HTMLElement && derivedTypeBadge instanceof HTMLElement) {
                const keepVisibleWhenEmpty = derivedTypeContainer.dataset.vzPersist === "true";

                if (!type) {
                    if (keepVisibleWhenEmpty) {
                        derivedTypeContainer.classList.remove("d-none");
                        derivedTypeBadge.textContent = "Select category";
                        derivedTypeBadge.className = "badge text-bg-secondary";
                    } else {
                        derivedTypeContainer.classList.add("d-none");
                        derivedTypeBadge.textContent = "";
                        derivedTypeBadge.className = "badge";
                    }
                } else {
                    derivedTypeContainer.classList.remove("d-none");
                    derivedTypeBadge.textContent = type;
                    derivedTypeBadge.className = type === "Income" ? "badge text-bg-success" : "badge text-bg-danger";
                }
            }

            applyTypeToggleState(type);
        };

        categorySelect.addEventListener("change", updateTypeBadge);
        updateTypeBadge();
        form.dataset.vzTransactionInit = "1";
    }

    function initializeBudgetCreateForm(root) {
        const form = root.querySelector(
            "form[data-modal-form='create-budget'], form[data-modal-form='edit-budget'], form[action$='/Budgets/Create'], form[action$='/Budgets/Edit']"
        );
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        if (form.dataset.vzBudgetInit === "1") {
            return;
        }

        const periodTypeInput = form.querySelector("#PeriodType");
        const startDateInput = form.querySelector("#StartDate");
        const endDateInput = form.querySelector("#EndDate");

        if (!(periodTypeInput instanceof HTMLSelectElement) ||
            !(startDateInput instanceof HTMLInputElement) ||
            !(endDateInput instanceof HTMLInputElement)) {
            return;
        }

        const formatDate = (value) => {
            const year = value.getFullYear();
            const month = String(value.getMonth() + 1).padStart(2, "0");
            const day = String(value.getDate()).padStart(2, "0");
            return `${year}-${month}-${day}`;
        };

        const applyPeriodDefaults = () => {
            if (!startDateInput.value) {
                return;
            }

            const selectedStartDate = new Date(`${startDateInput.value}T00:00:00`);
            if (Number.isNaN(selectedStartDate.getTime())) {
                return;
            }

            if (periodTypeInput.value === "2") {
                const monthStart = new Date(selectedStartDate.getFullYear(), selectedStartDate.getMonth(), 1);
                const monthEnd = new Date(selectedStartDate.getFullYear(), selectedStartDate.getMonth() + 1, 0);
                startDateInput.value = formatDate(monthStart);
                endDateInput.value = formatDate(monthEnd);
                return;
            }

            if (periodTypeInput.value === "1") {
                const weekEnd = new Date(selectedStartDate);
                weekEnd.setDate(weekEnd.getDate() + 6);
                endDateInput.value = formatDate(weekEnd);
            }
        };

        periodTypeInput.addEventListener("change", applyPeriodDefaults);
        startDateInput.addEventListener("change", applyPeriodDefaults);
        applyPeriodDefaults();
        form.dataset.vzBudgetInit = "1";
    }

    function initializeCategoryIconPicker(root) {
        const forms = root.querySelectorAll(
            "form[data-modal-form='create-category'], form[data-modal-form='edit-category'], form[action$='/Categories/Create'], form[action$='/Categories/Edit']"
        );

        forms.forEach((form) => {
            if (!(form instanceof HTMLFormElement) || form.dataset.vzCategoryIconInit === "1") {
                return;
            }

            const iconInput = form.querySelector("input[data-vz-icon-value]");
            const selectedLabel = form.querySelector("[data-vz-selected-icon-label]");
            const iconGrid = form.querySelector("[data-vz-icon-grid]");
            const iconLibraryGrid = form.querySelector("[data-vz-icon-library-grid]");
            const libraryModal = form.querySelector("[data-vz-icon-library-modal]");
            const searchInput = form.querySelector("[data-vz-icon-search]");
            const searchEmpty = form.querySelector("[data-vz-icon-search-empty]");

            if (!(iconGrid instanceof HTMLElement) ||
                !(iconLibraryGrid instanceof HTMLElement) ||
                !(iconInput instanceof HTMLInputElement)) {
                return;
            }

            iconGrid.innerHTML = "";
            iconLibraryGrid.innerHTML = "";

            const quickIcons = CATEGORY_ICON_LIBRARY.slice(0, QUICK_ICON_COUNT);
            const quickIconKeys = new Set(quickIcons.map((icon) => icon.key));

            quickIcons.forEach((icon) => {
                iconGrid.appendChild(createIconButton(icon, false));
            });

            const openLibraryButton = document.createElement("button");
            openLibraryButton.type = "button";
            openLibraryButton.className = "vz-cat-icon-option vz-cat-icon-library-trigger";
            openLibraryButton.dataset.vzOpenIconLibrary = "true";
            openLibraryButton.setAttribute("aria-label", "Open icon library");
            openLibraryButton.setAttribute("title", "Open icon library");
            openLibraryButton.textContent = "+";
            iconGrid.appendChild(openLibraryButton);

            CATEGORY_ICON_LIBRARY.forEach((icon) => {
                iconLibraryGrid.appendChild(createIconButton(icon, true));
            });

            const iconButtons = Array.from(form.querySelectorAll("[data-vz-icon-button]"));
            const libraryButtons = Array.from(form.querySelectorAll("[data-vz-icon-library-button]"));
            const startingCanonical = normalizeCanonicalIcon(iconInput.value);
            let selectedAlias =
                canonicalToAliasMap.get(startingCanonical) ||
                canonicalToAliasMap.get(DEFAULT_CANONICAL_ICON) ||
                CATEGORY_ICON_LIBRARY[0].key;
            let returnFocusTarget = null;

            const updateSelectionState = () => {
                const selectedCanonical = aliasToCanonicalMap.get(selectedAlias) || DEFAULT_CANONICAL_ICON;
                iconInput.value = selectedCanonical;

                iconButtons.forEach((button) => {
                    if (!(button instanceof HTMLButtonElement)) {
                        return;
                    }

                    const isSelected = (button.getAttribute("data-vz-icon-key") || "") === selectedAlias;
                    button.classList.toggle("is-selected", isSelected);
                    button.setAttribute("aria-pressed", isSelected ? "true" : "false");
                });

                if (selectedLabel instanceof HTMLElement) {
                    selectedLabel.textContent = formatIconLabel(selectedAlias);
                }

                openLibraryButton.classList.toggle("is-selected", !quickIconKeys.has(selectedAlias));
                openLibraryButton.setAttribute("aria-pressed", !quickIconKeys.has(selectedAlias) ? "true" : "false");
            };

            const closeLibrary = () => {
                if (!(libraryModal instanceof HTMLElement)) {
                    return;
                }

                libraryModal.hidden = true;

                if (searchInput instanceof HTMLInputElement) {
                    searchInput.value = "";
                    filterLibraryIcons();
                }

                if (returnFocusTarget instanceof HTMLElement && typeof returnFocusTarget.focus === "function") {
                    returnFocusTarget.focus();
                }

                returnFocusTarget = null;
            };

            const openLibrary = () => {
                if (!(libraryModal instanceof HTMLElement)) {
                    return;
                }

                returnFocusTarget = document.activeElement instanceof HTMLElement ? document.activeElement : null;
                libraryModal.hidden = false;

                if (searchInput instanceof HTMLInputElement) {
                    searchInput.value = "";
                    filterLibraryIcons();
                    setTimeout(() => searchInput.focus(), 0);
                } else {
                    const selectedButton = libraryButtons.find((button) =>
                        (button.getAttribute("data-vz-icon-key") || "") === selectedAlias
                    );

                    if (selectedButton instanceof HTMLElement) {
                        setTimeout(() => selectedButton.focus(), 0);
                    }
                }
            };

            const filterLibraryIcons = () => {
                if (!(searchInput instanceof HTMLInputElement)) {
                    return;
                }

                const query = searchInput.value.trim().toLowerCase();
                let visibleCount = 0;

                libraryButtons.forEach((button) => {
                    const key = (button.getAttribute("data-vz-icon-key") || "").toLowerCase();
                    const label = (button.getAttribute("data-vz-icon-label") || "").toLowerCase();
                    const isVisible = !query || key.includes(query) || label.includes(query);

                    button.classList.toggle("d-none", !isVisible);

                    if (isVisible) {
                        visibleCount += 1;
                    }
                });

                if (searchEmpty instanceof HTMLElement) {
                    searchEmpty.classList.toggle("d-none", visibleCount !== 0);
                }
            };

            const pickIcon = (button) => {
                const iconAlias = button.getAttribute("data-vz-icon-key");
                if (!iconAlias) {
                    return;
                }

                selectedAlias = iconAlias;
                updateSelectionState();

                if (button.hasAttribute("data-vz-icon-library-button")) {
                    closeLibrary();
                }
            };

            iconButtons.forEach((button) => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.addEventListener("click", () => pickIcon(button));
            });

            openLibraryButton.addEventListener("click", openLibrary);

            form.querySelectorAll("[data-vz-icon-library-close]").forEach((button) => {
                if (!(button instanceof HTMLElement)) {
                    return;
                }

                button.addEventListener("click", (event) => {
                    event.preventDefault();
                    closeLibrary();
                });
            });

            if (searchInput instanceof HTMLInputElement) {
                searchInput.addEventListener("input", filterLibraryIcons);
            }

            if (libraryModal instanceof HTMLElement) {
                libraryModal.addEventListener("keydown", (event) => {
                    if (event.key === "Escape") {
                        event.preventDefault();
                        closeLibrary();
                    }
                });
            }

            updateSelectionState();
            form.dataset.vzCategoryIconInit = "1";
        });
    }

    function createIconButton(icon, isLibraryButton) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = isLibraryButton
            ? "vz-cat-icon-option vz-cat-icon-library-option"
            : "vz-cat-icon-option";
        button.dataset.vzIconButton = "true";
        button.dataset.vzIconKey = icon.key;
        button.dataset.vzIconLabel = formatIconLabel(icon.key);

        if (isLibraryButton) {
            button.dataset.vzIconLibraryButton = "true";
        }

        const iconElement = document.createElement("i");
        iconElement.className = `ti ti-${icon.key}`;
        iconElement.setAttribute("aria-hidden", "true");
        button.appendChild(iconElement);

        const iconLabel = formatIconLabel(icon.key);
        button.setAttribute("aria-label", iconLabel);
        button.setAttribute("title", iconLabel);

        return button;
    }

    function formatIconLabel(iconKey) {
        const words = iconKey.split(/[-_]/).filter(Boolean);
        return words
            .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
            .join(" ");
    }

    function normalizeCanonicalIcon(iconValue) {
        const normalized = (iconValue || "").trim().toLowerCase();
        if (!normalized) {
            return DEFAULT_CANONICAL_ICON;
        }

        return normalized;
    }

    function focusFirstInput(container) {
        const firstInput = container.querySelector(
            "input:not([type='hidden']):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])"
        );

        if (!(firstInput instanceof HTMLElement)) {
            return;
        }

        setTimeout(() => firstInput.focus(), 0);
    }

    function setFormSubmitting(form, isSubmitting) {
        const submitButtons = form.querySelectorAll("button[type='submit'], input[type='submit']");
        submitButtons.forEach((button) => {
            button.disabled = isSubmitting;
        });
    }

    function shouldInterceptClick(event, trigger) {
        if (event.defaultPrevented || event.button !== 0) {
            return false;
        }

        if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return false;
        }

        const target = trigger.getAttribute("target");
        if (target && target !== "_self") {
            return false;
        }

        if (trigger.hasAttribute("download")) {
            return false;
        }

        return true;
    }

    function cancelLoadRequest() {
        if (loadController) {
            loadController.abort();
            loadController = null;
        }
    }

    function cancelSubmitRequest() {
        if (submitController) {
            submitController.abort();
            submitController = null;
        }
    }

    function isAbortError(error) {
        return error instanceof DOMException && error.name === "AbortError";
    }
})();
