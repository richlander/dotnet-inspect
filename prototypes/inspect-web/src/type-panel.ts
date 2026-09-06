import { pdbSourceLimitationHtml } from "./data.ts";
import { renderContentNavigationCloseButton } from "./content-frame.ts";
import type { KeybindingRegistry } from "./keybinding-registry.ts";
import { WORKBENCH_KEYBINDING_PRIORITY } from "./workbench-keybindings.ts";

// The type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) and the type viewer (the
// type heading, metadata working surface, and source sections shown for the "type" scope) as pure,
// dependency-injected render functions. This module also binds the controls that its nav pane
// renders; `dotnet-inspect.ts` owns the type index, filters, member grouping, and navigation
// state transitions behind explicit callbacks. Shared text helpers
// (kindIcon, shortKind, typeDisplayName, highlight, highlightCSharp, factRows,
// relatedTypeChip) stay in `dotnet-inspect.ts`, since they are used well beyond the
// type panel, and are passed in rather than duplicated here.

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

export interface CompositionCounts {
  total: number;
}

export interface TypeMetadata {
  modifiers?: readonly string[];
  kind?: string;
  accessibility?: string | null;
  namespace?: string | null;
  assembly?: string | null;
  baseType?: string | null;
  enumUnderlyingType?: string | null;
  typeParameters?: readonly TypeParameterSummary[];
  interfaces?: readonly string[];
  derivedTypes?: readonly string[];
  attributes?: readonly string[];
  composition?: CompositionCounts | null;
  graphNodes?: readonly unknown[];
  inspectionFailures?: readonly string[];
}

export interface TypeSourceResult {
  provider: string;
  provenance: string;
  url?: string | null;
  pdbSourceLimitation?: string | null;
  text: string;
}

type EscapeHtml = (value: unknown) => string;

// -- Type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) -----------------------------

export interface TypePanelBindingActions {
  onClearFilters: () => void;
  onCopyAnchor: (
    anchor: "selector" | "digest" | "canonical" | undefined,
  ) => void;
  onCopyMemberSource: () => void;
  onCopySignature: () => void;
  onCopyTypeSource: () => void;
  onKindSelect: (kind: string) => void;
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
  root.querySelector("#copy-type-source")?.addEventListener(
    "click",
    actions.onCopyTypeSource);

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
  libraryControlHtml: string;
  filtersExpanded: boolean;
  filterSummary: string;
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  kindIcon: (kind: string) => string;
  shortKind: (kind: string) => string;
}

export function renderTypeNav(options: TypeNavOptions): string {
  const {
    current, visible, typeGroups, typeFilter, namespaceFilter, kindFilter,
    namespaceCount, namespaceOptionsHtml, kindFilters, accessibilityControlHtml,
    libraryControlHtml, filtersExpanded, filterSummary, escapeHtml,
    typeDisplayName, kindIcon, shortKind,
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
      <div class="type-library-context">${libraryControlHtml}</div>
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
              return `<button class="type-row ${selected ? "selected" : ""}" data-type="${escapeHtml(item.id)}" role="option" aria-selected="${selected}">
                <span class="kind-icon">${kindIcon(item.kind)}</span>
                <span class="type-name">${escapeHtml(typeDisplayName(item))}</span>
                <small>${escapeHtml(shortKind(item.kind))}</small>
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
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  kindIcon: (kind: string) => string;
  highlight: (value: string) => string;
}

export function typeHeading(options: TypeHeadingOptions): string {
  const { item, packageContext, escapeHtml, typeDisplayName, kindIcon, highlight } = options;
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
      <div><dt>Library:</dt><dd>${escapeHtml(item.assembly)}</dd></div>
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

export function typeMetadataSignature(item: TypeSummary, packageContext: TypePanelPackageContext): string {
  return `${packageContext.id}@${packageContext.version}/${packageContext.activeFramework}/${item.assembly}/${item.id}`;
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
  metadataState: TypeMetadataStateSlice;
  memberCompositionHtml: string;
  escapeHtml: EscapeHtml;
  relatedTypeChip: (name: string) => string;
  factRows: (rows: readonly (readonly [string, string])[]) => string;
}

export function renderTypeMetadata(options: RenderTypeMetadataOptions): string {
  const {
    item, packageContext, metadataState, memberCompositionHtml,
    escapeHtml, relatedTypeChip, factRows,
  } = options;
  const current = typeMetadataSignature(item, packageContext);
  const fresh = metadataState.typeMetadataKey === current;
  const meta = fresh ? metadataState.typeMetadata : null;
  const renderSurface = (content: string) => {
    const kind = [
      ...(meta?.modifiers || []),
      meta?.kind || item.kind,
    ].filter(part => part.length > 0).join(" ");
    const accessibility = meta?.accessibility || item.accessibility || "public";
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

  const shape: (readonly [string, string])[] = [
    ["Kind", [...(meta.modifiers || []), meta.kind || item.kind].join(" ")],
    ["Accessibility", meta.accessibility || item.accessibility || "public"],
    ["Namespace", meta.namespace || item.namespace || "global"],
    ["Assembly", meta.assembly || item.assembly],
  ];
  if (meta.baseType) shape.push(["Base type", meta.baseType]);
  if (meta.enumUnderlyingType) shape.push(["Enum underlying", meta.enumUnderlyingType]);
  if (meta.typeParameters?.length) {
    shape.push(["Type parameters", meta.typeParameters
      .map(parameter => `${parameter.variance ? parameter.variance + " " : ""}${parameter.name}${parameter.constraints?.length ? ` : ${parameter.constraints.join(", ")}` : ""}`)
      .join(" · ")]);
  }

  const interfaces = (meta.interfaces || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Implements</h2><span>${meta.interfaces!.length} interface${meta.interfaces!.length === 1 ? "" : "s"}</span></div>
        <div class="type-chip-list">${meta.interfaces!.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const derived = (meta.derivedTypes || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Known derived types</h2><span>${meta.derivedTypes!.length} in ${escapeHtml(meta.assembly || item.assembly)}</span></div>
        <div class="type-chip-list">${meta.derivedTypes!.map(name => relatedTypeChip(name)).join("")}</div>
      </section>`
    : "";

  const attributes = (meta.attributes || []).length
    ? `<section class="document-section">
        <div class="section-title"><h2>Custom attributes</h2><span>${meta.attributes!.length}</span></div>
        <div class="type-chip-list">${meta.attributes!.map(name => `<code class="attr-chip">[${escapeHtml(name)}]</code>`).join("")}</div>
      </section>`
    : "";

  const composition = meta.composition && memberCompositionHtml
    ? `<section class="document-section">
        <div class="section-title"><h2>Members</h2><span>click a count to browse the member list</span></div>
        ${memberCompositionHtml}
      </section>`
    : "";

  const failures = (meta.inspectionFailures || []).length
    ? `<section class="document-section metadata-warning"><strong>⚠ Relationship view may be incomplete</strong><ul>${meta.inspectionFailures!.map(entry => `<li><code>${escapeHtml(entry)}</code></li>`).join("")}</ul></section>`
    : "";

  const graph = (meta.graphNodes || []).length > 1
    ? `<div data-type-graph-surface>
        <section class="document-section call-graph-section">
          <div class="section-title"><h2>Type relationships</h2><span>base · interfaces · derived — select a highlighted node to open</span></div>
          <div id="type-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
        </section>
        ${failures}
      </div>`
    : failures;

  return renderSurface(`
    <section class="document-section metadata-shape-section">
      <div class="section-title"><h2>Type shape</h2><span>ECMA-335 metadata</span></div>
      ${factRows(shape)}
    </section>
    ${composition}
    ${interfaces}
    ${derived}
    ${attributes}
    ${graph}`);
}

export function typeSourceSignature(
  item: TypeSummary,
  packageContext: TypePanelPackageContext,
  taste: readonly string[],
  memberRequestKey: (parts: readonly string[], taste: readonly string[]) => string,
): string {
  return memberRequestKey([
    packageContext.id,
    packageContext.version,
    packageContext.activeFramework,
    item.assembly,
    item.definitionId ?? item.id,
  ], taste);
}

export interface TypeSourceStateSlice {
  typeSourceKey: string;
  typeSourceLoading: boolean;
  typeSource: TypeSourceResult | null;
  typeSourceError: string | null;
}

export interface RenderTypeSourceOptions {
  item: TypeSummary;
  currentSignature: string;
  sourceState: TypeSourceStateSlice;
  escapeHtml: EscapeHtml;
  highlightCSharp: (value: string) => string;
}

export interface RenderSourceResultOptions {
  source: TypeSourceResult;
  escapeHtml: EscapeHtml;
  highlightCSharp: (value: string) => string;
}

export function renderSourceResult(options: RenderSourceResultOptions): string {
  const { source, escapeHtml, highlightCSharp } = options;
  return `<section class="source-result" aria-label="Source">
      <pre class="language-csharp" role="region" tabindex="0" aria-label="Source code"><code class="language-csharp">${highlightCSharp(source.text)}</code></pre>
      <footer class="source-provenance"><strong>${source.provider === "pdb" ? "PDB Source" : "Decompiled source"}</strong><span>${escapeHtml(source.provenance)}</span>${pdbSourceLimitationHtml(source)}</footer>
    </section>`;
}

export interface RenderSourcePageActionsOptions {
  source: TypeSourceResult | null;
  copyButtonId: "copy-source" | "copy-type-source";
  escapeHtml: EscapeHtml;
}

export function renderSourcePageActions(
  options: RenderSourcePageActionsOptions,
): string {
  const { source, copyButtonId, escapeHtml } = options;
  return `
    <button id="${copyButtonId}" type="button"${source ? "" : " disabled"}>Copy</button>
    ${source?.url
      ? `<a class="shell-action-link" href="${escapeHtml(source.url)}" target="_blank" rel="noreferrer">Open</a>`
      : ""}`;
}

export function renderTypeSource(options: RenderTypeSourceOptions): string {
  const {
    currentSignature,
    sourceState,
    escapeHtml,
    highlightCSharp,
  } = options;
  const fresh = sourceState.typeSourceKey === currentSignature;
  if (sourceState.typeSourceLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving type source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p></section>`;
  }
  if (fresh && sourceState.typeSource) {
    return renderSourceResult({
      source: sourceState.typeSource,
      escapeHtml,
      highlightCSharp,
    });
  }
  if (fresh && sourceState.typeSourceError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Type source failed</h2><p>${escapeHtml(sourceState.typeSourceError)}</p></section>`;
  }
  return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving type source…</h2><p>Trying PDB-checksum-verified source through SourceLink, then dotnet-inspect decompilation.</p></section>`;
}
