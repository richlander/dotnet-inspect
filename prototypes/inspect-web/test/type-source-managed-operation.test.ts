import assert from "node:assert/strict";
import test from "node:test";
import {
  createSourceInspectionCoordinator,
  type SourceInspectionState,
  type TypeSourceLoadRequest,
} from "../src/source-inspection.ts";
import {
  createOperationAuthorityPage,
  type OperationCancelReason,
  type OperationDiagnostic,
  type OperationId,
} from "../src/operation-authority.ts";
import type { BrowserTypeSourceResult } from "../src/facades/inspect-web-source.d.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
}

function fixture() {
  const state: SourceInspectionState = {
    settings: false, explorer: null, loading: false, error: "", home: false,
    package: {}, atPackageRoot: false, lens: "source", selectedMemberKey: "",
    memberSection: "overview", sourceRequestGeneration: 0,
    memberSource: null, memberSourceLoading: false, memberSourceError: "",
    memberSourceKey: "", typeSource: null, typeSourceLoading: false,
    typeSourceError: "", typeSourceKey: "", graphSourceOpen: false,
    graphSource: null, graphSourceLoading: false, graphSourceError: "",
    graphSourceTitle: "", graphSourceRequest: null, graphSourceSeq: 0, taste: [],
  };
  const queries = new Map<OperationId, ReturnType<typeof deferred<BrowserTypeSourceResult>>>();
  const cancellations: Array<readonly [OperationId, OperationCancelReason]> = [];
  const diagnostics: OperationDiagnostic[] = [];
  let nextId = 1;
  let legacyCancellations = 0;
  const coordinator = createSourceInspectionCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => `managed-source-${nextId++}` },
    }),
    queryTypeSource: (id, request) => {
      assert.equal(request.packageId, "Example");
      const query = deferred<BrowserTypeSourceResult>();
      queries.set(id, query);
      return query.promise;
    },
    cancelTypeSourceRequest: (id, reason) => { cancellations.push([id, reason]); },
    queryMemberSource: async () => { throw new Error("unused member query"); },
    queryGraphSource: async () => ({
      provider: "pdb", provenance: "verified", url: null,
      pdbSourceLimitation: null, text: "graph",
    }),
    memberSourceHasConcreteOverload: () => false,
    cancelEngineSourceRequest: () => { legacyCancellations++; },
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic);
      return undefined;
    },
    describeError: error => error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: () => ({
      selector: "#type-list", dataTarget: null, selection: null,
      navigationScope: null, navigationSelection: null, navigationScrollTop: null,
      focusLost: false,
    }),
  });
  function start(signature: string) {
    const request: TypeSourceLoadRequest = {
      signature, packageId: "Example", version: "1.0.0", framework: "net11.0",
      assembly: "Example.dll", type: signature, taste: "[]", isVisible: () => true,
    };
    const load = coordinator.loadTypeSource(request);
    const entry = [...queries.entries()].at(-1);
    assert.ok(entry);
    return { load, id: entry[0], query: entry[1] };
  }
  return { state, start, coordinator, cancellations, diagnostics,
    legacyCancellations: () => legacyCancellations };
}

function succeeded(text: string): BrowserTypeSourceResult {
  return {
    version: 1, kind: "Succeeded",
    value: { provider: "pdb", provenance: "verified", url: null,
      pdbSourceLimitation: null, text },
    failureKind: null, error: null, diagnostic: null, reason: null,
  };
}

function failed(kind: "Expected" | "Unexpected"): BrowserTypeSourceResult {
  return {
    version: 1, kind: "Failed", value: null, failureKind: kind,
    error: "source unavailable", diagnostic: "producer detail", reason: null,
  };
}

test("type requests forward page identity and exact reason without legacy cancellation", async () => {
  const f = fixture();
  const a = f.start("A");
  const b = f.start("B");
  assert.notEqual(a.id, b.id);
  assert.deepEqual(f.cancellations, [[a.id, "superseded"]]);
  a.query.resolve(succeeded("stale"));
  await a.load;
  assert.equal(f.state.typeSource, null);
  assert.equal(f.state.typeSourceLoading, true);
  assert.equal(f.coordinator.cancelCurrentRequest(), true);
  assert.equal(f.coordinator.cancelCurrentRequest(), false);
  assert.deepEqual(f.cancellations, [[a.id, "superseded"], [b.id, "user"]]);
  assert.equal(f.legacyCancellations(), 0);
  let quiesced = false;
  void b.load.then(() => {
    quiesced = true;
    return undefined;
  });
  await Promise.resolve();
  assert.equal(quiesced, false);
  b.query.resolve({ version: 1, kind: "Canceled", reason: "user",
    value: null, failureKind: null, error: null, diagnostic: null });
  await b.load;
  assert.equal(quiesced, true);
  assert.equal(f.state.typeSource, null);
  assert.equal(f.state.typeSourceError, "");
});

for (const kind of ["Expected", "Unexpected"] as const) {
  test(`${kind} managed failure remains visible when current`, async () => {
    const f = fixture();
    const operation = f.start("A");
    operation.query.resolve(failed(kind));
    await operation.load;
    assert.equal(f.state.typeSourceError, "source unavailable");
    assert.equal(f.state.typeSourceLoading, false);
    assert.equal(f.diagnostics.length, kind === "Unexpected" ? 1 : 0);
    if (kind === "Unexpected")
      assert.equal(f.diagnostics[0]?.error, "producer detail");
  });

  test(`${kind} stale failure cannot overwrite replacement or hide unexpected diagnostics`, async () => {
    const f = fixture();
    const a = f.start("A");
    const b = f.start("B");
    a.query.resolve(failed(kind));
    await a.load;
    assert.equal(f.state.typeSourceError, "");
    assert.equal(f.state.typeSourceKey, "B");
    assert.equal(f.diagnostics.length, kind === "Unexpected" ? 1 : 0);
    b.query.resolve(succeeded("current"));
    await b.load;
    assert.equal(f.state.typeSource?.text, "current");
  });
}

for (const canceled of [false, true]) {
  test(`Promise rejection is a visible boundary diagnostic even after cancellation=${canceled}`, async () => {
    const f = fixture();
    const operation = f.start("A");
    if (canceled) f.coordinator.cancelCurrentRequest();
    const error = new Error("OperationCanceledException: canceled");
    operation.query.reject(error);
    await operation.load;
    assert.equal(f.diagnostics.length, 1);
    assert.equal(f.diagnostics[0]?.operationId, operation.id);
    assert.equal(f.diagnostics[0]?.error, error);
    assert.equal(f.state.typeSourceError, canceled ? "" : error.message);
    assert.equal(f.state.typeSourceLoading, false);
  });
}

test("late Promise rejection cannot affect a successful replacement", async () => {
  const f = fixture();
  const a = f.start("A");
  const b = f.start("B");
  b.query.resolve(succeeded("B"));
  await b.load;
  a.query.reject(new Error("interop failed"));
  await a.load;
  assert.equal(f.state.typeSource?.text, "B");
  assert.equal(f.state.typeSourceError, "");
  assert.equal(f.diagnostics.length, 1);
  assert.equal(f.diagnostics[0]?.operationId, a.id);
});

test("managed cancellation reaches the logical authority without an error", async () => {
  const f = fixture();
  const operation = f.start("A");
  operation.query.resolve({ version: 1, kind: "Canceled", reason: "superseded",
    value: null, failureKind: null, error: null, diagnostic: null });
  await operation.load;
  assert.equal(f.state.typeSourceError, "");
  assert.equal(f.state.typeSourceKey, "");
  assert.equal(f.state.typeSourceLoading, false);
  assert.equal(f.diagnostics.length, 0);
});

test("legacy graph takeover preserves graph output and keyed type cancellation", async () => {
  const f = fixture();
  const type = f.start("A");
  await f.coordinator.openGraphSource({
    packageId: "Example", version: "1.0.0", framework: "net11.0",
    assembly: "Example.dll", type: "Example.Type", member: "Build",
    selectorKey: "method", metadataToken: 42,
  }, "Example.Type.Build");
  assert.deepEqual(f.cancellations, [[type.id, "superseded"]]);
  assert.equal(f.state.graphSource?.text, "graph");
  type.query.reject(new Error("late type boundary failure"));
  await type.load;
  assert.equal(f.state.graphSource?.text, "graph");
  assert.equal(f.state.graphSourceError, "");
  assert.equal(f.state.typeSource, null);
  assert.equal(f.diagnostics.length, 1);
  assert.equal(f.legacyCancellations(), 0);
});

test("malformed terminal payload is a boundary failure, not an empty success", async () => {
  const f = fixture();
  const operation = f.start("A");
  operation.query.resolve({ ...succeeded("A"), value: null });
  await operation.load;
  assert.equal(f.state.typeSource, null);
  assert.match(f.state.typeSourceError, /has no source/);
  assert.equal(f.diagnostics.length, 1);
});
