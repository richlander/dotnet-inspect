// The Metadata lens (the image-level summary of one library — format stamp, heap sizes,
// ECMA-335 table row counts, PE/CLI headers) and the Metadata Explorer (the spatial
// "browse the metadata like a database" table/heap drill-down) as pure,
// dependency-injected render functions. Both views describe the same subject — the shape of
// the metadata image rather than the API surface within it — so they live in one module, the
// way `type-panel.ts` combines the type selector and the type viewer.
//
// `package-inspection.ts` coordinates the package-level metadata request, while
// `metadata-inspection.ts` coordinates type metadata and the explorer's table-window and
// heap-listing requests. `dotnet-inspect.ts` keeps `state` and the explorer's focus/history
// stack (`openExplorer`, `pushExplorerFocus`, `applyExplorerFocus`,
// `explorerHistoryBack/Forward`, `explorerShowOverview`, `closeExplorer`), the
// `IntersectionObserver` that hydrates cards lazily, the resize listener, and the global
// gesture effects registered with the shared keybinding dispatcher. This module owns the
// markup and its interaction mapping given explicit state and action callbacks.
//
// The shared text helpers used well beyond these views (`escapeHtml`, `fmtBytes`) and the
// shared lens chrome (`platformLensPicker`, `scopedPlatformLibrary`, `packageScopeSignature`,
// `platformPackForAssembly`) stay in `dotnet-inspect.ts` and are injected rather than duplicated here.

type EscapeHtml = (value: unknown) => string;
type FormatBytes = (value: number) => string;

// -- Shared injected helpers ---------------------------------------------------------------

export interface MetadataTextHelpers {
  escapeHtml: EscapeHtml;
  fmtBytes: FormatBytes;
}

// -- Metadata lens data shapes -------------------------------------------------------------

export interface MetadataHeapSummary {
  name: string;
  sizeInBytes: number;
  maxAddress: number;
  addressing: string;
}

export interface MetadataTableSummary {
  index: number;
  name: string;
  rowCount: number;
  isProjected: boolean;
}

export interface MetadataHeaders {
  machine?: string;
  isPE32Plus?: boolean;
  subsystem?: string;
  corFlags?: string | null;
  majorRuntimeVersion?: number | null;
  minorRuntimeVersion?: number | null;
  entryPointToken?: number | null;
  managedNativeHeaderRva?: number;
  managedNativeHeaderSize?: number;
}

export interface MetadataAssembly {
  assembly: string;
  kind: string;
  isAssembly: boolean;
  metadataSize: number;
  metadataVersion: string;
  metadataVersionTruncated: boolean;
  projectedTableTotal: number;
  heaps?: readonly MetadataHeapSummary[];
  tables?: readonly MetadataTableSummary[];
  headers?: MetadataHeaders;
}

export interface PackageMetadata {
  assemblies?: readonly MetadataAssembly[];
  inspectionError?: string | null;
}

// -- Metadata Explorer data shapes ---------------------------------------------------------

export interface ExplorerDirectoryEntry {
  index: number;
  name: string;
  rowCount: number;
  isProjected: boolean;
}

export interface ExplorerHeapEntry {
  name: string;
  streamName: string;
  sizeInBytes: number;
  addressing: string;
}

export interface ExplorerColumn {
  name: string;
  kind: string;
  candidateTargets?: readonly number[];
}

/**
 * The flat cell union the engine projects: `nil`, `scalar`, `flags`, `heap`, `handle`,
 * `range`, and `malformed` each populate their own subset of these fields.
 */
export interface ExplorerCell {
  kind: string;
  display?: string | null;
  raw?: number | string | null;
  decoded?: string | null;
  heap?: string | null;
  text?: string | null;
  preview?: string | null;
  offset?: number | null;
  length?: number | null;
  truncated?: boolean | null;
  targetTable?: number | null;
  targetRowId?: number | null;
  startRowId?: number | null;
  endRowId?: number | null;
  count?: number | null;
  detail?: string | null;
}

export interface ExplorerRow {
  rowId: number;
  token: number;
  cells: readonly ExplorerCell[];
}

export interface ExplorerTableData {
  index: number;
  name: string;
  rowCount: number;
  startRowId: number;
  columns?: readonly ExplorerColumn[];
  rows?: readonly ExplorerRow[];
  error?: string | null;
}

export interface ExplorerWindow {
  loading: boolean;
  error: string;
  data: ExplorerTableData | null;
  startRowId?: number;
  maxRows?: number;
}

export interface HeapListingEntry {
  offset: number;
  value: ExplorerCell | null;
  referenceCount: number;
}

export interface HeapListingData {
  heap: string;
  streamName: string;
  coverage: string;
  entries?: readonly HeapListingEntry[];
  rowsTruncated?: boolean;
  entriesTruncated?: boolean;
  error?: string | null;
}

export interface HeapWindow {
  loading: boolean;
  error: string;
  data: HeapListingData | null;
}

/** A focus entry is either `{ index, rowId }` (rowId 0 = table, no highlighted row) or `{ heap }`. */
export interface ExplorerFocus {
  index?: number;
  rowId?: number;
  heap?: string;
}

export interface ExplorerRowRef {
  index: number;
  rowId: number;
}

export interface ExplorerDetailRef {
  index?: number;
  rowId?: number;
  heap?: string;
  offset?: number;
}

export interface ExplorerState {
  open: boolean;
  assemblyFileName: string;
  directory: readonly ExplorerDirectoryEntry[];
  heaps?: readonly ExplorerHeapEntry[];
  windows: Record<number, ExplorerWindow | undefined>;
  heapWindows: Record<string, HeapWindow | undefined>;
  focusIndex: number;
  focusHeap: string | null;
  highlight: ExplorerRowRef | null;
  detail: ExplorerDetailRef | null;
  history: ExplorerFocus[];
  historyPos: number;
  overview: boolean;
  pageSize?: number;
}

export interface MetadataExplorerBindingActions {
  onClose: () => void;
  onHistoryBack: () => void;
  onHistoryForward: () => void;
  onHeapFocus: (heap: string) => void;
  onJump: (index: number, rowId: number) => void;
  onOpenHeap: (assembly: string, heap: string) => void;
  onOpenOverview: (assembly: string) => void;
  onOpenTable: (assembly: string, index: number) => void;
  onPage: (index: number, startRowId: number) => void;
  onRetryPackageMetadata: () => void;
  onRowFocus: (index: number, rowId: number) => void;
  onShowOverview: () => void;
  onTableFocus: (index: number, rowId: number) => void;
}

function parseExplorerCoordinates(value: string | undefined): [number, number] | null {
  const parts = value?.split(":");
  if (!parts || parts.length !== 2) return null;
  const indexText = parts[0];
  const rowIdText = parts[1];
  if (!indexText || !rowIdText
    || !/^\d+$/.test(indexText) || !/^\d+$/.test(rowIdText)) {
    return null;
  }
  const index = Number(indexText);
  const rowId = Number(rowIdText);
  return Number.isSafeInteger(index) && Number.isSafeInteger(rowId)
    ? [index, rowId]
    : null;
}

export function bindMetadataExplorer(
  root: ParentNode,
  explorer: Pick<ExplorerState, "overview"> | null,
  actions: MetadataExplorerBindingActions,
) {
  root.querySelector("[data-package-metadata-retry]")
    ?.addEventListener("click", actions.onRetryPackageMetadata);
  root.querySelector("#mde-exit")?.addEventListener("click", actions.onClose);
  root.querySelector("#mde-hist-back")?.addEventListener(
    "click",
    actions.onHistoryBack);
  root.querySelector("#mde-hist-fwd")?.addEventListener(
    "click",
    actions.onHistoryForward);
  root.querySelectorAll<HTMLElement>("[data-mde-explore]").forEach(button =>
    button.addEventListener("click", () => {
      const assembly = button.dataset.mdeAssembly ?? "";
      if (assembly) actions.onOpenOverview(assembly);
    }));
  root.querySelectorAll<HTMLElement>("[data-mde-open]").forEach(button =>
    button.addEventListener("click", () => {
      const assembly = button.dataset.mdeAssembly ?? "";
      const tableIndex = button.dataset.mdeOpen ?? "";
      if (!assembly || !/^\d+$/.test(tableIndex)) return;
      const index = Number(tableIndex);
      if (Number.isSafeInteger(index)) actions.onOpenTable(assembly, index);
    }));
  root.querySelectorAll<HTMLElement>("[data-mde-open-heap]").forEach(button =>
    button.addEventListener("click", () => {
      const assembly = button.dataset.mdeAssembly ?? "";
      const heap = button.dataset.mdeOpenHeap ?? "";
      if (assembly && heap) actions.onOpenHeap(assembly, heap);
    }));
  if (!explorer) return;

  root.querySelectorAll<HTMLElement>("[data-mde-chip]").forEach(chip =>
    chip.addEventListener(
      "click",
      () => actions.onTableFocus(Number(chip.dataset.mdeChip), 0)));
  root.querySelectorAll<HTMLElement>("[data-mde-jump]").forEach(button =>
    button.addEventListener("click", event => {
      event.stopPropagation();
      const coordinates = parseExplorerCoordinates(button.dataset.mdeJump);
      if (coordinates) actions.onJump(...coordinates);
    }));
  root.querySelectorAll<HTMLElement>("[data-mde-overview]").forEach(button =>
    button.addEventListener("click", event => {
      event.stopPropagation();
      actions.onShowOverview();
    }));
  root.querySelectorAll<HTMLElement>("[data-mde-page]").forEach(button =>
    button.addEventListener("click", () => {
      const coordinates = parseExplorerCoordinates(button.dataset.mdePage);
      if (coordinates) actions.onPage(...coordinates);
    }));
  root.querySelectorAll<HTMLElement>("[data-mde-heap-chip]").forEach(chip =>
    chip.addEventListener("click", () => {
      const heap = chip.dataset.mdeHeapChip;
      if (heap) actions.onHeapFocus(heap);
    }));

  if (explorer.overview) {
    root.querySelectorAll<HTMLElement>(
      ".mde-wall .mde-card[data-mde-index] .mde-card-head",
    ).forEach(head => head.addEventListener("click", () => {
      const card = head.closest<HTMLElement>(".mde-card");
      if (card) actions.onTableFocus(Number(card.dataset.mdeIndex), 0);
    }));
    root.querySelectorAll<HTMLElement>(
      ".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head",
    ).forEach(head => head.addEventListener("click", () => {
      const card = head.closest<HTMLElement>(".mde-heap-card");
      const heap = card?.dataset.mdeHeap;
      if (heap) actions.onHeapFocus(heap);
    }));
    root.querySelectorAll<HTMLElement>(
      ".mde-wall .mde-row[data-mde-row]",
    ).forEach(row => row.addEventListener("click", () => {
      const coordinates = parseExplorerCoordinates(row.dataset.mdeRow);
      if (coordinates) actions.onTableFocus(...coordinates);
    }));
  } else {
    root.querySelector("#mde-canvas")?.addEventListener(
      "click",
      actions.onShowOverview);
    root.querySelectorAll<HTMLElement>(
      ".mde-focus .mde-row[data-mde-row]",
    ).forEach(row => row.addEventListener("click", () => {
      const coordinates = parseExplorerCoordinates(row.dataset.mdeRow);
      if (coordinates) actions.onRowFocus(...coordinates);
    }));
  }
}

/** Everything the explorer's markup needs: the open explorer plus the shared text helpers. */
export interface ExplorerRenderContext extends MetadataTextHelpers {
  explorer: ExplorerState;
}

// -- Constants and pure derivation ---------------------------------------------------------

/**
 * A conservative fallback page size; the real one adapts to the focus panel's visible height
 * (see `estimateExplorerPageSize` and `dotnet-inspect.ts`'s `syncExplorerPageSize`) so a tall panel is
 * not left half-empty.
 */
export const EXPLORER_PAGE = 50;

/** Approximate grid row height (px) for the pre-render page-size estimate. */
export const EXPLORER_ROW_H = 18;

/**
 * Pre-render estimate from the viewport (chrome above the grid ~ 180px), so the very first
 * window load already roughly fills the panel; `syncExplorerPageSize` refines it from real
 * measurements once the focus panel is laid out.
 */
export function estimateExplorerPageSize(viewportHeight: number): number {
  const grid = Math.max(120, (viewportHeight || 800) - 180);
  return Math.max(30, Math.min(400, Math.floor(grid / EXPLORER_ROW_H) + 2));
}

/** ECMA-335 stream name for a HeapKind name, matching the product's spelling. */
export function heapStreamName(name: string): string {
  switch (name) {
    case "String": return "#Strings";
    case "Blob": return "#Blob";
    case "Guid": return "#GUID";
    case "UserString": return "#US";
    default: return `#${name}`;
  }
}

/** Two focus entries name the same place when they name the same heap, or the same table. */
export function sameFocus(a: ExplorerFocus | null | undefined, b: ExplorerFocus | null | undefined): boolean {
  if (!a || !b) return false;
  if (a.heap != null || b.heap != null) return a.heap === b.heap;
  return a.index === b.index;
}

/** The directory's name for a table index, falling back to `#index` for an unknown table. */
export function explorerTableName(directory: readonly ExplorerDirectoryEntry[] | undefined, index: number): string {
  const hit = directory?.find(t => t.index === index);
  return hit ? hit.name : `#${index}`;
}

/** Attribute-selector-safe heap name (heap names are simple identifiers, but be defensive). */
export function cssEscape(value: unknown): string {
  return String(value).replace(/["\\]/g, "\\$&");
}

export function coverageLabel(coverage: string): string {
  switch (coverage) {
    case "Complete": return "every entry";
    case "ReferencedOnly": return "referenced only";
    case "NotEnumerable": return "not enumerable";
    default: return coverage;
  }
}

export interface MetadataTableGroup {
  name: string;
  tables: readonly MetadataTableSummary[];
}

const metadataTableGroupDefinitions: readonly {
  name: string;
  tables: ReadonlySet<string>;
}[] = [
  {
    name: "Modules & assemblies",
    tables: new Set([
      "Module", "ModuleRef", "Assembly", "AssemblyProcessor", "AssemblyOS",
      "AssemblyRef", "AssemblyRefProcessor", "AssemblyRefOS", "File",
      "ExportedType", "ManifestResource",
    ]),
  },
  {
    name: "Types",
    tables: new Set([
      "TypeRef", "TypeDef", "InterfaceImpl", "TypeSpec", "NestedClass",
    ]),
  },
  {
    name: "Members",
    tables: new Set([
      "FieldPtr", "Field", "MethodPtr", "MethodDef", "ParamPtr", "Param",
      "MemberRef", "EventMap", "EventPtr", "Event", "PropertyMap",
      "PropertyPtr", "Property", "MethodSemantics", "MethodImpl",
    ]),
  },
  {
    name: "Signatures & generics",
    tables: new Set([
      "StandAloneSig", "GenericParam", "MethodSpec",
      "GenericParamConstraint",
    ]),
  },
  {
    name: "Attributes & layout",
    tables: new Set([
      "Constant", "CustomAttribute", "FieldMarshal", "DeclSecurity",
      "ClassLayout", "FieldLayout", "ImplMap", "FieldRva",
    ]),
  },
  {
    name: "Debug & deltas",
    tables: new Set([
      "EncLog", "EncMap", "Document", "MethodDebugInformation", "LocalScope",
      "LocalVariable", "LocalConstant", "ImportScope", "StateMachineMethod",
      "CustomDebugInformation",
    ]),
  },
];

export function groupMetadataTables(
  tables: readonly MetadataTableSummary[],
): readonly MetadataTableGroup[] {
  const remaining = new Set(tables);
  const groups: MetadataTableGroup[] = [];
  for (const definition of metadataTableGroupDefinitions) {
    const matches = tables
      .filter(table => definition.tables.has(table.name))
      .sort((a, b) => a.index - b.index);
    if (!matches.length) continue;
    matches.forEach(table => remaining.delete(table));
    groups.push({ name: definition.name, tables: matches });
  }
  const other = [...remaining].sort((a, b) => a.index - b.index);
  if (other.length) groups.push({ name: "Other", tables: other });
  return groups;
}

// -- Metadata lens ---------------------------------------------------------------------------

export interface PackageMetadataOptions extends MetadataTextHelpers {
  /** True for the runtime pack. */
  isPlatform: boolean;
  /** The selected library name, or "" when the platform lens has no selection yet. */
  scopedLibrary: string;
  packageId: string;
  packageVersion: string;
  activeFramework: string;
  /** Compact coordinate controls rendered by `dotnet-inspect.ts`. */
  controlsHtml: string;
  /** True when the loaded metadata (or in-flight load) belongs to the current scope. */
  fresh: boolean;
  loading: boolean;
  error: string;
  metadata: PackageMetadata | null;
}

/**
 * The Metadata lens: the image-level "container" view of one library — metadata format
 * version, heap sizes, ECMA-335 table row counts, and PE/CLI header facts. This is the shape
 * of the metadata itself, distinct from the API surface (the types within).
 */
export function renderPackageMetadata(options: PackageMetadataOptions): string {
  const {
    isPlatform, scopedLibrary, packageId, packageVersion, activeFramework,
    controlsHtml, fresh, loading, error, metadata, escapeHtml, fmtBytes,
  } = options;
  const data = fresh ? metadata : null;
  const context = scopedLibrary
    ? `${activeFramework} · ${scopedLibrary}`
    : activeFramework;
  const renderSurface = (content: string, status: string) => `
    <section class="package-metadata-surface" aria-labelledby="package-metadata-surface-title">
      <header class="metadata-surface-head package-metadata-surface-head">
        <h1 id="package-metadata-surface-title">Metadata image</h1>
        <p>${escapeHtml(status)}</p>
      </header>
      ${controlsHtml}
      <div class="package-metadata-scroll">
        ${content}
      </div>
      <footer class="metadata-surface-footer package-metadata-surface-footer">
        <span title="${escapeHtml(`${packageId}@${packageVersion}`)}">${escapeHtml(`${packageId}@${packageVersion}`)}</span>
        <span title="${escapeHtml(context)}">${escapeHtml(context)}</span>
      </footer>
    </section>`;
  if (isPlatform && !scopedLibrary) {
    return renderSurface(
      `<section class="document-section package-metadata-state empty-document"><span class="large-glyph">△</span><h2>Pick a library to inspect</h2><p>Choose a .NET platform library above to read its metadata image — format version, heaps, tables, and PE/CLI headers.</p></section>`,
      "library required");
  }
  const scanScope =
    `${escapeHtml(scopedLibrary)} · ${escapeHtml(activeFramework)}`;
  if (loading && fresh) {
    return renderSurface(
      `<section class="document-section package-metadata-state source-progress"><span class="loader"></span><h2>Reading metadata…</h2><p>Describing the metadata image — heaps, tables, and headers.</p></section>`,
      "reading");
  }
  if (fresh && error) {
    return renderSurface(
      `<section class="document-section package-metadata-state empty-document"><span class="large-glyph">△</span><h2>Metadata read failed</h2><p>${escapeHtml(error)}</p><button type="button" data-package-metadata-retry>retry</button></section>`,
      "read failed");
  }
  if (!data) {
    return renderSurface(
      `<section class="document-section package-metadata-state empty-document"><span class="loader"></span><h2>Loading…</h2></section>`,
      "loading");
  }

  const assemblies = data.assemblies || [];
  if (!assemblies.length) {
    return renderSurface(
      data.inspectionError
        ? `<section class="document-section package-metadata-state empty-document"><span class="large-glyph">△</span><h2>Metadata read failed</h2><p>${escapeHtml(data.inspectionError)}</p></section>`
        : `<section class="document-section package-metadata-state empty-document"><span class="large-glyph">◇</span><h2>No metadata image</h2><p>${scanScope} does not carry ECMA-335 metadata (it may be native or resource-only).</p></section>`,
      data.inspectionError ? "read failed" : "no images");
  }

  const warning = data.inspectionError
    ? `<section class="document-section metadata-warning"><strong>⚠ This library could not be read completely</strong><ul><li><code>${escapeHtml(data.inspectionError)}</code></li></ul></section>`
    : "";
  const blocks = assemblies
    .map(asm => renderAssemblyMetadataBlock(asm, { escapeHtml, fmtBytes }))
    .join("");
  return renderSurface(
    `${warning}${blocks}`,
    `${assemblies.length} assembl${assemblies.length === 1 ? "y" : "ies"}`);
}

/**
 * One assembly's block: its non-empty heaps, its populated tables sorted by row count, and
 * its PE/CLI header facts. Every heap and table is a button that hands off to the explorer.
 */
export function renderAssemblyMetadataBlock(asm: MetadataAssembly, helpers: MetadataTextHelpers): string {
  const { escapeHtml, fmtBytes } = helpers;
  const heapRows = (asm.heaps || [])
    .filter(heap => heap.sizeInBytes > 0)
    .map(heap => `
      <button type="button" class="meta-heap" data-mde-open-heap="${escapeHtml(heap.name)}" data-mde-assembly="${escapeHtml(asm.assembly)}" title="Browse ${escapeHtml(heapStreamName(heap.name))} in the metadata explorer">
        <span class="meta-heap-name">${escapeHtml(heapStreamName(heap.name))}</span>
        <span class="meta-heap-size">${fmtBytes(heap.sizeInBytes)}</span>
        <span class="meta-heap-addr">${escapeHtml(heap.addressing === "Index" ? "index" : "byte offset")} · max ${heap.maxAddress}</span>
      </button>`).join("");

  const tables = asm.tables || [];
  const tableGroups = groupMetadataTables(tables);
  const tableGroupsHtml = tableGroups.map(group => `
    <section class="meta-table-group">
      <h4>${escapeHtml(group.name)}<span>${group.tables.length}</span></h4>
      <div class="meta-table-list">${group.tables.map(table => `
        <button type="button" class="meta-table-row ${table.isProjected ? "" : "meta-table-unprojected"}" data-mde-open="${table.index}" data-mde-assembly="${escapeHtml(asm.assembly)}" title="${table.isProjected ? "Open in the metadata explorer" : "Present in the image but not modeled by the projection"}">
          <span class="meta-table-name">${escapeHtml(table.name)}</span>
          <span class="meta-table-count">${table.rowCount.toLocaleString()}</span>
          <span class="meta-table-go">→</span>
        </button>`).join("")}</div>
    </section>`).join("");

  const h = asm.headers || {};
  const corLine = h.corFlags
    ? `<span class="meta-fact"><span class="meta-fact-k">CLI</span><span class="meta-fact-v">v${h.majorRuntimeVersion}.${h.minorRuntimeVersion} · ${escapeHtml(h.corFlags)}${h.entryPointToken ? ` · entry 0x${(h.entryPointToken >>> 0).toString(16)}` : ""}</span></span>`
    : "";
  const readyToRunLine = (h.managedNativeHeaderSize || 0) > 0
    ? `<span class="meta-fact"><span class="meta-fact-k">ReadyToRun</span><span class="meta-fact-v">managed native header · ${fmtBytes(h.managedNativeHeaderSize || 0)} · RVA 0x${((h.managedNativeHeaderRva || 0) >>> 0).toString(16)}</span></span>`
    : "";

  return `
    <section class="document-section meta-assembly">
      <div class="section-title meta-assembly-title">
        <div>
          <h2>${escapeHtml(asm.assembly)}</h2>
          <span>${escapeHtml(asm.kind)}${asm.isAssembly ? " · assembly manifest" : " · module"} · metadata ${fmtBytes(asm.metadataSize)}</span>
        </div>
        <button type="button" class="meta-explore primary-action" data-mde-explore data-mde-assembly="${escapeHtml(asm.assembly)}">Explore</button>
      </div>
      <div class="meta-facts">
        <span class="meta-fact"><span class="meta-fact-k">Format</span><span class="meta-fact-v">${escapeHtml(asm.metadataVersion)}${asm.metadataVersionTruncated ? "…" : ""}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Machine</span><span class="meta-fact-v">${escapeHtml(h.machine || "—")}${h.isPE32Plus ? " · PE32+" : " · PE32"}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Subsystem</span><span class="meta-fact-v">${escapeHtml(h.subsystem || "—")}</span></span>
        <span class="meta-fact"><span class="meta-fact-k">Tables</span><span class="meta-fact-v">${asm.projectedTableTotal}/${tables.length} populated</span></span>
        ${corLine}
        ${readyToRunLine}
      </div>
      <div class="meta-heaps-section">
        <h3 class="meta-col-title">Heaps</h3>
        <div class="meta-heaps">${heapRows || '<div class="meta-empty">No non-empty heaps</div>'}</div>
      </div>
      <div class="meta-table-directory">
        <h3 class="meta-col-title">Tables <span class="meta-col-note">grouped by role</span></h3>
        <div class="meta-table-groups">${tableGroupsHtml || '<div class="meta-empty">No populated tables</div>'}</div>
      </div>
    </section>`;
}

// -- Metadata Explorer -----------------------------------------------------------------------
// A spatial "browse the metadata like a database" view. The overview lens hands off an
// assembly + a starting table; the explorer lays every populated table out as a card,
// `dotnet-inspect.ts` lazy-loads each table's row window on demand, and handle/range cells render as
// ref->def jumps that `dotnet-inspect.ts` transports you along.

/**
 * The whole explorer surface: the nav bar, the table/heap chips, the wall of cards, and (when
 * not zoomed out to the overview) the focus lightbox. `dotnet-inspect.ts` mounts this markup and binds
 * its events; every `data-mde-*` attribute here is a binding contract with `dotnet-inspect.ts`.
 */
export function renderMetadataExplorer(context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml, fmtBytes } = context;
  const chips = ex.directory.map(t => `
    <button type="button" class="mde-chip ${t.index === ex.focusIndex && !ex.focusHeap ? "active" : ""} ${t.isProjected ? "" : "mde-chip-unprojected"}" data-mde-chip="${t.index}" title="${t.rowCount.toLocaleString()} rows${t.isProjected ? "" : " · not modeled"}">
      ${escapeHtml(t.name)}<span class="mde-chip-count">${t.rowCount.toLocaleString()}</span>
    </button>`).join("");
  const heapChips = (ex.heaps || []).map(h => `
    <button type="button" class="mde-chip mde-chip-heap ${ex.focusHeap === h.name ? "active" : ""}" data-mde-heap-chip="${escapeHtml(h.name)}" title="${escapeHtml(h.streamName)} · ${fmtBytes(h.sizeInBytes)}">
      ${escapeHtml(h.streamName)}<span class="mde-chip-count">${fmtBytes(h.sizeInBytes)}</span>
    </button>`).join("");

  const cards = ex.directory.map(t => renderExplorerCard(t, context)).join("");
  const heapCards = (ex.heaps || []).length
    ? `<div class="mde-heap-divider"><span>heaps</span></div>` + (ex.heaps || []).map(h => renderHeapCard(h, context)).join("")
    : "";

  const canBack = ex.historyPos > 0;
  const canForward = ex.historyPos < ex.history.length - 1;
  const focusPanel = ex.overview ? "" : renderExplorerFocusPanel(context);
  const note = ex.overview
    ? `metadata tables · ${ex.directory.length} populated · click a table to focus · Esc to exit`
    : `metadata tables · ${ex.directory.length} populated · click a ref to jump · Esc / click away for all tables`;

  return `
    <div class="metadata-explorer">
      <header class="mde-bar">
        <div class="mde-nav" role="group" aria-label="Explorer navigation">
          <button id="mde-exit" class="mde-navbtn mde-nav-exit" title="Exit the explorer">✕ Exit</button>
          <button id="mde-hist-back" class="mde-navbtn" ${canBack ? "" : "disabled"} title="Back (Backspace)">← Back</button>
          <button id="mde-hist-fwd" class="mde-navbtn" ${canForward ? "" : "disabled"} title="Forward (Shift+Backspace)">Forward →</button>
        </div>
        <div class="mde-title">
          <span class="mde-title-asm">${escapeHtml(ex.assemblyFileName)}</span>
          <span class="mde-title-note">${note}</span>
        </div>
      </header>
      <nav class="mde-chips">${chips}${heapChips ? `<span class="mde-chip-sep"></span>${heapChips}` : ""}</nav>
      <div class="mde-body">
        <div class="mde-canvas mde-wall ${ex.overview ? "mde-wall-open" : ""}" id="mde-canvas">${cards}${heapCards}</div>
        ${focusPanel}
      </div>
    </div>`;
}

/**
 * The focus lightbox: the current table (or heap) blown up front-and-center over the dim wall,
 * with the row inspector docked on its right. Corner ✕ buttons (top-right + bottom-right) zoom
 * back out to the all-tables wall. Auto-focus (every ref->def jump lands here) makes this the
 * primary reading surface — the wall behind is spatial context you can click into.
 */
export function renderExplorerFocusPanel(context: ExplorerRenderContext): string {
  const ex = context.explorer;
  const card = ex.focusHeap
    ? renderHeapCard((ex.heaps || []).find(h => h.name === ex.focusHeap) || emptyHeapEntry(), context)
    : renderExplorerCard(ex.directory.find(t => t.index === ex.focusIndex) || emptyDirectoryEntry(), context);
  const detail = renderExplorerDetail(context);
  return `
    <div class="mde-focus">
      <div class="mde-focus-inner">
        <div class="mde-focus-card">${card}</div>
        ${detail}
      </div>
      <button type="button" class="mde-focus-x mde-focus-x-top" data-mde-overview="1" title="Back to all tables (Esc)">✕</button>
      <button type="button" class="mde-focus-x mde-focus-x-bottom" data-mde-overview="1" title="Back to all tables (Esc)">✕</button>
    </div>`;
}

function emptyHeapEntry(): ExplorerHeapEntry {
  return { name: "", streamName: "", sizeInBytes: 0, addressing: "" };
}

function emptyDirectoryEntry(): ExplorerDirectoryEntry {
  return { index: NaN, name: "", rowCount: NaN, isProjected: false };
}

/**
 * A heap card: header (stream name, size, coverage badge), a coverage caveat banner, and the
 * listed entries (address · refs · value). The value reuses the same cell renderer as the grid,
 * so a listed #Strings entry and a Name cell pointing at it render identically.
 */
export function renderHeapCard(h: ExplorerHeapEntry, context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml, fmtBytes } = context;
  const win = ex.heapWindows[h.name];
  const focused = ex.focusHeap === h.name;
  let body: string;
  if (win?.loading && !win.data) {
    body = `<div class="mde-card-empty"><span class="loader"></span> Reading ${escapeHtml(h.streamName)}…</div>`;
  } else if (win?.error) {
    body = `<div class="mde-card-empty mde-card-error">△ ${escapeHtml(win.error)}</div>`;
  } else if (win?.data) {
    body = renderHeapListing(win.data, context);
  } else {
    body = `<div class="mde-card-empty mde-card-lazy" data-mde-heap-needs-load="${escapeHtml(h.name)}"><span class="loader"></span> Loading ${escapeHtml(h.streamName)}…</div>`;
  }
  const coverage = win?.data?.coverage;
  const badge = coverage
    ? `<span class="mde-cov-badge mde-cov-${coverage.toLowerCase()}">${escapeHtml(coverageLabel(coverage))}</span>`
    : "";
  return `
    <section class="mde-heap-card ${focused ? "mde-card-focus" : ""}" data-mde-heap="${escapeHtml(h.name)}">
      <div class="mde-card-head">
        <h3>${escapeHtml(h.streamName)}</h3>
        <span class="mde-card-meta">heap · ${fmtBytes(h.sizeInBytes)}${badge ? " · " : ""}</span>${badge}
      </div>
      ${body}
    </section>`;
}

/**
 * The listing body: a coverage caveat line, then the entry rows. Coverage is stated as part of
 * the answer so a referenced-only or truncated list is never read as the whole heap.
 */
export function renderHeapListing(data: HeapListingData, context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml } = context;
  const note = heapCoverageNote(data, escapeHtml);
  if (data.coverage === "NotEnumerable" || !(data.entries || []).length) {
    return `<div class="mde-heap-note">${note}</div>`;
  }
  const isIndex = data.heap === "Guid";
  const sel = ex.detail;
  const rows = (data.entries || []).map(entry => {
    const addr = isIndex ? `#${entry.offset}` : `0x${(entry.offset >>> 0).toString(16)}`;
    const isSel = sel && sel.heap === data.heap && sel.offset === entry.offset;
    return `<tr class="mde-heap-row ${isSel ? "mde-heap-row-sel" : ""}" data-mde-heap-row="${escapeHtml(data.heap)}:${entry.offset}">
      <td class="mde-heap-addr" title="${isIndex ? "GUID index" : "heap byte offset"}">${addr}</td>
      <td class="mde-heap-val">${renderHeapValueCell(entry.value, context)}</td>
      <td class="mde-heap-refs" title="referenced by ${entry.referenceCount} projected cell${entry.referenceCount === 1 ? "" : "s"}">${entry.referenceCount.toLocaleString()}×</td>
    </tr>`;
  }).join("");
  return `
    <div class="mde-heap-note">${note}</div>
    <div class="mde-grid-scroll"><table class="mde-grid mde-heap-grid">
      <thead><tr><th class="mde-heap-addr">addr</th><th>value</th><th class="mde-heap-refs" title="reference count">refs</th></tr></thead>
      <tbody>${rows}</tbody>
    </table></div>`;
}

/** States the listing's coverage and any truncation, so a partial answer never reads as whole. */
export function heapCoverageNote(data: HeapListingData, escapeHtml: EscapeHtml): string {
  const parts: string[] = [];
  switch (data.coverage) {
    case "Complete":
      parts.push(`Every entry in this heap is listed — the GUID heap is fixed-size records at consecutive indices, so it enumerates exactly.`);
      break;
    case "ReferencedOnly":
      parts.push(`Only entries a projected table row points at are listed — the heap may hold values nothing references, still readable by address.`);
      break;
    case "NotEnumerable":
      parts.push(`No entry can be listed: no ECMA-335 table column points into ${escapeHtml(data.streamName)} — its references are <code>ldstr</code> operands inside method bodies. An empty list here is a blind spot, not an empty heap.`);
      break;
    default:
      break;
  }
  if (data.rowsTruncated) parts.push(`Reference scan did not cover every row of every table, so some references are uncounted.`);
  if (data.entriesTruncated) parts.push(`The entry budget cut the listing short.`);
  return parts.join(" ");
}

/**
 * A heap entry's value renders exactly like the same heap cell in a grid, minus the jump (a heap
 * value has no ref->def target). Falls back through the flat cell union defensively.
 */
export function renderHeapValueCell(cell: ExplorerCell | null | undefined, context: ExplorerRenderContext): string {
  const { escapeHtml } = context;
  if (!cell) return `<span class="mde-nil">·</span>`;
  if (cell.kind === "heap") {
    const val = cell.text != null ? cell.text : cell.preview;
    const cls = `mde-cell-heap mde-heap-${(cell.heap || "").toLowerCase()}`;
    return `<span class="${cls}" title="${cell.length} byte${cell.length === 1 ? "" : "s"}${cell.truncated ? " · truncated" : ""}">${escapeHtml(val ?? "")}${cell.truncated ? "…" : ""}</span>`;
  }
  return renderExplorerCell(cell, null, context);
}

/** One table card on the wall: header, body (loading / error / grid / lazy stub), and pager. */
export function renderExplorerCard(t: ExplorerDirectoryEntry, context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml } = context;
  const win = ex.windows[t.index];
  const focused = t.index === ex.focusIndex;
  let body: string;
  if (!t.isProjected) {
    body = `<div class="mde-card-empty">This table has ${t.rowCount.toLocaleString()} rows but is not modeled by the projection yet.</div>`;
  } else if (win?.loading && !win.data) {
    body = `<div class="mde-card-empty"><span class="loader"></span> Reading rows…</div>`;
  } else if (win?.error) {
    body = `<div class="mde-card-empty mde-card-error">△ ${escapeHtml(win.error)}</div>`;
  } else if (win?.data) {
    body = renderExplorerGrid(win.data, context);
  } else {
    body = `<div class="mde-card-empty mde-card-lazy" data-mde-needs-load="${t.index}"><span class="loader"></span> Loading ${t.name}…</div>`;
  }

  const win2 = win?.data;
  const pager = win2 && win2.rows?.length
    ? (() => {
        const rows = win2.rows || [];
        const from = win2.startRowId;
        const to = win2.startRowId + rows.length - 1;
        const hasPrev = from > 1;
        const hasNext = to < win2.rowCount;
        const pageSize = Math.max(1, win?.maxRows ?? rows.length);
        return `<div class="mde-pager">
          <span>rows ${from.toLocaleString()}–${to.toLocaleString()} of ${win2.rowCount.toLocaleString()}</span>
          <span class="mde-pager-btns">
            <button type="button" data-mde-page="${t.index}:${Math.max(1, from - pageSize)}" ${hasPrev ? "" : "disabled"}>‹ prev</button>
            <button type="button" data-mde-page="${t.index}:${to + 1}" ${hasNext ? "" : "disabled"}>next ›</button>
          </span>
        </div>`;
      })()
    : "";

  return `
    <section class="mde-card ${focused ? "mde-card-focus" : ""} ${t.isProjected ? "" : "mde-card-dim"}" data-mde-index="${t.index}">
      <div class="mde-card-head">
        <h3>${escapeHtml(t.name)}</h3>
        <span class="mde-card-meta">table ${t.index} · ${t.rowCount.toLocaleString()} row${t.rowCount === 1 ? "" : "s"}</span>
      </div>
      ${body}
      ${pager}
    </section>`;
}

/** One table's loaded row window as a grid, with the highlighted and selected rows marked. */
export function renderExplorerGrid(data: ExplorerTableData, context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml } = context;
  const cols = data.columns || [];
  const header = `<tr><th class="mde-gutter">#</th>${cols.map(c => `<th title="${escapeHtml(c.kind)}${c.candidateTargets?.length ? " → " + c.candidateTargets.map(index => explorerTableName(ex.directory, index)).join(", ") : ""}">${escapeHtml(c.name)}</th>`).join("")}</tr>`;
  const rows = (data.rows || []).map(row => {
    const hot = ex.highlight && ex.highlight.index === data.index && ex.highlight.rowId === row.rowId;
    const sel = ex.detail && ex.detail.index === data.index && ex.detail.rowId === row.rowId;
    const cells = row.cells.map((cell, i) => `<td>${renderExplorerCell(cell, cols[i] ?? null, context)}</td>`).join("");
    return `<tr class="mde-row ${hot ? "mde-row-hot" : ""} ${sel ? "mde-row-sel" : ""}" data-mde-row="${data.index}:${row.rowId}"><td class="mde-gutter" title="token 0x${(row.token >>> 0).toString(16)}">${row.rowId}</td>${cells}</tr>`;
  }).join("");
  return `<div class="mde-grid-scroll"><table class="mde-grid"><thead>${header}</thead><tbody>${rows}</tbody></table></div>`;
}

/** One projected cell. Handle and range cells render as ref->def jump buttons. */
export function renderExplorerCell(
  cell: ExplorerCell | null | undefined,
  _column: ExplorerColumn | null,
  context: ExplorerRenderContext,
): string {
  const { explorer: ex, escapeHtml } = context;
  const tableName = (index: number | null | undefined) =>
    explorerTableName(ex.directory, Number(index ?? undefined));
  if (!cell) return "";
  switch (cell.kind) {
    case "nil":
      return `<span class="mde-nil">·</span>`;
    case "scalar":
      return `<span class="mde-cell-scalar">${escapeHtml(cell.display ?? String(cell.raw ?? ""))}</span>`;
    case "flags":
      return `<span class="mde-cell-flags" title="0x${((Number(cell.raw) || 0) >>> 0).toString(16)}">${escapeHtml(cell.decoded || String(cell.raw ?? 0))}</span>`;
    case "heap": {
      const val = cell.text != null ? cell.text : cell.preview;
      const cls = `mde-cell-heap mde-heap-${(cell.heap || "").toLowerCase()}`;
      return `<span class="${cls}" title="#${escapeHtml(cell.heap || "")} @${cell.offset} · ${cell.length} byte${cell.length === 1 ? "" : "s"}">${escapeHtml(val ?? "")}${cell.truncated ? "…" : ""}</span>`;
    }
    case "handle": {
      if (!cell.targetRowId) return `<span class="mde-nil">nil</span>`;
      const label = cell.display || `${tableName(cell.targetTable)} #${cell.targetRowId}`;
      return `<button type="button" class="mde-ref" data-mde-jump="${cell.targetTable}:${cell.targetRowId}" title="→ ${escapeHtml(tableName(cell.targetTable))} #${cell.targetRowId}">${escapeHtml(label)}${cell.truncated ? "…" : ""} <span class="mde-ref-arrow">↗</span></button>`;
    }
    case "range": {
      if (!cell.count) return `<span class="mde-nil">empty</span>`;
      const lastRowId = Number(cell.endRowId) - 1;
      return `<button type="button" class="mde-ref mde-ref-range" data-mde-jump="${cell.targetTable}:${cell.startRowId}" title="→ ${escapeHtml(tableName(cell.targetTable))} rows ${cell.startRowId}‥${lastRowId}">${escapeHtml(tableName(cell.targetTable))} #${cell.startRowId}‥${lastRowId} <span class="mde-ref-count">${cell.count}</span></button>`;
    }
    case "malformed":
      return `<span class="mde-cell-malformed" title="${escapeHtml(cell.detail || "")}">malformed</span>`;
    default:
      return "";
  }
}

/**
 * The row inspector: the selected row's cells laid out vertically, labeled by column, with
 * handle/range cells still jumpable. A focused "read this one row" companion to the grid.
 */
export function renderExplorerDetail(context: ExplorerRenderContext): string {
  const { explorer: ex, escapeHtml } = context;
  if (!ex.detail || ex.detail.index == null) return "";
  const win = ex.windows[ex.detail.index];
  const row = win?.data?.rows?.find(r => r.rowId === ex.detail?.rowId);
  if (!row || !win?.data) return "";
  const cols = win.data.columns || [];
  const fields = row.cells.map((cell, i) => `
    <div class="mde-detail-field">
      <span class="mde-detail-k">${escapeHtml(cols[i]?.name || `col ${i}`)}</span>
      <span class="mde-detail-v">${renderExplorerCell(cell, cols[i] ?? null, context)}</span>
    </div>`).join("");
  return `
    <aside class="mde-detail">
      <div class="mde-detail-head">
        <span class="mde-detail-title">${escapeHtml(win.data.name)} #${row.rowId}</span>
      </div>
      <div class="mde-detail-token">token 0x${(row.token >>> 0).toString(16)}</div>
      <div class="mde-detail-fields">${fields}</div>
    </aside>`;
}
