import assert from "node:assert/strict";
import test from "node:test";
import {
  bindTypePanel,
  createMemberSourcePartSelector,
  memberSourceText,
  renderGraphMemberPending,
  renderMemberNav,
  renderSourcePageActions,
  renderSourceResult,
  renderTypeMetadata,
  renderTypeNav,
  renderTypeSource,
  typeHeading,
  typeMetadataSignature,
  typeSourceSignature,
  typeCodeViewText,
} from "../src/type-panel.ts";
import type {
  ExactTypeApi,
  InertString,
  InspectionDiagnostic,
} from "../src/facades/inspect-web-metadata.d.ts";
import type {
  BrowserMemberSource,
} from "../src/facades/inspect-web-source.d.ts";
import type {
  MemberNavEntry,
  TypePanelBindingActions,
  TypeSummary,
} from "../src/type-panel.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "../src/workbench-keybindings.ts";
import { fakeDom } from "./fake-dom.ts";
import { apiDeclarationsFixture } from "./type-api-declarations-fixture.ts";
import {
  inertStringFixture,
  metadataInertStringFixture,
} from "./inert-string-fixture.ts";

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

function inertString(value: string): InertString {
  // Test fixtures model values after the generated JSON boundary.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as InertString;
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

function availableExactTypeInspection(
  overrides: Partial<ExactTypeApi> = {},
  diagnostics: readonly InspectionDiagnostic[] = [],
  isComplete = diagnostics.length === 0,
) {
  const fullName = overrides.fullName ?? "System.Text.Json.JsonSerializer";
  return {
    content: {
      outcome: 0,
      requestedType: fullName,
      matchedType: fullName,
      type: {
        fullName,
        namespace: "System.Text.Json",
        name: "JsonSerializer",
        definitionIdentity: {
          namespace: "System.Text.Json",
          segments: ["JsonSerializer"],
        },
        introducedTypeParameterCounts: [0],
        kind: "class",
        accessibility: "public",
        attributes: [],
        isSealed: false,
        isAbstract: false,
        isStatic: false,
        isByRefLike: false,
        isReadOnly: false,
        baseType: null,
        interfaces: [],
        derivedTypes: [],
        typeParameters: [],
        members: [],
        enumUnderlyingType: null,
        isForwarded: false,
        ...overrides,
      },
      requestedAssembly: null,
      supplierAssembly: null,
      forwardingHops: [],
      suggestions: [],
      inspectionFailures: [],
      failures: [],
      isAvailable: true,
      isComplete,
    },
    share: {
      kind: "nonProjectable" as const,
      fullUrl: null,
      packet: null,
      path: "exact-type",
      reason: metadataInertStringFixture(
        "Fixture exact-type inspection is not shareable."),
    },
    diagnostics,
  };
}

function unavailableExactTypeInspection(
  outcome: number,
  diagnostics: readonly InspectionDiagnostic[],
) {
  return {
    content: {
      outcome,
      requestedType: "System.Text.Json.JsonSerializer",
      matchedType: null,
      type: null,
      requestedAssembly: null,
      supplierAssembly: null,
      forwardingHops: [],
      suggestions: [],
      inspectionFailures: [],
      failures: [],
      isAvailable: false,
      isComplete: false,
    },
    share: {
      kind: "nonProjectable" as const,
      fullUrl: null,
      packet: null,
      path: "exact-type",
      reason: metadataInertStringFixture(
        "Fixture exact-type inspection is not shareable."),
    },
    diagnostics,
  };
}

function typeDisplayName(item: TypeSummary) {
  return item?.displayName || item?.name || "";
}

function noTypeLibraryLabel() {
  return "";
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
    onMemberSourcePartSelect: part => {
      calls.push(`member-source-part:${part}`);
    },
    onCopySignature: () => {
      calls.push("copy-signature");
    },
    onCopyTypeSource: () => {
      calls.push("copy-type-source");
    },
    onTypeSourceViewSelect: view => {
      calls.push(`type-source-view:${view}`);
    },
    onExploreSource: () => {
      calls.push("explore-source");
    },
    onKindSelect: value => calls.push(`kind:${value}`),
    onTypeNavBack: () => calls.push("type-nav-back"),
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
  const back = root.add("[data-type-nav-back]", new FakeElement());

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
  back.dispatch("click");
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
    "type-nav-back",
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
  const memberSourcePart =
    root.add("#member-source-part", new FakeElement());
  const copyTypeSource = root.add("#copy-type-source", new FakeElement());
  const exploreSource = root.add("#explore-source", new FakeElement());
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
  memberSourcePart.value = "Body";
  memberSourcePart.dispatch("change");
  copyTypeSource.dispatch("click");
  exploreSource.dispatch("click");

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
    "member-source-part:Body",
    "copy-type-source",
    "explore-source",
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
    parentSubject: "library",
    filtersExpanded: false,
    filterSummary: "public",
    escapeHtml,
    typeDisplayName,
    typeLibraryLabel: item =>
      item.id === jsonSerializer.id ? "System.Text.Json" : "",
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
  assert.match(html, /data-type-nav-back title="Back to library" aria-label="System\.Text\.Json: Back to library"/);
  assert.match(html, />System\.Text\.Json<\/span>/);
  assert.doesNotMatch(html, /type-library-context/);
  assert.match(html, /data-nav-selection="type:System\.Text\.Json\.JsonSerializer"/);
  assert.match(html, /System\.Text\.Json · class/);
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
    parentSubject: "library",
    filtersExpanded: true,
    filterSummary: "nothing-matches · public",
    escapeHtml,
    typeDisplayName,
    typeLibraryLabel: noTypeLibraryLabel,
    kindIcon,
    shortKind,
  });

  assert.match(html, /No public types match this filter\./);
  assert.match(html, /data-type-filter-disclosure open/);
});

test("the type nav omits a parent action when the Library has no visible parent", () => {
  const html = renderTypeNav({
    current: jsonSerializer,
    visible: [jsonSerializer],
    typeGroups: new Map([["System.Text.Json", [jsonSerializer]]]),
    typeFilter: "",
    namespaceFilter: "",
    kindFilter: "",
    namespaceCount: 1,
    namespaceOptionsHtml: "",
    kindFilters: ["class"],
    accessibilityControlHtml: "",
    library: "System.Text.Json",
    parentSubject: null,
    filtersExpanded: false,
    filterSummary: "public",
    escapeHtml,
    typeDisplayName,
    typeLibraryLabel: noTypeLibraryLabel,
    kindIcon,
    shortKind,
  });

  assert.doesNotMatch(html, /data-type-nav-back/);
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
    parentSubject: "package",
    filtersExpanded: false,
    filterSummary: "All types",
    escapeHtml,
    typeDisplayName,
    typeLibraryLabel: noTypeLibraryLabel,
    kindIcon,
    shortKind,
  });

  assert.match(html, /data-nav-selection=""/);
  assert.match(html, /No public types match this filter\./);
  assert.match(html, /data-type-nav-back title="Back to package" aria-label="System\.Text\.Json: Back to package"/);
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

test("the type heading accepts a product-owned defining Library label", () => {
  const html = typeHeading({
    item: jsonSerializer,
    packageContext: { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" },
    libraryLabel: "System.Text.Json · lib/net9.0/right/System.Text.Json.dll",
    escapeHtml,
    typeDisplayName,
    kindIcon,
    highlight,
  });

  assert.match(
    html,
    /System\.Text\.Json · lib\/net9\.0\/right\/System\.Text\.Json\.dll/);
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

test("type metadata signature distinguishes exact Platform Libraries", () => {
  const packageContext = { id: "Microsoft.NETCore.App", version: "11.0.0", activeFramework: "net11.0" };
  assert.notEqual(
    typeMetadataSignature(
      jsonSerializer,
      packageContext,
      '["netcore.app","System.Text.Json.dll"]'),
    typeMetadataSignature(
      jsonSerializer,
      packageContext,
      '["aspnetcore.app","System.Text.Json.dll"]'));
});

test("type metadata signature distinguishes Workspace dependency populations", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  assert.notEqual(
    typeMetadataSignature(
      jsonSerializer,
      packageContext,
      "",
      '[{"package":"System.Text.Json","version":"9.0.0","framework":"net9.0"}]'),
    typeMetadataSignature(
      jsonSerializer,
      packageContext,
      "",
      '[{"package":"System.Data.Common","version":"9.0.0","framework":"net9.0"},{"package":"System.Text.Json","version":"9.0.0","framework":"net9.0"}]'));
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
      "source",
    ],
    taste: ["identifier-casing"],
  }]);
});

test("declaration request identity separates scope and ignores decompiler taste", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const requestKey = (parts: readonly string[], taste: readonly string[]) =>
    JSON.stringify([parts, taste]);
  const api = typeSourceSignature(jsonSerializer, packageContext, [], requestKey, "api-declarations");
  assert.notEqual(api, typeSourceSignature(jsonSerializer, packageContext, [], requestKey, "source"));
  assert.notEqual(api, typeSourceSignature(jsonSerializer, packageContext, [], requestKey, "all-declarations"));
  assert.equal(api, typeSourceSignature(
    jsonSerializer, packageContext, ["identifier-casing"], requestKey, "api-declarations"));
});

test("type source picker dispatches supported views without eager work", () => {
  const root = new FakeRoot();
  const picker = root.add("#type-source-view", new FakeElement());
  const calls: string[] = [];
  bindPanel(root, recordingActions(calls));
  assert.deepEqual(calls, []);
  picker.value = "api-declarations";
  picker.dispatch("change");
  picker.value = "all-declarations";
  picker.dispatch("change");
  picker.value = "unknown";
  picker.dispatch("change");
  assert.deepEqual(calls, [
    "type-source-view:api-declarations",
    "type-source-view:all-declarations",
  ]);
});

test("declaration actions remain inside the type viewer and disable unavailable copy", () => {
  const html = renderSourcePageActions({
    source: null,
    typeView: "all-declarations",
    copyButtonId: "copy-type-source",
    escapeHtml,
  });
  assert.match(html, /id="type-source-view"/);
  assert.match(html, /value="all-declarations" selected/);
  assert.match(html, /id="copy-type-source" type="button" disabled/);
  assert.doesNotMatch(html, /id="explore-source"/);
  assert.doesNotMatch(renderSourcePageActions({
    source: null,
    copyButtonId: "copy-source",
    escapeHtml,
  }), /id="type-source-view"/);
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
        exactTypeInspection: availableExactTypeInspection({
          interfaces: ["System.IDisposable"],
          members: [{
            name: "Serialize",
            kind: "method",
            signature: null,
          }],
        }),
        derivedTypes: ["System.Text.Json.MyJsonSerializer"],
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

test("type metadata renders exact ambiguity instead of a legacy Type surface", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: typeMetadataSignature(jsonSerializer, packageContext),
      typeMetadataLoading: false,
      typeMetadataError: null,
      typeMetadata: {
        exactTypeInspection: unavailableExactTypeInspection(2, [{
          code: "exact-type.ambiguous",
          severity: 2,
          summary: inertString(
            "The Type resolved to more than one exact Metadata definition."),
          correspondence: null,
        }]),
      },
    },
    memberCompositionHtml: "<div>legacy members</div>",
    escapeHtml,
    relatedTypeChip: escapeHtml,
    factRows,
  });

  assert.match(html, /Type selection is ambiguous/);
  assert.match(html, /exact-type\.ambiguous/);
  assert.doesNotMatch(html, /Type shape/);
  assert.doesNotMatch(html, /legacy members/);
});

test("type metadata renders exact diagnostics for incomplete available content", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: typeMetadataSignature(jsonSerializer, packageContext),
      typeMetadataLoading: false,
      typeMetadataError: null,
      typeMetadata: {
        exactTypeInspection: availableExactTypeInspection(
          {},
          [{
            code: "exact-type.inspection-incomplete",
            severity: 1,
            summary: inertString(
              "One metadata row could not be decoded."),
            correspondence: null,
          }],
          false),
      },
    },
    memberCompositionHtml: "",
    escapeHtml,
    relatedTypeChip: escapeHtml,
    factRows,
  });

  assert.match(html, /Type shape/);
  assert.match(html, /Exact type inspection may be incomplete/);
  assert.match(html, /exact-type\.inspection-incomplete/);
  assert.match(html, /One metadata row could not be decoded/);
});

test("type metadata renders nonfatal exact constraint diagnostics", () => {
  const packageContext = { id: "System.Text.Json", version: "9.0.0", activeFramework: "net9.0" };
  const html = renderTypeMetadata({
    item: jsonSerializer,
    packageContext,
    metadataState: {
      typeMetadataKey: typeMetadataSignature(jsonSerializer, packageContext),
      typeMetadataLoading: false,
      typeMetadataError: null,
      typeMetadata: {
        exactTypeInspection: availableExactTypeInspection(
          {},
          [{
            code: "exact-type.constraint-resolution-incomplete",
            severity: 1,
            summary: inertString(
              "Generic-constraint classification was incomplete."),
            correspondence: null,
          }],
          true),
      },
    },
    memberCompositionHtml: "",
    escapeHtml,
    relatedTypeChip: escapeHtml,
    factRows,
  });

  assert.match(html, /Type shape/);
  assert.match(html, /Exact type inspection may be incomplete/);
  assert.match(html, /exact-type\.constraint-resolution-incomplete/);
  assert.match(html, /Generic-constraint classification was incomplete/);
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
          exactTypeInspection: availableExactTypeInspection(),
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
      status: "ready",
      signature: "sig",
      source: {
        kind: "source",
        value: {
          provider: "pdb",
          provenance: inertStringFixture("SourceLink"),
          url: "https://example.test",
          pdbSourceLimitation: null,
          text: "class JsonSerializer {}",
        },
        share: {
          kind: "available",
          fullUrl: "https://example.test/type-source",
          packet: "type-source",
        },
        diagnostics: [],
      },
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

for (const scope of ["ApiVisible", "All"] as const) {
  test(`${scope} declaration view displays and copies the unchanged shared text`, () => {
    const source = apiDeclarationsFixture(scope);
    const html = renderTypeSource({
      item: jsonSerializer,
      currentSignature: "declarations",
      sourceState: { status: "ready", signature: "declarations", source },
      view: scope === "All" ? "all-declarations" : "api-declarations",
      escapeHtml,
      highlightCSharp,
    });
    assert.match(html, /aria-label="API Declarations"/);
    assert.match(html, /protected JsonNamingPolicy\(\);/);
    assert.equal(html.includes("static JsonNamingPolicy();"), scope === "All");
    assert.doesNotMatch(html, /PDB Source|Decompiled source/);
    assert.equal(typeCodeViewText(source), source.inspection.content.text);
    assert.match(renderSourcePageActions({
      source: null, typeCodeView: source, typeView: "api-declarations",
      copyButtonId: "copy-type-source", escapeHtml,
    }), /id="copy-type-source" type="button">Copy/);
  });
}

test("unavailable declarations retain visible diagnostics without source substitution", () => {
  const source = apiDeclarationsFixture("ApiVisible", true);
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "declarations",
    sourceState: { status: "ready", signature: "declarations", source },
    view: "api-declarations",
    escapeHtml,
    highlightCSharp,
  });
  assert.match(html, /API Declarations unavailable/);
  assert.match(html, /The &lt;metadata&gt; bound was exceeded/);
  assert.doesNotMatch(html, /<pre|PDB Source|Decompiled source/);
  assert.equal(typeCodeViewText(source), null);
  assert.match(renderSourcePageActions({
    source: null, typeCodeView: source, typeView: "api-declarations",
    copyButtonId: "copy-type-source", escapeHtml,
  }), /id="copy-type-source" type="button" disabled/);
});

test("source page actions render copy, open, and Explore for the page-owned group", () => {
  const html = renderSourcePageActions({
    source: {
      provider: "pdb",
      provenance: inertStringFixture("SourceLink"),
      url: "https://example.test/source.cs?x=1&y=2",
      pdbSourceLimitation: null,
      text: "class JsonSerializer {}",
    },
    copyButtonId: "copy-type-source",
    escapeHtml,
  });

  assert.match(html, /id="copy-type-source"[^>]*>Copy<\/button>/);
  assert.match(
    html,
    /class="shell-action-link" href="https:\/\/example\.test\/source\.cs\?x=1&amp;y=2" target="_blank" rel="noreferrer">Open<\/a>/);
  assert.match(
    html,
    /id="explore-source"[^>]*title="Explore source options"[^>]*>Explore<\/button>/);
});

test("source page actions disable copy until source is available", () => {
  const html = renderSourcePageActions({
    source: null,
    copyButtonId: "copy-source",
    escapeHtml,
  });

  assert.match(html, /id="copy-source"[^>]* disabled>Copy<\/button>/);
  assert.doesNotMatch(html, /shell-action-link/);
  assert.match(html, /id="explore-source"[^>]*>Explore<\/button>/);
  assert.doesNotMatch(html, /id="explore-source"[^>]* disabled/);
});

test("authored member source actions expose only available parts", () => {
  const source = memberSourceFixture();
  const html = renderSourcePageActions({
    source: source.source,
    memberSource: source,
    selectedMemberPart: "Attributes",
    copyButtonId: "copy-source",
    escapeHtml,
  });

  assert.match(
    html,
    /id="member-source-part" aria-label="Select member source part"/);
  assert.match(html, />Member<\/option>/);
  assert.match(html, />XML docs<\/option>/);
  assert.match(
    html,
    /value="Attributes" selected>Attributes<\/option>/);
  assert.match(html, />Signature<\/option>/);
  assert.match(html, />Body<\/option>/);
});

test("member source selection lowers every original fragment for display and copy", () => {
  const source = memberSourceFixture();

  assert.equal(
    memberSourceText(source, "XmlDocumentation"),
    "/// first\n/// second");
  assert.equal(
    memberSourceText(source, "Attributes"),
    "[First]\n[Second]");
  assert.equal(
    memberSourceText(source, "Member"),
    source.source.text);
  const body = memberSourceText(source, "Body");
  const html = renderSourceResult({
    source: source.source,
    text: body,
    escapeHtml,
    highlightCSharp,
  });
  assert.match(html, /\{\r\n    return;\r\n\}/);
  assert.doesNotMatch(html, /public void M/);
});

test("member source restores Markout WriteHeading indentation for display and copy", () => {
  const text =
    "/// <summary>\n"
    + "    /// Writes a heading at the specified level.\n"
    + "    /// </summary>\n"
    + "    /// <returns><c>true</c> if rendered or filtered; "
    + "<c>false</c> if the formatter does not support headings.</returns>\n"
    + "    public bool WriteHeading(int level, string text) "
    + "=> WriteHeading(level, text, null);";
  const signature = "public bool WriteHeading(int level, string text)";
  const body = "=> WriteHeading(level, text, null);";
  const signatureStart = text.indexOf(signature);
  const documentationEnd = text.indexOf("\n    public bool WriteHeading");
  const bodyStart = text.indexOf(body);
  const source: BrowserMemberSource = {
    source: {
      provider: "pdb",
      provenance: inertStringFixture("SourceLink"),
      url: "https://example.test/MarkoutWriter.cs",
      pdbSourceLimitation: null,
      text,
    },
    parts: [
      {
        kind: "Member",
        spans: [{
          start: 0,
          length: text.length,
          startLine: 1,
          endLine: 5,
          leadingIndentation: "    ",
          end: text.length,
        }],
      },
      {
        kind: "XmlDocumentation",
        spans: [{
          start: 0,
          length: documentationEnd,
          startLine: 1,
          endLine: 4,
          leadingIndentation: "    ",
          end: documentationEnd,
        }],
      },
      {
        kind: "Signature",
        spans: [{
          start: signatureStart,
          length: signature.length,
          startLine: 5,
          endLine: 5,
          leadingIndentation: "    ",
          end: signatureStart + signature.length,
        }],
      },
      {
        kind: "Body",
        spans: [{
          start: bodyStart,
          length: body.length,
          startLine: 5,
          endLine: 5,
          leadingIndentation: "    ",
          end: bodyStart + body.length,
        }],
      },
    ],
  };

  const member = memberSourceText(source, "Member");
  const documentation = memberSourceText(source, "XmlDocumentation");
  assert.equal(source.source.text, text);
  assert.equal(member, `    ${text}`);
  assert.deepEqual(
    documentation.split("\n").map(line => line.match(/^ */)?.[0].length),
    [4, 4, 4, 4]);
  assert.equal(
    memberSourceText(source, "Signature"),
    `    ${signature}`);
  assert.equal(memberSourceText(source, "Body"), `    ${body}`);
});

test("member source indentation preserves multiline literal characters", () => {
  const body = "{\r\n"
    + "    const string value = \"\"\"\r\n"
    + "        first\tvalue\r\n"
    + "        second value\r\n"
    + "        \"\"\";\r\n"
    + "}";
  const source: BrowserMemberSource = {
    source: {
      provider: "pdb",
      provenance: inertStringFixture("SourceLink"),
      url: "https://example.test/source.cs",
      pdbSourceLimitation: null,
      text: body,
    },
    parts: [{
      kind: "Member",
      spans: [{
        start: 0,
        length: body.length,
        startLine: 1,
        endLine: 6,
        leadingIndentation: "\t",
        end: body.length,
      }],
    }, {
      kind: "Body",
      spans: [{
        start: 0,
        length: body.length,
        startLine: 1,
        endLine: 6,
        leadingIndentation: "\t",
        end: body.length,
      }],
    }],
  };

  assert.equal(memberSourceText(source, "Body"), `\t${body}`);
});

test("member source part selection resets across request signatures and absent parts", () => {
  const selector = createMemberSourcePartSelector();
  const authored = memberSourceFixture();

  assert.equal(selector.current("first", authored), "Member");
  assert.equal(selector.select("first", authored, "Body"), true);
  assert.equal(selector.current("first", authored), "Body");
  assert.equal(selector.current("second", authored), "Member");
  assert.equal(selector.select("second", authored, "Body"), true);
  assert.equal(
    selector.current("second", { ...authored, parts: authored.parts.slice(0, 4) }),
    "Member");
  assert.equal(selector.select("second", authored, "Body"), true);
  assert.equal(selector.select("second", authored, "Attributes"), true);
});

test("decompiled member source has no authored selector", () => {
  const memberSource: BrowserMemberSource = {
    source: {
      provider: "decompiled",
      provenance: inertStringFixture("decompiled"),
      url: null,
      pdbSourceLimitation: "No PDB",
      text: "public void M() { }",
    },
    parts: [],
  };
  const html = renderSourcePageActions({
    source: memberSource.source,
    memberSource,
    copyButtonId: "copy-source",
    escapeHtml,
  });

  assert.doesNotMatch(html, /member-source-part/);
  assert.match(html, /id="copy-source"/);
  assert.equal(
    memberSourceText(memberSource, "Member"),
    memberSource.source.text);
});

test("decompiled type source discloses an escaped PDB-source limitation", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      status: "ready",
      signature: "sig",
      source: {
        kind: "source",
        value: {
          provider: "decompiled",
          provenance: inertStringFixture("decompiled from IL"),
          url: null,
          pdbSourceLimitation: "<checksum mismatch>",
          text: "class JsonSerializer {}",
        },
        share: {
          kind: "nonProjectable",
          fullUrl: null,
          packet: null,
          path: "type-source/share",
          reason: inertStringFixture("No portable Workspace Share representation."),
        },
        diagnostics: [],
      },
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
      status: "failed",
      signature: "sig",
      error: "The decompiler query failed.",
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Type source failed/);
  assert.match(html, /The decompiler query failed\./);
});

test("type source renders a settled fallback for an empty failure", () => {
  const html = renderTypeSource({
    item: jsonSerializer,
    currentSignature: "sig",
    sourceState: {
      status: "failed",
      signature: "sig",
      error: "",
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /Type source failed/);
  assert.match(html, /No type source result was returned\./);
  assert.doesNotMatch(html, /Resolving type source/);
});

function memberSourceFixture(): BrowserMemberSource {
  const text =
    "/// first\r\n"
    + "[First]\r\n"
    + "/// second\n"
    + "[Second]\r\n"
    + "public void M()\r\n"
    + "{\r\n"
    + "    return;\r\n"
    + "}";
  const span = (
    fragment: string,
    startLine: number,
    from = 0,
  ) => {
    const start = text.indexOf(fragment, from);
    assert.notEqual(start, -1);
    return {
      start,
      length: fragment.length,
      startLine,
      endLine: startLine + fragment.split(/\r\n|\r|\n/).length - 1,
      leadingIndentation: "",
      end: start + fragment.length,
    };
  };
  const secondDocumentationStart = text.indexOf("/// second");
  return {
    source: {
      provider: "pdb",
      provenance: inertStringFixture("SourceLink"),
      url: "https://example.test/source.cs",
      pdbSourceLimitation: null,
      text,
    },
    parts: [
      {
        kind: "Member",
        spans: [{
          start: 0,
          length: text.length,
          startLine: 1,
          endLine: 8,
          leadingIndentation: "",
          end: text.length,
        }],
      },
      {
        kind: "XmlDocumentation",
        spans: [
          span("/// first", 1),
          span("/// second", 3, secondDocumentationStart),
        ],
      },
      {
        kind: "Attributes",
        spans: [
          span("[First]", 2),
          span("[Second]", 4),
        ],
      },
      {
        kind: "Signature",
        spans: [span("public void M()", 5)],
      },
      {
        kind: "Body",
        spans: [span("{\r\n    return;\r\n}", 6)],
      },
    ],
  };
}
