import assert from "node:assert/strict";
import test from "node:test";
import {
  coverageLabel,
  cssEscape,
  estimateExplorerPageSize,
  explorerTableName,
  EXPLORER_PAGE,
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
} from "../src/metadata-viewer.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function fmtBytes(value) {
  return `${value} B`;
}

const helpers = { escapeHtml, fmtBytes };

function assembly(overrides = {}) {
  return {
    assembly: "Contoso.dll",
    kind: "Managed",
    isAssembly: true,
    metadataSize: 4096,
    metadataVersion: "v4.0.30319",
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
    },
    ...overrides,
  };
}

function lensOptions(overrides = {}) {
  return {
    isPlatform: false,
    scopedLibrary: "",
    activeFramework: "net10.0",
    pickerHtml: "<div id=picker></div>",
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
});

test("the metadata lens reports loading and failure only for the current scope", () => {
  const loading = renderPackageMetadata(lensOptions({ loading: true, metadata: null }));
  assert.match(loading, /Reading metadata…/);

  const failed = renderPackageMetadata(lensOptions({ error: "boom & <bang>", metadata: null }));
  assert.match(failed, /Metadata read failed/);
  assert.match(failed, /boom &amp; &lt;bang&gt;/);

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
  assert.match(html, /1 assembly · net10\.0/);
});

test("the metadata lens reports an image with no ECMA-335 metadata", () => {
  const html = renderPackageMetadata(lensOptions({ metadata: { assemblies: [] } }));
  assert.match(html, /No metadata images/);
});

test("an assembly block lists non-empty heaps and tables sorted by row count", () => {
  const html = renderAssemblyMetadataBlock(assembly(), helpers);
  // The empty #Blob heap is omitted; #Strings and #GUID keep their ECMA-335 spellings.
  assert.match(html, /#Strings/);
  assert.match(html, /#GUID/);
  assert.doesNotMatch(html, /#Blob/);
  assert.match(html, /data-mde-open-heap="Contoso\.dll\|String"/);

  const typeDefAt = html.indexOf("TypeDef");
  const methodDefAt = html.indexOf("MethodDef");
  assert.ok(methodDefAt > 0 && methodDefAt < typeDefAt, "tables sort by descending row count");
  assert.match(html, /data-mde-open="Contoso\.dll\|2"/);
  assert.match(html, /meta-table-unprojected/);
  assert.match(html, /2\/3 populated/);
  assert.match(html, /v2\.5 · ILOnly · entry 0x6000001/);
  assert.match(html, /Amd64 · PE32\+/);
});

// -- Pure derivation ---------------------------------------------------------------------------

test("heap stream names match the product's ECMA-335 spelling", () => {
  assert.equal(heapStreamName("String"), "#Strings");
  assert.equal(heapStreamName("Blob"), "#Blob");
  assert.equal(heapStreamName("Guid"), "#GUID");
  assert.equal(heapStreamName("UserString"), "#US");
  assert.equal(heapStreamName("Future"), "#Future");
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
    { kind: "range", targetTable: 2, startRowId: 3, endRowId: 5, count: 3 }, null, ctx);
  assert.match(range, /data-mde-jump="2:3"/);
  assert.match(range, /TypeDef #3‥5/);
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
