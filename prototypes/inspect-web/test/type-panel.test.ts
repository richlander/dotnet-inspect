import assert from "node:assert/strict";
import test from "node:test";
import {
  bindTypePanel,
  renderGraphMemberPending,
  renderMemberNav,
  renderSourcePageActions,
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
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "../src/workbench-keybindings.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  value = "";
  open = false;
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

  dispatch(type: string, event: Event = fakeDom.event()) {
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

function keyboardEvent(
  key: string,
  modifiers: Partial<Pick<
    KeyboardEvent,
    "altKey" | "ctrlKey" | "metaKey" | "shiftKey"
  >> = {},
) {
  const state = { prevented: false };
  const event = fakeDom.keyboardEvent({
    altKey: modifiers.altKey ?? false,
    ctrlKey: modifiers.ctrlKey ?? false,
    key,
    metaKey: modifiers.metaKey ?? false,
    preventDefault: () => {
      state.prevented = true;
    },
    shiftKey: modifiers.shiftKey ?? false,
  });
  return { event, state };
}

function bindPanel(
  root: FakeRoot,
  actions: TypePanelBindingActions,
): KeybindingRegistry {
  const keybindings = new KeybindingRegistry();
  bindTypePanel(fakeDom.parentNode(root), actions, keybindings);
  return keybindings;
}

function dispatchKey(
  keybindings: KeybindingRegistry,
  target: FakeElement,
  input: ReturnType<typeof keyboardEvent>,
) {
  const scopedTarget = fakeDom.eventTarget(target);
  Object.assign(input.event, {
    defaultPrevented: false,
    target: scopedTarget,
    composedPath: () => [scopedTarget],
  });
  return keybindings.dispatch(input.event);
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
    onCopyAnchor: value => {
      calls.push(`copy-anchor:${value}`);
    },
    onCopyMemberSource: () => {
      calls.push("copy-member-source");
    },
    onCopySignature: () => {
      calls.push("copy-signature");
    },
    onCopyTypeSource: () => {
      calls.push("copy-type-source");
    },
    onKindSelect: value => calls.push(`kind:${value}`),
    onLibraryOpen: () => calls.push("library"),
    onListKeyDown: event => {
      calls.push(`list:${event.key}`);
      return true;
    },
    onMemberAccessibilityFilterSelect: value =>
      calls.push(`member-access:${value}`),
    onMemberBack: () => calls.push("member-back"),
    onMemberCompositionAccessibilitySelect: value =>
      calls.push(`member-jump-access:${value}`),
    onMemberCompositionKindSelect: value =>
      calls.push(`member-jump-kind:${value}`),
    onMemberCompositionTraitSelect: value =>
      calls.push(`member-jump-trait:${value}`),
    onMemberFilterChange: value => calls.push(`member-filter:${value}`),
    onMemberFilterClear: () => calls.push("member-filter-clear"),
    onMemberFilterDisclosureToggle: value =>
      calls.push(`member-filter-disclosure:${value}`),
    onMemberFilterKeyDown: (event, value) => {
      calls.push(`member-filter-key:${event.key}:${value}`);
      return true;
    },
    onMemberGroupOpen: value => calls.push(`member-open:${value}`),
    onMemberKindFilterSelect: value => calls.push(`member-kind:${value}`),
    onMemberOverloadOpen: value => calls.push(`member-overload:${value}`),
    onMemberSelect: value => calls.push(`member:${value}`),
    onMemberTraitFilterSelect: value => calls.push(`member-trait:${value}`),
    onNamespaceSelect: value => calls.push(`namespace:${value}`),
    onOverloadSelect: value => calls.push(`overload:${value}`),
    onShowTypes: () => calls.push("types"),
    onTypeFilterChange: value => calls.push(`filter:${value}`),
    onTypeFilterDisclosureToggle: value =>
      calls.push(`type-filter-disclosure:${value}`),
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
  const disclosure = root.add(
    "[data-member-filter-disclosure]",
    new FakeElement());
  const clear = root.add("#clear-member-filter", new FakeElement());
  const calls: string[] = [];

  const keybindings = bindPanel(root, recordingActions(calls));

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
  disclosure.open = true;
  disclosure.dispatch("toggle");
  const arrow = keyboardEvent("ArrowDown");
  dispatchKey(keybindings, filter, arrow);
  clear.dispatch("click");
  assert.deepEqual(calls, [
    "member-kind:method",
    "member-access:protected",
    "member-trait:isStatic",
    "member-filter:parse",
    "member-filter-disclosure:true",
    "member-filter-key:ArrowDown:parse",
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
  const disclosure = root.add(
    "[data-type-filter-disclosure]",
    new FakeElement());
  const namespaceJump = root.add("#namespace-jump", new FakeElement());
  const filter = root.add("#type-filter", new FakeElement());
  const typeList = root.add("#type-list", new FakeElement());
  const library = root.add("[data-library-root]", new FakeElement());

  const calls: string[] = [];
  let forwardedListEvent: KeyboardEvent | null = null;
  const actions = recordingActions(calls);
  actions.onListKeyDown = event => {
    forwardedListEvent = event;
    calls.push(`list:${event.key}`);
    return true;
  };
  const keybindings = bindPanel(root, actions);
  namespaceJump.value = "System.Text";
  filter.value = "json";

  type.dispatch("click");
  library.dispatch("click");
  secondType.dispatch("click");
  namespace.dispatch("click");
  secondNamespace.dispatch("click");
  namespaceJump.dispatch("change");
  kind.dispatch("click");
  secondKind.dispatch("click");
  disclosure.open = true;
  disclosure.dispatch("toggle");
  clear.dispatch("click");
  filter.dispatch("input");
  const listKey = keyboardEvent("End");
  dispatchKey(keybindings, typeList, listKey);

  assert.deepEqual(calls, [
    "type:System.String",
    "library",
    "type:System.Int32",
    "namespace:System",
    "namespace:System.Collections",
    "namespace:System.Text",
    "kind:class",
    "kind:interface",
    "type-filter-disclosure:true",
    "clear",
    "filter:json",
    "list:End",
  ]);
  assert.equal(clear.focused, true);
  assert.equal(forwardedListEvent, listKey.event);
  assert.equal(listKey.state.prevented, false);
});

test("type list navigation yields Cmd/Ctrl+K to the command palette", () => {
  const root = new FakeRoot();
  const typeList = root.add("#type-list", new FakeElement());
  const calls: string[] = [];
  const keybindings = bindPanel(root, recordingActions(calls));
  keybindings.register({
    id: "workspace.open-commands",
    key: "k",
    modifiers: { commandOrControl: true },
    allowExtraModifiers: true,
    priority: WORKBENCH_KEYBINDING_PRIORITY.workspace,
    run: () => {
      calls.push("commands");
      return true;
    },
  });

  const controlK = keyboardEvent("k", { ctrlKey: true });
  assert.equal(
    dispatchKey(keybindings, typeList, controlK).bindingId,
    "workspace.open-commands",
  );
  const metaK = keyboardEvent("k", { metaKey: true });
  assert.equal(
    dispatchKey(keybindings, typeList, metaK).bindingId,
    "workspace.open-commands",
  );
  assert.deepEqual(calls, ["commands", "commands"]);

  assert.equal(
    dispatchKey(keybindings, typeList, keyboardEvent("k")).bindingId,
    "type-list.navigate",
  );
  assert.equal(
    dispatchKey(
      keybindings,
      typeList,
      keyboardEvent("j", { ctrlKey: true }),
    ).bindingId,
    "type-list.navigate",
  );
  assert.deepEqual(calls, ["commands", "commands", "list:k", "list:j"]);
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
  const keybindings = bindPanel(root, recordingActions(calls));

  member.dispatch("click");
  secondMember.dispatch("click");
  overload.dispatch("click");
  secondOverload.dispatch("click");
  showTypes.dispatch("click");
  dispatchKey(keybindings, typeList, keyboardEvent("Home"));

  assert.deepEqual(calls, [
    "member:M:Length",
    "member:M:Count",
    "overload:2",
    "overload:0",
    "types",
    "list:Home",
  ]);
});

test("type panel bindings dispatch member composition and detail controls", () => {
  const root = new FakeRoot();
  const jumpKind = new FakeElement({ memberJumpKind: "method" });
  const defaultJumpKind = new FakeElement();
  const jumpAccess = new FakeElement({ memberJumpAccess: "protected" });
  const defaultJumpAccess = new FakeElement();
  const jumpTrait = new FakeElement({ memberJumpTrait: "isStatic" });
  const defaultJumpTrait = new FakeElement();
  const member = new FakeElement({ member: "M:Parse" });
  const defaultMember = new FakeElement();
  const overload = new FakeElement({ overload: "2" });
  const defaultOverload = new FakeElement();
  const anchor = new FakeElement({ copyAnchor: "digest" });
  const invalidAnchor = new FakeElement({ copyAnchor: "unknown" });
  root.addAll("[data-member-jump-kind]", jumpKind, defaultJumpKind);
  root.addAll("[data-member-jump-access]", jumpAccess, defaultJumpAccess);
  root.addAll("[data-member-jump-trait]", jumpTrait, defaultJumpTrait);
  root.addAll("[data-member]", member, defaultMember);
  root.addAll("[data-overload]", overload, defaultOverload);
  root.addAll("[data-copy-anchor]", anchor, invalidAnchor);
  const back = root.add("#member-back", new FakeElement());
  const copySignature = root.add("#copy-signature", new FakeElement());
  const copyMemberSource = root.add("#copy-source", new FakeElement());
  const copyTypeSource = root.add("#copy-type-source", new FakeElement());
  const calls: string[] = [];

  bindPanel(root, recordingActions(calls));

  assert.deepEqual(calls, []);
  jumpKind.dispatch("click");
  defaultJumpKind.dispatch("click");
  jumpAccess.dispatch("click");
  defaultJumpAccess.dispatch("click");
  jumpTrait.dispatch("click");
  defaultJumpTrait.dispatch("click");
  member.dispatch("click");
  defaultMember.dispatch("click");
  overload.dispatch("click");
  defaultOverload.dispatch("click");
  back.dispatch("click");
  copySignature.dispatch("click");
  anchor.dispatch("click");
  invalidAnchor.dispatch("click");
  copyMemberSource.dispatch("click");
  copyTypeSource.dispatch("click");

  assert.deepEqual(calls, [
    "member-jump-kind:method",
    "member-jump-kind:all",
    "member-jump-access:protected",
    "member-jump-access:all",
    "member-jump-trait:isStatic",
    "member-jump-trait:",
    "member-open:M:Parse",
    "member-open:",
    "member-overload:2",
    "member-overload:NaN",
    "member-back",
    "copy-signature",
    "copy-anchor:digest",
    "copy-anchor:undefined",
    "copy-member-source",
    "copy-type-source",
  ]);
});

test("type filter keys preserve list focus and Escape behavior", () => {
  const root = new FakeRoot();
  const filter = root.add("#type-filter", new FakeElement());
  const typeList = root.add("#type-list", new FakeElement());
  let escapes = 0;
  let listKeys = 0;
  const keybindings = bindPanel(root, {
    ...recordingActions([]),
    onListKeyDown: () => {
      listKeys++;
      return true;
    },
    onTypeFilterEscape: () => {
      escapes++;
    },
  });

  const ignored = keyboardEvent("a");
  dispatchKey(keybindings, filter, ignored);
  assert.equal(typeList.focused, false);
  assert.equal(ignored.state.prevented, false);
  assert.equal(escapes, 0);
  assert.equal(listKeys, 0);

  const down = keyboardEvent("ArrowDown");
  dispatchKey(keybindings, filter, down);
  assert.equal(typeList.focused, true);
  assert.equal(down.state.prevented, true);
  assert.equal(escapes, 0);
  assert.equal(listKeys, 0);

  typeList.focused = false;
  filter.value = "json";
  const escape = keyboardEvent("Escape");
  dispatchKey(keybindings, filter, escape);
  assert.equal(escapes, 1);
  assert.equal(escape.state.prevented, true);
  assert.equal(typeList.focused, false);
  assert.equal(listKeys, 0);

  filter.value = "";
  const emptyEscape = keyboardEvent("Escape");
  dispatchKey(keybindings, filter, emptyEscape);
  assert.equal(escapes, 1);
  assert.equal(emptyEscape.state.prevented, false);
});

test("type panel binding tolerates controls from the inactive nav being absent", () => {
  const root = new FakeRoot();
  assert.doesNotThrow(() => bindPanel(root, recordingActions([])));
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
    library: "System.Text.Json",
    filtersExpanded: false,
    filterSummary: "public",
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
  assert.match(
    html,
    /<details class="filter-disclosure type-filter-disclosure" data-type-filter-disclosure>/);
  assert.match(html, /<summary id="type-filter-summary">/);
  assert.match(html, /<strong>Filters<\/strong><small>public<\/small>/);
  assert.match(html, /id="type-filter"/);
  assert.match(html, /id="namespace-jump"/);
  assert.match(html, /id="content-navigation-pane"/);
  assert.match(html, /data-kind-filter="class"/);
  assert.match(html, /id="type-list" data-nav-scope="types"/);
  assert.match(html, /data-library-root/);
  assert.match(html, />System\.Text\.Json<\/span>/);
  assert.doesNotMatch(html, /type-library-context/);
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
    library: "System.Text.Json",
    filtersExpanded: true,
    filterSummary: "nothing-matches · public",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    shortKind,
  });

  assert.match(html, /No public types match this filter\./);
  assert.match(html, /data-type-filter-disclosure open/);
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
    library: "System.Text.Json",
    filtersExpanded: false,
    filterSummary: "All types",
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
  assert.match(html, /id="content-navigation-pane"/);
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

test("the member nav labels a selected graph-only target", () => {
  const graphGroup = {
    key: "graph:method:MoveNext",
    name: "MoveNext",
    kind: "method",
    overloads: [{
      signature: "void MoveNext()",
      graphOnly: true,
    }],
  };

  const html = renderMemberNav({
    type: jsonSerializer,
    entries: [{ kind: "member", group: graphGroup }],
    memberCount: 0,
    visibleMemberCount: 0,
    filterControlsHtml: "",
    selectedMemberKey: graphGroup.key,
    selectedOverloadIndex: 0,
    escapeHtml,
    typeDisplayName,
    shortKind,
    highlight,
  });

  assert.match(html, /class="type-row member-row graph-member-row active-group/);
  assert.match(html, /graph target · method/);
  assert.match(html, /0 of 0/);
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

test("an absent overload entry fails visibly rather than rendering an empty list", () => {
  const group = {
    key: "method:Serialize",
    name: "Serialize",
    kind: "method",
    overloads: [{ signature: "string Serialize(object value)" }],
  };

  assert.throws(
    () => renderMemberNav({
      type: jsonSerializer,
      entries: [{ kind: "overload", group, index: 1 }],
      memberCount: 1,
      visibleMemberCount: 0,
      filterControlsHtml: "",
      selectedMemberKey: "method:Serialize",
      selectedOverloadIndex: 1,
      escapeHtml,
      typeDisplayName,
      shortKind,
      highlight,
    }),
    /Member group 'method:Serialize' has no overload 1\./);
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

test("pending graph-member rendering composes the extracted type heading", () => {
  const html = renderGraphMemberPending({
    item: jsonSerializer,
    title: "JsonSerializer.<Open>",
    packageContext: { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" },
    escapeHtml,
    typeDisplayName,
    kindIcon,
    highlight,
  });

  assert.match(html, /<h1>JsonSerializer<\/h1>/);
  assert.match(html, /Opening JsonSerializer\.&lt;Open&gt;…/);
  assert.match(html, /class="document-section graph-member-pending" aria-live="polite"/);
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
  assert.match(
    html,
    /class="metadata-surface"[\s\S]*?<h1 id="metadata-surface-title">Metadata<\/h1>[\s\S]*?class="metadata-surface-scroll"[\s\S]*?Projecting type metadata…[\s\S]*?class="metadata-surface-footer"/);
  assert.match(html, /System\.Text\.Json\.JsonSerializer/);
  assert.match(html, /net9\.0 · System\.Text\.Json\.dll · System\.Text\.Json@9\.0\.0/);
  assert.doesNotMatch(html, /class="type-heading"/);
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

  assert.match(
    html,
    /class="metadata-surface-scroll"[\s\S]*?class="document-section metadata-shape-section"[\s\S]*?Type shape/);
  assert.match(html, /Implements/);
  assert.match(html, /data-graph-type="System\.IDisposable"/);
  assert.match(html, /Known derived types/);
  assert.match(html, /Members/);
  assert.match(html, /data-member-jump-kind="method"/);
});

test("type metadata keeps projection failures inside the full-area surface", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const key = typeMetadataSignature(jsonSerializer, packageContext);
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: key,
      typeMetadataLoading: false,
      typeMetadataError: "projection unavailable",
      typeMetadata: null,
    },
    memberCompositionHtml: "",
    escapeHtml,
    relatedTypeChip: name => `<button>${escapeHtml(name)}</button>`,
    factRows,
  });

  assert.match(
    html,
    /class="metadata-surface"[\s\S]*?class="document-section metadata-surface-state empty-document"[\s\S]*?Metadata projection failed[\s\S]*?projection unavailable[\s\S]*?class="metadata-surface-footer"/);
  assert.match(html, /data-type-graph-surface/);
});

for (const nodeCount of [0, 1, 2]) {
  test(`type metadata keeps relationship warnings visible with ${nodeCount} graph nodes`, () => {
    const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
    const html = renderTypeMetadata({
      item: jsonSerializer,
      packageContext,
      metadataState: {
        typeMetadataKey: typeMetadataSignature(jsonSerializer, packageContext),
        typeMetadataLoading: false,
        typeMetadataError: null,
        typeMetadata: {
          graphNodes: Array.from({ length: nodeCount }, (_, index) => ({ id: `Type${index}` })),
          inspectionFailures: ["Unable to project <related> type"],
        },
      },
      memberCompositionHtml: "",
      escapeHtml,
      relatedTypeChip: escapeHtml,
      factRows,
    });
    assert.match(html, /Unable to project &lt;related&gt; type/);
    assert.equal(html.includes("data-type-graph-surface"), nodeCount > 1);
    if (nodeCount > 1) {
      assert.match(html, /data-type-graph-surface>[\s\S]*?type-graph-diagram[\s\S]*?metadata-warning/);
    }
    assert.match(html, /Type shape/);
    assert.match(html, /metadata-surface-footer/);
  });
}

test("type PDB source renders code above provenance once loaded", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: { provider: "pdb", provenance: "SourceLink", url: "https://example.test", text: "class JsonSerializer {}" },
      typeSourceError: null,
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /PDB Source/);
  assert.match(html, /SourceLink/);
  assert.match(
    html,
    /<pre[^>]*role="region"[^>]*aria-label="Source code"[\s\S]*class JsonSerializer \{\}[\s\S]*<\/pre>[\s\S]*<footer class="source-provenance">/);
  assert.doesNotMatch(html, /copy-type-source|open source/);
});

test("source page actions render copy and open for the page-owned group", () => {
  const html = renderSourcePageActions({
    source: {
      provider: "pdb",
      provenance: "SourceLink",
      url: "https://example.test/source.cs?x=1&y=2",
      text: "class JsonSerializer {}",
    },
    copyButtonId: "copy-type-source",
    escapeHtml,
  });

  assert.match(html, /id="copy-type-source"[^>]*>Copy<\/button>/);
  assert.match(
    html,
    /class="shell-action-link" href="https:\/\/example\.test\/source\.cs\?x=1&amp;y=2" target="_blank" rel="noreferrer">Open<\/a>/);
});

test("source page actions disable copy until source is available", () => {
  const html = renderSourcePageActions({
    source: null,
    copyButtonId: "copy-source",
    escapeHtml,
  });

  assert.match(html, /id="copy-source"[^>]* disabled>Copy<\/button>/);
  assert.doesNotMatch(html, /shell-action-link/);
});

test("decompiled type source discloses an escaped PDB-source limitation", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      typeSourceKey: "sig",
      typeSourceLoading: false,
      typeSource: {
        provider: "decompiled",
        provenance: "decompiled from IL",
        pdbSourceLimitation: "<checksum mismatch>",
        text: "class JsonSerializer {}",
      },
      typeSourceError: null,
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Decompiled source/);
  assert.match(html, /PDB source unavailable: &lt;checksum mismatch&gt;/);
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
