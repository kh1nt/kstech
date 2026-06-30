document.addEventListener("DOMContentLoaded", () => {
  const sidebar = document.querySelector(".sidebar");
  const mainContent = document.querySelector("main");
  const sidebarToggle = document.querySelector(".sidebar-toggle");
  const sidebarToggleIcon = document.querySelector(".sidebar-toggle-icon");
  const sidebarTexts = document.querySelectorAll(".sidebar-text");
  const kstechLogoText = document.querySelector(
    ".sidebar-header span.text-3xl",
  );

  let isCollapsed = sessionStorage.getItem("sidebarCollapsed") === "true";

  function setDesktopToggleIcon(collapsed) {
    if (!sidebarToggle || !sidebarToggleIcon) {
      return;
    }

    sidebarToggleIcon.textContent = collapsed ? "menu" : "menu_open";
    sidebarToggle.setAttribute(
      "aria-label",
      collapsed ? "Open sidebar" : "Collapse sidebar",
    );
    sidebarToggle.setAttribute(
      "title",
      collapsed ? "Open sidebar" : "Collapse sidebar",
    );
  }

  function applyCollapsedState() {
    if (!sidebar || !mainContent) {
      return;
    }

    sidebar.classList.remove("w-64");
    sidebar.classList.add("w-20");
    mainContent.classList.remove("ml-64");
    mainContent.classList.add("ml-20");
    sidebarTexts.forEach((text) => text.classList.add("hidden"));
    if (kstechLogoText) {
      kstechLogoText.classList.add("hidden");
    }
    setDesktopToggleIcon(true);
  }

  function applyExpandedState() {
    if (!sidebar || !mainContent) {
      return;
    }

    sidebar.classList.remove("w-20");
    sidebar.classList.add("w-64");
    mainContent.classList.remove("ml-20");
    mainContent.classList.add("ml-64");
    sidebarTexts.forEach((text) => text.classList.remove("hidden"));
    if (kstechLogoText) {
      kstechLogoText.classList.remove("hidden");
    }
    setDesktopToggleIcon(false);
  }

  if (isCollapsed) {
    applyCollapsedState();
  } else {
    setDesktopToggleIcon(false);
  }

  if (sidebar && mainContent && sidebarToggle) {
    sidebarToggle.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();

      isCollapsed = !isCollapsed;
      if (isCollapsed) {
        applyCollapsedState();
      } else {
        applyExpandedState();
      }

      sessionStorage.setItem(
        "sidebarCollapsed",
        isCollapsed ? "true" : "false",
      );
    });
  }

  const mobileOpenBtn = document.querySelector(".sidebar-open-mobile");
  const mobileCloseBtn = document.querySelector(".sidebar-close-mobile");
  const overlay = document.querySelector(".sidebar-overlay");
  const mobileViewport = window.matchMedia("(max-width: 767.98px)");

  function isMobileSidebarOpen() {
    return Boolean(sidebar?.classList.contains("sidebar-open"));
  }

  function setMobileSidebarState(isOpen) {
    if (!sidebar || !overlay) {
      return;
    }

    sidebar.classList.toggle("sidebar-open", isOpen);
    overlay.classList.toggle("hidden", !isOpen);
    document.body.classList.toggle("mobile-sidebar-open", isOpen);
  }

  function toggleMobileSidebar(forceOpen) {
    const nextState =
      typeof forceOpen === "boolean" ? forceOpen : !isMobileSidebarOpen();
    setMobileSidebarState(nextState);
  }

  if (mobileOpenBtn) {
    mobileOpenBtn.addEventListener("click", (event) => {
      event.preventDefault();
      toggleMobileSidebar(true);
    });
  }

  if (mobileCloseBtn) {
    mobileCloseBtn.addEventListener("click", (event) => {
      event.preventDefault();
      toggleMobileSidebar(false);
    });
  }

  if (overlay) {
    overlay.addEventListener("click", () => toggleMobileSidebar(false));
  }

  if (mobileViewport.addEventListener) {
    mobileViewport.addEventListener("change", (event) => {
      if (!event.matches) {
        setMobileSidebarState(false);
      }
    });
  } else {
    window.addEventListener("resize", () => {
      if (!mobileViewport.matches) {
        setMobileSidebarState(false);
      }
    });
  }

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && isMobileSidebarOpen()) {
      setMobileSidebarState(false);
    }
  });

  const storeMobileToggle = document.querySelector(".mobile-menu-toggle");
  const storeMobileMenu = document.querySelector(".mobile-menu");

  if (storeMobileToggle && storeMobileMenu) {
    const setStoreMenuState = (isOpen) => {
      storeMobileMenu.classList.toggle("hidden", !isOpen);
      storeMobileToggle.setAttribute(
        "aria-expanded",
        isOpen ? "true" : "false",
      );
    };

    const toggleStoreMenu = (forceOpen) => {
      const nextState =
        typeof forceOpen === "boolean"
          ? forceOpen
          : storeMobileMenu.classList.contains("hidden");
      setStoreMenuState(nextState);
    };

    storeMobileToggle.addEventListener("click", () => {
      toggleStoreMenu();
    });

    storeMobileMenu.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => toggleStoreMenu(false));
    });

    const resetStoreMenuForDesktop = () => {
      if (window.innerWidth >= 768) {
        setStoreMenuState(false);
      }
    };

    window.addEventListener("resize", resetStoreMenuForDesktop);
    setStoreMenuState(!storeMobileMenu.classList.contains("hidden"));
    resetStoreMenuForDesktop();
  }

  document.addEventListener("click", (event) => {
    const clickTarget = event.target instanceof Element ? event.target : null;
    if (!clickTarget) {
      return;
    }

    const toggleButton = clickTarget.closest("[data-toggle-password]");
    if (!toggleButton) {
      return;
    }

    const targetId = toggleButton.getAttribute("data-toggle-password");
    const targetInput = targetId ? document.getElementById(targetId) : null;
    if (!targetInput) {
      return;
    }

    const shouldShow = targetInput.type === "password";
    targetInput.type = shouldShow ? "text" : "password";

    const toggleIcon = toggleButton.querySelector("i");
    if (toggleIcon) {
      toggleIcon.classList.toggle("bi-eye", !shouldShow);
      toggleIcon.classList.toggle("bi-eye-slash", shouldShow);
    }

    toggleButton.setAttribute(
      "aria-label",
      shouldShow ? "Hide password" : "Show password",
    );
    toggleButton.setAttribute(
      "title",
      shouldShow ? "Hide password" : "Show password",
    );
  });

  const authLiveInputs = document.querySelectorAll("input.auth-live-input");

  const syncAuthInputState = (input, markTouched = false) => {
    if (!(input instanceof HTMLInputElement)) {
      return;
    }

    if (markTouched) {
      input.dataset.touched = "true";
    }

    const matchTargetId = input.dataset.matches;
    if (matchTargetId) {
      const matchTarget = document.getElementById(matchTargetId);
      const shouldCompare =
        Boolean(input.value) || Boolean(matchTarget?.value) || input.dataset.touched === "true";

      if (
        shouldCompare &&
        matchTarget instanceof HTMLInputElement &&
        input.value !== matchTarget.value
      ) {
        input.setCustomValidity("Values do not match.");
      } else {
        input.setCustomValidity("");
      }
    }

    const shouldShowState =
      input.dataset.touched === "true" || input.value.trim().length > 0;

    input.classList.remove("field-status-valid", "field-status-invalid");
    if (!shouldShowState) {
      input.removeAttribute("aria-invalid");
      return;
    }

    if (input.checkValidity()) {
      input.classList.add("field-status-valid");
      input.setAttribute("aria-invalid", "false");
    } else {
      input.classList.add("field-status-invalid");
      input.setAttribute("aria-invalid", "true");
    }
  };

  authLiveInputs.forEach((input) => {
    syncAuthInputState(input);

    input.addEventListener("input", () => {
      syncAuthInputState(input, true);

      if (input.id) {
        document
          .querySelectorAll(`input.auth-live-input[data-matches="${input.id}"]`)
          .forEach((dependentInput) => {
            syncAuthInputState(dependentInput);
          });
      }
    });

    input.addEventListener("blur", () => {
      syncAuthInputState(input, true);
    });
  });

  const nonTransactionalPostActions = new Set([
    "login",
    "logout",
    "register",
    "verifycode",
    "verifymfa",
    "forgotpassword",
    "resetpassword",
    "profile",
    "changepassword",
    "updateprofile",
    "selectownerscope",
    "clearownerscope",
    "addtocart",
    "updatequantity",
    "removefromcart",
    "clearcart",
  ]);

  const transactionConfirmMessages = {
    addproduct: "Add this product to inventory?",
    editproduct: "Save changes to this product?",
    archiveproduct: "Archive this product?",
    unarchiveproduct: "Restore this product to active inventory?",
    refreshmarketprices: "Refresh market prices now?",
    createprocurement: "Create this procurement draft purchase order?",
    createautoreorderdraftpurchaseorder:
      "Create an auto-reorder draft purchase order?",
    approveprocurement: "Approve this draft purchase order?",
    deletedraftprocurement:
      "Delete this draft purchase order? This cannot be undone.",
    receiveprocurement: "Confirm receiving this procurement order?",
    processpayment: "Confirm and process this payment?",
    checkout: "Proceed to checkout and place this order?",
    clearcart: "Clear all items from this cart?",
    updatequantity: "Update this cart item quantity?",
    removefromcart: "Remove this item from the cart?",
    unlinksteam: "Unlink your Steam account from this profile?",
    contact: "Send this inquiry to support?",
    createcampaign: "Create this campaign?",
    executecampaign: "Execute this campaign now?",
    cancelcampaign: "Cancel this campaign?",
    sendquickmessage: "Send this quick message?",
    replyinquiry: "Send this inquiry reply?",
    resolveinquiry: "Mark this inquiry as resolved?",
    savefinancialbudget: "Save this financial budget entry?",
    archivefinancialbudget: "Archive this budget entry?",
    restorefinancialbudget: "Restore this archived budget entry?",
    refreshmarketdata: "Refresh market data now?",
    updateproductprice: "Apply this product price update?",
    cancelorder: "Cancel this order?",
    refundorder: "Mark this order as refunded?",
    create: "Create this record?",
    edit: "Save changes to this record?",
    archive: "Archive this record?",
    unarchive: "Unarchive this record?",
    deactivate: "Deactivate this record?",
    reactivate: "Reactivate this record?",
  };

  function getFormActionKey(form) {
    const action = form.getAttribute("action") || form.action || "";
    if (!action) {
      return "";
    }

    try {
      const url = new URL(action, window.location.href);
      const segments = url.pathname.split("/").filter(Boolean);
      return segments.length ? segments[segments.length - 1].toLowerCase() : "";
    } catch {
      return "";
    }
  }

  function hasInlineConfirm(form) {
    const onSubmit = form.getAttribute("onsubmit");
    return typeof onSubmit === "string" && onSubmit.toLowerCase().includes("confirm(");
  }

  function getTransactionConfirmMessage(form, submitter) {
    const submitterMessage = submitter?.getAttribute("data-confirm-message");
    if (submitterMessage) {
      return submitterMessage;
    }

    const formMessage = form.getAttribute("data-confirm-message");
    if (formMessage) {
      return formMessage;
    }

    const actionKey = getFormActionKey(form);
    if (!actionKey || nonTransactionalPostActions.has(actionKey)) {
      return "";
    }

    return (
      transactionConfirmMessages[actionKey] ||
      "Are you sure you want to continue this transaction?"
    );
  }

  document.addEventListener(
    "submit",
    (event) => {
      const form = event.target;
      if (!(form instanceof HTMLFormElement)) {
        return;
      }

      if (form.method.toUpperCase() !== "POST") {
        return;
      }

      if (
        form.id === "store-anti-forgery-form" ||
        form.dataset.skipConfirm === "true" ||
        form.classList.contains("js-store-add-to-cart") ||
        hasInlineConfirm(form)
      ) {
        return;
      }

      const submitter =
        event.submitter instanceof HTMLElement ? event.submitter : null;
      if (submitter?.getAttribute("data-skip-confirm") === "true") {
        return;
      }

      const message = getTransactionConfirmMessage(form, submitter);
      if (!message) {
        return;
      }

      if (!window.confirm(message)) {
        event.preventDefault();
        event.stopPropagation();
      }
    },
    true,
  );

  const storeConfig = document.getElementById("store-cart-config");
  const cartModal = document.getElementById("store-cart-modal");
  const cartModalBody = document.getElementById("store-cart-modal-body");
  const cartModalSubtitle = document.getElementById(
    "store-cart-modal-subtitle",
  );
  const cartModalSubtotal = document.getElementById(
    "store-cart-modal-subtotal",
  );
  const cartPrimaryAction = document.getElementById(
    "store-cart-modal-primary-action",
  );
  const cartTriggers = document.querySelectorAll("[data-store-cart-trigger]");
  const cartCloseButtons = document.querySelectorAll(".js-store-cart-close");
  const cartCountBadges = document.querySelectorAll("[data-store-cart-count]");
  const globalAntiForgeryInput = document.querySelector(
    '#store-anti-forgery-form input[name="__RequestVerificationToken"]',
  );

  if (
    !storeConfig ||
    !cartModal ||
    !cartModalBody ||
    !cartModalSubtitle ||
    !cartModalSubtotal ||
    !cartPrimaryAction
  ) {
    return;
  }

  const miniUrl = storeConfig.dataset.miniUrl || "/Cart/Mini";
  const removeUrl = storeConfig.dataset.removeUrl || "/Cart/RemoveFromCart";
  const clearUrl = storeConfig.dataset.clearUrl || "/Cart/ClearCart";
  const cartUrl = storeConfig.dataset.cartUrl || "/Cart";
  const loginUrl = storeConfig.dataset.loginUrl || "/Store/Login";
  const detailsUrlTemplate =
    storeConfig.dataset.detailsUrlTemplate || "/Store/Details/__id__";
  const clearCartButton = document.getElementById("store-cart-modal-clear");

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#39;");
  }

  function getAntiForgeryToken(form) {
    const formToken = form?.querySelector(
      'input[name="__RequestVerificationToken"]',
    )?.value;
    if (formToken) {
      return formToken;
    }

    return globalAntiForgeryInput?.value || "";
  }

  function showToast(message, isError = false) {
    if (!message) {
      return;
    }

    const toast = document.createElement("div");
    toast.className = `fixed bottom-5 left-1/2 -translate-x-1/2 z-[90] rounded-xl px-4 py-2 text-sm font-semibold shadow-lg ${
      isError ? "bg-red-600 text-white" : "bg-store-ink text-white"
    }`;
    toast.textContent = message;
    document.body.appendChild(toast);

    setTimeout(() => {
      toast.remove();
    }, 2200);
  }

  function setCartBadgeCount(itemCount) {
    cartCountBadges.forEach((badge) => {
      badge.textContent = String(itemCount);
      if (itemCount > 0) {
        badge.classList.remove("hidden");
      } else {
        badge.classList.add("hidden");
      }
    });
  }

  function setModalLoadingState(message) {
    cartModalBody.innerHTML = `<div class="h-full grid place-items-center text-store-muted text-sm">${escapeHtml(message)}</div>`;
  }

  function renderCartItems(items) {
    if (!items || items.length === 0) {
      cartModalBody.innerHTML = `
        <div class="h-full grid place-items-center text-center">
          <div>
            <span class="material-icons-outlined text-4xl text-slate-300">shopping_cart</span>
            <p class="text-store-ink font-semibold mt-2 mb-1">Your cart is empty</p>
            <p class="text-sm text-store-muted mb-0">Add products while you browse.</p>
          </div>
        </div>`;
      return;
    }

    cartModalBody.innerHTML = items
      .map((item) => {
        const detailsUrl = detailsUrlTemplate.replace(
          "__id__",
          encodeURIComponent(item.productId),
        );
        const image = item.imageUrl
          ? `<img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.productName)}" class="w-full h-full object-cover" />`
          : '<span class="material-icons-outlined text-slate-300 text-3xl">image</span>';

        return `
        <article class="border border-slate-200 rounded-xl p-3 mb-3">
          <div class="flex items-start gap-3">
            <a href="${escapeHtml(detailsUrl)}" class="block w-16 h-16 bg-store-surface rounded-lg overflow-hidden shrink-0 text-center no-underline">
              <div class="w-full h-full flex items-center justify-center">${image}</div>
            </a>
            <div class="flex-1 min-w-0">
              <a href="${escapeHtml(detailsUrl)}" class="font-semibold text-sm text-store-ink no-underline hover:text-store-accent line-clamp-2">${escapeHtml(item.productName)}</a>
              <p class="text-xs text-store-muted mb-1">Qty ${escapeHtml(item.quantity)} &middot; ${escapeHtml(item.priceDisplay)}</p>
              <p class="text-sm font-semibold text-store-ink mb-0">${escapeHtml(item.totalDisplay)}</p>
            </div>
            <button type="button" class="text-xs font-semibold text-red-600 hover:text-red-700" data-cart-remove="${escapeHtml(item.cartItemId)}">
              Remove
            </button>
          </div>
        </article>`;
      })
      .join("");
  }

  function applyCartPayload(cart) {
    const itemCount = Number(cart?.itemCount ?? 0);
    const itemLabel = itemCount === 1 ? "item" : "items";
    const subtotalDisplay = cart?.subtotalDisplay ?? "$0.00";
    const requiresSignIn = Boolean(cart?.requiresSignIn);
    const checkoutUrl =
      cart?.checkoutUrl || (requiresSignIn ? loginUrl : cartUrl);

    setCartBadgeCount(itemCount);
    cartModalSubtitle.textContent = `${itemCount} ${itemLabel} in cart`;
    cartModalSubtotal.textContent = subtotalDisplay;
    cartPrimaryAction.href = checkoutUrl;
    cartPrimaryAction.textContent = requiresSignIn
      ? "Sign in to checkout"
      : "Review cart and checkout";
    if (clearCartButton) {
      if (itemCount > 0) {
        clearCartButton.classList.remove("hidden");
        clearCartButton.disabled = false;
      } else {
        clearCartButton.classList.add("hidden");
        clearCartButton.disabled = true;
      }
    }

    renderCartItems(cart?.items ?? []);
  }

  async function parseJsonSafe(response) {
    try {
      return await response.json();
    } catch {
      return null;
    }
  }

  async function postCartMutation(url, body, errorMessage) {
    const response = await fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
        "X-Requested-With": "XMLHttpRequest",
      },
      body: body.toString(),
    });

    const payload = await parseJsonSafe(response);
    if (!response.ok || !payload?.success) {
      throw new Error(payload?.message || errorMessage);
    }

    return payload;
  }

  async function fetchCartPayload() {
    const response = await fetch(miniUrl, {
      method: "GET",
      credentials: "same-origin",
      headers: { "X-Requested-With": "XMLHttpRequest" },
    });

    if (!response.ok) {
      throw new Error("Unable to load cart.");
    }

    return response.json();
  }

  async function openCartModal(preloadedCart = null) {
    cartModal.classList.remove("hidden");
    document.body.classList.add("overflow-hidden");

    if (preloadedCart) {
      applyCartPayload(preloadedCart);
      return;
    }

    setModalLoadingState("Loading cart...");
    try {
      const cart = await fetchCartPayload();
      applyCartPayload(cart);
    } catch {
      setModalLoadingState("Could not load cart right now.");
    }
  }

  function closeCartModal() {
    cartModal.classList.add("hidden");
    document.body.classList.remove("overflow-hidden");
  }

  cartTriggers.forEach((trigger) => {
    trigger.addEventListener("click", (event) => {
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
        return;
      }

      event.preventDefault();
      openCartModal();
    });
  });

  cartCloseButtons.forEach((button) => {
    button.addEventListener("click", closeCartModal);
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !cartModal.classList.contains("hidden")) {
      closeCartModal();
    }
  });

  cartModalBody.addEventListener("click", async (event) => {
    const removeButton = event.target.closest("[data-cart-remove]");
    if (!removeButton) {
      return;
    }

    const cartItemId = removeButton.getAttribute("data-cart-remove");
    if (!cartItemId) {
      return;
    }

    // Replace the Remove button with an inline confirm row
    const originalLabel = removeButton.textContent;
    removeButton.textContent = "Removing…";
    removeButton.disabled = true;

    // Show inline confirm: [Cancel] [Yes, remove]
    const confirmRow = document.createElement("div");
    confirmRow.className = "flex items-center gap-2 text-xs";
    confirmRow.innerHTML = `
      <span class="text-store-muted">Remove item?</span>
      <button type="button" data-cancel-remove class="font-semibold text-store-muted hover:text-store-ink">Keep</button>
      <button type="button" data-confirm-remove class="font-semibold text-red-600 hover:text-red-700">Remove</button>
    `;
    removeButton.parentNode.replaceChild(confirmRow, removeButton);

    const cancelBtn = confirmRow.querySelector("[data-cancel-remove]");
    const confirmBtn = confirmRow.querySelector("[data-confirm-remove]");

    const restoreRemoveButton = () => {
      confirmRow.parentNode.replaceChild(removeButton, confirmRow);
      removeButton.textContent = originalLabel;
      removeButton.disabled = false;
    };

    cancelBtn.addEventListener("click", restoreRemoveButton);

    confirmBtn.addEventListener("click", async () => {
      confirmBtn.disabled = true;
      cancelBtn.disabled = true;
      confirmBtn.textContent = "Removing…";

      const token = getAntiForgeryToken();
      const body = new URLSearchParams();
      body.append("cartItemId", cartItemId);
      if (token) {
        body.append("__RequestVerificationToken", token);
      }

      try {
        const payload = await postCartMutation(
          removeUrl,
          body,
          "Could not remove item.",
        );
        applyCartPayload(payload.cart);
      } catch (error) {
        showToast(error.message || "Could not remove item.", true);
        restoreRemoveButton();
      }
    });
  });

  if (clearCartButton) {
    clearCartButton.addEventListener("click", async () => {
      if (clearCartButton.disabled) {
        return;
      }

      // Show inline confirm bar above the items list
      let existingConfirm = cartModalBody.querySelector("[data-clear-confirm-bar]");
      if (existingConfirm) {
        existingConfirm.remove();
        return;
      }

      const bar = document.createElement("div");
      bar.setAttribute("data-clear-confirm-bar", "");
      bar.className = "rounded-xl border border-red-200 bg-red-50 px-3 py-2 mb-3 flex items-center justify-between gap-2";
      bar.innerHTML = `
        <span class="text-xs font-semibold text-red-700">Clear all items?</span>
        <div class="flex gap-2">
          <button type="button" data-cancel-clear class="text-xs font-semibold text-store-muted hover:text-store-ink">Keep</button>
          <button type="button" data-confirm-clear class="text-xs font-semibold text-red-600 hover:text-red-700">Yes, clear</button>
        </div>
      `;
      cartModalBody.prepend(bar);

      bar.querySelector("[data-cancel-clear]").addEventListener("click", () => bar.remove());

      bar.querySelector("[data-confirm-clear]").addEventListener("click", async () => {
        bar.remove();
        clearCartButton.disabled = true;
        const token = getAntiForgeryToken();
        const body = new URLSearchParams();
        if (token) {
          body.append("__RequestVerificationToken", token);
        }

        try {
          const payload = await postCartMutation(
            clearUrl,
            body,
            "Could not clear cart.",
          );
          applyCartPayload(payload.cart);
          showToast(payload.message || "Cart cleared.");
        } catch (error) {
          showToast(error.message || "Could not clear cart.", true);
          clearCartButton.disabled = false;
        }
      });
    });
  }

  document.querySelectorAll("form.js-store-add-to-cart").forEach((form) => {
    form.addEventListener("submit", async (event) => {
      event.preventDefault();

      const submitButton = form.querySelector('button[type="submit"]');
      if (submitButton?.disabled) {
        return;
      }

      const token = getAntiForgeryToken(form);
      const formData = new FormData(form);
      if (token && !formData.has("__RequestVerificationToken")) {
        formData.append("__RequestVerificationToken", token);
      }

      const body = new URLSearchParams();
      formData.forEach((value, key) => {
        body.append(key, String(value));
      });

      if (submitButton) {
        submitButton.disabled = true;
      }

      try {
        const response = await fetch(form.action, {
          method: "POST",
          credentials: "same-origin",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
            "X-Requested-With": "XMLHttpRequest",
          },
          body: body.toString(),
        });

        const payload = await parseJsonSafe(response);
        if (!response.ok || !payload?.success) {
          if (payload?.requiresSignIn && payload?.loginUrl) {
            window.location.href = payload.loginUrl;
            return;
          }

          throw new Error(payload?.message || "Could not add product to cart.");
        }

        if (payload.cart) {
          await openCartModal(payload.cart);
        } else {
          await openCartModal();
        }

        showToast(payload.message || "Added to cart.");
      } catch (error) {
        showToast(error.message || "Could not add product to cart.", true);
      } finally {
        if (submitButton) {
          submitButton.disabled = false;
        }
      }
    });
  });

  // Global Auto-Submit Filters Utility
  function debounce(func, wait) {
    let timeout;
    return function (...args) {
      clearTimeout(timeout);
      timeout = setTimeout(() => func.apply(this, args), wait);
    };
  }

  function setupAutoSubmitSearchInputs() {
    const searchInputs = document.querySelectorAll('input[type="search"]');

    searchInputs.forEach((input) => {
      // If the input explicitly has data-no-auto-submit, skip it
      if (input.hasAttribute("data-no-auto-submit")) return;

      const form = input.closest("form");
      if (!form) return;

      input.addEventListener(
        "input",
        debounce(() => {
          form.submit();
        }, 500),
      );
    });
  }

  function setupAutoSubmitSelects() {
    // Select all form select, checkbox, and radio inputs that shouldn't be skipped
    const inputs = document.querySelectorAll(
      'select, input[type="radio"], input[type="checkbox"]',
    );

    inputs.forEach((input) => {
      // If the input explicitly has data-no-auto-submit, skip it
      if (input.hasAttribute("data-no-auto-submit")) return;

      const form = input.closest("form");
      // Only auto-submit GET forms to prevent destructive actions
      if (!form || form.method.toUpperCase() !== "GET") return;

      input.addEventListener("change", () => {
        form.submit();
      });
    });
  }

  setupAutoSubmitSearchInputs();
  setupAutoSubmitSelects();
});
