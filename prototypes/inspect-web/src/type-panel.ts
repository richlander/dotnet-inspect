// The type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) and the type viewer (the
// type heading, metadata, and source sections shown for the "type" scope) as pure,
// dependency-injected render functions. `dotnet-inspect.ts` owns the type index, filters, member
// grouping, and navigation/click handling; this module owns only markup shape given an
// explicit snapshot of the data those helpers already computed. Shared text helpers
// (kindIcon, shortKind, typeDisplayName, highlight, highlightCSharp, factRows,
// factEvidence, relatedTypeChip) stay in `dotnet-inspect.ts`, since they are used well beyond the
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
  text: string;
}

type EscapeHtml = (value: unknown) => string;

// -- Type selector (the "PUBLIC TYPES" / "MEMBERS" nav pane) -----------------------------

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
  escapeHtml: EscapeHtml;
  typeDisplayName: (item: TypeSummary) => string;
  kindIcon: (kind: string) => string;
  shortKind: (kind: string) => string;
}

export function renderTypeNav(options: TypeNavOptions): string {
  const {
    current, visible, typeGroups, typeFilter, namespaceFilter, kindFilter,
    namespaceCount, namespaceOptionsHtml, kindFilters, accessibilityControlHtml,
    libraryControlHtml, escapeHtml, typeDisplayName, kindIcon, shortKind,
  } = options;
  return `
    <aside class="type-browser" aria-label="Public types">
      <div class="browser-head">
        <div>
          <span class="pane-label">PUBLIC TYPES</span>
          <span class="result-count">${visible.length} shown</span>
        </div>
        <button class="tiny-button" id="clear-filter" title="Clear filter">×</button>
      </div>
      <label class="type-search">
        <span>/</span>
        <input id="type-filter" value="${escapeHtml(typeFilter)}" placeholder="Filter types, members, libraries" autocomplete="off" spellcheck="false" />
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
        ${libraryControlHtml}
      </div>
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
    <aside class="type-browser member-nav" aria-label="Members of ${escapeHtml(typeDisplayName(type))}">
      <div class="browser-head">
        <div>
          <span class="pane-label">MEMBERS</span>
          <span class="result-count">${visibleMemberCount} of ${memberCount}</span>
        </div>
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
            const active = group.key === selectedMemberKey;
            const selected = active && (isMulti ? selectedOverloadIndex == null : true);
            return `<button class="type-row member-row ${active ? "active-group" : ""} ${selected ? "selected" : ""}" data-nav-member="${escapeHtml(group.key)}" role="option" aria-selected="${selected}">
              <span class="member-icon">${escapeHtml(group.kind?.slice(0, 1)?.toUpperCase() || "M")}</span>
              <span class="type-name">${escapeHtml(group.name)}</span>
              <small>${isMulti ? `${group.overloads.length}×` : escapeHtml(shortKind(group.kind))}</small>
            </button>`;
          }
          const selected = entry.group.key === selectedMemberKey && selectedOverloadIndex === entry.index;
          return `<button class="type-row overload-nav-row ${selected ? "selected" : ""}" data-nav-overload="${entry.index}" role="option" aria-selected="${selected}">
            <span class="overload-branch">↳</span>
            <code>${highlight(entry.group.overloads[entry.index].signature)}</code>
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
  if (metadataState.typeMetadataLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Projecting type metadata…</h2><p>Composing type facts through the shared dotnet-inspect projection.</p></section>`;
  }
  if (fresh && metadataState.typeMetadataError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Metadata projection failed</h2><p>${escapeHtml(metadataState.typeMetadataError)}</p></section>`;
  }
  const meta = fresh ? metadataState.typeMetadata : null;
  if (!meta) {
    return `<section class="document-section empty-document"><span class="loader"></span><h2>Loading…</h2></section>`;
  }

  const shape: (readonly [string, string])[] = [
    ["Kind", [...(meta.modifiers || []), meta.kind].join(" ")],
    ["Accessibility", meta.accessibility || "public"],
    ["Namespace", meta.namespace || "global"],
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

  const graph = (meta.graphNodes || []).length > 1
    ? `<section class="document-section call-graph-section">
        <div class="section-title"><h2>Type relationships</h2><span>base · interfaces · derived — click a highlighted node to open</span></div>
        <div id="type-graph-diagram" class="call-graph-diagram"><span class="loader"></span><p>Rendering graph…</p></div>
      </section>`
    : "";

  const failures = (meta.inspectionFailures || []).length
    ? `<section class="document-section metadata-warning"><strong>⚠ Relationship view may be incomplete</strong><ul>${meta.inspectionFailures!.map(entry => `<li><code>${escapeHtml(entry)}</code></li>`).join("")}</ul></section>`
    : "";

  return `
    <section class="document-section">
      <div class="section-title"><h2>Type shape</h2><span>ECMA-335 metadata</span></div>
      ${factRows(shape)}
    </section>
    ${composition}
    ${interfaces}
    ${derived}
    ${attributes}
    ${graph}
    ${failures}`;
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

export function renderTypeSource(options: RenderTypeSourceOptions): string {
  const { currentSignature, sourceState, escapeHtml, highlightCSharp } = options;
  const fresh = sourceState.typeSourceKey === currentSignature;
  if (sourceState.typeSourceLoading && fresh) {
    return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving type source…</h2><p>Trying checksum-verified SourceLink source, then dotnet-inspect decompilation.</p></section>`;
  }
  if (fresh && sourceState.typeSource) {
    const typeSource = sourceState.typeSource;
    return `<section class="document-section source-result">
        <div class="source-provenance"><strong>${typeSource.provider === "original" ? "Original source" : "Decompiled source"}</strong><span>${escapeHtml(typeSource.provenance)}</span>${typeSource.url ? `<a href="${escapeHtml(typeSource.url)}" target="_blank" rel="noreferrer">open source ↗</a>` : ""}<button id="copy-type-source" type="button">copy</button></div>
        <pre class="language-csharp"><code class="language-csharp">${highlightCSharp(typeSource.text)}</code></pre>
      </section>`;
  }
  if (fresh && sourceState.typeSourceError) {
    return `<section class="document-section empty-document"><span class="large-glyph">⌁</span><h2>Type source failed</h2><p>${escapeHtml(sourceState.typeSourceError)}</p></section>`;
  }
  return `<section class="document-section source-progress"><span class="loader"></span><h2>Resolving type source…</h2><p>Trying checksum-verified SourceLink source, then dotnet-inspect decompilation.</p></section>`;
}
