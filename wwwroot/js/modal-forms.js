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
        initializeBudgetCreateForm
    };

    window.VizoraModalForms = api;

    initializeTransactionCreateForm(document);
    initializeBudgetCreateForm(document);

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
        await initializeClientValidation(container);
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

        if (!(categorySelect instanceof HTMLSelectElement) ||
            !(derivedTypeContainer instanceof HTMLElement) ||
            !(derivedTypeBadge instanceof HTMLElement) ||
            !(categoryMapElement instanceof HTMLInputElement)) {
            return;
        }

        let categoryTypeMap = {};
        try {
            categoryTypeMap = JSON.parse(categoryMapElement.value || "{}");
        } catch {
            categoryTypeMap = {};
        }

        const updateTypeBadge = () => {
            const type = categoryTypeMap[categorySelect.value];

            if (!type) {
                derivedTypeContainer.classList.add("d-none");
                derivedTypeBadge.textContent = "";
                derivedTypeBadge.className = "badge";
                return;
            }

            derivedTypeContainer.classList.remove("d-none");
            derivedTypeBadge.textContent = type;
            derivedTypeBadge.className = type === "Income" ? "badge bg-success" : "badge bg-danger";
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
