import assert from "node:assert/strict";
import test from "node:test";

import {
  createMetadataInspectionCoordinator,
  type AppExplorerState,
  type MetadataInspectionDependencies,
  type MetadataInspectionState,
  type TypeMetadataLoadRequest,
} from "../src/metadata-inspection.ts";
import type {
  BrowserTypeMetadata,
} from "../src/facades/inspect-web-metadata.d.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";
import type {
  ExplorerTableData,
  HeapListingData,
} from "../src/metadata-viewer.ts";

function metadataResult(fullName = "Example.Widget"): BrowserTypeMetadata {
  return {
    fullName,
    namespace: "Example",
    name: "Widget",
    kind: "Class",
    modifiers: [],
    accessibility: "Public",
    assembly: "Example.Package.dll",
    baseType: null,
    interfaces: [],
    derivedTypes: [],
    typeParameters: [],
    attributes: [],
    enumUnderlyingType: null,
    composition: null,
    graphNodes: [],
    graphEdges: [],
    inspectionFailures: [],
  };
}

function explorerState(
  overrides: Partial<AppExplorerState> = {},
): AppExplorerState {
  return {
    open: true,
    assemblyId: "asset:example",
    assemblyFileName: "Example.Package.dll",
    metadataRoot: "cli",
    canonicalRoot: "Cli",
    aliasesCliMetadata: false,
    directory: [],
    windows: {},
    heapWindows: {},
    focusIndex: 2,
    focusHeap: null,
    highlight: null,
    detail: null,
    history: [],
    historyPos: -1,
    overview: false,
    isPlatform: false,
    pack: null,
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    pendingScroll: false,
    ...overrides,
  };
}

function inspectionState(
  overrides: Partial<MetadataInspectionState> = {},
): MetadataInspectionState {
  return {
    typeMetadata: null,
    typeMetadataLoading: false,
    typeMetadataError: "",
    typeMetadataKey: "",
    typeMetadataGeneration: 0,
    explorer: null,
    ...overrides,
  };
}

function tableResult(
  index = 2,
  startRowId = 1,
  error = "",
): ExplorerTableData {
  return {
    index,
    name: "TypeDef",
    rowCount: 1,
    startRowId,
    rows: [],
    error,
  };
}

function heapResult(heapName = "String"): HeapListingData {
  return {
    heap: heapName,
    streamName: "#Strings",
    coverage: "Referenced",
    entries: [],
  };
}

function typeRequest(
  overrides: Partial<TypeMetadataLoadRequest> = {},
): TypeMetadataLoadRequest {
  return {
    signature: "Example.Widget",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package.dll",
    type: "Example.Widget",
    isVisible: () => true,
    ...overrides,
  };
}

function focusSnapshot(): MemberFocusSnapshot {
  return {
    selector: "[data-member-id='M:Example.Widget.Run']",
    dataTarget: null,
    selection: null,
    navigationScope: null,
    navigationSelection: null,
    navigationScrollTop: null,
    focusLost: false,
  };
}

function inspectionDependencies(
  state: MetadataInspectionState,
  overrides: Partial<Omit<MetadataInspectionDependencies, "state">> = {},
): MetadataInspectionDependencies {
  return {
    state,
    queryTypeMetadata: async () => metadataResult(),
    queryPackageTable: async (_explorer, index, startRowId) =>
      tableResult(index, startRowId),
    queryPlatformTable: async (_explorer, index, startRowId) =>
      tableResult(index, startRowId),
    queryPackageHeap: async (_explorer, heapName) => heapResult(heapName),
    queryPlatformHeap: async (_explorer, heapName) => heapResult(heapName),
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: () => focusSnapshot(),
    scrollExplorerToFocus: () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

test("type metadata publishes the current result and restores visible focus", async () => {
  const request = deferred<BrowserTypeMetadata>();
  const preservedFocus = focusSnapshot();
  const focusCalls: (MemberFocusSnapshot | null | undefined)[] = [];
  const state = inspectionState();
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeMetadata: async value => {
        assert.deepEqual(
          [
            value.packageId,
            value.version,
            value.framework,
            value.assembly,
            value.type,
          ],
          [
            "Example.Package",
            "1.2.3",
            "net10.0",
            "Example.Package.dll",
            "Example.Widget",
          ]);
        return request.promise;
      },
      renderPreservingMemberFocus: fallback => {
        focusCalls.push(fallback);
        return preservedFocus;
      },
    }));

  const load = coordinator.loadTypeMetadata(typeRequest());
  assert.equal(state.typeMetadataLoading, true);
  assert.equal(state.typeMetadataKey, "Example.Widget");
  assert.deepEqual(focusCalls, [undefined]);

  const result = metadataResult();
  request.resolve(result);
  await load;

  assert.equal(state.typeMetadata, result);
  assert.equal(state.typeMetadataLoading, false);
  assert.deepEqual(focusCalls, [undefined, preservedFocus]);
});

test("hidden type metadata completion caches without repainting", async () => {
  const request = deferred<BrowserTypeMetadata>();
  let visible = false;
  let renders = 0;
  const state = inspectionState();
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeMetadata: async () => request.promise,
      renderPreservingMemberFocus: () => {
        renders++;
        return focusSnapshot();
      },
    }));

  const load = coordinator.loadTypeMetadata(typeRequest({
    isVisible: () => visible,
  }));
  request.resolve(metadataResult());
  await load;

  assert.ok(state.typeMetadata);
  assert.equal(state.typeMetadataLoading, false);
  assert.equal(renders, 1);
  visible = true;
  await coordinator.loadTypeMetadata(typeRequest());
  assert.equal(renders, 2);
});

test("newer type metadata requests suppress stale success and failure", async () => {
  const first = deferred<BrowserTypeMetadata>();
  const second = deferred<BrowserTypeMetadata>();
  const state = inspectionState();
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeMetadata: request =>
        request.signature === "first" ? first.promise : second.promise,
    }));

  const firstLoad = coordinator.loadTypeMetadata(typeRequest({
    signature: "first",
  }));
  const secondLoad = coordinator.loadTypeMetadata(typeRequest({
    signature: "second",
  }));
  first.reject(new Error("stale failure"));
  second.resolve(metadataResult("Example.Current"));
  await Promise.all([firstLoad, secondLoad]);

  assert.equal(state.typeMetadata?.fullName, "Example.Current");
  assert.equal(state.typeMetadataError, "");
  assert.equal(state.typeMetadataLoading, false);
  assert.equal(state.typeMetadataKey, "second");
});

test("cached type metadata failures render without querying again", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    typeMetadataKey: "Example.Widget",
    typeMetadataError: "metadata unavailable",
  });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeMetadata: async () => {
        queries++;
        return metadataResult();
      },
      renderPreservingMemberFocus: () => {
        renders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadTypeMetadata(typeRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.typeMetadataError, "metadata unavailable");
});

test("explorer windows route package coordinates and publish errors", async () => {
  const explorer = explorerState({
    metadataRoot: "r2r-manifest",
    canonicalRoot: "ReadyToRunManifest",
  });
  const events: string[] = [];
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async (requestExplorer, index, startRowId, maxRows) => {
        assert.equal(requestExplorer, explorer);
        assert.equal(requestExplorer.metadataRoot, "r2r-manifest");
        assert.deepEqual([index, startRowId, maxRows], [2, 51, 50]);
        return tableResult(index, startRowId, "malformed row");
      },
      queryPlatformTable: async () => {
        throw new Error("unexpected platform query");
      },
      render: () => events.push("render"),
      scrollExplorerToFocus: () => events.push("scroll"),
    }));

  await coordinator.loadExplorerWindow(2, 51, 50);

  assert.deepEqual(events, ["render", "render", "scroll"]);
  assert.equal(explorer.windows[2]?.loading, false);
  assert.equal(explorer.windows[2]?.error, "malformed row");
  assert.equal(explorer.windows[2]?.data, null);
  assert.equal(explorer.windows[2]?.startRowId, 51);
});

test("explorer window failures remain visible for the current explorer", async () => {
  const explorer = explorerState();
  let renders = 0;
  let scrolls = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async () => {
        throw new Error("table unavailable");
      },
      render: () => renders++,
      scrollExplorerToFocus: () => scrolls++,
    }));

  await coordinator.loadExplorerWindow(2, 1, 50);

  assert.equal(explorer.windows[2]?.loading, false);
  assert.equal(explorer.windows[2]?.error, "table unavailable");
  assert.equal(explorer.windows[2]?.data, null);
  assert.equal(renders, 2);
  assert.equal(scrolls, 1);
});

test("explorer typed table failures remain visible and can retry", async () => {
  const explorer = explorerState({ focusIndex: 2 });
  const state = inspectionState({ explorer });
  let queries = 0;
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async (_requestExplorer, index) => {
        queries++;
        return tableResult(
          index,
          1,
          "Assembly unavailable: InvalidImage.");
      },
    }));

  await coordinator.loadExplorerWindow(2, 101, 50);
  await coordinator.loadExplorerWindow(2, 101, 50);

  assert.equal(queries, 2);
  assert.equal(
    explorer.windows[2]?.error,
    "Assembly unavailable: InvalidImage.");
  assert.equal(explorer.windows[2]?.data, null);
});

test("explorer window completion cannot publish after explorer replacement", async () => {
  const explorer = explorerState({ isPlatform: true, pack: "Microsoft.NETCore.App.Ref" });
  const request = deferred<ExplorerTableData>();
  let renders = 0;
  let scrolls = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatformTable: async requestExplorer => {
        assert.equal(requestExplorer.pack, "Microsoft.NETCore.App.Ref");
        return request.promise;
      },
      render: () => renders++,
      scrollExplorerToFocus: () => scrolls++,
    }));

  const load = coordinator.loadExplorerWindow(2, 1, 50);
  state.explorer = explorerState({ packageId: "Replacement.Package" });
  request.resolve(tableResult());
  await load;

  assert.equal(explorer.windows[2]?.loading, true);
  assert.equal(renders, 1);
  assert.equal(scrolls, 0);
});

test("newer explorer window requests suppress stale completions", async () => {
  const explorer = explorerState();
  const first = deferred<ExplorerTableData>();
  const second = deferred<ExplorerTableData>();
  const starts: number[] = [];
  let renders = 0;
  let scrolls = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async (_requestExplorer, index, startRowId) => {
        starts.push(startRowId);
        return startRowId === 1 ? first.promise : second.promise;
      },
      render: () => renders++,
      scrollExplorerToFocus: () => scrolls++,
    }));

  const firstLoad = coordinator.loadExplorerWindow(2, 1, 50);
  await coordinator.loadExplorerWindow(2, 1, 50);
  const secondLoad = coordinator.loadExplorerWindow(2, 101, 50);
  first.resolve(tableResult(2, 1));
  await firstLoad;

  assert.deepEqual(starts, [1, 101]);
  assert.equal(explorer.windows[2]?.loading, true);
  assert.equal(explorer.windows[2]?.startRowId, 101);
  assert.equal(explorer.windows[2]?.data, null);
  assert.equal(renders, 2);
  assert.equal(scrolls, 0);

  second.resolve(tableResult(2, 101));
  await secondLoad;

  assert.equal(explorer.windows[2]?.loading, false);
  assert.equal(state.explorer?.windows[2]?.data?.startRowId, 101);
  assert.equal(renders, 3);
  assert.equal(scrolls, 1);
});

test("a focused retained window supersedes the pending range", async () => {
  const explorer = explorerState({
    windows: {
      2: {
        loading: false,
        error: "",
        data: tableResult(2, 1),
        startRowId: 1,
        maxRows: 50,
      },
    },
  });
  const nextPage = deferred<ExplorerTableData>();
  const focusedPage = deferred<ExplorerTableData>();
  const starts: number[] = [];
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async (_requestExplorer, _index, startRowId) => {
        starts.push(startRowId);
        return startRowId === 51 ? nextPage.promise : focusedPage.promise;
      },
    }));

  const nextPageLoad = coordinator.loadExplorerWindow(2, 51, 50);
  assert.equal(explorer.windows[2]?.data?.startRowId, 1);

  const focusLoad = coordinator.loadExplorerWindow(2, 1, 50);
  assert.deepEqual(starts, [51, 1]);

  nextPage.resolve(tableResult(2, 51));
  await nextPageLoad;
  assert.equal(explorer.windows[2]?.loading, true);
  assert.equal(explorer.windows[2]?.startRowId, 1);

  focusedPage.resolve(tableResult(2, 1));
  await focusLoad;
  assert.equal(explorer.windows[2]?.loading, false);
  assert.equal(explorer.windows[2]?.data?.startRowId, 1);
});

test("explorer window cache requires the same row range", async () => {
  const explorer = explorerState({
    windows: {
      2: {
        loading: false,
        error: "",
        data: tableResult(2, 1),
        startRowId: 1,
        maxRows: 50,
      },
    },
  });
  let queries = 0;
  let renders = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageTable: async (_requestExplorer, index, startRowId) => {
        queries++;
        return tableResult(index, startRowId);
      },
      render: () => renders++,
    }));

  await coordinator.loadExplorerWindow(2, 1, 50);
  await coordinator.loadExplorerWindow(2, 51, 50);

  assert.equal(queries, 1);
  assert.equal(renders, 2);
  assert.equal(explorer.windows[2]?.data?.startRowId, 51);
});

test("explorer heaps route platform coordinates and scroll the focused heap", async () => {
  const explorer = explorerState({
    isPlatform: true,
    pack: "Microsoft.NETCore.App.Ref",
    focusHeap: "String",
    metadataRoot: "r2r-manifest",
    canonicalRoot: "Cli",
    aliasesCliMetadata: true,
  });
  const events: string[] = [];
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageHeap: async () => {
        throw new Error("unexpected package query");
      },
      queryPlatformHeap: async (requestExplorer, heapName) => {
        assert.equal(requestExplorer.pack, "Microsoft.NETCore.App.Ref");
        assert.equal(requestExplorer.metadataRoot, "r2r-manifest");
        return heapResult(heapName);
      },
      render: () => events.push("render"),
      scrollExplorerToFocus: () => events.push("scroll"),
    }));

  await coordinator.loadExplorerHeap("String");
  await coordinator.loadExplorerHeap("String");

  assert.deepEqual(events, ["render", "render", "scroll"]);
  assert.equal(explorer.heapWindows.String?.data?.heap, "String");
});

test("explorer heap completion cannot publish after explorer replacement", async () => {
  const explorer = explorerState({ focusHeap: "String" });
  const request = deferred<HeapListingData>();
  let renders = 0;
  let scrolls = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageHeap: async () => request.promise,
      render: () => renders++,
      scrollExplorerToFocus: () => scrolls++,
    }));

  const load = coordinator.loadExplorerHeap("String");
  state.explorer = null;
  request.resolve(heapResult());
  await load;

  assert.equal(explorer.heapWindows.String?.loading, true);
  assert.equal(renders, 1);
  assert.equal(scrolls, 0);
});

test("explorer heap failures remain visible for the current explorer", async () => {
  const explorer = explorerState({ focusHeap: "Blob" });
  let renders = 0;
  let scrolls = 0;
  const state = inspectionState({ explorer });
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageHeap: async () => {
        throw new Error("heap unavailable");
      },
      render: () => renders++,
      scrollExplorerToFocus: () => scrolls++,
    }));

  await coordinator.loadExplorerHeap("Blob");

  assert.equal(explorer.heapWindows.Blob?.loading, false);
  assert.equal(explorer.heapWindows.Blob?.error, "heap unavailable");
  assert.equal(explorer.heapWindows.Blob?.data, null);
  assert.equal(renders, 2);
  assert.equal(scrolls, 1);
});

test("explorer typed heap failures remain visible and can retry", async () => {
  const explorer = explorerState({ focusHeap: "String" });
  const state = inspectionState({ explorer });
  let queries = 0;
  const coordinator = createMetadataInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageHeap: async () => {
        queries++;
        return {
          ...heapResult(),
          coverage: "NotEnumerable",
          error: "InvalidImage: identity mismatch",
        };
      },
    }));

  await coordinator.loadExplorerHeap("String");
  await coordinator.loadExplorerHeap("String");

  assert.equal(queries, 2);
  assert.equal(
    explorer.heapWindows.String?.error,
    "InvalidImage: identity mismatch");
  assert.equal(explorer.heapWindows.String?.data, null);
});
