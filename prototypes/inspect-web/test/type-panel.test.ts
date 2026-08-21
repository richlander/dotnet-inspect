import assert from "node:assert/strict";
import test from "node:test";
import {
  bindTypePanel,
  renderMemberNav,
  renderTypeMetadata,
  renderTypeNav,
  renderTypeSource,
  typeHeading,
  typeMetadataSignature,
  typeSourceSignature,
} from "../src/type-panel.ts";
import type {
  MemberNavEntry,
  TypePanelBindingActions,
  TypeSummary,
} from "../src/type-panel.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  value = "";
  focused = false;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, event: Event = {} as Event) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }

  focus() {
    this.focused = true;
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

function keyboardEvent(key: string) {
  const state = { prevented: false };
  const event = {
    key,
    preventDefault: () => {
      state.prevented = true;
    },
  } as unknown as KeyboardEvent;
  return { event, state };
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function typeDisplayName(item: TypeSummary) {
  return item?.displayName || item?.name || "";
}

function kindIcon(kind: string) {
  if (kind.includes("struct")) return "S";
  if (kind === "enum") return "E";
  if (kind.includes("interface")) return "I";
  return "C";
}

function shortKind(kind: string) {
  return kind.replace("sealed ", "").replace("abstract ", "");
}

function highlight(value: string) {
  return escapeHtml(value).replace(/\b(public|class)\b/g, '<span class="kw">$1</span>');
}

function highlightCSharp(value: string) {
  return escapeHtml(value);
}

function factRows(rows: readonly (readonly [string, string])[]) {
  return `<dl>${rows.map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd></div>`).join("")}</dl>`;
}

const jsonSerializer: TypeSummary = {
  id: "System.Text.Json.JsonSerializer",
  name: "JsonSerializer",
  namespace: "System.Text.Json",
  kind: "sealed class",
  signature: "public sealed class JsonSerializer",
  members: 12,
  accessibility: "public",
  assembly: "System.Text.Json.dll",
  definitionId: "T:System.Text.Json.JsonSerializer",
};

const jsonDocument: TypeSummary = {
  id: "System.Text.Json.JsonDocument",
  name: "JsonDocument",
  namespace: "System.Text.Json",
  kind: "class",
  signature: "public class JsonDocument",
  members: 8,
  accessibility: "public",
  assembly: "System.Text.Json.dll",
};

function recordingActions(calls: string[]): TypePanelBindingActions {
  return {
    onClearFilters: () => calls.push("clear"),
    onKindSelect: value => calls.push(`kind:${value}`),
    onListKeyDown: event => calls.push(`list:${event.key}`),
    onMemberAccessibilityFilterSelect: value =>
      calls.push(`member-access:${value}`),
    onMemberFilterChange: value => calls.push(`member-filter:${value}`),
    onMemberFilterClear: () => calls.push("member-filter-clear"),
    onMemberFilterKeyDown: event =>
      calls.push(`member-filter-key:${event.key}`),
    onMemberKindFilterSelect: value => calls.push(`member-kind:${value}`),
    onMemberSelect: value => calls.push(`member:${value}`),
    onMemberTraitFilterSelect: value => calls.push(`member-trait:${value}`),
    onNamespaceSelect: value => calls.push(`namespace:${value}`),
    onOverloadSelect: value => calls.push(`overload:${value}`),
    onShowTypes: () => calls.push("types"),
    onTypeFilterChange: value => calls.push(`filter:${value}`),
    onTypeFilterEscape: () => calls.push("escape"),
    onTypeSelect: value => calls.push(`type:${value}`),
  };
}

test("type panel bindings dispatch member filters without eager work", () => {
  const root = new FakeRoot();
  const allKinds = new FakeElement({ memberKindFilter: "all" });
  const kind = new FakeElement({ memberKindFilter: "method" });
  const allAccessibilities =
    new FakeElement({ memberAccessFilter: "all" });
  const accessibility =
    new FakeElement({ memberAccessFilter: "protected" });
  const allTraits = new FakeElement({ memberTraitFilter: "all" });
  const trait = new FakeElement({ memberTraitFilter: "isStatic" });
  root.addAll("[data-member-kind-filter]", allKinds, kind);
  root.addAll(
    "[data-member-access-filter]",
    allAccessibilities,
    accessibility);
  root.addAll("[data-member-trait-filter]", allTraits, trait);
  const filter = root.add("#member-filter", new FakeElement());
  filter.value = "parse";
  const clear = root.add("#clear-member-filter", new FakeElement());
  const calls: string[] = [];

  bindTypePanel(
    root as unknown as ParentNode,
    recordingActions(calls));

  assert.deepEqual(calls, []);
  kind.dispatch("click");
  assert.deepEqual(calls, ["member-kind:method"]);
  accessibility.dispatch("click");
  assert.deepEqual(calls, [
    "member-kind:method",
    "member-access:protected",
  ]);
  trait.dispatch("click");
  assert.deepEqual(calls, [
    "member-kind:method",
    "member-access:protected",
    "member-trait:isStatic",
  ]);
  filter.dispatch("input");
  const arrow = keyboardEvent("ArrowDown");
  filter.dispatch("keydown", arrow.event);
  clear.dispatch("click");
  assert.deepEqual(calls, [
    "member-kind:method",
    "member-access:protected",
    "member-trait:isStatic",
    "member-filter:parse",
    "member-filter-key:ArrowDown",
    "member-filter-clear",
  ]);
});

test("type panel bindings dispatch the rendered type navigation controls", () => {
  const root = new FakeRoot();
  const type = new FakeElement({ type: "System.String" });
  const secondType = new FakeElement({ type: "System.Int32" });
  const namespace = new FakeElement({ namespace: "System" });
  const secondNamespace = new FakeElement({ namespace: "System.Collections" });
  const kind = new FakeElement({ kindFilter: "class" });
  const secondKind = new FakeElement({ kindFilter: "interface" });
  root.addAll("[data-type]", type, secondType);
  root.addAll("[data-namespace]", namespace, secondNamespace);
  root.addAll("[data-kind-filter]", kind, secondKind);
  const clear = root.add("#clear-filter", new FakeElement());
  const namespaceJump = root.add("#namespace-jump", new FakeElement());
  const filter = root.add("#type-filter", new FakeElement());
  const typeList = root.add("#type-list", new FakeElement());

  const calls: string[] = [];
  let forwardedListEvent: KeyboardEvent | null = null;
  const actions = recordingActions(calls);
  actions.onListKeyDown = event => {
    forwardedListEvent = event;
    event.preventDefault();
    calls.push(`list:${event.key}`);
  };
  bindTypePanel(root as unknown as ParentNode, actions);
  namespaceJump.value = "System.Text";
  filter.value = "json";

  type.dispatch("click");
  secondType.dispatch("click");
  namespace.dispatch("click");
  secondNamespace.dispatch("click");
  namespaceJump.dispatch("change");
  kind.dispatch("click");
  secondKind.dispatch("click");
  clear.dispatch("click");
  filter.dispatch("input");
  const listKey = keyboardEvent("End");
  typeList.dispatch("keydown", listKey.event);

  assert.deepEqual(calls, [
    "type:System.String",
    "type:System.Int32",
    "namespace:System",
    "namespace:System.Collections",
    "namespace:System.Text",
    "kind:class",
    "kind:interface",
    "clear",
    "filter:json",
    "list:End",
  ]);
  assert.equal(forwardedListEvent, listKey.event);
  assert.equal(listKey.state.prevented, true);
});

test("type panel bindings dispatch the rendered member navigation controls", () => {
  const root = new FakeRoot();
  const member = new FakeElement({ navMember: "M:Length" });
  const secondMember = new FakeElement({ navMember: "M:Count" });
  const overload = new FakeElement({ navOverload: "2" });
  const secondOverload = new FakeElement({ navOverload: "0" });
  root.addAll("[data-nav-member]", member, secondMember);
  root.addAll("[data-nav-overload]", overload, secondOverload);
  const showTypes = root.add("#nav-to-types", new FakeElement());
  const typeList = root.add("#type-list", new FakeElement());
  const calls: string[] = [];
  bindTypePanel(
    root as unknown as ParentNode,
    recordingActions(calls));

  member.dispatch("click");
  secondMember.dispatch("click");
  overload.dispatch("click");
  secondOverload.dispatch("click");
  showTypes.dispatch("click");
  typeList.dispatch("keydown", keyboardEvent("Home").event);

  assert.deepEqual(calls, [
    "member:M:Length",
    "member:M:Count",
    "overload:2",
    "overload:0",
    "types",
    "list:Home",
  ]);
});

test("type filter keys preserve list focus and Escape behavior", () => {
  const root = new FakeRoot();
  const filter = root.add("#type-filter", new FakeElement());
  const typeList = root.add("#type-list", new FakeElement());
  let escapes = 0;
  let listKeys = 0;
  bindTypePanel(root as unknown as ParentNode, {
    ...recordingActions([]),
    onListKeyDown: () => {
      listKeys++;
    },
    onTypeFilterEscape: () => {
      escapes++;
    },
  });

  const ignored = keyboardEvent("a");
  filter.dispatch("keydown", ignored.event);
  assert.equal(typeList.focused, false);
  assert.equal(ignored.state.prevented, false);
  assert.equal(escapes, 0);
  assert.equal(listKeys, 0);

  const down = keyboardEvent("ArrowDown");
  filter.dispatch("keydown", down.event);
  assert.equal(typeList.focused, true);
  assert.equal(down.state.prevented, true);
  assert.equal(escapes, 0);
  assert.equal(listKeys, 0);

  typeList.focused = false;
  const escape = keyboardEvent("Escape");
  filter.dispatch("keydown", escape.event);
  assert.equal(escapes, 1);
  assert.equal(escape.state.prevented, false);
  assert.equal(typeList.focused, false);
  assert.equal(listKeys, 0);
});

test("type panel binding tolerates controls from the inactive nav being absent", () => {
  const root = new FakeRoot();
  assert.doesNotThrow(() => bindTypePanel(
    root as unknown as ParentNode,
    recordingActions([])));
});

test("the type nav lists namespace groups with the current type selected", () => {
  const html = renderTypeNav({
    current: jsonSerializer,
    visible: [jsonSerializer, jsonDocument],
    typeGroups: new Map([["System.Text.Json", [jsonSerializer, jsonDocument]]]),
    typeFilter: "",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 1,
    namespaceOptionsHtml: '<option value="System.Text.Json">System.Text.Json · 2</option>',
    kindFilters: ["class"],
    accessibilityControlHtml: "",
    libraryControlHtml: "",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /2 shown/);
  assert.match(
    html,
    /data-type="System\.Text\.Json\.JsonSerializer" role="option" aria-selected="true"/);
  assert.match(
    html,
    /data-type="System\.Text\.Json\.JsonDocument" role="option" aria-selected="false"/);
  assert.match(html, /data-namespace="System\.Text\.Json"/);
  assert.match(html, /id="clear-filter"/);
  assert.match(html, /id="type-filter"/);
  assert.match(html, /id="namespace-jump"/);
  assert.match(html, /data-kind-filter="class"/);
  assert.match(html, /id="type-list" data-nav-scope="types"/);
  assert.match(html, /data-nav-selection="type:System\.Text\.Json\.JsonSerializer"/);
});

test("the type nav reports no matches for an empty filtered group", () => {
  const html = renderTypeNav({
    current: jsonSerializer,
    visible: [],
    typeGroups: new Map(),
    typeFilter: "nothing-matches",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 0,
    namespaceOptionsHtml: "",
    kindFilters: [],
    accessibilityControlHtml: "",
    libraryControlHtml: "",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /No public types match this filter\./);
});

test("the type nav handles a package with no projected types", () => {
  const html = renderTypeNav({
    current: null,
    visible: [],
    typeGroups: new Map(),
    typeFilter: "",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 0,
    namespaceOptionsHtml: "",
    kindFilters: [],
    accessibilityControlHtml: "",
    libraryControlHtml: "",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /data-nav-selection=""/);
  assert.match(html, /No public types match this filter\./);
});

test("the member nav marks the active group and its selected overload", () => {
  const group = {
    key: "method:Serialize",
    name: "Serialize",
    kind: "method",
    overloads: [
      { signature: "string Serialize(object value)" },
      { signature: "string Serialize<T>(T value)" },
    ],
  };
  const entries: MemberNavEntry[] = [
    { kind: "member", group },
    { kind: "overload", group, index: 0 },
    { kind: "overload", group, index: 1 },
  ];

  const html = renderMemberNav({
    type: jsonSerializer,
    entries,
    memberCount: 1,
    visibleMemberCount: 1,
    filterControlsHtml: '<label id="member-filters">filters</label>',
    selectedMemberKey: "method:Serialize",
    selectedOverloadIndex: 1,
    escapeHtml,
    typeDisplayName,
    shortKind,
    highlight,
  });

  assert.match(html, /class="type-row member-row active-group [^"]*" data-nav-member="method:Serialize"/);
  assert.match(
    html,
    /data-nav-overload="1" role="option" aria-selected="true"/);
  assert.match(
    html,
    /data-nav-overload="0" role="option" aria-selected="false"/);
  assert.match(html, /id="nav-to-types"/);
  assert.match(
    html,
    /id="type-list" data-nav-scope="members:System\.Text\.Json\.JsonSerializer"/);
  assert.match(
    html,
    /data-nav-selection="overload:method:Serialize:1"/);
  assert.match(html, /id="member-filters"/);
  assert.match(html, /←→ sections/);
});

test("the member nav does not advertise sections without a selected member", () => {
  const html = renderMemberNav({
    type: jsonSerializer,
    entries: [],
    memberCount: 1,
    visibleMemberCount: 0,
    filterControlsHtml: "",
    selectedMemberKey: "",
    selectedOverloadIndex: null,
    escapeHtml,
    typeDisplayName,
    shortKind,
    highlight,
  });

  assert.doesNotMatch(html, /←→ sections/);
});

test("the type heading reports the owning package and library", () => {
  const html = typeHeading({
    item: jsonSerializer,
    packageContext: { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" },
    escapeHtml,
    typeDisplayName,
    kindIcon,
    highlight,
  });

  assert.match(html, /<h1>JsonSerializer<\/h1>/);
  assert.match(html, /System\.Text\.Json\.dll/);
  assert.match(html, /System\.Text\.Json@9\.0\.0/);
});

test("type metadata signature keys on the exact package, framework, and type coordinate", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  assert.equal(
    typeMetadataSignature(jsonSerializer, packageContext),
    "System.Text.Json@9.0.0/net9.0/System.Text.Json.dll/System.Text.Json.JsonSerializer");
});

test("type source signature routes through the shared decompiler-taste-aware key", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const calls: {
    parts: readonly string[];
    taste: readonly string[];
  }[] = [];
  const memberRequestKey = (parts: readonly string[], taste: readonly string[]) => {
    calls.push({ parts, taste });
    return "computed-key";
  };

  const signature = typeSourceSignature(jsonSerializer, packageContext, ["identifier-casing"], memberRequestKey);

  assert.equal(signature, "computed-key");
  assert.deepEqual(calls, [{
    parts: [
      "System.Text.Json",
      "9.0.0",
      "net9.0",
      "System.Text.Json.dll",
      "T:System.Text.Json.JsonSerializer",
    ],
    taste: ["identifier-casing"],
  }]);
});

test("type metadata renders a loading state while the projection is in flight", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const key = typeMetadataSignature(jsonSerializer, packageContext);
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: key,
      typeMetadataLoading: true,
      typeMetadataError: null,
      typeMetadata: null,
    },
    memberCompositionHtml: "",
    escapeHtml,
    relatedTypeChip: name => `<button>${escapeHtml(name)}</button>`,
    factRows,
  });

  assert.match(html, /Projecting type metadata…/);
});

test("type metadata renders composition, interfaces, and derived types once loaded", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const key = typeMetadataSignature(jsonSerializer, packageContext);
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: key,
      typeMetadataLoading: false,
      typeMetadataError: null,
      typeMetadata: {
        kind: "class",
        accessibility: "public",
        namespace: "System.Text.Json",
        assembly: "System.Text.Json.dll",
        interfaces: ["System.IDisposable"],
        derivedTypes: ["System.Text.Json.MyJsonSerializer"],
        composition: { total: 3 },
      },
    },
    memberCompositionHtml: `
      <div class="composition-filters">
        <button data-member-jump-kind="method"><strong>3</strong><span>method</span></button>
      </div>`,
    escapeHtml,
    relatedTypeChip: name => `<button data-graph-type="${escapeHtml(name)}">${escapeHtml(name)}</button>`,
    factRows,
  });

  assert.match(html, /Implements/);
  assert.match(html, /data-graph-type="System\.IDisposable"/);
  assert.match(html, /Known derived types/);
  assert.match(html, /Members/);
  assert.match(html, /data-member-jump-kind="method"/);
});

test("type source renders the provenance and copy action once loaded", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: { provider: "original", provenance: "SourceLink", url: "https://example.test", text: "class JsonSerializer {}" },
      typeSourceError: null,
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Original source/);
  assert.match(html, /SourceLink/);
  assert.match(html, /id="copy-type-source"/);
  assert.match(html, /open source ↗/);
});

test("type source reports a failure without a stale result", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: null,
      typeSourceError: "The decompiler query failed.",
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Type source failed/);
  assert.match(html, /The decompiler query failed\./);
});
