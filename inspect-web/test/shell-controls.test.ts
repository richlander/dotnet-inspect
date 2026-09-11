import assert from "node:assert/strict";
import test from "node:test";
import {
  bindHomeShell,
  bindLoadErrorShell,
  bindWorkbenchShell,
  captureApplicationMenuFocusOwner,
  focusWorkbenchSearch,
  renderApplicationMenu,
  renderApplicationMenuButton,
  renderKeyboardHelpDialog,
  renderTitleNavigation,
  restoreApplicationMenuFocusIfOwned,
  workbenchShellHtml,
} from "../src/shell-controls.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  readonly offsetHeight = 105;
  readonly offsetWidth = 180;
  readonly ownerDocument: FakeRoot;
  readonly scrollHeight = 105;
  readonly style: Record<string, string> = {};
  hidden = true;
  focused = false;
  isConnected = true;
  rendered = true;
  value = "";
  private readonly attributes = new Map<string, string>();
  private readonly multiple = new Map<string, FakeElement[]>();
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(
    ownerDocument: FakeRoot,
    dataset: Record<string, string | undefined> = {},
  ) {
    this.ownerDocument = ownerDocument;
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  focus() {
    this.focused = true;
    this.ownerDocument.activeElement = this;
  }

  getClientRects() {
    return this.rendered ? [{}] : [];
  }

  getBoundingClientRect() {
    return { top: 40, bottom: 72, right: 792 };
  }

  setAttribute(name: string, value: string) {
    this.attributes.set(name, value);
  }

  getAttribute(name: string) {
    return this.attributes.get(name) ?? null;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }

  contains(target: unknown) {
    return target === this
      || [...this.multiple.values()].some(elements =>
        elements.some(element => element === target));
  }

  closest() {
    return null;
  }

  dispatch(type: string, values: Record<string, unknown> = {}) {
    let prevented = false;
    const event = fakeDom.event({
      target: this,
      ...values,
      preventDefault: () => prevented = true,
    });
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
    return prevented;
  }
}

class FakeVisualViewport {
  height = 900;
  offsetLeft = 0;
  offsetTop = 0;
  width = 800;
  private readonly listeners = new Map<string, EventListener[]>();

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    this.listeners.set(
      type,
      listeners.filter(candidate => candidate !== listener));
  }

  listenerCount(type: string) {
    return this.listeners.get(type)?.length ?? 0;
  }
}

class FakeRoot {
  activeElement: FakeElement | null = null;
  readonly body = this.element();
  readonly documentElement = this.element();
  readonly visualViewport = new FakeVisualViewport();
  readonly defaultView = {
    innerWidth: 800,
    innerHeight: 900,
    visualViewport: this.visualViewport,
    addEventListener: (type: string, listener: EventListener) => {
      const listeners = this.viewListeners.get(type) ?? [];
      listeners.push(listener);
      this.viewListeners.set(type, listeners);
    },
    removeEventListener: (type: string, listener: EventListener) => {
      const listeners = this.viewListeners.get(type) ?? [];
      this.viewListeners.set(
        type,
        listeners.filter(candidate => candidate !== listener));
    },
  };
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();
  private readonly listeners = new Map<string, EventListener[]>();
  private readonly viewListeners = new Map<string, EventListener[]>();

  element(dataset: Record<string, string | undefined> = {}) {
    return new FakeElement(this, dataset);
  }

  add(selector: string, element: FakeElement = this.element()) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    this.listeners.set(
      type,
      listeners.filter(candidate => candidate !== listener));
  }

  listenerCount(type: string) {
    return this.listeners.get(type)?.length ?? 0;
  }

  viewListenerCount(type: string) {
    return this.viewListeners.get(type)?.length ?? 0;
  }

  dispatch(type: string, values: Record<string, unknown> = {}) {
    const event = fakeDom.event({ ...values });
    for (const listener of this.listeners.get(type) ?? []) listener(event);
  }
}

function applicationActions(calls: string[]) {
  return {
    onApplicationAction: (action: string) => calls.push(action),
    onCopySubjectSegment: (index: number) => calls.push(`copy-subject:${index}`),
    onDismissNotice: () => calls.push("dismiss-notice"),
    onDismissPackageNotice: () => calls.push("dismiss-package-notice"),
    onNavigateBack: () => calls.push("navigate-back"),
    onNavigateForward: () => calls.push("navigate-forward"),
    onRetryNotice: () => calls.push("retry-notice"),
    onSearch: () => calls.push("search"),
  };
}

test("workbench shell binds persistent controls without eager work", () => {
  const root = new FakeRoot();
  const controls = new Map([
    ["#dismiss-notice", "dismiss-notice"],
    ["#retry-notice", "retry-notice"],
    ["#dismiss-package-notice", "dismiss-package-notice"],
    ["#nav-back", "navigate-back"],
    ["#nav-forward", "navigate-forward"],
    ["#open-search", "search"],
  ]);
  for (const selector of controls.keys()) {
    root.add(selector);
  }
  const packageSubject = root.element({ subjectCopy: "0" });
  const typeSubject = root.element({ subjectCopy: "1" });
  root.addAll("[data-subject-copy]", packageSubject, typeSubject);
  const calls: string[] = [];
  let searchArgumentCount = -1;

  bindWorkbenchShell(fakeDom.parentNode(root), {
    ...applicationActions(calls),
    onSearch: (...args: unknown[]) => {
      searchArgumentCount = args.length;
      calls.push("search");
    },
  });

  assert.deepEqual(calls, []);
  for (const [selector, call] of controls) {
    root.querySelector(selector)?.dispatch("click");
    assert.equal(calls.at(-1), call);
  }
  packageSubject.dispatch("click");
  typeSubject.dispatch("click");
  assert.deepEqual(calls.slice(-2), ["copy-subject:0", "copy-subject:1"]);
  assert.equal(calls.length, controls.size + 2);
  assert.equal(searchArgumentCount, 0);
});

test("application menu renders exact conditional inventory", () => {
  const button = renderApplicationMenuButton();
  const withShare = renderApplicationMenu(true);
  const withoutShare = renderApplicationMenu(false);

  assert.match(button, /id="application-menu-button"/);
  assert.match(button, /aria-label="Application menu"/);
  assert.match(button, /aria-haspopup="menu"/);
  assert.match(
    withShare,
    /data-application-action="share"[\s\S]*role="separator"[\s\S]*data-application-action="settings"[\s\S]*data-application-action="keyboard-help"/);
  assert.doesNotMatch(withoutShare, /data-application-action="share"|role="separator"/);
  assert.match(
    withoutShare,
    /data-application-action="settings"[\s\S]*data-application-action="keyboard-help"/);
});

test("application menu follows menu-button keyboard and dismissal behavior", () => {
  const root = new FakeRoot();
  const button = root.add("#application-menu-button");
  const menu = root.add("#application-menu");
  menu.hidden = true;
  const share = root.element({ applicationAction: "share" });
  const settings = root.element({ applicationAction: "settings" });
  const help = root.element({ applicationAction: "keyboard-help" });
  share.hidden = false;
  settings.hidden = false;
  help.hidden = false;
  menu.addAll('[role="menuitem"]', share, settings, help);
  menu.addAll("[data-application-action]", share, settings, help);
  root.addAll(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    button);
  const calls: string[] = [];

  const binding = bindWorkbenchShell(
    fakeDom.parentNode(root),
    applicationActions(calls));
  assert.equal(root.listenerCount("pointerdown"), 1);
  assert.equal(root.viewListenerCount("resize"), 1);
  assert.equal(root.visualViewport.listenerCount("resize"), 1);
  assert.equal(root.visualViewport.listenerCount("scroll"), 1);

  assert.equal(button.dispatch("keydown", { key: "ArrowDown" }), true);
  assert.equal(menu.hidden, false);
  assert.equal(button.getAttribute("aria-expanded"), "true");
  assert.equal(share.focused, true);

  menu.dispatch("keydown", { key: "ArrowDown" });
  assert.equal(settings.focused, true);
  menu.dispatch("keydown", { key: "End" });
  assert.equal(help.focused, true);
  menu.dispatch("keydown", { key: "ArrowDown" });
  assert.equal(share.focused, true);
  assert.equal(menu.dispatch("keydown", { key: "Escape" }), true);
  assert.equal(menu.hidden, true);
  assert.equal(button.focused, true);

  button.dispatch("keydown", { key: "ArrowUp" });
  assert.equal(help.focused, true);
  settings.dispatch("click");
  assert.equal(menu.hidden, true);
  assert.deepEqual(calls, ["settings"]);

  button.dispatch("click");
  root.dispatch("pointerdown", { target: root.element() });
  assert.equal(menu.hidden, true);
  binding.disconnect();
  assert.equal(root.listenerCount("pointerdown"), 0);
  assert.equal(root.viewListenerCount("resize"), 0);
  assert.equal(root.visualViewport.listenerCount("resize"), 0);
  assert.equal(root.visualViewport.listenerCount("scroll"), 0);
});

test("delayed Application actions restore focus only while the menu owns it", () => {
  const root = new FakeRoot();
  const button = root.add("#application-menu-button");
  const other = root.element();
  button.focus();
  const owner =
    captureApplicationMenuFocusOwner(fakeDom.document(root));
  other.focus();

  assert.equal(
    restoreApplicationMenuFocusIfOwned(fakeDom.document(root), owner),
    false);
  assert.equal(root.activeElement, other);

  button.focus();
  const replacedOwner =
    captureApplicationMenuFocusOwner(fakeDom.document(root));
  button.isConnected = false;
  const replacement = root.add("#application-menu-button");
  root.activeElement = root.body;

  assert.equal(
    restoreApplicationMenuFocusIfOwned(
      fakeDom.document(root),
      replacedOwner),
    true);
  assert.equal(root.activeElement, replacement);
});

test("keyboard help is rendered from registered keybinding descriptions", () => {
  const html = renderKeyboardHelpDialog([{
    id: "workspace.open-all",
    keys: ["p"],
    modifiers: { commandOrControl: true },
    allowExtraModifiers: true,
    priority: 100,
    preventDefault: true,
  }, {
    id: "graph.zoom",
    keys: ["+", "-", "0"],
    modifiers: {},
    allowExtraModifiers: true,
    priority: 200,
    preventDefault: true,
  }]);

  assert.match(html, /role="dialog"/);
  assert.match(html, /Keyboard help/);
  assert.match(html, /Search types, members, and packages/);
  assert.match(html, /Ctrl\/Command\+P/);
  assert.match(html, /Zoom the current graph/);
  assert.match(html, /\+ \/ - \/ 0/);
});

test("workbench shell separates navigation and inspected target rows", () => {
  const html = workbenchShellHtml({
    applicationScopeHtml:
      '<nav class="application-scope-strip">Query Workspace</nav>',
    contextualActionsHtml: '<div class="working-surface-actions">Copy</div>',
    inspectedTargetHtml: '<div class="inspected-target" data-test="target">System.Text.Json</div>',
    subjectInspectorHtml: '<div class="lensbar">Subjects</div>',
    titleNavigationHtml: renderTitleNavigation(true, false),
  });

  assert.match(
    html,
    /class="titlebar"[\s\S]*class="brand"[\s\S]*class="application-scope-region"[\s\S]*class="lensbar"[\s\S]*class="title-navigation"[\s\S]*class="application-menu-slot"[\s\S]*class="targetbar"[\s\S]*data-test="target"[\s\S]*class="working-surface-actions"/);
  assert.doesNotMatch(html, /workspace-window|workspace-strip/);
  assert.doesNotMatch(
    html,
    /workspace-title|coordinate-selectors|package-version|framework-select/);
  assert.match(html, /class="brand-icon"[\s\S]*dotnet-inspect-bot\.png/);
  assert.match(html, /id="open-search"/);
  assert.match(html, /id="nav-back"[\s\S]*<svg[\s\S]*id="nav-forward"/);
  assert.match(html, /id="nav-forward"[\s\S]*disabled/);
  assert.match(html, /id="application-menu-button"/);
  assert.doesNotMatch(html, /id="go-home"|>Home<\/button>/);
  assert.doesNotMatch(html, /id="open-settings"/);
  assert.doesNotMatch(html, /id="share"/);
  assert.doesNotMatch(html, /id="help"/);
  assert.doesNotMatch(
    html,
    /Package or Package@version|theme-toggle|shell-command-center/);
});

test("workbench search focus stays with the shell selector owner", () => {
  const root = new FakeRoot();
  const search = root.element();
  root.add("#open-search", search);

  assert.equal(focusWorkbenchSearch(fakeDom.parentNode(root)), true);
  assert.equal(search.focused, true);
  search.rendered = false;
  search.focused = false;
  assert.equal(focusWorkbenchSearch(fakeDom.parentNode(root)), false);
  assert.equal(search.focused, false);
  assert.equal(
    focusWorkbenchSearch(fakeDom.parentNode(new FakeRoot())),
    false);
});

test("home shell opens the product demo catalog", () => {
  const root = new FakeRoot();
  const theme = root.element();
  const dismiss = root.element();
  const credits = root.element();
  const demos = root.element();
  root.add("#home-theme", theme);
  root.add("#dismiss-notice", dismiss);
  root.add("#home-credits", credits);
  root.add("#home-demos", demos);
  const calls: string[] = [];

  bindHomeShell(fakeDom.parentNode(root), {
    onDismissNotice: () => calls.push("dismiss"),
    onOpenDemos: () => calls.push("demos"),
    onOpenCredits: () => calls.push("credits"),
    onToggleTheme: () => calls.push("theme"),
  });

  assert.deepEqual(calls, []);
  theme.dispatch("click");
  dismiss.dispatch("click");
  assert.equal(credits.dispatch("click", { button: 0, metaKey: true }), false);
  assert.equal(credits.dispatch("click", { button: 1 }), false);
  assert.deepEqual(calls, ["theme", "dismiss"]);
  assert.equal(credits.dispatch("click"), true);
  demos.dispatch("click");
  assert.deepEqual(calls, [
    "theme",
    "dismiss",
    "credits",
    "demos",
  ]);
});

test("load error shell parses replacement packages and owns local detail state", () => {
  const root = new FakeRoot();
  const retry = root.element();
  const form = root.element();
  const input = root.element();
  const toggle = root.element();
  const detail = root.element();
  root.add("#retry-load", retry);
  root.add("#error-package-query", form);
  root.add("#error-package-input", input);
  root.add("#toggle-error-detail", toggle);
  root.add(".load-error-detail", detail);
  const calls: string[] = [];

  bindLoadErrorShell(fakeDom.parentNode(root), {
    onOpenPackage: query =>
      calls.push(`open:${query.packageId}@${query.version}:${query.explicitVersion}`),
    onRetry: () => calls.push("retry"),
  });

  assert.deepEqual(calls, []);
  retry.dispatch("click");
  input.value = " Example.Package@2.0.0 ";
  assert.equal(form.dispatch("submit"), true);
  input.value = "Latest.Package";
  assert.equal(form.dispatch("submit"), true);
  input.value = "Invalid.Package@";
  assert.equal(form.dispatch("submit"), true);
  input.value = " ";
  assert.equal(form.dispatch("submit"), true);
  toggle.dispatch("click");
  assert.equal(detail.hidden, false);
  toggle.dispatch("click");
  assert.equal(detail.hidden, true);
  assert.deepEqual(calls, [
    "retry",
    "open:Example.Package@2.0.0:true",
    "open:Latest.Package@latest:false",
  ]);
});

test("shell bindings tolerate inactive surfaces", () => {
  const root = fakeDom.parentNode(new FakeRoot());
  assert.doesNotThrow(() => bindWorkbenchShell(root, {
    onApplicationAction() {},
    onCopySubjectSegment() {},
    onDismissNotice() {},
    onDismissPackageNotice() {},
    onNavigateBack() {},
    onNavigateForward() {},
    onRetryNotice() {},
    onSearch() {},
  }));
  assert.doesNotThrow(() => bindHomeShell(root, {
    onDismissNotice() {},
    onOpenDemos() {},
    onOpenCredits() {},
    onToggleTheme() {},
  }));
  assert.doesNotThrow(() => bindLoadErrorShell(root, {
    onOpenPackage() {},
    onRetry() {},
  }));
});
