import assert from "node:assert/strict";
import test from "node:test";
import {
  bindScopeBar,
  captureScopeBarFocus,
  renderApplicationScopeBar,
  renderScopeBar,
  restoreScopeBarFocus,
  selectAdaptiveNavigationPair,
  type ScopeBarBindingActions,
} from "../src/scope-bar.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  focused = false;
  hidden = false;
  rendered = true;
  tabIndex = 0;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  closest() {
    return null;
  }

  focus() {
    this.focused = true;
  }

  click() {
    this.dispatch("click");
  }

  checkVisibility() {
    return this.rendered;
  }

  dispatch(type: string, values: Record<string, unknown> = {}) {
    let prevented = false;
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({
        ...values,
        preventDefault: () => prevented = true,
      }));
    }
    return prevented;
  }
}

class FakeRoot {
  private readonly elements = new Map<string, FakeElement[]>();

  add(selector: string, ...elements: FakeElement[]) {
    this.elements.set(selector, elements);
    return elements;
  }

  querySelector(selector: string): FakeElement | null {
    return this.elements.get(selector)?.[0] ?? null;
  }

  querySelectorAll(selector: string) {
    return this.elements.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): ScopeBarBindingActions {
  return {
    onApplicationScopeSelect: value =>
      calls.push(`application:${value}`),
    onLibraryLensSelect: value => calls.push(`library:${value}`),
    onMemberSectionSelect: value => calls.push(`member:${value}`),
    onPackageLensSelect: value => calls.push(`package:${value}`),
    onScopeSelect: value => calls.push(`scope:${value}`),
    onTypeLensSelect: value => calls.push(`type:${value}`),
  };
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const typeLenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"],
] as const;

test("adaptive subject and inspector groups choose one measured presentation", () => {
  const common = {
    availableWidth: 500,
    separatorAndGapsWidth: 20,
    subjectTabsWidth: 180,
    subjectChooserWidth: 100,
    subjectCount: 4,
    subjectCommitted: true,
    inspectorTabsWidth: 260,
    inspectorChooserWidth: 120,
    inspectorCount: 5,
    inspectorCommitted: true,
  };

  assert.deepEqual(selectAdaptiveNavigationPair(common), {
    subject: "tabs",
    inspector: "tabs",
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    ...common,
    availableWidth: 390,
  }), {
    subject: "chooser",
    inspector: "tabs",
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    ...common,
    availableWidth: 320,
  }), {
    subject: "tabs",
    inspector: "chooser",
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    ...common,
    availableWidth: 230,
  }), {
    subject: "chooser",
    inspector: "chooser",
  });

  const tie = selectAdaptiveNavigationPair({
    ...common,
    availableWidth: 400,
    subjectCount: 4,
    inspectorCount: 4,
  });
  assert.deepEqual(tie, {
    subject: "tabs",
    inspector: "chooser",
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    ...common,
    availableWidth: 500,
    pinnedChooser: "inspector",
  }), {
    subject: "tabs",
    inspector: "chooser",
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    availableWidth: 150,
    separatorAndGapsWidth: 0,
    subjectTabsWidth: 180,
    subjectChooserWidth: 100,
    subjectCount: 4,
    subjectCommitted: false,
  }), {
    subject: "chooser",
    inspector: null,
  });
  assert.deepEqual(selectAdaptiveNavigationPair({
    availableWidth: 200,
    separatorAndGapsWidth: 0,
    subjectTabsWidth: 180,
    subjectChooserWidth: 100,
    subjectCount: 4,
    subjectCommitted: false,
  }), {
    subject: "tabs",
    inspector: null,
  });
});

test("application scopes render separately with honest selection", () => {
  const workspace = renderApplicationScopeBar(
    "workspace",
    true,
    escapeHtml);
  const queryOnly = renderApplicationScopeBar("query", false, escapeHtml);
  const inspection = renderApplicationScopeBar(null, true, escapeHtml);

  assert.match(
    workspace,
    /data-application-scope="query"(?![^>]*aria-current)[^>]*>[\s\S]*data-application-scope="workspace"[^>]*aria-current="page"/);
  assert.match(
    queryOnly,
    /data-application-scope="query"[^>]*aria-current="page"[\s\S]*data-application-scope="workspace"(?![^>]*aria-current)[^>]*disabled/);
  assert.match(
    inspection,
    /data-application-scope="query"(?![^>]*aria-current)[^>]*tabindex="0"[\s\S]*data-application-scope="workspace"(?![^>]*aria-current)[^>]*tabindex="-1"/);
  assert.doesNotMatch(workspace, /role="tab(?:list)?"/);
});

test("scope bar renders complete full-label Tabs and Chooser inventories", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    panelId: "inspector-panel",
    escapeHtml,
  });

  assert.match(html, /data-navigation-group="subject"/);
  assert.match(html, /data-navigation-group="inspector"/);
  assert.match(html, /data-navigation-trigger="subject"/);
  assert.match(html, /data-navigation-trigger="inspector"/);
  assert.match(
    html,
    /data-navigation-item="tab" data-inspector-tab role="tab" aria-selected="true"[^>]*id="active-inspector-tab" aria-controls="inspector-panel"/);
  assert.match(
    html,
    /data-navigation-item="menuitem" role="menuitemradio" aria-checked="true"/);
  for (const label of ["Package", "Library", "Type", "API", "Metadata", "Source"]) {
    assert.ok(html.includes(`>${label}<`) || html.includes(`>${label}</span>`));
  }
  assert.doesNotMatch(
    html,
    /data-slide-strip|data-more-subjects|data-more-inspectors|short-label|<kbd/);
});

test("workspace keeps subjects available without inventing a committed subject", () => {
  const html = renderScopeBar({
    scope: "workspace",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-workspace-lens",
    escapeHtml,
  });

  assert.match(
    html,
    /data-navigation-trigger="subject"[\s\S]*aria-label="Choose subject"/);
  assert.match(
    html,
    /data-scope="package"[^>]*aria-selected="false" tabindex="0"/);
  assert.doesNotMatch(html, /data-scope="workspace"/);
  assert.doesNotMatch(html, /data-navigation-current="true"/);
  assert.doesNotMatch(html, /data-navigation-group="inspector"/);
});

test("an unavailable committed subject starts roving focus at the first item", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-member-section",
    availableScopes: ["package", "library", "type"],
    escapeHtml,
  });

  assert.match(
    html,
    /data-scope="package"[^>]*aria-selected="false" tabindex="0"/);
  assert.match(
    html,
    /data-scope="type"[^>]*aria-selected="false" tabindex="-1"/);
});

test("an inspector inventory without an effective item remains uncommitted", () => {
  const html = renderScopeBar<string>({
    scope: "package",
    strip: [["overview", "Overview"], ["dependencies", "Dependencies"]],
    activeStripId: "missing",
    stripAttribute: "data-package-lens",
    panelId: "inspector-panel",
    escapeHtml,
  });

  assert.match(
    html,
    /data-navigation-trigger="inspector"[\s\S]*aria-label="Choose inspector"/);
  assert.match(
    html,
    /data-package-lens="overview"[^>]*aria-selected="false" tabindex="0"/);
  assert.doesNotMatch(
    html,
    /data-package-lens="(?:overview|dependencies)"[^>]*aria-checked="true"/);
  assert.doesNotMatch(html, /id="active-inspector-tab"/);
});

test("empty inspector inventories omit the group and preserve context", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-member-section",
    emptyStripLabel: "Filtered member list",
    escapeHtml,
  });

  assert.doesNotMatch(html, /data-navigation-group="inspector"/);
  assert.doesNotMatch(html, /class="lens-separator"/);
  assert.match(html, /<span class="lens-context">Filtered member list<\/span>/);
});

test("scope bar labels and identities are escaped", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: [["x", '<script>alert("x")</script>']],
    activeStripId: null,
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /aria-label="&lt;script&gt;alert\(&quot;x&quot;\)&lt;\/script&gt;"/);
});

test("tab navigation moves focus without activation until Enter", () => {
  const root = new FakeRoot();
  const packageSubject = new FakeElement({
    scope: "package",
    navigationItem: "tab",
    navigationCurrent: "true",
  });
  const library = new FakeElement({
    scope: "library",
    navigationItem: "tab",
    navigationCurrent: "false",
  });
  const type = new FakeElement({
    scope: "type",
    navigationItem: "tab",
    navigationCurrent: "false",
  });
  packageSubject.tabIndex = 0;
  library.tabIndex = -1;
  type.tabIndex = -1;
  root.add("[data-subject-tab]", packageSubject, library, type);
  root.add("[data-inspector-tab]");
  root.add("[data-scope]", packageSubject, library, type);
  root.add("[data-package-lens]");
  root.add("[data-library-lens]");
  root.add("[data-lens]");
  root.add("[data-member-section]");
  root.add("[data-application-scope]");
  root.add("[data-application-scope-tab]:not([disabled])");
  const calls: string[] = [];

  bindScopeBar(fakeDom.parentNode(root), recordingActions(calls));

  assert.equal(packageSubject.dispatch("keydown", { key: "ArrowRight" }), true);
  assert.equal(library.focused, true);
  assert.deepEqual(
    [packageSubject.tabIndex, library.tabIndex, type.tabIndex],
    [-1, 0, -1]);
  assert.deepEqual(calls, []);
  assert.equal(library.dispatch("keydown", { key: "Enter" }), true);
  assert.deepEqual(calls, ["scope:library"]);
});

test("bindings dispatch typed tab and Chooser items but not current items", () => {
  const root = new FakeRoot();
  const current = new FakeElement({
    lens: "api",
    navigationCurrent: "true",
  });
  const metadata = new FakeElement({
    lens: "metadata",
    navigationCurrent: "false",
  });
  const source = new FakeElement({
    lens: "source",
    navigationCurrent: "false",
  });
  root.add("[data-subject-tab]");
  root.add("[data-inspector-tab]");
  root.add("[data-scope]");
  root.add("[data-package-lens]");
  root.add("[data-library-lens]");
  root.add("[data-lens]", current, metadata, source);
  root.add("[data-member-section]");
  root.add("[data-application-scope]");
  root.add("[data-application-scope-tab]:not([disabled])");
  const calls: string[] = [];

  bindScopeBar(fakeDom.parentNode(root), recordingActions(calls));
  current.dispatch("click");
  metadata.dispatch("click");
  source.dispatch("click");

  assert.deepEqual(calls, ["type:metadata", "type:source"]);
});

test("typed focus records its presentation and restores the visible replacement", () => {
  const original = new FakeElement({
    lens: "metadata",
    navigationItem: "menuitem",
  });
  const target = captureScopeBarFocus(fakeDom.htmlElement(original));
  assert.deepEqual(target, {
    kind: "type-lens",
    value: "metadata",
    presentation: "menuitem",
  });
  assert.ok(target);

  const hiddenTab = new FakeElement({
    lens: "metadata",
    navigationItem: "tab",
  });
  hiddenTab.hidden = true;
  const menuItem = new FakeElement({
    lens: "metadata",
    navigationItem: "menuitem",
  });
  const root = new FakeRoot();
  root.add("[data-lens]", hiddenTab, menuItem);

  assert.equal(
    restoreScopeBarFocus(fakeDom.parentNode(root), target),
    true);
  assert.equal(menuItem.focused, true);
  assert.equal(hiddenTab.focused, false);
});

test("application scope bindings dispatch independently of subjects", () => {
  const root = new FakeRoot();
  const query = new FakeElement({ applicationScope: "query" });
  const workspace = new FakeElement({ applicationScope: "workspace" });
  root.add("[data-subject-tab]");
  root.add("[data-inspector-tab]");
  root.add("[data-scope]");
  root.add("[data-package-lens]");
  root.add("[data-library-lens]");
  root.add("[data-lens]");
  root.add("[data-member-section]");
  root.add("[data-application-scope]", query, workspace);
  root.add("[data-application-scope-tab]:not([disabled])");
  const calls: string[] = [];

  bindScopeBar(fakeDom.parentNode(root), recordingActions(calls));
  query.dispatch("click");
  workspace.dispatch("click");

  assert.deepEqual(calls, [
    "application:query",
    "application:workspace",
  ]);
});

test("scope bar binding tolerates absent navigation groups", () => {
  const root = new FakeRoot();
  root.add("[data-subject-tab]");
  root.add("[data-inspector-tab]");
  root.add("[data-scope]");
  root.add("[data-package-lens]");
  root.add("[data-library-lens]");
  root.add("[data-lens]");
  root.add("[data-member-section]");
  root.add("[data-application-scope]");
  root.add("[data-application-scope-tab]:not([disabled])");

  assert.doesNotThrow(() => bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions([])));
});
