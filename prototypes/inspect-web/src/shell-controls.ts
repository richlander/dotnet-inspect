import {
  parsePackageQuery,
  type ParsedPackageQuery,
} from "./package-controls.ts";
import {
  isProductHomeDemoId,
  type ProductHomeDemoId,
} from "./product-home-demos.ts";
import { renderBrand } from "./brand.ts";
import type { KeybindingDescription } from "./keybinding-registry.ts";

/** Product home-demo ids (`ProductInspectionDemos` / CLI `demo <id>`). */
export type HomeDemo = ProductHomeDemoId;
export type ApplicationAction = "share" | "settings" | "keyboard-help";

export interface WorkbenchShellBindingActions {
  onApplicationAction: (action: ApplicationAction) => void;
  onCopySubjectSegment: (index: number) => void;
  onDismissNotice: () => void;
  onDismissPackageNotice: () => void;
  onNavigateBack: () => void;
  onNavigateForward: () => void;
  onRetryNotice: () => void;
  onSearch: () => void;
}

export interface WorkbenchShellBinding {
  disconnect(): void;
}

export interface HomeShellBindingActions {
  onDemo: (demo: HomeDemo) => void;
  onDismissNotice: () => void;
  onOpenCredits: () => void;
  onToggleTheme: () => void;
}

export interface LoadErrorShellBindingActions {
  onOpenPackage: (query: ParsedPackageQuery) => void;
  onRetry: () => void;
}

export interface WorkbenchShellHtmlOptions {
  inspectedTargetHtml: string;
  titleNavigationHtml: string;
}

export function workbenchShellHtml(
  options: WorkbenchShellHtmlOptions,
): string {
  return `
      <header class="titlebar">
        ${renderBrand()}
        ${options.inspectedTargetHtml}
        ${options.titleNavigationHtml}
      </header>`;
}

export function renderApplicationMenuButton(): string {
  return `<div class="application-menu-slot">
    <button id="application-menu-button" type="button"
      aria-label="Application menu" title="Application menu"
      aria-haspopup="menu" aria-controls="application-menu"
      aria-expanded="false"><span aria-hidden="true">☰</span></button>
  </div>`;
}

export function renderApplicationMenu(shareAvailable: boolean): string {
  return `<div id="application-menu-overlay" class="application-menu-overlay">
    <div id="application-menu" class="application-menu" role="menu"
      aria-label="Application menu" hidden>
      ${shareAvailable
        ? `<button type="button" role="menuitem" data-application-action="share">Share</button>
          <div class="application-menu-separator" role="separator"></div>`
        : ""}
      <button type="button" role="menuitem" data-application-action="settings">Settings</button>
      <button type="button" role="menuitem" data-application-action="keyboard-help">Keyboard help</button>
    </div>
  </div>`;
}

const keyboardHelpLabels = new Map<string, string>([
  ["workspace.open-all", "Search types, members, and packages"],
  ["workspace.open-commands", "Open commands"],
  ["workspace.focus-filter", "Focus the current list filter"],
  ["workspace.focus-filter-slash", "Focus the current list filter"],
  ["workspace.select-lens", "Select a subject or inspector"],
  ["workspace.navigate-vertical", "Move through the current list"],
  ["workspace.navigate-horizontal", "Move across subjects or inspectors"],
  ["workspace.drill-in", "Open the selected item"],
  ["workspace.drill-out-backspace", "Go to the containing subject"],
  ["workspace.drill-out-escape", "Leave the current member or subject"],
  ["workspace.history-alt-ArrowLeft", "Go back"],
  ["workspace.history-alt-ArrowRight", "Go forward"],
  ["workspace.history-shift-ArrowLeft", "Go back"],
  ["workspace.history-shift-ArrowRight", "Go forward"],
  ["graph.zoom", "Zoom the current graph"],
  ["graph.pan-horizontal", "Pan the current graph horizontally"],
  ["graph.pan-vertical", "Pan the current graph vertically"],
]);

function keyLabel(binding: KeybindingDescription): string {
  const modifiers: string[] = [];
  if (binding.modifiers.commandOrControl) modifiers.push("Ctrl/Command");
  if (binding.modifiers.control) modifiers.push("Ctrl");
  if (binding.modifiers.meta) modifiers.push("Command");
  if (binding.modifiers.alt) modifiers.push("Alt");
  if (binding.modifiers.shift) modifiers.push("Shift");
  const keys = binding.keys.map(key => {
    if (key === " ") return "Space";
    if (key.startsWith("Arrow")) return key.slice("Arrow".length);
    return key.length === 1 ? key.toUpperCase() : key;
  }).join(" / ");
  return [...modifiers, keys].join("+");
}

export function renderKeyboardHelpDialog(
  bindings: readonly KeybindingDescription[],
): string {
  const entries = bindings.flatMap(binding => {
    const label = keyboardHelpLabels.get(binding.id);
    return label ? [{ label, shortcut: keyLabel(binding) }] : [];
  });
  return `<div id="keyboard-help-backdrop" class="modal-backdrop">
    <section id="keyboard-help-dialog" class="application-dialog"
      role="dialog" aria-modal="true" aria-labelledby="keyboard-help-title">
      <header class="application-dialog-head">
        <div>
          <p class="section-eyebrow">Application</p>
          <h2 id="keyboard-help-title" tabindex="-1">Keyboard help</h2>
        </div>
        <button id="keyboard-help-close" type="button">Close</button>
      </header>
      <div class="keyboard-help-list">
        ${entries.map(entry => `<div class="keyboard-help-row">
          <span>${entry.label}</span>
          <kbd>${entry.shortcut}</kbd>
        </div>`).join("")}
      </div>
    </section>
  </div>`;
}

function applicationMenuItems(menu: HTMLElement): HTMLElement[] {
  return [...menu.querySelectorAll<HTMLElement>('[role="menuitem"]')]
    .filter(item => !item.hidden);
}

function setApplicationMenuOpen(
  button: HTMLElement,
  menu: HTMLElement,
  open: boolean,
): void {
  button.setAttribute("aria-expanded", String(open));
  menu.hidden = !open;
  if (!open) return;
  const rect = button.getBoundingClientRect();
  const view = button.ownerDocument.defaultView;
  const visualViewport = view?.visualViewport;
  const viewportTop = visualViewport?.offsetTop ?? 0;
  const viewportLeft = visualViewport?.offsetLeft ?? 0;
  const viewportHeight =
    visualViewport?.height ?? view?.innerHeight ?? 600;
  const viewportWidth =
    visualViewport?.width ?? view?.innerWidth ?? rect.right;
  const viewportBottom = viewportTop + viewportHeight;
  const viewportRight = viewportLeft + viewportWidth;
  const margin = 8;
  const gap = 4;
  const availableBelow = Math.max(
    0,
    viewportBottom - rect.bottom - gap - margin);
  const availableAbove = Math.max(
    0,
    rect.top - gap - margin - viewportTop);
  const menuHeight = menu.scrollHeight;
  const placeBelow = availableBelow >= menuHeight
    || (availableAbove < menuHeight && availableBelow >= availableAbove);
  menu.style.top = `${placeBelow
    ? rect.bottom + gap
    : Math.max(
        viewportTop + margin,
        rect.top - gap - Math.min(menuHeight, availableAbove))
  }px`;
  menu.style.bottom = "auto";
  const menuWidth = menu.offsetWidth;
  const minimumLeft = viewportLeft + margin;
  const maximumLeft = Math.max(
    minimumLeft,
    viewportRight - margin - menuWidth);
  menu.style.left = `${Math.min(
    Math.max(rect.right - menuWidth, minimumLeft),
    maximumLeft,
  )}px`;
  menu.style.right = "auto";
  menu.style.maxWidth = `${Math.max(0, viewportWidth - 2 * margin)}px`;
  menu.style.maxHeight =
    `${placeBelow ? availableBelow : availableAbove}px`;
}

function openApplicationMenu(
  button: HTMLElement,
  menu: HTMLElement,
  position: "first" | "last",
): void {
  setApplicationMenuOpen(button, menu, true);
  const items = applicationMenuItems(menu);
  (position === "first" ? items[0] : items.at(-1))?.focus();
}

function closeApplicationMenu(
  button: HTMLElement,
  menu: HTMLElement,
  restoreFocus: boolean,
): void {
  setApplicationMenuOpen(button, menu, false);
  if (restoreFocus) button.focus();
}

function documentFocusableElements(document: Document): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), '
      + 'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )].filter(element =>
    !element.hidden
    && element.getClientRects().length > 0
    && element.closest("#application-menu") === null);
}

function continueDocumentOrder(
  button: HTMLElement,
  menu: HTMLElement,
  event: KeyboardEvent,
): void {
  const focusable = documentFocusableElements(button.ownerDocument);
  const buttonIndex = focusable.indexOf(button);
  const target = event.shiftKey
    ? focusable[buttonIndex - 1]
    : focusable[buttonIndex + 1];
  event.preventDefault();
  closeApplicationMenu(button, menu, false);
  target?.focus();
}

function isNodeTarget(target: EventTarget | null): target is Node {
  return target !== null && "nodeType" in target;
}

export function applicationMenuOwnsFocus(document: Document): boolean {
  const active = document.activeElement;
  return active instanceof HTMLElement
    && (active.id === "application-menu-button"
      || active.closest("#application-menu") !== null);
}

export function focusApplicationMenuButton(document: Document): boolean {
  const button =
    document.querySelector<HTMLElement>("#application-menu-button");
  button?.focus({ preventScroll: true });
  return button !== null;
}

export function trapModalTab(modal: HTMLElement, event: KeyboardEvent): void {
  const focusable = [...modal.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), '
      + 'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )].filter(element => !element.hidden && element.getClientRects().length > 0);
  const first = focusable[0];
  const last = focusable.at(-1);
  if (!first || !last) {
    event.preventDefault();
    modal.focus();
    return;
  }
  const active = modal.ownerDocument.activeElement;
  const activeIsNonTabStop = active instanceof HTMLElement
    && modal.contains(active)
    && !focusable.includes(active);
  if (event.shiftKey && (active === first || activeIsNonTabStop)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && modal.ownerDocument.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

export function bindWorkbenchShell(
  root: ParentNode,
  actions: WorkbenchShellBindingActions,
): WorkbenchShellBinding {
  let outsidePointerHandler: ((event: Event) => void) | null = null;
  let resizeHandler: (() => void) | null = null;
  let menuView: Window | null = null;
  let visualViewport: VisualViewport | null = null;
  root.querySelectorAll<HTMLElement>("[data-subject-copy]").forEach(button =>
    button.addEventListener("click", () => {
      const index = Number(button.dataset.subjectCopy);
      if (Number.isInteger(index) && index >= 0)
        actions.onCopySubjectSegment(index);
    }));
  const menuButton =
    root.querySelector<HTMLElement>("#application-menu-button");
  const menu = root.querySelector<HTMLElement>("#application-menu");
  if (menuButton && menu) {
    menuButton.addEventListener("click", () => {
      if (menu.hidden) openApplicationMenu(menuButton, menu, "first");
      else closeApplicationMenu(menuButton, menu, true);
    });
    menuButton.addEventListener("keydown", event => {
      if (!["Enter", " ", "ArrowDown", "ArrowUp"].includes(event.key)) return;
      event.preventDefault();
      openApplicationMenu(
        menuButton,
        menu,
        event.key === "ArrowUp" ? "last" : "first");
    });
    menu.addEventListener("keydown", event => {
      const items = applicationMenuItems(menu);
      const active = menu.ownerDocument.activeElement;
      const index = items.findIndex(item => item === active);
      if (event.key === "Escape") {
        event.preventDefault();
        closeApplicationMenu(menuButton, menu, true);
      } else if (event.key === "Tab") {
        continueDocumentOrder(menuButton, menu, event);
      } else if (event.key === "Home" || event.key === "End") {
        event.preventDefault();
        (event.key === "Home" ? items[0] : items.at(-1))?.focus();
      } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        const offset = event.key === "ArrowDown" ? 1 : -1;
        items[(index + offset + items.length) % items.length]?.focus();
      }
    });
    menu.querySelectorAll<HTMLElement>("[data-application-action]")
      .forEach(item => item.addEventListener("click", () => {
        const action = item.dataset.applicationAction;
        if (action !== "share"
          && action !== "settings"
          && action !== "keyboard-help") return;
        closeApplicationMenu(menuButton, menu, action === "share");
        actions.onApplicationAction(action);
      }));
    outsidePointerHandler = event => {
      if (menu.hidden) return;
      const target = event.target;
      if (isNodeTarget(target)
        && (menu.contains(target)
          || menuButton.contains(target))) return;
      closeApplicationMenu(menuButton, menu, false);
    };
    root.addEventListener("pointerdown", outsidePointerHandler);
    menuView = menu.ownerDocument.defaultView;
    visualViewport = menuView?.visualViewport ?? null;
    resizeHandler = () => {
      if (!menu.hidden)
        setApplicationMenuOpen(menuButton, menu, true);
    };
    menuView?.addEventListener("resize", resizeHandler);
    visualViewport?.addEventListener("resize", resizeHandler);
  }
  root.querySelector("#dismiss-notice")
    ?.addEventListener("click", actions.onDismissNotice);
  root.querySelector("#retry-notice")
    ?.addEventListener("click", actions.onRetryNotice);
  root.querySelector("#dismiss-package-notice")
    ?.addEventListener("click", actions.onDismissPackageNotice);
  root.querySelector("#nav-back")
    ?.addEventListener("click", actions.onNavigateBack);
  root.querySelector("#nav-forward")
    ?.addEventListener("click", actions.onNavigateForward);
  root.querySelector("#open-search")
    ?.addEventListener("click", () => actions.onSearch());

  const helpBackdrop =
    root.querySelector<HTMLElement>("#keyboard-help-backdrop");
  const helpDialog =
    root.querySelector<HTMLElement>("#keyboard-help-dialog");
  const closeKeyboardHelp = () =>
    actions.onApplicationAction("keyboard-help");
  root.querySelector("#keyboard-help-close")?.addEventListener(
    "click",
    closeKeyboardHelp);
  helpBackdrop?.addEventListener("click", event => {
    if (event.target === helpBackdrop) closeKeyboardHelp();
  });
  helpDialog?.addEventListener("keydown", event => {
    if (event.key === "Tab") trapModalTab(helpDialog, event);
  });

  return {
    disconnect() {
      if (outsidePointerHandler)
        root.removeEventListener("pointerdown", outsidePointerHandler);
      if (resizeHandler) {
        menuView?.removeEventListener("resize", resizeHandler);
        visualViewport?.removeEventListener("resize", resizeHandler);
      }
    },
  };
}

export function focusWorkbenchSearch(root: ParentNode): boolean {
  const search = root.querySelector<HTMLElement>("#open-search");
  if (!search || search.getClientRects().length === 0) return false;
  search.focus();
  return true;
}

export function bindHomeShell(
  root: ParentNode,
  actions: HomeShellBindingActions,
) {
  root.querySelector("#home-theme")
    ?.addEventListener("click", actions.onToggleTheme);
  root.querySelector("#dismiss-notice")
    ?.addEventListener("click", actions.onDismissNotice);
  root.querySelector("#home-credits")
    ?.addEventListener("click", event => {
      if (("button" in event && event.button !== 0)
          || ("metaKey" in event && event.metaKey === true)
          || ("ctrlKey" in event && event.ctrlKey === true)
          || ("shiftKey" in event && event.shiftKey === true)
          || ("altKey" in event && event.altKey === true)) {
        return;
      }
      event.preventDefault();
      actions.onOpenCredits();
    });
  root.querySelectorAll<HTMLElement>("[data-home-demo]").forEach(button =>
    button.addEventListener("click", () => {
      const demo = button.dataset.homeDemo;
      if (isProductHomeDemoId(demo)) {
        actions.onDemo(demo);
      }
    }));
}

export function bindLoadErrorShell(
  root: ParentNode,
  actions: LoadErrorShellBindingActions,
) {
  root.querySelector("#retry-load")
    ?.addEventListener("click", actions.onRetry);
  root.querySelector("#error-package-query")
    ?.addEventListener("submit", event => {
      event.preventDefault();
      const input =
        root.querySelector<HTMLInputElement>("#error-package-input");
      const parsed = parsePackageQuery(input?.value ?? "");
      if (parsed) actions.onOpenPackage(parsed);
    });
  root.querySelector("#toggle-error-detail")
    ?.addEventListener("click", () => {
      const detail =
        root.querySelector<HTMLElement>(".load-error-detail");
      if (detail) detail.hidden = !detail.hidden;
    });
}
