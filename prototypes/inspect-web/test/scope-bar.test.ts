import assert from "node:assert/strict";
import test from "node:test";
import {
  bindScopeBar,
  captureScopeBarFocus,
  renderScopeBar,
  restoreScopeBarFocus,
  type ScopeBarBindingActions,
} from "../src/scope-bar.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  focused = false;
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

  focus() {
    this.focused = true;
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

  querySelectorAll(selector: string) {
    return this.elements.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): ScopeBarBindingActions {
  return {
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

test("typed tab focus survives element replacement", () => {
  const original = new FakeElement({ lens: "metadata" });
  const target = captureScopeBarFocus(fakeDom.htmlElement(original));
  assert.deepEqual(target, { kind: "type-lens", value: "metadata" });
  assert.ok(target);

  const selected = new FakeElement({ lens: "api" });
  selected.tabIndex = 0;
  const replacement = new FakeElement({ lens: "metadata" });
  replacement.tabIndex = -1;
  const root = new FakeRoot();
  root.add("[data-lens]", selected, replacement);

  assert.equal(
    restoreScopeBarFocus(fakeDom.parentNode(root), target),
    true);
  assert.equal(replacement.focused, true);
  assert.equal(replacement.tabIndex, 0);
  assert.equal(selected.tabIndex, -1);
});

test("package scope bindings dispatch only scope and package-lens controls", () => {
  const root = new FakeRoot();
  const workspaceScope = new FakeElement({ scope: "workspace" });
  const packageScope = new FakeElement({ scope: "package" });
  const typeScope = new FakeElement({ scope: "type" });
  const dependencies = new FakeElement({ packageLens: "dependencies" });
  root.add("[data-scope]", workspaceScope, packageScope, typeScope);
  root.add("[data-package-lens]", dependencies);
  const calls: string[] = [];
  bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions(calls));

  workspaceScope.dispatch("click");
  packageScope.dispatch("click");
  typeScope.dispatch("click");
  dependencies.dispatch("click");

  assert.deepEqual(calls, [
    "scope:workspace",
    "scope:package",
    "scope:type",
    "package:dependencies",
  ]);
});

test("workspace scope leads the subject ladder without an inspector", () => {
  const html = renderScopeBar({
    scope: "workspace",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-workspace-lens",
    escapeHtml,
  });

  assert.match(
    html,
    /data-scope="workspace"[^>]*role="tab" aria-selected="true" tabindex="0" id="active-subject-tab" data-subject-tab aria-controls="subject-panel"[\s\S]*data-scope="package"[\s\S]*data-scope="type"/);
  assert.doesNotMatch(html, /package-coordinate-controls|class="[^"]* lens(?: |")/);
});

test("type scope bindings dispatch only scope and type-lens controls", () => {
  const root = new FakeRoot();
  const typeScope = new FakeElement({ scope: "type" });
  const metadata = new FakeElement({ lens: "metadata" });
  root.add("[data-scope]", typeScope);
  root.add("[data-lens]", metadata);
  const calls: string[] = [];
  bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions(calls));

  typeScope.dispatch("click");
  metadata.dispatch("click");

  assert.deepEqual(calls, ["scope:type", "type:metadata"]);
});

test("member scope bindings dispatch only scope and member-section controls", () => {
  const root = new FakeRoot();
  const memberScope = new FakeElement({ scope: "member" });
  const facts = new FakeElement({ memberSection: "facts" });
  root.add("[data-scope]", memberScope);
  root.add("[data-member-section]", facts);
  const calls: string[] = [];
  bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions(calls));

  memberScope.dispatch("click");
  facts.dispatch("click");

  assert.deepEqual(calls, ["scope:member", "member:facts"]);
});

test("scope bar binding tolerates an empty strip", () => {
  const root = new FakeRoot();
  assert.doesNotThrow(() => bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions([])));
});

test("subject tabs use manual roving keyboard focus", () => {
  const root = new FakeRoot();
  const workspace = new FakeElement();
  const packageSubject = new FakeElement();
  const type = new FakeElement();
  workspace.tabIndex = -1;
  packageSubject.tabIndex = 0;
  type.tabIndex = -1;
  root.add("[data-subject-tab]", workspace, packageSubject, type);

  bindScopeBar(fakeDom.parentNode(root), recordingActions([]));

  assert.equal(packageSubject.dispatch("keydown", { key: "ArrowRight" }), true);
  assert.equal(type.focused, true);
  assert.deepEqual(
    [workspace.tabIndex, packageSubject.tabIndex, type.tabIndex],
    [-1, -1, 0]);
});

test("scope bar bindings ignore missing and unknown dataset values", () => {
  const root = new FakeRoot();
  root.add(
    "[data-scope]",
    new FakeElement(),
    new FakeElement({ scope: "assembly" }));
  root.add(
    "[data-package-lens]",
    new FakeElement(),
    new FakeElement({ packageLens: "files" }));
  root.add(
    "[data-lens]",
    new FakeElement(),
    new FakeElement({ lens: "implementation" }));
  root.add(
    "[data-member-section]",
    new FakeElement(),
    new FakeElement({ memberSection: "history" }));
  const calls: string[] = [];
  bindScopeBar(
    fakeDom.parentNode(root),
    recordingActions(calls));

  for (const selector of [
    "[data-scope]",
    "[data-package-lens]",
    "[data-lens]",
    "[data-member-section]",
  ]) {
    for (const element of root.querySelectorAll(selector)) {
      element.dispatch("click");
    }
  }

  assert.deepEqual(calls, []);
});

test("subject and inspector strips omit package coordinate selectors", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    panelId: "inspector-panel",
    escapeHtml,
  });

  assert.match(html, /class="[^"]*scope-switch[^"]*"[\s\S]*class="lens-separator"[\s\S]*data-lens="api"/);
  assert.doesNotMatch(
    html,
    /package-coordinate-controls|package-version|framework-select/);
});

test("package scope marks only the package segment and the active package lens", () => {
  const html = renderScopeBar({
    scope: "package",
    strip: [["overview", "Overview"], ["dependencies", "Dependencies"]],
    activeStripId: "dependencies",
    stripAttribute: "data-package-lens",
    escapeHtml,
  });

  assert.match(html, /data-scope="package"[^>]*role="tab" aria-selected="true"/);
  assert.match(html, /data-scope="type"[^>]*role="tab" aria-selected="false"/);
  assert.doesNotMatch(html, /data-scope="member"/);
  assert.match(html, /class="[^"]*\blens active" data-package-lens="dependencies"/);
  assert.doesNotMatch(html, /class="[^"]*\blens active" data-package-lens="overview"/);
});

test("type scope marks the type segment and renders the fixed type lenses", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    panelId: "inspector-panel",
    escapeHtml,
  });

  assert.match(html, /data-scope="type"[^>]*role="tab" aria-selected="true"/);
  assert.doesNotMatch(html, /data-scope="member"/);
  assert.match(html, /class="[^"]*\blens active" data-lens="api"/);
  assert.match(
    html,
    /role="tab" aria-selected="true" tabindex="0" id="active-inspector-tab" aria-controls="inspector-panel"[\s\S]*aria-label="API" title="API">[\s\S]*data-slide-strip-representation="label">API<\/span>[\s\S]*data-slide-strip-representation="index" aria-hidden="true">1<\/kbd>/);
  assert.match(
    html,
    /class="[^"]*inspector-strip"[\s\S]*role="tablist"[\s\S]*aria-label="Type lenses"/);
  assert.match(html, /data-lens="metadata"/);
  assert.match(html, /data-lens="source"/);
});

test("member scope adds a member segment alongside package and type", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [["overview", "Overview"], ["facts", "Facts"]],
    activeStripId: "facts",
    stripAttribute: "data-member-section",
    escapeHtml,
  });

  assert.match(html, /data-scope="member"[^>]*role="tab" aria-selected="true"/);
  assert.match(html, /class="[^"]*\blens active" data-member-section="facts"/);
});

test("type scope can expose the first-class member segment", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    showMemberScope: true,
    escapeHtml,
  });

  assert.match(html, /data-scope="member"[^>]*role="tab" aria-selected="false"/);
});

test("member scope names an empty filtered strip", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-member-section",
    emptyStripLabel: "Filtered member list",
    escapeHtml,
  });

  assert.match(html, /<span class="lens-context">Filtered member list<\/span>/);
});

test("lens buttons separate accessible labels from compact order symbols", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.match(
    html,
    /role="tab" aria-selected="true" tabindex="0" id="active-inspector-tab"[\s\S]*aria-label="API" title="API">[\s\S]*data-slide-strip-representation="label">API<\/span>[\s\S]*data-slide-strip-representation="index" aria-hidden="true">1<\/kbd>/);
  assert.match(
    html,
    /role="tab" aria-selected="false" tabindex="-1"[\s\S]*aria-label="Metadata" title="Metadata">[\s\S]*data-slide-strip-representation="label">Metadata<\/span>[\s\S]*data-slide-strip-representation="index" aria-hidden="true">2<\/kbd>/);
  assert.match(
    html,
    /aria-label="Source" title="Source">[\s\S]*data-slide-strip-representation="label">Source<\/span>[\s\S]*data-slide-strip-representation="index" aria-hidden="true">3<\/kbd>/);
});

test("lens button labels are escaped", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: [["x", '<script>alert(1)</script>']],
    activeStripId: null,
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /&lt;script&gt;/);
});

test("no strip entry is marked active when nothing matches activeStripId", () => {
  const html = renderScopeBar<string>({
    scope: "package",
    strip: [["overview", "Overview"]],
    activeStripId: "dependencies",
    stripAttribute: "data-package-lens",
    escapeHtml,
  });

  assert.match(html, /data-slide-strip="inspector"[\s\S]*data-initial-anchor="overview"/);
  assert.doesNotMatch(html, /class="[^"]*\blens active"/);
  assert.match(
    html,
    /data-package-lens="overview"[^>]*data-inspector-tab role="tab" aria-selected="false" tabindex="0"/);
  assert.doesNotMatch(
    html,
    /data-package-lens="overview"[^>]*aria-controls=/);
});

test("a missing active subject anchors to the nearest installed subject", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [],
    activeStripId: null,
    stripAttribute: "data-member-section",
    showMemberScope: false,
    escapeHtml,
  });

  assert.match(
    html,
    /data-slide-strip="subject"[\s\S]*data-initial-anchor="type"/);
});
