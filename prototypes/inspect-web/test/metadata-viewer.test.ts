import assert from "node:assert/strict";
import test from "node:test";
import {
  bindMetadataExplorer,
  coverageLabel,
  cssEscape,
  estimateExplorerPageSize,
  explorerTableName,
  EXPLORER_PAGE,
  groupMetadataTables,
  heapCoverageNote,
  heapStreamName,
  renderAssemblyMetadataBlock,
  renderExplorerCell,
  renderExplorerDetail,
  renderExplorerGrid,
  renderHeapCard,
  renderHeapListing,
  renderMetadataExplorer,
  renderPackageMetadata,
  sameFocus,
  type MetadataExplorerBindingActions,
} from "../src/metadata-viewer.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  private readonly closestElements = new Map<string, FakeElement>();
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

  withClosest(selector: string, element: FakeElement) {
    this.closestElements.set(selector, element);
    return this;
  }

  closest(selector: string) {
    return this.closestElements.get(selector) ?? null;
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();
  readonly queriedSelectors = new Set<string>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    this.queriedSelectors.add(selector);
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    this.queriedSelectors.add(selector);
    return this.multiple.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): MetadataExplorerBindingActions {
  return {
    onClose: () => calls.push("close"),
    onHistoryBack: () => calls.push("back"),
    onHistoryForward: () => calls.push("forward"),
    onHeapFocus: heap => calls.push(`heap:${heap}`),
    onJump: (index, rowId) => calls.push(`jump:${index}:${rowId}`),
    onOpenHeap: (assemblyFileName, heap) =>
      calls.push(`open-heap:${assemblyFileName}:${heap}`),
    onOpenOverview: assemblyFileName =>
      calls.push(`open-overview:${assemblyFileName}`),
    onOpenTable: (assemblyFileName, index) =>
      calls.push(`open-table:${assemblyFileName}:${index}`),
    onPage: (index, startRowId) =>
      calls.push(`page:${index}:${startRowId}`),
    onRetryPackageMetadata: () => calls.push("retry-metadata"),
    onRowFocus: (index, rowId) => calls.push(`row:${index}:${rowId}`),
    onShowOverview: () => calls.push("overview"),
    onTableFocus: (index, rowId) =>
      calls.push(`table:${index}:${rowId}`),
  };
}

function stoppableEvent() {
  const state = { stopped: false };
  const event = fakeDom.event({
    stopPropagation: () => {
      state.stopped = true;
    },
  });
  return { event, state };
}

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function fmtBytes(value: number) {
  return `${value} B`;
}

const helpers = { escapeHtml, fmtBytes };

test("overview bindings dispatch explorer navigation from the wall controls", () => {
  const root = new FakeRoot();
  const exit = root.add("#mde-exit", new FakeElement());
  const back = root.add("#mde-hist-back", new FakeElement());
  const forward = root.add("#mde-hist-fwd", new FakeElement());
  const chip = new FakeElement({ mdeChip: "3" });
  root.addAll("[data-mde-chip]", chip);
  const heapChip = new FakeElement({ mdeHeapChip: "String" });
  const emptyHeapChip = new FakeElement({ mdeHeapChip: "" });
  root.addAll("[data-mde-heap-chip]", heapChip, emptyHeapChip);

  const tableCard = new FakeElement({ mdeIndex: "6" });
  const tableHead = new FakeElement().withClosest(".mde-card", tableCard);
  const orphanTableHead = new FakeElement();
  root.addAll(
    ".mde-wall .mde-card[data-mde-index] .mde-card-head",
    tableHead,
    orphanTableHead);
  const heapCard = new FakeElement({ mdeHeap: "Blob" });
  const heapHead = new FakeElement().withClosest(".mde-heap-card", heapCard);
  const emptyHeapHead = new FakeElement().withClosest(
    ".mde-heap-card",
    new FakeElement({ mdeHeap: "" }));
  root.addAll(
    ".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head",
    heapHead,
    emptyHeapHead);
  const row = new FakeElement({ mdeRow: "7:8" });
  root.addAll(".mde-wall .mde-row[data-mde-row]", row);

  const calls: string[] = [];
  bindMetadataExplorer(
    fakeDom.parentNode(root),
    { overview: true },
    recordingActions(calls));

  exit.dispatch("click");
  back.dispatch("click");
  forward.dispatch("click");
  chip.dispatch("click");
  heapChip.dispatch("click");
  emptyHeapChip.dispatch("click");
  tableHead.dispatch("click");
  orphanTableHead.dispatch("click");
  heapHead.dispatch("click");
  emptyHeapHead.dispatch("click");
  row.dispatch("click");

  assert.deepEqual(calls, [
    "close",
    "back",
    "forward",
    "table:3:0",
    "heap:String",
    "table:6:0",
    "heap:Blob",
    "table:7:8",
  ]);
  assert.equal(root.queriedSelectors.has("#mde-canvas"), false);
  assert.equal(
    root.queriedSelectors.has(".mde-focus .mde-row[data-mde-row]"),
    false);
});

test("metadata failure retry binds without an open explorer", () => {
  const root = new FakeRoot();
  const retry = root.add("[data-package-metadata-retry]", new FakeElement());
  const calls: string[] = [];

  bindMetadataExplorer(
    fakeDom.parentNode(root),
    null,
    recordingActions(calls));
  retry.dispatch("click");

  assert.deepEqual(calls, ["retry-metadata"]);
});

test("focus bindings dispatch lightbox controls and keep inner clicks contained", () => {
  const root = new FakeRoot();
  const canvas = root.add("#mde-canvas", new FakeElement());
  const jump = new FakeElement({ mdeJump: "2:4" });
  root.addAll("[data-mde-jump]", jump);
  const overview = new FakeElement();
  root.addAll("[data-mde-overview]", overview);
  const page = new FakeElement({ mdePage: "5:20" });
  root.addAll("[data-mde-page]", page);
  const row = new FakeElement({ mdeRow: "4:9" });
  root.addAll(".mde-focus .mde-row[data-mde-row]", row);
  const calls: string[] = [];
  bindMetadataExplorer(
    fakeDom.parentNode(root),
    { overview: false },
    recordingActions(calls));
  const jumpClick = stoppableEvent();
  const overviewClick = stoppableEvent();
  const pageClick = stoppableEvent();

  canvas.dispatch("click");
  jump.dispatch("click", jumpClick.event);
  overview.dispatch("click", overviewClick.event);
  page.dispatch("click", pageClick.event);
  row.dispatch("click");

  assert.deepEqual(calls, [
    "overview",
    "jump:2:4",
    "overview",
    "page:5:20",
    "row:4:9",
  ]);
  assert.equal(jumpClick.state.stopped, true);
  assert.equal(overviewClick.state.stopped, true);
  assert.equal(pageClick.state.stopped, false);
  for (const selector of [
    ".mde-wall .mde-card[data-mde-index] .mde-card-head",
    ".mde-wall .mde-heap-card[data-mde-heap] .mde-card-head",
    ".mde-wall .mde-row[data-mde-row]",
  ]) {
    assert.equal(root.queriedSelectors.has(selector), false, selector);
  }
});

test("explorer bindings ignore malformed encoded coordinates", () => {
  const focusRoot = new FakeRoot();
  const invalidJumps = [
    new FakeElement({ mdeJump: "" }),
    new FakeElement({ mdeJump: "2" }),
    new FakeElement({ mdeJump: "two:4" }),
    new FakeElement({ mdeJump: "2:9007199254740992" }),
  ];
  const invalidPages = [
    new FakeElement({ mdePage: "5:" }),
    new FakeElement({ mdePage: "5:twenty" }),
  ];
  const invalidFocusRows = [
    new FakeElement({ mdeRow: ":9" }),
    new FakeElement({ mdeRow: "4:9:10" }),
  ];
  focusRoot.addAll("[data-mde-jump]", ...invalidJumps);
  focusRoot.addAll("[data-mde-page]", ...invalidPages);
  focusRoot.addAll(".mde-focus .mde-row[data-mde-row]", ...invalidFocusRows);

  const overviewRoot = new FakeRoot();
  const invalidOverviewRows = [
    new FakeElement({ mdeRow: "7" }),
    new FakeElement({ mdeRow: "-1:8" }),
  ];
  overviewRoot.addAll(
    ".mde-wall .mde-row[data-mde-row]",
    ...invalidOverviewRows);

  const calls: string[] = [];
  bindMetadataExplorer(
    fakeDom.parentNode(focusRoot),
    { overview: false },
    recordingActions(calls));
  bindMetadataExplorer(
    fakeDom.parentNode(overviewRoot),
    { overview: true },
    recordingActions(calls));

  for (const element of [
    ...invalidJumps,
    ...invalidPages,
    ...invalidFocusRows,
    ...invalidOverviewRows,
  ]) {
    element.dispatch("click", stoppableEvent().event);
  }

  assert.deepEqual(calls, []);
});

test("metadata lens bindings open table and heap explorer views", () => {
  const root = new FakeRoot();
  const explore = new FakeElement({ mdeAssembly: "Contoso.dll" });
  const invalidExplore = new FakeElement({ mdeAssembly: "" });
  root.addAll("[data-mde-explore]", explore, invalidExplore);
  const table = new FakeElement({
    mdeAssembly: "Contoso.dll",
    mdeOpen: "6",
  });
  const tableZero = new FakeElement({
    mdeAssembly: "Contoso.dll",
    mdeOpen: "0",
  });
  const pipeAssemblyTable = new FakeElement({
    mdeAssembly: "A|6|B.dll",
    mdeOpen: "2",
  });
  const invalidTables = [
    new FakeElement({ mdeAssembly: "", mdeOpen: "6" }),
    new FakeElement({ mdeAssembly: "Contoso.dll", mdeOpen: "" }),
    new FakeElement({ mdeAssembly: "Contoso.dll", mdeOpen: "NaN" }),
    new FakeElement({
      mdeAssembly: "Contoso.dll",
      mdeOpen: "9007199254740992",
    }),
  ];
  root.addAll(
    "[data-mde-open]",
    table,
    tableZero,
    pipeAssemblyTable,
    ...invalidTables);
  const heap = new FakeElement({
    mdeAssembly: "Contoso.dll",
    mdeOpenHeap: "String",
  });
  const pipeAssemblyHeap = new FakeElement({
    mdeAssembly: "A|6|B.dll",
    mdeOpenHeap: "Blob",
  });
  const invalidHeaps = [
    new FakeElement({ mdeAssembly: "", mdeOpenHeap: "String" }),
    new FakeElement({ mdeAssembly: "Contoso.dll", mdeOpenHeap: "" }),
  ];
  root.addAll(
    "[data-mde-open-heap]",
    heap,
    pipeAssemblyHeap,
    ...invalidHeaps);
  const chip = new FakeElement({ mdeChip: "3" });
  root.addAll("[data-mde-chip]", chip);
  const calls: string[] = [];
  bindMetadataExplorer(
    fakeDom.parentNode(root),
    null,
    recordingActions(calls));

  explore.dispatch("click");
  invalidExplore.dispatch("click");
  table.dispatch("click");
  tableZero.dispatch("click");
  pipeAssemblyTable.dispatch("click");
  for (const invalidTable of invalidTables) invalidTable.dispatch("click");
  heap.dispatch("click");
  pipeAssemblyHeap.dispatch("click");
  for (const invalidHeap of invalidHeaps) invalidHeap.dispatch("click");
  chip.dispatch("click");

  assert.deepEqual(calls, [
    "open-overview:Contoso.dll",
    "open-table:Contoso.dll:6",
    "open-table:Contoso.dll:0",
    "open-table:A|6|B.dll:2",
    "open-heap:Contoso.dll:String",
    "open-heap:A|6|B.dll:Blob",
  ]);
  assert.equal(root.queriedSelectors.has("[data-mde-chip]"), false);
});

function assembly(overrides = {}) {
  return {
    assembly: "Contoso.dll",
    kind: "Managed",
    isAssembly: true,
    metadataSize: 4096,
    metadataVersion: "v4.0.30319",
    metadataVersionTruncated: false,
    projectedTableTotal: 2,
    heaps: [
      { name: "String", sizeInBytes: 1024, maxAddress: 900, addressing: "Offset" },
      { name: "Guid", sizeInBytes: 16, maxAddress: 1, addressing: "Index" },
      { name: "Blob", sizeInBytes: 0, maxAddress: 0, addressing: "Offset" },
    ],
    tables: [
      { index: 2, name: "TypeDef", rowCount: 12, isProjected: true },
      { index: 6, name: "MethodDef", rowCount: 400, isProjected: true },
      { index: 42, name: "Unmodeled", rowCount: 3, isProjected: false },
    ],
    headers: {
      machine: "Amd64",
      isPE32Plus: true,
      subsystem: "WindowsCui",
      corFlags: "ILOnly",
      majorRuntimeVersion: 2,
      minorRuntimeVersion: 5,
      entryPointToken: 0x06000001,
      managedNativeHeaderRva: 0,
      managedNativeHeaderSize: 0,
    },
    ...overrides,
  };
}

function lensOptions(overrides = {}) {
  return {
    isPlatform: false,
    scopedLibrary: "",
    packageId: "Contoso",
    packageVersion: "1.2.3",
    activeFramework: "net10.0",
    controlsHtml: "<section class=package-metadata-controls><div id=picker></div></section>",
    fresh: true,
    loading: false,
    error: "",
    metadata: { assemblies: [assembly()] },
    ...helpers,
    ...overrides,
  };
}

function explorerState(overrides = {}) {
  return {
    open: true,
    assemblyFileName: "Contoso.dll",
    directory: [
      { index: 2, name: "TypeDef", rowCount: 12, isProjected: true },
      { index: 42, name: "Unmodeled", rowCount: 3, isProjected: false },
    ],
    heaps: [{ name: "String", streamName: "#Strings", sizeInBytes: 1024, addressing: "Offset" }],
    windows: {},
    heapWindows: {},
    focusIndex: 2,
    focusHeap: null,
    highlight: null,
    detail: null,
    history: [{ index: 2, rowId: 0 }],
    historyPos: 0,
    overview: false,
    ...overrides,
  };
}

function context(overrides = {}) {
  return { explorer: explorerState(overrides), ...helpers };
}

// -- Metadata lens ---------------------------------------------------------------------------

test("the metadata lens asks the platform to pick a library before reading an image", () => {
  const html = renderPackageMetadata(lensOptions({ isPlatform: true, scopedLibrary: "" }));
  assert.match(html, /Pick a library to inspect/);
  assert.match(html, /id=picker/);
  assert.match(
    html,
    /class="package-metadata-surface"[\s\S]*?class=package-metadata-controls[\s\S]*?Pick a library to inspect[\s\S]*?class="metadata-surface-footer package-metadata-surface-footer"/);
});

test("the metadata lens reports loading and failure only for the current scope", () => {
  const loading = renderPackageMetadata(lensOptions({ loading: true, metadata: null }));
  assert.match(loading, /Reading metadata…/);
  assert.match(loading, /<p>reading<\/p>/);

  const failed = renderPackageMetadata(lensOptions({ error: "boom & <bang>", metadata: null }));
  assert.match(failed, /Metadata read failed/);
  assert.match(failed, /boom &amp; &lt;bang&gt;/);
  assert.match(failed, /data-package-metadata-retry/);

  // A stale key means the loaded image belongs to a different scope, so neither the error nor
  // the result may be shown as this scope's answer.
  const stale = renderPackageMetadata(lensOptions({ fresh: false, error: "boom" }));
  assert.match(stale, /Loading…/);
  assert.doesNotMatch(stale, /boom/);
});

test("the metadata lens surfaces a partial-read warning alongside the image", () => {
  const html = renderPackageMetadata(lensOptions({
    metadata: { assemblies: [assembly()], inspectionError: "Native.dll unreadable" },
  }));
  assert.match(html, /Some assemblies could not be read/);
  assert.match(html, /Native\.dll unreadable/);
  assert.match(html, /<p>1 assembly<\/p>/);
  assert.match(html, /title="Contoso@1\.2\.3"/);
  assert.match(html, /title="net10\.0"/);
});

test("the metadata lens keeps selected platform context in its stable frame", () => {
  const html = renderPackageMetadata(lensOptions({
    isPlatform: true,
    scopedLibrary: "System.Runtime",
  }));
  assert.match(html, /<p>1 assembly<\/p>/);
  assert.match(html, /title="net10\.0 · System\.Runtime"/);
  assert.match(
    html,
    /class=package-metadata-controls[\s\S]*?class="package-metadata-scroll"[\s\S]*?class="metadata-surface-footer package-metadata-surface-footer"/);
});

test("the metadata lens distinguishes a truncated metadata version", () => {
  const truncated = renderPackageMetadata(lensOptions({
    metadata: {
      assemblies: [assembly({
        metadataVersion: "v4.0.30319",
        metadataVersionTruncated: true,
      })],
    },
  }));
  const complete = renderPackageMetadata(lensOptions());

  assert.match(truncated, /v4\.0\.30319…/);
  assert.doesNotMatch(complete, /v4\.0\.30319…/);
});

test("the metadata lens reports an image with no ECMA-335 metadata", () => {
  const html = renderPackageMetadata(lensOptions({ metadata: { assemblies: [] } }));
  assert.match(html, /No metadata images/);
});

test("the metadata lens does not render all-failed inspection as valid emptiness", () => {
  const html = renderPackageMetadata(lensOptions({
    metadata: {
      assemblies: [],
      inspectionError: "Assembly unavailable: InvalidImage.",
    },
  }));
  assert.match(html, /Metadata read failed/);
  assert.match(html, /Assembly unavailable: InvalidImage\./);
  assert.doesNotMatch(html, /No metadata images/);
  assert.doesNotMatch(html, /native or resource-only/);
});

test("an assembly block exposes Explore and groups tables by role", () => {
  const html = renderAssemblyMetadataBlock(assembly(), helpers);
  // The empty #Blob heap is omitted; #Strings and #GUID keep their ECMA-335 spellings.
  assert.match(html, /#Strings/);
  assert.match(html, /#GUID/);
  assert.doesNotMatch(html, /#Blob/);
  assert.match(
    html,
    /data-mde-open-heap="String" data-mde-assembly="Contoso\.dll"/);

  assert.match(
    html,
    /class="meta-explore primary-action" data-mde-explore data-mde-assembly="Contoso\.dll">Explore/);
  assert.match(html, /Types/);
  assert.match(html, /Members/);
  assert.match(html, /Other/);
  const typeDefAt = html.indexOf("TypeDef");
  const methodDefAt = html.indexOf("MethodDef");
  assert.ok(typeDefAt > 0 && typeDefAt < methodDefAt, "tables retain metadata order within groups");
  assert.match(html, /data-mde-open="2" data-mde-assembly="Contoso\.dll"/);
  assert.match(html, /meta-table-unprojected/);
  assert.match(html, /2\/3 populated/);
  assert.match(html, /v2\.5 · ILOnly · entry 0x6000001/);
  assert.match(html, /Amd64 · PE32\+/);
});

test("an assembly block reports an available managed ReadyToRun header", () => {
  const readyToRun = renderAssemblyMetadataBlock(assembly({
    headers: {
      ...assembly().headers,
      managedNativeHeaderRva: 0x1234,
      managedNativeHeaderSize: 96,
    },
  }), helpers);
  const ilOnly = renderAssemblyMetadataBlock(assembly(), helpers);

  assert.match(
    readyToRun,
    /ReadyToRun[\s\S]*managed native header · 96 B · RVA 0x1234/);
  assert.doesNotMatch(ilOnly, /ReadyToRun/);
});

test("the metadata lens places assembly content directly in its owned scroller", () => {
  const html = renderPackageMetadata(lensOptions());
  assert.match(
    html,
    /<h1 id="package-metadata-surface-title">Metadata images<\/h1>[\s\S]*?<div class="package-metadata-scroll">[\s\S]*?class="document-section meta-assembly"/);
  assert.doesNotMatch(
    html,
    /<h2>Metadata image<\/h2>|class="type-heading"|package-coordinate-editor/);
});

// -- Pure derivation ---------------------------------------------------------------------------

test("heap stream names match the product's ECMA-335 spelling", () => {
  assert.equal(heapStreamName("String"), "#Strings");
  assert.equal(heapStreamName("Blob"), "#Blob");
  assert.equal(heapStreamName("Guid"), "#GUID");
  assert.equal(heapStreamName("UserString"), "#US");
  assert.equal(heapStreamName("Future"), "#Future");
});

test("metadata tables are grouped by role with unknown tables retained", () => {
  const groups = groupMetadataTables([
    { index: 42, name: "FutureTable", rowCount: 1, isProjected: false },
    { index: 6, name: "MethodDef", rowCount: 2, isProjected: true },
    { index: 2, name: "TypeDef", rowCount: 1, isProjected: true },
    { index: 32, name: "Assembly", rowCount: 1, isProjected: true },
    { index: 37, name: "AssemblyRefOS", rowCount: 1, isProjected: false },
  ]);

  assert.deepEqual(groups.map(group => group.name), [
    "Modules & assemblies",
    "Types",
    "Members",
    "Other",
  ]);
  assert.deepEqual(
    groups.flatMap(group => group.tables.map(table => table.name)),
    ["Assembly", "AssemblyRefOS", "TypeDef", "MethodDef", "FutureTable"]);
});

test("focus identity compares heaps by name and tables by index, ignoring the row", () => {
  assert.equal(sameFocus({ index: 2, rowId: 1 }, { index: 2, rowId: 9 }), true);
  assert.equal(sameFocus({ index: 2 }, { index: 3 }), false);
  assert.equal(sameFocus({ heap: "String" }, { heap: "String" }), true);
  // A heap and a table are never the same place, even when the table index is absent.
  assert.equal(sameFocus({ heap: "String" }, { index: 2 }), false);
  assert.equal(sameFocus(null, { index: 2 }), false);
});

test("an unknown table index falls back to its numeric name", () => {
  const directory = [{ index: 2, name: "TypeDef", rowCount: 1, isProjected: true }];
  assert.equal(explorerTableName(directory, 2), "TypeDef");
  assert.equal(explorerTableName(directory, 9), "#9");
  assert.equal(explorerTableName(undefined, 9), "#9");
});

test("the page-size estimate stays within its bounds for tiny and huge viewports", () => {
  assert.equal(estimateExplorerPageSize(200), 30);
  assert.equal(estimateExplorerPageSize(1_000_000), 400);
  assert.ok(estimateExplorerPageSize(900) > EXPLORER_PAGE / 2);
});

test("heap names are escaped for use inside an attribute selector", () => {
  assert.equal(cssEscape('a"b\\c'), 'a\\"b\\\\c');
});

test("coverage labels state the answer's completeness", () => {
  assert.equal(coverageLabel("Complete"), "every entry");
  assert.equal(coverageLabel("ReferencedOnly"), "referenced only");
  assert.equal(coverageLabel("NotEnumerable"), "not enumerable");
  assert.equal(coverageLabel("Novel"), "Novel");
});

test("a not-enumerable heap reports a blind spot rather than an empty heap", () => {
  const note = heapCoverageNote(
    { heap: "UserString", streamName: "#US", coverage: "NotEnumerable" },
    escapeHtml);
  assert.match(note, /blind spot, not an empty heap/);
  assert.match(note, /#US/);
});

test("truncation caveats accumulate onto the coverage note", () => {
  const note = heapCoverageNote(
    {
      heap: "String",
      streamName: "#Strings",
      coverage: "ReferencedOnly",
      rowsTruncated: true,
      entriesTruncated: true,
    },
    escapeHtml);
  assert.match(note, /Only entries a projected table row points at are listed/);
  assert.match(note, /some references are uncounted/);
  assert.match(note, /entry budget cut the listing short/);
});

// -- Metadata Explorer ------------------------------------------------------------------------

test("the explorer renders table and heap chips with the focused one active", () => {
  const html = renderMetadataExplorer(context());
  assert.match(html, /data-mde-chip="2"[^>]*/);
  assert.match(html, /class="mde-chip active [^"]*" data-mde-chip="2"/);
  assert.match(html, /mde-chip-unprojected/);
  assert.match(html, /data-mde-heap-chip="String"/);
  assert.match(html, /Contoso\.dll/);
});

test("the explorer disables history buttons at the ends of the stack", () => {
  const start = renderMetadataExplorer(context());
  assert.match(start, /id="mde-hist-back" class="mde-navbtn" disabled/);
  assert.match(start, /id="mde-hist-fwd" class="mde-navbtn" disabled/);

  const middle = renderMetadataExplorer(context({
    history: [{ index: 2 }, { index: 42 }, { index: 2 }],
    historyPos: 1,
  }));
  assert.doesNotMatch(middle, /id="mde-hist-back" class="mde-navbtn" disabled/);
  assert.doesNotMatch(middle, /id="mde-hist-fwd" class="mde-navbtn" disabled/);
});

test("the overview drops the focus lightbox and opens the wall", () => {
  const focused = renderMetadataExplorer(context());
  assert.match(focused, /mde-focus/);
  assert.match(focused, /click a ref to jump/);

  const overview = renderMetadataExplorer(context({ overview: true }));
  assert.doesNotMatch(overview, /mde-focus/);
  assert.match(overview, /mde-wall-open/);
  assert.match(overview, /click a table to focus/);
});

test("an unloaded table card carries the lazy-load hook the observer binds to", () => {
  const html = renderMetadataExplorer(context());
  assert.match(html, /data-mde-needs-load="2"/);
  assert.match(html, /data-mde-heap-needs-load="String"/);
});

test("an unprojected table states it is unmodeled instead of loading rows", () => {
  const html = renderMetadataExplorer(context());
  assert.match(html, /not modeled by the projection yet/);
  assert.doesNotMatch(html, /data-mde-needs-load="42"/);
});

test("a loaded table card pages within its row count", () => {
  const data = {
    index: 2,
    name: "TypeDef",
    rowCount: 12,
    startRowId: 5,
    columns: [{ name: "Name", kind: "String" }],
    rows: [
      { rowId: 5, token: 0x02000005, cells: [{ kind: "scalar", display: "Alpha" }] },
      { rowId: 6, token: 0x02000006, cells: [{ kind: "scalar", display: "Beta" }] },
    ],
  };
  const html = renderMetadataExplorer(context({
    windows: { 2: { loading: false, error: "", data } },
  }));
  assert.match(html, /rows 5–6 of 12/);
  assert.match(html, /data-mde-page="2:3"/);
  assert.match(html, /data-mde-page="2:7"/);
  assert.doesNotMatch(html, /data-mde-page="2:3" disabled/);
});

test("a partial final page returns to the preceding requested window", () => {
  const data = {
    index: 2,
    name: "TypeDef",
    rowCount: 101,
    startRowId: 101,
    columns: [{ name: "Name", kind: "String" }],
    rows: [
      {
        rowId: 101,
        token: 0x02000065,
        cells: [{ kind: "scalar", display: "Final" }],
      },
    ],
  };
  const html = renderMetadataExplorer(context({
    windows: {
      2: {
        loading: false,
        error: "",
        data,
        startRowId: 101,
        maxRows: 50,
      },
    },
  }));

  assert.match(html, /rows 101–101 of 101/);
  assert.match(html, /data-mde-page="2:51"/);
  assert.doesNotMatch(html, /data-mde-page="2:100"/);
});

test("a table window error is shown rather than an empty grid", () => {
  const html = renderMetadataExplorer(context({
    windows: { 2: { loading: false, error: "table read failed", data: null } },
  }));
  assert.match(html, /mde-card-error/);
  assert.match(html, /table read failed/);
});

test("the grid marks the highlighted and selected rows", () => {
  const data = {
    index: 2,
    name: "TypeDef",
    rowCount: 2,
    startRowId: 1,
    columns: [{ name: "Name", kind: "String" }],
    rows: [
      { rowId: 1, token: 0x02000001, cells: [{ kind: "scalar", display: "Alpha" }] },
      { rowId: 2, token: 0x02000002, cells: [{ kind: "scalar", display: "Beta" }] },
    ],
  };
  const html = renderExplorerGrid(data, context({
    highlight: { index: 2, rowId: 1 },
    detail: { index: 2, rowId: 2 },
  }));
  assert.match(html, /data-mde-row="2:1"/);
  assert.match(html, /mde-row-hot/);
  assert.match(html, /mde-row-sel/);
  assert.match(html, /token 0x2000001/);
});

test("handle and range cells render as ref->def jumps naming the target table", () => {
  const ctx = context();
  const handle = renderExplorerCell(
    { kind: "handle", targetTable: 2, targetRowId: 7 }, null, ctx);
  assert.match(handle, /data-mde-jump="2:7"/);
  assert.match(handle, /TypeDef #7/);

  const range = renderExplorerCell(
    { kind: "range", targetTable: 2, startRowId: 3, endRowId: 5, count: 2 }, null, ctx);
  assert.match(range, /data-mde-jump="2:3"/);
  assert.match(range, /title="→ TypeDef rows 3‥4"/);
  assert.match(range, /TypeDef #3‥4/);
  assert.doesNotMatch(range, /3‥5/);
});

test("empty handle and range cells are nil rather than dead jumps", () => {
  const ctx = context();
  assert.match(renderExplorerCell({ kind: "handle", targetRowId: 0 }, null, ctx), /mde-nil">nil</);
  assert.match(renderExplorerCell({ kind: "range", count: 0 }, null, ctx), /mde-nil">empty</);
  assert.match(renderExplorerCell({ kind: "nil" }, null, ctx), /mde-nil/);
  assert.equal(renderExplorerCell(null, null, ctx), "");
  assert.equal(renderExplorerCell({ kind: "unknown-kind" }, null, ctx), "");
});

test("heap, flags, and malformed cells escape their projected text", () => {
  const ctx = context();
  const heap = renderExplorerCell(
    { kind: "heap", heap: "String", offset: 4, length: 6, text: "<hi>", truncated: true },
    null, ctx);
  assert.match(heap, /&lt;hi&gt;…/);
  assert.match(heap, /mde-heap-string/);

  const flags = renderExplorerCell({ kind: "flags", raw: 255, decoded: "Public" }, null, ctx);
  assert.match(flags, /0xff/);
  assert.match(flags, /Public/);

  const malformed = renderExplorerCell({ kind: "malformed", detail: "bad blob" }, null, ctx);
  assert.match(malformed, /mde-cell-malformed/);
  assert.match(malformed, /bad blob/);
});

test("flags cells coerce a non-numeric raw value the same way the prior bitwise cast did", () => {
  const ctx = context();
  // raw as a numeric string (e.g. round-tripped through JSON) must hex-format identically to
  // the equivalent number, matching the ">>> 0" coercion this replaced.
  assert.match(renderExplorerCell({ kind: "flags", raw: "255", decoded: "Public" }, null, ctx), /0xff/);
  // A missing raw value falls back to 0 rather than throwing or printing "NaN"/"undefined".
  const missing = renderExplorerCell({ kind: "flags", decoded: "None" }, null, ctx);
  assert.match(missing, /0x0/);
  assert.doesNotMatch(missing, /NaN|undefined/);
});

test("a heap listing addresses #GUID by index and other heaps by byte offset", () => {
  const ctx = context();
  const guid = renderHeapListing({
    heap: "Guid",
    streamName: "#GUID",
    coverage: "Complete",
    entries: [{ offset: 1, referenceCount: 2, value: { kind: "scalar", display: "abc" } }],
  }, ctx);
  assert.match(guid, /#1</);
  assert.match(guid, /GUID index/);

  const strings = renderHeapListing({
    heap: "String",
    streamName: "#Strings",
    coverage: "ReferencedOnly",
    entries: [{ offset: 255, referenceCount: 1, value: { kind: "heap", heap: "String", text: "n", length: 1 } }],
  }, ctx);
  assert.match(strings, /0xff</);
  assert.match(strings, /data-mde-heap-row="String:255"/);
  assert.match(strings, /1×/);
});

test("a not-enumerable heap listing is the coverage note alone", () => {
  const html = renderHeapListing(
    { heap: "UserString", streamName: "#US", coverage: "NotEnumerable", entries: [] },
    context());
  assert.match(html, /blind spot/);
  assert.doesNotMatch(html, /mde-grid-scroll/);
});

test("a heap card badges its coverage once the listing loads", () => {
  const heap = { name: "String", streamName: "#Strings", sizeInBytes: 1024, addressing: "Offset" };
  const html = renderHeapCard(heap, context({
    focusHeap: "String",
    heapWindows: {
      String: {
        loading: false,
        error: "",
        data: { heap: "String", streamName: "#Strings", coverage: "ReferencedOnly", entries: [] },
      },
    },
  }));
  assert.match(html, /mde-cov-referencedonly/);
  assert.match(html, /referenced only/);
  assert.match(html, /mde-card-focus/);
});

test("the row inspector renders the selected row's cells and stays empty without one", () => {
  const data = {
    index: 2,
    name: "TypeDef",
    rowCount: 1,
    startRowId: 1,
    columns: [{ name: "Name", kind: "String" }, { name: "Extends", kind: "Handle" }],
    rows: [{
      rowId: 1,
      token: 0x02000001,
      cells: [
        { kind: "scalar", display: "Alpha" },
        { kind: "handle", targetTable: 2, targetRowId: 2 },
      ],
    }],
  };
  const detailed = renderExplorerDetail(context({
    detail: { index: 2, rowId: 1 },
    windows: { 2: { loading: false, error: "", data } },
  }));
  assert.match(detailed, /TypeDef #1/);
  assert.match(detailed, /token 0x2000001/);
  assert.match(detailed, /Extends/);
  assert.match(detailed, /data-mde-jump="2:2"/);

  assert.equal(renderExplorerDetail(context()), "");
  // A selected row outside the loaded window has nothing to inspect yet.
  assert.equal(
    renderExplorerDetail(context({
      detail: { index: 2, rowId: 99 },
      windows: { 2: { loading: false, error: "", data } },
    })),
    "");
});
