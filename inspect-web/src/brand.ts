import { continueMenuButtonDocumentOrder } from "./menu-button.ts";

function escapeAttribute(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

export type ProductDestination = "home" | "query" | "workspace" | "activity";
export type ProductAction = "open-library";

export interface ProductNavigationActions {
  currentDestination: () => ProductDestination | null;
  onAction: (action: ProductAction) => void;
  onNavigate: (destination: ProductDestination) => void;
  unavailableReason: (destination: ProductDestination) => string | null;
}

export interface ProductNavigationBinding {
  disconnect(): void;
}

const productDestinations = [
  ["home", "Home"],
  ["query", "Query"],
  ["workspace", "Workspace"],
  ["activity", "Activity"],
] as const;

export function renderBrand(options: {
  ariaLabel?: string;
  id?: string;
} = {}): string {
  const ariaLabel = escapeAttribute(
    options.ariaLabel ?? "dotnet-inspect navigation");
  const id = escapeAttribute(options.id ?? "product-navigation-button");
  const menuId = `${id}-menu`;
  return `<button id="${id}" class="brand" type="button"
    data-product-navigation-button aria-label="${ariaLabel}"
    aria-controls="${menuId}" aria-expanded="false" aria-haspopup="menu">
    <span class="brand-icon"><img src="/assets/dotnet-inspect-bot.png" width="28" height="28" alt="" /></span>
    <span class="brand-label">dotnet-inspect</span>
    <span class="brand-chevron" aria-hidden="true">⌄</span>
  </button>
  <nav id="${menuId}" class="product-navigation-menu" role="menu"
    data-product-navigation-menu aria-label="dotnet-inspect" hidden>
    ${productDestinations.map(([destination, label]) =>
      `<button type="button" role="menuitem" tabindex="-1" data-product-destination="${destination}">${label}</button>`).join("")}
    <div class="product-navigation-separator" role="separator"></div>
    <button type="button" role="menuitem" tabindex="-1"
      data-product-action="open-library">Open Library…</button>
  </nav>`;
}

function isProductDestination(
  value: string | null | undefined,
): value is ProductDestination {
  return value === "home"
    || value === "query"
    || value === "workspace"
    || value === "activity";
}

function isProductAction(
  value: string | null | undefined,
): value is ProductAction {
  return value === "open-library";
}

function buttonMenu(
  root: ParentNode,
  button: HTMLElement,
): HTMLElement | null {
  const menuId = button.getAttribute("aria-controls");
  return menuId ? root.querySelector<HTMLElement>(`#${CSS.escape(menuId)}`) : null;
}

function productItems(menu: HTMLElement): HTMLButtonElement[] {
  return [...menu.querySelectorAll<HTMLButtonElement>(
    "[data-product-destination], [data-product-action]")];
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}

function positionProductNavigation(
  button: HTMLElement,
  menu: HTMLElement,
): void {
  const rect = button.getBoundingClientRect();
  const view = button.ownerDocument.defaultView;
  const visualViewport = view?.visualViewport;
  const viewportTop = visualViewport?.offsetTop ?? 0;
  const viewportLeft = visualViewport?.offsetLeft ?? 0;
  const viewportHeight =
    visualViewport?.height ?? view?.innerHeight ?? 600;
  const viewportWidth =
    visualViewport?.width ?? view?.innerWidth ?? rect.right;
  const margin = 8;
  const gap = 4;
  const usableTop = viewportTop + margin;
  const usableBottom =
    Math.max(usableTop, viewportTop + viewportHeight - margin);
  const usableLeft = viewportLeft + margin;
  const usableRight =
    Math.max(usableLeft, viewportLeft + viewportWidth - margin);
  const usableWidth = Math.max(0, usableRight - usableLeft);
  const top = clamp(rect.bottom + gap, usableTop, usableBottom);
  menu.style.minWidth = `${Math.min(rect.width, usableWidth)}px`;
  menu.style.maxWidth = `${usableWidth}px`;
  menu.style.maxHeight = `${Math.max(0, usableBottom - top)}px`;
  const menuWidth = menu.offsetWidth;
  const maximumLeft = Math.max(usableLeft, usableRight - menuWidth);
  menu.style.left =
    `${clamp(rect.left, usableLeft, maximumLeft)}px`;
  menu.style.right = "auto";
  menu.style.top = `${top}px`;
  menu.style.bottom = "auto";
}

function synchronizeProductNavigation(
  menu: HTMLElement,
  actions: ProductNavigationActions,
): void {
  const current = actions.currentDestination();
  for (const item of menu.querySelectorAll<HTMLButtonElement>(
    "[data-product-destination]")) {
    const destination = item.dataset.productDestination;
    if (!isProductDestination(destination)) continue;
    if (destination === current) item.setAttribute("aria-current", "page");
    else item.removeAttribute("aria-current");
    const unavailableReason = actions.unavailableReason(destination);
    if (unavailableReason !== null) {
      item.setAttribute("aria-disabled", "true");
      item.setAttribute("aria-description", unavailableReason);
      item.title = unavailableReason;
    } else {
      item.removeAttribute("aria-disabled");
      item.removeAttribute("aria-description");
      item.removeAttribute("title");
    }
  }
}

function setProductNavigationOpen(
  button: HTMLElement,
  menu: HTMLElement,
  open: boolean,
  actions: ProductNavigationActions,
): void {
  button.setAttribute("aria-expanded", String(open));
  menu.hidden = !open;
  if (!open) return;
  synchronizeProductNavigation(menu, actions);
  positionProductNavigation(button, menu);
}

function openProductNavigation(
  button: HTMLElement,
  menu: HTMLElement,
  actions: ProductNavigationActions,
  position: "first" | "last",
): void {
  setProductNavigationOpen(button, menu, true, actions);
  const items = productItems(menu);
  (position === "first" ? items[0] : items.at(-1))?.focus();
}

function eventElement(target: EventTarget | null): Element | null {
  return target instanceof Element ? target : null;
}

export function bindProductNavigation(
  root: HTMLElement,
  actions: ProductNavigationActions,
): ProductNavigationBinding {
  const currentOpenMenu = () =>
    root.querySelector<HTMLElement>(
      "[data-product-navigation-menu]:not([hidden])");
  const currentButton = (menu: HTMLElement) => {
    const id = menu.id;
    return root.querySelector<HTMLElement>(
      `[data-product-navigation-button][aria-controls="${CSS.escape(id)}"]`);
  };
  const closeOpenMenu = (restoreFocus: boolean) => {
    const menu = currentOpenMenu();
    if (!menu) return;
    const button = currentButton(menu);
    if (!button) return;
    setProductNavigationOpen(button, menu, false, actions);
    if (restoreFocus) button.focus();
  };
  const clickHandler = (event: Event) => {
    const target = eventElement(event.target);
    const button =
      target?.closest<HTMLElement>("[data-product-navigation-button]");
    if (button && root.contains(button)) {
      event.preventDefault();
      const menu = buttonMenu(root, button);
      if (!menu) return;
      const open = menu.hidden !== false;
      closeOpenMenu(false);
      if (open) openProductNavigation(button, menu, actions, "first");
      return;
    }
    const item = target?.closest<HTMLButtonElement>(
      "[data-product-destination], [data-product-action]");
    if (!item || !root.contains(item)) return;
    if (item.getAttribute("aria-disabled") === "true") {
      event.preventDefault();
      return;
    }
    const destination = item.dataset.productDestination;
    if (isProductDestination(destination)) {
      closeOpenMenu(true);
      actions.onNavigate(destination);
      return;
    }
    const action = item.dataset.productAction;
    if (!isProductAction(action)) return;
    closeOpenMenu(true);
    actions.onAction(action);
  };
  const keyDownHandler = (event: KeyboardEvent) => {
    const target = eventElement(event.target);
    const button =
      target?.closest<HTMLElement>("[data-product-navigation-button]");
    if (button
      && root.contains(button)
      && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      event.preventDefault();
      const menu = buttonMenu(root, button);
      if (!menu) return;
      closeOpenMenu(false);
      openProductNavigation(
        button,
        menu,
        actions,
        event.key === "ArrowUp" ? "last" : "first");
      return;
    }
    const menu = target?.closest<HTMLElement>(
      "[data-product-navigation-menu]");
    if (!menu || menu.hidden || !root.contains(menu)) return;
    if (event.key === "Escape") {
      event.preventDefault();
      closeOpenMenu(true);
      return;
    }
    if (event.key === "Tab") {
      const trigger = currentButton(menu);
      if (!trigger) return;
      continueMenuButtonDocumentOrder(
        trigger,
        menu,
        event,
        () => closeOpenMenu(false));
      return;
    }
    const items = productItems(menu);
    const activeIndex = items.findIndex(item =>
      item === menu.ownerDocument.activeElement);
    const targetIndex = event.key === "ArrowDown"
      ? (activeIndex + 1) % items.length
      : event.key === "ArrowUp"
        ? (activeIndex - 1 + items.length) % items.length
        : event.key === "Home"
          ? 0
          : event.key === "End"
            ? items.length - 1
            : null;
    if (targetIndex === null || items.length === 0) return;
    event.preventDefault();
    items[targetIndex]?.focus();
  };
  const pointerDownHandler = (event: PointerEvent) => {
    const menu = currentOpenMenu();
    if (!menu) return;
    const button = currentButton(menu);
    const target = event.target;
    if (!(target instanceof Node)
      || menu.contains(target)
      || button?.contains(target)) return;
    closeOpenMenu(false);
  };
  const focusInHandler = (event: FocusEvent) => {
    const menu = currentOpenMenu();
    if (!menu) return;
    const button = currentButton(menu);
    const target = event.target;
    if (!(target instanceof Node)
      || menu.contains(target)
      || button?.contains(target)) return;
    closeOpenMenu(false);
  };
  const resizeHandler = () => {
    const menu = currentOpenMenu();
    if (!menu) return;
    const button = currentButton(menu);
    if (button) positionProductNavigation(button, menu);
  };
  root.addEventListener("click", clickHandler);
  root.addEventListener("keydown", keyDownHandler);
  root.addEventListener("pointerdown", pointerDownHandler);
  root.addEventListener("focusin", focusInHandler);
  const view = root.ownerDocument.defaultView;
  const visualViewport = view?.visualViewport ?? null;
  view?.addEventListener("resize", resizeHandler);
  visualViewport?.addEventListener("resize", resizeHandler);
  visualViewport?.addEventListener("scroll", resizeHandler);
  return {
    disconnect() {
      root.removeEventListener("click", clickHandler);
      root.removeEventListener("keydown", keyDownHandler);
      root.removeEventListener("pointerdown", pointerDownHandler);
      root.removeEventListener("focusin", focusInHandler);
      view?.removeEventListener("resize", resizeHandler);
      visualViewport?.removeEventListener("resize", resizeHandler);
      visualViewport?.removeEventListener("scroll", resizeHandler);
    },
  };
}
