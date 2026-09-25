import { assertNever, pdbSourceLimitationHtml } from "./data.ts";
import { renderContentNavigationCloseButton } from "./content-frame.ts";
import type { BrowserTypeMetadata } from "./facades/inspect-web-metadata.d.ts";
import { typeGraphLegendHtml } from "./graph-legends.ts";
import type { KeybindingRegistry } from "./keybinding-registry.ts";
import {
  typeSourceView,
  type SourceResultState,
  type TypeSourceView,
} from "./source-inspection.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";

export const TYPE_RELATIONSHIPS_GRAPH_SUMMARY =
  "base · interfaces · derived — select a highlighted node to open";

const EXACT_TYPE_NOT_FOUND = 1;
const EXACT_TYPE_AMBIGUOUS = 2;

// The type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) and the type viewer (the
// type heading, metadata working surface, and source sections shown for the "type" scope) as pure,
// dependency-injected render functions. This module also binds the controls that its nav pane
// renders; `dotnet-inspect.ts` owns the type index, filters, member grouping, and navigation
// state transitions behind explicit callbacks. Shared text helpers
// (kindIcon, shortKind, typeDisplayName, highlight, highlightCSharp, factRows,
// relatedTypeChip) stay in `dotnet-inspect.ts`, since they are used well beyond the
// type panel, and are passed in rather than duplicated here.

import type {
  BrowserMemberSource,
  BrowserMemberSourcePart,
  BrowserMemberSourcePartKind,
  BrowserSource,
  BrowserTypeCodeView,
} from "./facades/inspect-web-source.d.ts";

export interface TypeSummary {
  id: string;
  name: string;
  displayName?: string;
  namespace: string;
  kind: string;
  signature: string;
  members: number;
  accessibility?: string;
  assembly: string;
  definitionId?: string;
}

export interface MemberOverloadSummary {
  signature: string;
  graphOnly?: boolean;
}

export interface MemberGroup {
  key: string;
  name: string;
  kind: string;
  overloads: readonly MemberOverloadSummary[];
}

export type MemberNavEntry =
  | { kind: "member"; group: MemberGroup }
  | { kind: "overload"; group: MemberGroup; index: number };

export interface TypePanelPackageContext {
  id: string;
  version: string;
  activeFramework: string;
}

export interface TypeParameterSummary {
  name: string;
  variance?: string | null;
  constraints?: readonly string[];
}

export interface TypeMetadata {
  exactTypeInspection?: BrowserTypeMetadata["exactTypeInspection"];
  implementers?: readonly string[];
  derivedTypes?: readonly string[];
  graphNodes?: readonly unknown[];
  inspectionFailures?: readonly string[];
}

export type TypeSourceResult = BrowserSource;
export type MemberSourcePartSelection =
  Exclude<BrowserMemberSourcePartKind, number>;

export interface SourceTextRange {
  start: number;
  length: number;
}

export type SourceHighlighter = (
  value: string,
  collapsedRanges?: readonly SourceTextRange[],
) => string;

export interface MemberSourcePartSelector {
  current(
    signature: string,
    source: BrowserMemberSource | null,
  ): MemberSourcePartSelection;
  select(
    signature: string,
    source: BrowserMemberSource,
    part: MemberSourcePartSelection,
  ): boolean;
}

export function createMemberSourcePartSelector(): MemberSourcePartSelector {
  let selectedSignature = "";
  let selectedPart: MemberSourcePartSelection = "Member";
  return {
    current(signature, source) {
      if (selectedSignature !== signature) {
        selectedSignature = signature;
        selectedPart = "Member";
      }
      if (source !== null
        && !source.parts.some(
          part => part.kind === selectedPart && part.spans.length > 0)) {
        selectedPart = "Member";
      }
      return selectedPart;
    },
    select(signature, source, part) {
      if (!source.parts.some(candidate => candidate.kind === part))
        return false;
      selectedSignature = signature;
      selectedPart = part;
      return true;
    },
  };
}

type EscapeHtml = (value: unknown) => string;

// -- Type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) -----------------------------

export interface TypePanelBindingActions {
  onClearFilters: () => void;
  onCopyAnchor: (
    anchor: "selector" | "digest" | "canonical" | undefined,
  ) => void;
  onCopyMemberSource: () => void;
  onMemberSourcePartSelect: (part: MemberSourcePartSelection) => void;
  onCopySignature: () => void;
  onCopyTypeSource: () => void;
  onTypeSourceViewSelect: (view: TypeSourceView) => void;
  onExploreSource: () => void;
  onKindSelect: (kind: string) => void;
  onTypeNavBack: () => void;
  onListKeyDown: (event: KeyboardEvent) => boolean;
  onMemberAccessibilityFilterSelect: (accessibility: string | undefined) => void;
  onMemberBack: () => void;
  onMemberCompositionAccessibilitySelect: (accessibility: string) => void;
  onMemberCompositionKindSelect: (kind: string) => void;
  onMemberCompositionTraitSelect: (trait: string) => void;
  onMemberFilterChange: (value: string) => void;
  onMemberFilterClear: () => void;
  onMemberFilterDisclosureToggle: (expanded: boolean) => void;
  onMemberFilterKeyDown: (event: KeyboardEvent, value: string) => boolean;
  onMemberGroupOpen: (memberKey: string) => void;
  onMemberKindFilterSelect: (kind: string | undefined) => void;
  onMemberOverloadOpen: (index: number) => void;
  onMemberSelect: (memberKey: string | undefined) => void;
  onMemberTraitFilterSelect: (trait: string | undefined) => void;
  onNamespaceSelect: (namespace: string) => void;
  onOverloadSelect: (index: number) => void;
  onShowTypes: () => void;
  onTypeFilterChange: (value: string) => void;
  onTypeFilterDisclosureToggle: (expanded: boolean) => void;
  onTypeFilterEscape: () => void;
  onTypeSelect: (typeId: string) => void;
}

export function bindTypePanel(
  root: ParentNode,
  actions: TypePanelBindingActions,
  keybindings: KeybindingRegistry,
) {
  root.querySelectorAll<HTMLElement>("[data-type]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onTypeSelect(button.dataset.type ?? "")));
  root.querySelectorAll<HTMLElement>("[data-namespace]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onNamespaceSelect(button.dataset.namespace ?? "")));
  root.querySelectorAll<HTMLElement>("[data-kind-filter]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onKindSelect(button.dataset.kindFilter ?? "")));
  root.querySelector("[data-type-nav-back]")?.addEventListener(
    "click",
    actions.onTypeNavBack);
  root.querySelectorAll<HTMLElement>("[data-nav-member]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onMemberSelect(button.dataset.navMember)));
  root.querySelectorAll<HTMLElement>("[data-nav-overload]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onOverloadSelect(Number(button.dataset.navOverload))));
  root.querySelectorAll<HTMLElement>("[data-member-jump-kind]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberCompositionKindSelect(
          button.dataset.memberJumpKind ?? "all")));
  root.querySelectorAll<HTMLElement>("[data-member-jump-access]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberCompositionAccessibilitySelect(
          button.dataset.memberJumpAccess ?? "all")));
  root.querySelectorAll<HTMLElement>("[data-member-jump-trait]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberCompositionTraitSelect(
          button.dataset.memberJumpTrait ?? "")));
  root.querySelectorAll<HTMLElement>("[data-member]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onMemberGroupOpen(button.dataset.member ?? "")));
  root.querySelectorAll<HTMLElement>("[data-overload]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onMemberOverloadOpen(Number(button.dataset.overload))));
  root.querySelectorAll<HTMLElement>("[data-member-kind-filter]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberKindFilterSelect(
          button.dataset.memberKindFilter)));
  root.querySelectorAll<HTMLElement>("[data-member-access-filter]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberAccessibilityFilterSelect(
          button.dataset.memberAccessFilter)));
  root.querySelectorAll<HTMLElement>("[data-member-trait-filter]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => actions.onMemberTraitFilterSelect(
          button.dataset.memberTraitFilter)));
  root.querySelector("#nav-to-types")?.addEventListener(
    "click",
    actions.onShowTypes);
  root.querySelector("#clear-filter")?.addEventListener("click", () => {
    actions.onClearFilters();
    root.querySelector<HTMLElement>("#clear-filter")?.focus();
  });
  root.querySelector("#clear-member-filter")?.addEventListener(
    "click",
    actions.onMemberFilterClear);
  root.querySelector("#member-back")?.addEventListener(
    "click",
    actions.onMemberBack);
  root.querySelector("#copy-signature")?.addEventListener(
    "click",
    actions.onCopySignature);
  root.querySelectorAll<HTMLElement>("[data-copy-anchor]").forEach(button =>
    button.addEventListener("click", () => {
      const anchor = button.dataset.copyAnchor;
      actions.onCopyAnchor(
        anchor === "selector" || anchor === "digest" || anchor === "canonical"
          ? anchor
          : undefined);
    }));
  root.querySelector("#copy-source")?.addEventListener(
    "click",
    actions.onCopyMemberSource);
  const memberSourcePart =
    root.querySelector<HTMLSelectElement>("#member-source-part");
  memberSourcePart?.addEventListener("change", () => {
    const part = memberSourcePartSelection(memberSourcePart.value);
    if (part !== null) actions.onMemberSourcePartSelect(part);
  });
  root.querySelector("#copy-type-source")?.addEventListener(
    "click",
    actions.onCopyTypeSource);
  const typeSourcePicker =
    root.querySelector<HTMLSelectElement>("#type-source-view");
  typeSourcePicker?.addEventListener("change", () => {
    const view = typeSourceView(typeSourcePicker.value);
    if (view !== null) actions.onTypeSourceViewSelect(view);
  });
  root.querySelector("#explore-source")?.addEventListener(
    "click",
    actions.onExploreSource);

  const namespaceJump =
    root.querySelector<HTMLSelectElement>("#namespace-jump");
  namespaceJump?.addEventListener(
    "change",
    () => actions.onNamespaceSelect(namespaceJump.value));

  const typeList = root.querySelector<HTMLElement>("#type-list");
  if (typeList) {
    keybindings.register({
      id: "type-list.navigate",
      key: ["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "j", "k", "/"],
      allowExtraModifiers: true,
      priority: WORKBENCH_KEYBINDING_PRIORITY.element,
      when: event => event.key.toLowerCase() !== "k"
        || (!event.metaKey && !event.ctrlKey),
      run: actions.onListKeyDown,
    }, typeList);
    keybindings.register({
      id: "type-list.extent",
      key: ["Home", "End"],
      allowExtraModifiers: true,
      preventDefault: false,
      priority: WORKBENCH_KEYBINDING_PRIORITY.element,
      run: actions.onListKeyDown,
    }, typeList);
  }
  const memberFilter =
    root.querySelector<HTMLInputElement>("#member-filter");
  memberFilter?.addEventListener(
    "input",
    () => actions.onMemberFilterChange(memberFilter.value));
  const memberFilterDisclosure =
    root.querySelector<HTMLDetailsElement>("[data-member-filter-disclosure]");
  memberFilterDisclosure?.addEventListener(
    "toggle",
    () => actions.onMemberFilterDisclosureToggle(memberFilterDisclosure.open));
  if (memberFilter) {
    keybindings.register({
      id: "member-filter.navigate",
      key: ["Escape", "ArrowUp", "ArrowDown"],
      allowExtraModifiers: true,
      priority: WORKBENCH_KEYBINDING_PRIORITY.element,
      run: event => actions.onMemberFilterKeyDown(event, memberFilter.value),
    }, memberFilter);
  }
  const filter = root.querySelector<HTMLInputElement>("#type-filter");
  const typeFilterDisclosure =
    root.querySelector<HTMLDetailsElement>("[data-type-filter-disclosure]");
  typeFilterDisclosure?.addEventListener(
    "toggle",
    () => actions.onTypeFilterDisclosureToggle(typeFilterDisclosure.open));
  filter?.addEventListener(
    "input",
    () => actions.onTypeFilterChange(filter.value));
  if (filter) {
    keybindings.register({
      id: "type-filter.navigate",
      key: ["ArrowDown", "Escape"],
      allowExtraModifiers: true,
      priority: WORKBENCH_KEYBINDING_PRIORITY.element,
      run: event => {
        if (event.key === "ArrowDown") {
          typeList?.focus();
          return true;
        }
        if (filter.value === "") return false;
        actions.onTypeFilterEscape();
        return true;
      },
    }, filter);
  }
}

export interface TypeNavOptions {
  current?: TypeSummary | null;
  visible: readonly TypeSummary[];
  typeGroups: ReadonlyMap<string, readonly TypeSummary[]>;
  typeFilter: string;
  namespaceFilter: string;
  kindFilter: string;
  namespaceCount: number;
  namespaceOptionsHtml: string;
  kindFilters: readonly string[];
  accessibilityControlHtml: string;
  library: string;
  parentSubject: "package" | "platform" | "library" | null;
  filtersExpanded: boolean;
  filterSummary: string;
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  typeLibraryLabel: (item: TypeSummary) => string;
  kindIcon: (kind: string) => string;
  shortKind: (kind: string) => string;
}

export function renderTypeNav(options: TypeNavOptions): string {
  const {
    current, visible, typeGroups, typeFilter, namespaceFilter, kindFilter,
    namespaceCount, namespaceOptionsHtml, kindFilters, accessibilityControlHtml,
    library, parentSubject, filtersExpanded, filterSummary, escapeHtml,
    typeDisplayName, typeLibraryLabel, kindIcon, shortKind,
  } = options;
  return `
    <aside id="content-navigation-pane" class="type-browser" aria-label="Public types">
      <div class="browser-head">
        <div>
          <span class="pane-label">PUBLIC TYPES</span>
          <span class="result-count">${visible.length} shown</span>
        </div>
        <div class="browser-head-actions">
          <button class="tiny-button" id="clear-filter" title="Clear filters" aria-label="Clear filters">×</button>
          ${renderContentNavigationCloseButton()}
        </div>
      </div>
      ${parentSubject ? `<button class="nav-back-row" type="button" data-type-nav-back title="Back to ${parentSubject}" aria-label="${escapeHtml(library)}: Back to ${parentSubject}">
        <span class="chevron">‹</span>
        <span class="type-name">${escapeHtml(library)}</span>
        <small>library</small>
      </button>` : ""}
      <details class="filter-disclosure type-filter-disclosure" data-type-filter-disclosure${filtersExpanded ? " open" : ""}>
        <summary id="type-filter-summary"><span aria-hidden="true">›</span><strong>Filters</strong><small>${escapeHtml(filterSummary)}</small></summary>
        <label class="type-search">
          <span aria-hidden="true">/</span>
          <input id="type-filter" aria-label="Filter types" value="${escapeHtml(typeFilter)}" placeholder="Filter types" autocomplete="off" spellcheck="false" />
          <kbd>⌘F</kbd>
        </label>
        <div class="namespace-picker">
          <select id="namespace-jump" class="scope-select" aria-label="Filter by namespace">
            <option value="" ${!namespaceFilter ? "selected" : ""}>All namespaces · ${namespaceCount}</option>
            ${namespaceOptionsHtml}
          </select>
        </div>
        <div class="chip-stack">
          <div class="namespace-chips kind-chips" aria-label="Type kind filters">
            <button class="${!kindFilter ? "active" : ""}" data-kind-filter="">all kinds</button>
            ${kindFilters.map(kind => `<button class="${kindFilter === kind ? "active" : ""}" data-kind-filter="${kind}">${kind}</button>`).join("")}
          </div>
          ${accessibilityControlHtml}
        </div>
      </details>
      <div class="type-list" role="listbox" tabindex="0" id="type-list" data-nav-scope="types" data-nav-selection="${current ? `type:${escapeHtml(current.id)}` : ""}">
        ${[...typeGroups].map(([namespace, types]) => `
          <section class="type-group">
            <button class="namespace-row" data-namespace="${escapeHtml(namespace)}">
              <span class="chevron">⌄</span>
              <span>${escapeHtml(namespace)}</span>
              <small>${types.length}</small>
            </button>
            ${types.map(item => {
              const selected = item.id === current?.id;
              const definingLibrary = typeLibraryLabel(item);
              return `<button class="type-row ${selected ? "selected" : ""}" data-type="${escapeHtml(item.id)}" role="option" aria-selected="${selected}">
                <span class="kind-icon">${kindIcon(item.kind)}</span>
                <span class="type-name">${escapeHtml(typeDisplayName(item))}</span>
                <small>${definingLibrary ? `${escapeHtml(definingLibrary)} · ` : ""}${escapeHtml(shortKind(item.kind))}</small>
              </button>`;
            }).join("")}
          </section>`).join("") || '<div class="empty-list">No public types match this filter.</div>'}
      </div>
      <footer class="pane-footer"><span>↑↓ types</span><span>←→ lens</span><span>↵ open</span></footer>
    </aside>`;
}

export interface MemberNavOptions {
  type: TypeSummary;
  entries: readonly MemberNavEntry[];
  memberCount: number;
  visibleMemberCount: number;
  filterControlsHtml: string;
  selectedMemberKey: string;
  selectedOverloadIndex: number | null;
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  shortKind: (kind: string) => string;
  highlight: (value: string) => string;
}

export function renderMemberNav(options: MemberNavOptions): string {
  const {
    type, entries, memberCount, visibleMemberCount, filterControlsHtml,
    selectedMemberKey, selectedOverloadIndex,
    escapeHtml, typeDisplayName, shortKind, highlight,
  } = options;
  const navigationSelection = selectedMemberKey
    ? (selectedOverloadIndex == null
      ? `member:${selectedMemberKey}`
      : `overload:${selectedMemberKey}:${selectedOverloadIndex}`)
    : "";
  return `
    <aside id="content-navigation-pane" class="type-browser member-nav" aria-label="Members of ${escapeHtml(typeDisplayName(type))}">
      <div class="browser-head">
        <div>
          <span class="pane-label">MEMBERS</span>
          <span class="result-count">${visibleMemberCount} of ${memberCount}</span>
        </div>
        ${renderContentNavigationCloseButton()}
      </div>
      <button class="nav-back-row" id="nav-to-types" title="Back to types (Esc)">
        <span class="chevron">‹</span>
        <span class="type-name">${escapeHtml(typeDisplayName(type))}</span>
        <small>types</small>
      </button>
      ${filterControlsHtml}
      <div class="type-list member-list" role="listbox" tabindex="0" id="type-list" data-nav-scope="members:${escapeHtml(type.id)}" data-nav-selection="${escapeHtml(navigationSelection)}">
        ${entries.map(entry => {
          if (entry.kind === "member") {
            const group = entry.group;
            const isMulti = group.overloads.length > 1;
            const graphOnly =
              group.overloads.some(overload => overload.graphOnly);
            const active = group.key === selectedMemberKey;
            const selected = active && (isMulti ? selectedOverloadIndex == null : true);
            return `<button class="type-row member-row${graphOnly ? " graph-member-row" : ""} ${active ? "active-group" : ""} ${selected ? "selected" : ""}" data-nav-member="${escapeHtml(group.key)}" role="option" aria-selected="${selected}">
              <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
              <span class="type-name">${escapeHtml(group.name)}</span>
              <small>${graphOnly ? `graph target · ${escapeHtml(shortKind(group.kind))}` : (isMulti ? `${group.overloads.length}×` : escapeHtml(shortKind(group.kind)))}</small>
            </button>`;
          }
          const selected = entry.group.key === selectedMemberKey && selectedOverloadIndex === entry.index;
          const overload = entry.group.overloads[entry.index];
          if (!overload) {
            throw new Error(
              `Member group '${entry.group.key}' has no overload ${entry.index}.`);
          }
          return `<button class="type-row overload-nav-row ${selected ? "selected" : ""}" data-nav-overload="${entry.index}" role="option" aria-selected="${selected}">
            <span class="overload-branch">↳</span>
            <code>${highlight(overload.signature)}</code>
          </button>`;
        }).join("") || '<div class="empty-list">No members match these filters.</div>'}
      </div>
      <footer class="pane-footer"><span>↑↓ members</span>${selectedMemberKey ? "<span>←→ sections</span>" : ""}<span>esc types</span></footer>
    </aside>`;
}

// -- Type viewer (the type heading, metadata, and source sections) -----------------------

export interface TypeHeadingOptions {
  item: TypeSummary;
  packageContext: TypePanelPackageContext;
  libraryLabel?: string;
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  kindIcon: (kind: string) => string;
  highlight: (value: string) => string;
}

export function typeHeading(options: TypeHeadingOptions): string {
  const {
    item, packageContext, libraryLabel,
    escapeHtml, typeDisplayName, kindIcon, highlight,
  } = options;
  return `<header class="type-heading">
    <div class="type-badge">${kindIcon(item.kind)}</div>
    <div>
      <div class="type-namespace">${escapeHtml(item.namespace)}</div>
      <h1>${escapeHtml(typeDisplayName(item))}</h1>
      <code class="type-signature">${highlight(item.signature)}</code>
    </div>
    <div class="type-metrics"><span><strong>${item.members}</strong> members</span><span><strong>${escapeHtml(item.accessibility || "public")}</strong> accessibility</span></div>
    <dl class="definition-list">
      <div><dt>TFM:</dt><dd>${escapeHtml(packageContext.activeFramework)}</dd></div>
      <div><dt>Library:</dt><dd>${escapeHtml(libraryLabel ?? item.assembly)}</dd></div>
      <div><dt>Package:</dt><dd>${escapeHtml(packageContext.id)}@${escapeHtml(packageContext.version)}</dd></div>
    </dl>
  </header>`;
}

export interface RenderGraphMemberPendingOptions extends TypeHeadingOptions {
  title: string;
}

export function renderGraphMemberPending(options: RenderGraphMemberPendingOptions): string {
  return `
    ${typeHeading(options)}
    <section class="document-section graph-member-pending" aria-live="polite">
      <div class="graph-expanding"><span class="loader"></span> Opening ${options.escapeHtml(options.title)}…</div>
    </section>`;
}

export function typeMetadataSignature(
  item: TypeSummary,
  packageContext: TypePanelPackageContext,
  libraryIdentity = "",
  workspaceIdentity = "",
): string {
  let signature =
    `${packageContext.id}@${packageContext.version}/${packageContext.activeFramework}/${item.assembly}/${item.id}`;
  if (libraryIdentity) signature = `${signature}/${libraryIdentity}`;
  return workspaceIdentity ? `${signature}#${workspaceIdentity}` : signature;
}

export interface TypeMetadataStateSlice {
  typeMetadataKey: string;
  typeMetadataLoading: boolean;
  typeMetadataError: string | null;
  typeMetadata: TypeMetadata | null;
}

export interface RenderTypeMetadataOptions {
  item: TypeSummary;
  packageContext: TypePanelPackageContext;
  libraryIdentity?: string;
  workspaceIdentity?: string;
  metadataState: TypeMetadataStateSlice;
  memberCompositionHtml: string;
  escapeHtml: EscapeHtml;
  relatedTypeChip: (name: string) => string;
  factRows: (rows: readonly (readonly [string, string])[]) => string;
}

export function renderTypeMetadata(options: RenderTypeMetadataOptions): string {
  const {
    item, packageContext, libraryIdentity, workspaceIdentity, metadataState,
    memberCompositionHtml,
    escapeHtml, relatedTypeChip, factRows,
  } = options;
  const current = typeMetadataSignature(
    item,
    packageContext,
    libraryIdentity,
    workspaceIdentity);
  const fresh = metadataState.typeMetadataKey === current;
  const meta = fresh ? metadataState.typeMetadata : null;
  const exactEnvelope = meta?.exactTypeInspection;
  const exact = exactEnvelope?.content.type ?? null;
  const exactModifiers = [
    exact?.isStatic ? "static" : "",
    exact?.isAbstract && !exact?.isStatic ? "abstract" : "",
    exact?.isSealed && !exact?.isStatic ? "sealed" : "",
    exact?.isReadOnly ? "readonly" : "",
    exact?.isByRefLike ? "ref" : "",
  ].filter(part => part.length > 0);
  const exactAssembly =
    exactEnvelope?.content.supplierAssembly?.identity.name;
  const renderSurface = (content: string) => {
    const exactUnavailable =
      exactEnvelope && !exactEnvelope.content.isAvailable;
    const kind = [
      ...exactModifiers,
      exactUnavailable ? "exact Type" : exact?.kind || item.kind,
    ].filter(part => part.length > 0).join(" ");
    const accessibility =
      exactUnavailable
        ? "unavailable"
        : exact?.accessibility || item.accessibility || "public";
    const coordinate =
      `${packageContext.activeFramework} · ${item.assembly} · ${packageContext.id}@${packageContext.version}`;
    return `
      <section class="metadata-surface" aria-labelledby="metadata-surface-title">
        <header class="metadata-surface-head">
          <h1 id="metadata-surface-title">Metadata</h1>
          <p>${escapeHtml(kind)} <span>· ${escapeHtml(accessibility)}</span></p>
        </header>
        <div class="metadata-surface-scroll">
          ${content}
        </div>
        <footer class="metadata-surface-footer">
          <span title="${escapeHtml(item.id)}">${escapeHtml(item.id)}</span>
          <span title="${escapeHtml(coordinate)}">${escapeHtml(coordinate)}</span>
        </footer>
      </section>`;
  };
  if (metadataState.typeMetadataLoading && fresh) {
    return renderSurface(`<section class="document-section metadata-surface-state source-progress" data-type-graph-surface><span class="loader"></span><h2>Projecting type metadata…</h2><p>Composing type facts through the shared dotnet-inspect projection.</p></section>`);
  }
  if (fresh && metadataState.typeMetadataError) {
    return renderSurface(`<section class="document-section metadata-surface-state empty-document" data-type-graph-surface><span class="large-glyph">⌁</span><h2>Metadata projection failed</h2><p>${escapeHtml(metadataState.typeMetadataError)}</p></section>`);
  }
  if (!meta) {
    return renderSurface(`<section class="document-section metadata-surface-state empty-document" data-type-graph-surface><span class="loader"></span><h2>Loading…</h2></section>`);
  }
  if (!exactEnvelope) {
    return renderSurface(`
      <section class="document-section metadata-surface-state empty-document" data-type-graph-surface>
        <span class="large-glyph">⌁</span>
        <h2>Type metadata is unavailable</h2>
        <p>The exact metadata inspection envelope was not returned.</p>
      </section>`);
  }
  const exactResult = exactEnvelope.content;
  if (!exactResult.isAvailable || !exact) {
    const outcome = exactResult.outcome === EXACT_TYPE_NOT_FOUND
      ? "Type not found"
      : exactResult.outcome === EXACT_TYPE_AMBIGUOUS
        ? "Type selection is ambiguous"
        : "Type metadata is unavailable";
    const diagnostics = exactEnvelope.diagnostics.length
      ? exactEnvelope.diagnostics
        .map(diagnostic => `<li><code>${escapeHtml(diagnostic.code)}</code> ${escapeHtml(diagnostic.summary)}</li>`)
        .join("")
      : exactResult.failures
        .map(failure => `<li>${escapeHtml(failure.detail)}</li>`)
        .join("");
    return renderSurface(`
      <section class="document-section metadata-surface-state empty-document" data-type-graph-surface>
        <span class="large-glyph">⌁</span>
        <h2>${outcome}</h2>
        <p>Exact metadata inspection did not produce an available Type.</p>
        ${diagnostics ? `<ul>${diagnostics}</ul>` : ""}
      </section>`);
  }

  const shape: (readonly [string, string])[] = [
    ["Kind", [...exactModifiers, exact?.kind || item.kind].join(" ")],
    ["Accessibility", exact?.accessibility || item.accessibility || "public"],
    ["Namespace", exact?.namespace || item.namespace || "global"],
    ["Assembly", exactAssembly || item.assembly],
  ];
  if (exact?.baseType) shape.push(["Base type", exact.baseType]);
  if (exact?.enumUnderlyingType)
    shape.push(["Enum underlying", exact.enumUnderlyingType]);
  if (exact?.typeParameters?.length) {
    shape.push(["Type parameters", exact.typeParameters
      .map(parameter => `${parameter.variance ? parameter.variance + " " : ""}${parameter.name}${parameter.constraints?.length ? ` : ${parameter.constraints.join(", ")}` : ""}`)
      .join(" · ")]);
  }

  const interfaces = exact.interfaces.length
    ? `<section class="document-section">
        <div class="section-title"><h2>Implements</h2><span>${exact.interfaces.length} interface${exact.interfaces.length === 1 ? "" : "s"}</span></div>
        <div class="type-chip-list">${exact.interfaces.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const implementers = meta.implementers ?? [];
  const implementations = implementers.length
    ? `<section class="document-section">
        <div class="section-title"><h2>Known implementers</h2><span>${implementers.length} in ${escapeHtml(exactAssembly || item.assembly)}</span></div>
        <div class="type-chip-list">${implementers.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const derivedTypes = meta.derivedTypes ?? [];
  const derived = derivedTypes.length
    ? `<section class="document-section">
        <div class="section-title"><h2>Known derived types</h2><span>${derivedTypes.length} in ${escapeHtml(exactAssembly || item.assembly)}</span></div>
        <div class="type-chip-list">${derivedTypes.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const attributes = exact.attributes.length
    ? `<section class="document-section">
        <div class="section-title"><h2>Custom attributes</h2><span>${exact.attributes.length}</span></div>
        <div class="type-chip-list">${exact.attributes.map(name => `<code class="attr-chip">[${escapeHtml(name)}]</code>`).join("")}</div>
      </section>`
    : "";

  const composition = exact.members.length && memberCompositionHtml
    ? `<section class="document-section">
        <div class="section-title"><h2>Members</h2><span>click a count to browse the member list</span></div>
        ${memberCompositionHtml}
      </section>`
    : "";

  const exactDiagnostics = (
    !exactResult.isComplete
    || exactEnvelope.diagnostics.length > 0)
    ? `<section class="document-section metadata-warning"><strong>⚠ Exact type inspection may be incomplete</strong><ul>${
      exactEnvelope.diagnostics.length
        ? exactEnvelope.diagnostics
          .map(diagnostic => `<li><code>${escapeHtml(diagnostic.code)}</code> ${escapeHtml(diagnostic.summary)}</li>`)
          .join("")
        : "<li>Exact type inspection reported incomplete content.</li>"
    }</ul></section>`
    : "";

  const failures = (meta.inspectionFailures || []).length
    ? `<section class="document-section metadata-warning"><strong>⚠ Relationship view may be incomplete</strong><ul>${meta.inspectionFailures!.map(entry => `<li><code>${escapeHtml(entry)}</code></li>`).join("")}</ul></section>`
    : "";

  const graph = (meta.graphNodes || []).length > 1
    ? `<div data-type-graph-surface>
        <section class="document-section call-graph-section">
          <div class="section-title"><h2>Type relationships</h2><span>${TYPE_RELATIONSHIPS_GRAPH_SUMMARY}</span></div>
          <div id="type-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
          ${typeGraphLegendHtml()}
        </section>
        ${failures}
      </div>`
    : failures;

  return renderSurface(`
    <section class="document-section metadata-shape-section">
      <div class="section-title"><h2>Type shape</h2><span>ECMA-335 metadata</span></div>
      ${factRows(shape)}
    </section>
    ${exactDiagnostics}
    ${composition}
    ${interfaces}
    ${implementations}
    ${derived}
    ${attributes}
    ${graph}`);
}

export function typeSourceSignature(
  item: TypeSummary,
  packageContext: TypePanelPackageContext,
  taste: readonly string[],
  memberRequestKey: (parts: readonly string[], taste: readonly string[]) => string,
  view: TypeSourceView = "source",
): string {
  return memberRequestKey([
    packageContext.id,
    packageContext.version,
    packageContext.activeFramework,
    item.assembly,
    item.definitionId ?? item.id,
    view,
  ], view === "source" ? taste : []);
}

export type TypeSourceStateSlice = SourceResultState<BrowserTypeCodeView>;

export function typeCodeViewText(view: BrowserTypeCodeView | null): string | null {
  if (view === null) return null;
  switch (view.kind) {
    case "source":
      return view.value.text;
    case "apiDeclarations":
      return view.inspection.content.text;
    default:
      return assertNever(view, "type code view");
  }
}

export interface RenderTypeSourceOptions {
  item: TypeSummary;
  currentSignature: string;
  sourceState: TypeSourceStateSlice;
  view?: TypeSourceView;
  escapeHtml: EscapeHtml;
  highlightCSharp: SourceHighlighter;
}

export interface RenderSourceResultOptions {
  source: TypeSourceResult;
  text?: string;
  leftJustify?: boolean;
  escapeHtml: EscapeHtml;
  highlightCSharp: SourceHighlighter;
}

export function renderSourceResult(options: RenderSourceResultOptions): string {
  const {
    source,
    text = source.text,
    leftJustify = false,
    escapeHtml,
    highlightCSharp,
  } = options;
  return `<section class="source-result" aria-label="Source">
      ${renderSourceCode(text, highlightCSharp, leftJustify)}
      <footer class="source-provenance"><strong>${source.provider === "pdb" ? "PDB Source" : "Decompiled source"}</strong><span>${escapeHtml(source.provenance)}</span>${pdbSourceLimitationHtml(source)}</footer>
    </section>`;
}

function renderSourceCode(
  text: string,
  highlightCSharp: SourceHighlighter,
  leftJustify = false,
): string {
  const collapsedRanges = leftJustify
    ? sharedLeadingIndentationRanges(text)
    : undefined;
  return `<pre class="language-csharp" role="region" tabindex="0" aria-label="Source code"><code class="language-csharp">${highlightCSharp(text, collapsedRanges)}</code></pre>`;
}

export interface RenderSourcePageActionsOptions {
  source: TypeSourceResult | null;
  typeCodeView?: BrowserTypeCodeView | null;
  typeView?: TypeSourceView;
  memberSource?: BrowserMemberSource | null;
  selectedMemberPart?: MemberSourcePartSelection;
  copyButtonId: "copy-source" | "copy-type-source";
  escapeHtml: EscapeHtml;
}

export function renderSourcePageActions(
  options: RenderSourcePageActionsOptions,
): string {
  const {
    source,
    typeCodeView = null,
    typeView = "source",
    memberSource = null,
    selectedMemberPart = "Member",
    copyButtonId,
    escapeHtml,
  } = options;
  const selectableParts = memberSource === null
    ? []
    : availableMemberSourceParts(memberSource.parts);
  return `
    ${copyButtonId === "copy-type-source"
      ? `<label class="source-part-picker">
          <span>View</span>
          <select id="type-source-view" aria-label="Select type code view">
            <option value="source"${typeView === "source" ? " selected" : ""}>Source</option>
            <option value="api-declarations"${typeView === "api-declarations" ? " selected" : ""}>API Declarations</option>
            <option value="all-declarations"${typeView === "all-declarations" ? " selected" : ""}>All Declarations</option>
          </select>
        </label>`
      : ""}
    ${selectableParts.length > 1
      ? `<label class="source-part-picker">
          <span>View</span>
          <select id="member-source-part" aria-label="Select member source part">
            ${selectableParts.map(part =>
              `<option value="${part.kind}"${part.kind === selectedMemberPart ? " selected" : ""}>${memberSourcePartLabel(part.kind)}</option>`).join("")}
          </select>
        </label>`
      : ""}
    <button id="${copyButtonId}" type="button"${source || typeCodeViewText(typeCodeView) !== null ? "" : " disabled"}>Copy</button>
    ${source?.url
      ? `<a class="shell-action-link" href="${escapeHtml(source.url)}" target="_blank" rel="noreferrer">Open</a>`
      : ""}
    ${copyButtonId !== "copy-type-source" || typeView === "source"
      ? `<button id="explore-source" class="primary-action" type="button"
          title="Explore source options">Explore</button>`
      : ""}`;
}

export function memberSourceText(
  memberSource: BrowserMemberSource,
  selectedPart: MemberSourcePartSelection,
): string {
  const available = availableMemberSourceParts(memberSource.parts);
  const selected = available.find(part => part.kind === selectedPart)
    ?? available.find(part => part.kind === "Member");
  if (selected === undefined) {
    if (memberSource.parts.length === 0)
      return memberSource.source.text;
    throw new Error("Member source has no complete-member part.");
  }
  return selected.spans.map(span => {
    if (!Number.isInteger(span.start)
      || !Number.isInteger(span.length)
      || !Number.isInteger(span.end)
      || span.start < 0
      || span.length < 0
      || span.end !== span.start + span.length
      || span.end > memberSource.source.text.length) {
      throw new Error(`Member source ${selected.kind} span is invalid.`);
    }
    return span.leadingIndentation
      + memberSource.source.text.slice(span.start, span.end);
  }).join("\n");
}

export function sharedLeadingIndentationRanges(
  text: string,
): SourceTextRange[] {
  const lines: Array<{
    start: number;
    indentation: string;
  }> = [];
  let commonIndentation: string | null = null;
  let lineStart = 0;

  while (lineStart <= text.length) {
    let lineEnd = lineStart;
    while (lineEnd < text.length && !isLineTerminator(text[lineEnd]!))
      lineEnd++;

    let indentationEnd = lineStart;
    while (indentationEnd < lineEnd
      && isInlineWhitespace(text[indentationEnd]!)) {
      indentationEnd++;
    }

    if (indentationEnd < lineEnd) {
      const indentation = text.slice(lineStart, indentationEnd);
      commonIndentation = commonIndentation === null
        ? indentation
        : commonPrefix(commonIndentation, indentation);
      lines.push({ start: lineStart, indentation });
      if (commonIndentation.length === 0) return [];
    }

    if (lineEnd === text.length) break;
    lineStart = lineEnd + (
      text[lineEnd] === "\r" && text[lineEnd + 1] === "\n" ? 2 : 1);
  }

  if (!commonIndentation) return [];
  return lines.map(line => ({
    start: line.start,
    length: commonIndentation.length,
  }));
}

function commonPrefix(left: string, right: string): string {
  let length = 0;
  while (length < left.length
    && length < right.length
    && left[length] === right[length]) {
    length++;
  }
  return left.slice(0, length);
}

function isInlineWhitespace(value: string): boolean {
  return !isLineTerminator(value) && /^\s$/u.test(value);
}

function isLineTerminator(value: string): boolean {
  return value === "\r"
    || value === "\n"
    || value === "\u0085"
    || value === "\u2028"
    || value === "\u2029";
}

function availableMemberSourceParts(
  parts: readonly BrowserMemberSourcePart[],
): Array<BrowserMemberSourcePart & {
  readonly kind: MemberSourcePartSelection;
}> {
  return parts.flatMap(part => {
    const kind = memberSourcePartSelection(part.kind);
    return kind === null || part.spans.length === 0
      ? []
      : [{ ...part, kind }];
  });
}

function memberSourcePartSelection(
  value: BrowserMemberSourcePartKind,
): MemberSourcePartSelection | null;
function memberSourcePartSelection(
  value: string,
): MemberSourcePartSelection | null;
function memberSourcePartSelection(
  value: string | number,
): MemberSourcePartSelection | null {
  switch (value) {
    case "Member":
    case "XmlDocumentation":
    case "Attributes":
    case "Signature":
    case "Body":
      return value;
    default:
      return null;
  }
}

function memberSourcePartLabel(part: MemberSourcePartSelection): string {
  switch (part) {
    case "Member":
      return "Member";
    case "XmlDocumentation":
      return "XML docs";
    case "Attributes":
      return "Attributes";
    case "Signature":
      return "Signature";
    case "Body":
      return "Body";
    default:
      return assertNever(part, "member source part selection");
  }
}

export function renderTypeSource(options: RenderTypeSourceOptions): string {
  const {
    currentSignature,
    sourceState,
    view = "source",
    escapeHtml,
    highlightCSharp,
  } = options;
  const loading = view === "source"
    ? `<h2>Resolving type source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p>`
    : `<h2>Reading API declarations…</h2><p>Projecting bodyless declarations from the selected library metadata.</p>`;
  if (sourceState.status === "idle"
    || sourceState.signature !== currentSignature) {
    return `<section class="document-section source-progress"><span class="loader"></span>${loading}</section>`;
  }
  switch (sourceState.status) {
    case "loading":
      return `<section class="document-section source-progress"><span class="loader"></span>${loading}</section>`;
    case "ready":
      if (sourceState.source.kind === "apiDeclarations") {
        const { content, diagnostics } = sourceState.source.inspection;
        const diagnosticHtml = diagnostics.length > 0
          ? `<ul>${diagnostics.map(item => `<li>${escapeHtml(item.summary)}</li>`).join("")}</ul>`
          : "";
        if (content.outcome !== "Available" || content.text === null) {
          return `<section class="document-section empty-document"><h2>API Declarations unavailable</h2>${diagnosticHtml}</section>`;
        }
        return `<section class="source-result" aria-label="API Declarations">
          ${renderSourceCode(content.text, highlightCSharp)}
          <footer class="source-provenance"><strong>API Declarations</strong><span>${content.scope === "All" ? "All declarations" : "Public and protected API"} · metadata, without implementation bodies</span>${diagnosticHtml}</footer>
        </section>`;
      }
      return renderSourceResult({
        source: sourceState.source.value,
        escapeHtml,
        highlightCSharp,
      });
    case "failed":
      return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Type source failed</h2><p>${escapeHtml(sourceState.error || "No type source result was returned.")}</p></section>`;
    default:
      return assertNever(sourceState, "type source result state");
  }
}
