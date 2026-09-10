import assert from "node:assert/strict";
import test from "node:test";
import { createOperationAuthorityPage, type OperationProducerAdapter } from "../src/operation-authority.ts";
import {
  registerEngineWorkerComparisonOperations,
  type EngineWorkerComparisonFacade,
} from "../src/engine-worker-comparison.ts";
import { bindProductionEngineClient } from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses, engineWorkerDiagnostic, engineWorkerPolicy, engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  FakeWorkerRuntime, ManualWorkerRuntimeEnvironment, QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";
import { decodeBoundMainToWorkerEnvelope } from "../src/worker-runtime-protocol.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";
import { engineOperationMaximumCharacters } from "../src/engine-worker-operations.ts";
import { registerEngineWorkerStartupOperations } from "../src/engine-worker-startup.ts";
import {
  createMethodBodyComparisonCoordinator, createMethodBodyDiffState, methodBodySelectionKey,
  type MethodBodyComparisonContext,
} from "../src/method-body-comparison.ts";
import { createSourceComparisonCoordinator, createSourceDiffState } from "../src/source-comparison.ts";
import { sourceComparison, sourceContext, sourceRequest, sourceResult } from "./source-comparison-fixture.ts";
import type {
  BrowserMethodBodyTargets, BrowserMethodBodyComparisonRequest,
  BrowserSourceComparison, BrowserSourceComparisonRequest,
  BrowserMethodBodyTargetsResult, BrowserMethodBodyComparisonResult, BrowserSourceComparisonResult,
} from "../src/facades/inspect-web-source.d.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

const context: MethodBodyComparisonContext = { ...sourceContext, label: "Build" };
const selection = {
  typeIdentity: context.typeIdentity, memberName: context.memberName, selectorKey: context.selectorKey,
  metadataToken: context.metadataToken, label: context.label,
};
const targets: BrowserMethodBodyTargets = {
  packageId: context.packageId, version: context.version, framework: context.framework,
  assembly: context.assembly, moduleVersionId: "test-module", before: selection, methods: [selection],
};
const request: BrowserMethodBodyComparisonRequest = {
  ...targets, before: selection, after: selection,
};
const results = {
  targets: { version: 1, kind: "Succeeded", value: targets,
    failureKind: null, error: null, diagnostic: null, reason: null },
  method: { version: 1, kind: "Succeeded",
    value: { request, stage: "Research", outcome: "Completed", producers: [], diagnostics: [] },
    failureKind: null, error: null, diagnostic: null, reason: null },
  source: sourceResult(),
} as const;
interface Results {
  targets: BrowserMethodBodyTargetsResult;
  method: BrowserMethodBodyComparisonResult;
  source: BrowserSourceComparisonResult;
}
type Result = Results[keyof Results];

class ComparisonEnvironment extends ManualWorkerRuntimeEnvironment {
  override async flushAsync(): Promise<void> {
    for (let turn = 0; turn < 10; turn++) await super.flushAsync();
  }
}

function fixture(overrides: Partial<EngineWorkerComparisonFacade> = {}) {
  const environment = new ComparisonEnvironment();
  const operations = new WorkerOperationCatalog();
  const calls: Array<{
    kind: keyof Results; id: string; args: unknown[];
    result: { resolve(value: Result): void };
  }> = [];
  const cancellations: Array<{ kind: "method" | "source"; id: string; reason: string }> = [];
  function query<K extends keyof Results>(kind: K, id: string, args: unknown[]) {
    const result = deferred<Results[K]>();
    calls.push({ kind, id, args, result });
    return result.promise;
  }
  const facade: EngineWorkerComparisonFacade = {
    queryMethodBodyComparisonTargets: (id, ...args) => query("targets", id, args),
    queryMethodBodyComparison: (id, ...args) => query("method", id, args),
    queryMemberSourceComparison: (id, ...args) => query("source", id, args),
    cancelMethodBodyComparison(id, reason) {
      cancellations.push({ kind: "method", id, reason });
      return { kind: "Requested", reason };
    },
    cancelMemberSourceComparison(id, reason) {
      cancellations.push({ kind: "source", id, reason });
      return { kind: "Requested", reason };
    },
    ...overrides,
  };
  registerEngineWorkerComparisonOperations(operations, () => facade);
  registerEngineWorkerStartupOperations(operations, {
    async buildIdentity() { return { version: "test", commit: null, builtAtUtc: null, commitUrl: null }; },
    async listVocabulary() { return { schema_version: 1, sections: [] }; },
    async listHomeDemos() { return { demos: [] }; },
    async listPackageQueryFacets() { return { facets: [] }; },
    async listGalleryDiscoveryCatalog() {
      return { packageType: { id: "type", label: "Type", summary: "", suggestions: [] }, orders: [] };
    },
  });
  const worker = new FakeWorkerRuntime({
    scheduler: environment, operations,
    bootstrap: { decoder: engineWorkerText, bootstrap: () => undefined },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({ error: kind, diagnostic: kind }),
    producerClasses: createEngineWorkerProducerClasses(),
  });
  const released: number[] = [];
  const diagnostics: unknown[] = [];
  const host = new WorkerRuntimeHost({
    ...engineWorkerPolicy, drainBudgetMilliseconds: 20,
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: environment, lifecycle: environment,
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText, createDiagnostic: (_kind, detail) => engineWorkerDiagnostic(detail),
    producerClasses: createEngineWorkerProducerClasses(),
    callbacks: {
      failure: () => undefined, diagnostic: () => undefined,
      realmReleased: epoch => { released.push(epoch); },
    },
  });
  let sequence = 0;
  const authority = createOperationAuthorityPage({
    allocation: { createId: () => `feature-${++sequence}` },
  });
  const reportOperationDiagnostic = (diagnostic: unknown): undefined => { diagnostics.push(diagnostic); };
  assert.equal(host.start("").kind, "started");
  const client = bindProductionEngineClient(host, reportOperationDiagnostic, authority);
  return { host, worker, environment, calls, cancellations, released, authority, client, diagnostics,
    reportOperationDiagnostic };
}

function start<I, V, E>(h: ReturnType<typeof fixture>, adapter: OperationProducerAdapter<I, V, string, never, E>, input: I) {
  const session = h.authority.createSession<I, V, string, never, E>({
    feature: { publish: () => undefined }, diagnostic: { report: h.reportOperationDiagnostic },
  });
  const started = session.start(input, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started") throw new Error("Comparison must start.");
  return started.handle;
}

function startAll(h: ReturnType<typeof fixture>) {
  return [
    start(h, h.client.source.methodBodyTargetsAdapter, context),
    start(h, h.client.source.methodBodyComparisonAdapter, request),
    start(h, h.client.source.memberSourceComparisonAdapter, sourceRequest),
  ];
}

test("all comparison page, Worker and generated admission IDs match; cancellation is exact and quiescence waits", async () => {
  const h = fixture();
  await h.environment.flushAsync();
  await h.client.ready;
  const handles = startAll(h);
  await h.environment.flushAsync();
  assert.equal(h.calls.length, 3);
  for (const [index, call] of h.calls.entries()) {
    assert.equal(call.id, handles[index]!.id);
    const message = h.worker.receivedMessages.map(value => {
      const decoded = decodeBoundMainToWorkerEnvelope(value, 1);
      if (decoded.kind === "failure") assert.fail("Malformed Worker request.");
      return decoded.value;
    }).find(value =>
      value.kind === "start" && value.operation.operationId === call.id);
    assert.ok(message?.kind === "start");
    assert.equal(message.operation.operationId, call.id);
    assert.ok(typeof message.payload === "string");
    assert.doesNotMatch(message.payload, /feature-\d+/);
    let quiesced = false;
    void handles[index]!.quiesced.then(() => { quiesced = true; return undefined; });
    handles[index]!.cancel("superseded");
    handles[index]!.cancel("user");
    await h.environment.flushAsync();
    assert.deepEqual(await handles[index]!.outcome, { kind: "canceled", reason: "superseded" });
    assert.equal(quiesced, false);
    call.result.resolve(results[call.kind]);
    await h.environment.flushAsync();
    await handles[index]!.quiesced;
    assert.equal(quiesced, true);
  }
  assert.deepEqual(h.calls[0]!.args, [
    context.packageId, context.version, context.framework, context.assembly,
    context.typeIdentity, context.memberName, context.selectorKey, context.metadataToken,
  ]);
  assert.ok(typeof h.calls[1]!.args[0] === "string");
  assert.ok(typeof h.calls[2]!.args[0] === "string");
  assert.deepEqual(JSON.parse(h.calls[1]!.args[0]), request);
  assert.deepEqual(JSON.parse(h.calls[2]!.args[0]), sourceRequest);
  assert.deepEqual(h.cancellations, h.calls.map(call => ({
    kind: call.kind === "source" ? "source" : "method", id: call.id, reason: "superseded",
  })));
  h.host.dispose();
});

test("all three comparison adapters preserve success, expected/unexpected failure and cancellation envelopes", async () => {
  for (const terminal of ["Succeeded", "Expected", "Unexpected", "Canceled"] as const) {
    const h = fixture();
    await h.environment.flushAsync();
    const handles = startAll(h);
    await h.environment.flushAsync();
    for (const call of h.calls) {
      const result: Result = terminal === "Succeeded" ? results[call.kind] : {
        version: 1, kind: terminal === "Canceled" ? "Canceled" : "Failed", value: null,
        failureKind: terminal === "Canceled" ? null : terminal,
        error: terminal === "Canceled" ? null : "comparison failed",
        diagnostic: terminal === "Canceled" ? null : "comparison diagnostic",
        reason: terminal === "Canceled" ? "timeout" : null,
      };
      call.result.resolve(result);
    }
    await h.environment.flushAsync();
    for (const [index, handle] of handles.entries()) {
      assert.deepEqual(await handle.outcome, terminal === "Succeeded"
        ? { kind: "succeeded", value: results[h.calls[index]!.kind].value }
        : terminal === "Canceled" ? { kind: "canceled", reason: "timeout" }
          : { kind: "failed", error: "comparison failed" });
      await handle.quiesced;
    }
    assert.equal(h.diagnostics.length, terminal === "Unexpected" ? 3 : 0);
    h.host.dispose();
  }
});

for (const physicalRelease of ["managed settlement", "drain expiry"] as const) {
  test(`Worker loss fails all comparisons before quiescence; release requires ${physicalRelease}`, async () => {
    const h = fixture();
    await h.environment.flushAsync();
    const handles = startAll(h);
    await h.environment.flushAsync();
    let quiesced = 0;
    for (const handle of handles) void handle.quiesced.then(() => { quiesced++; return undefined; });
    h.worker.emitError("lost");
    await h.environment.flushAsync();
    for (const handle of handles) assert.equal((await handle.outcome).kind, "failed");
    assert.equal(h.host.snapshot().phase, "draining");
    assert.equal(quiesced, 0);
    assert.deepEqual(h.released, []);
    if (physicalRelease === "managed settlement") {
      for (const call of h.calls) call.result.resolve(results[call.kind]);
      await h.environment.flushAsync();
    } else {
      h.environment.advanceActive(19);
      assert.equal(quiesced, 0);
      h.environment.advanceActive(2);
    }
    await Promise.all(handles.map(handle => handle.quiesced));
    assert.equal(quiesced, 3);
    assert.deepEqual(h.released, [1]);
    h.host.dispose();
  });
}

test("injected comparison coordinators retain submitted requests, supersession and Worker-loss draining", async () => {
  const h = fixture();
  await h.environment.flushAsync();
  const methodState = createMethodBodyDiffState();
  const sourceState = createSourceDiffState();
  const common = {
    operationAuthority: h.authority, reportOperationDiagnostic: h.reportOperationDiagnostic,
    describeError: String, render: () => undefined,
  };
  const method = createMethodBodyComparisonCoordinator({
    ...common, state: methodState, targetsAdapter: h.client.source.methodBodyTargetsAdapter,
    comparisonAdapter: h.client.source.methodBodyComparisonAdapter,
  });
  const source = createSourceComparisonCoordinator({
    ...common, state: sourceState, comparisonAdapter: h.client.source.memberSourceComparisonAdapter,
  });
  const opened = method.open(context, "#method");
  await h.environment.flushAsync();
  h.calls[0]!.result.resolve(results.targets);
  await h.environment.flushAsync();
  await opened;
  method.selectCandidate(methodBodySelectionKey(selection));
  let methodDone = false;
  const compared = method.compare().then(() => { methodDone = true; return undefined; });
  source.open(sourceContext, "#source");
  source.setAfterVersion("2.0.0");
  let oldDone = false;
  const old = source.compare().then(() => { oldDone = true; return undefined; });
  await h.environment.flushAsync();
  source.setAfterVersion("3.0.0");
  const current = source.compare();
  await h.environment.flushAsync();
  assert.equal(oldDone, false);
  assert.equal(sourceState.submittedRequest?.afterVersion, "3.0.0");
  assert.deepEqual(methodState.submittedRequest?.after, selection);
  assert.equal(h.cancellations[0]!.id, h.calls[2]!.id);
  h.worker.emitError("lost");
  await h.environment.flushAsync();
  assert.match(methodState.comparisonError, /Worker/);
  assert.match(sourceState.error, /Worker/);
  assert.equal(methodDone, false);
  assert.equal(oldDone, false);
  h.environment.advanceActive(21);
  await Promise.all([compared, old, current]);
  assert.equal(methodDone, true);
  assert.equal(oldDone, true);
  assert.equal(methodState.comparison, null);
  assert.equal(sourceState.comparison, null);
  h.host.dispose();
});

test("bounded comparison transport rejects getters without invoking them and malformed envelopes fail visibly", async () => {
  const h = fixture();
  await h.environment.flushAsync();
  let invoked = false;
  const invalid = Object.defineProperty({ ...sourceRequest }, "packageId", {
    enumerable: true, get() { invoked = true; return "P"; },
  });
  const session = h.authority.createSession<
    BrowserSourceComparisonRequest, BrowserSourceComparison, string, never, WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined }, diagnostic: { report: h.reportOperationDiagnostic },
  });
  assert.equal(session.start(invalid, h.client.source.memberSourceComparisonAdapter).kind, "rejected");
  assert.equal(session.start({
    ...sourceRequest, packageId: "x".repeat(engineOperationMaximumCharacters + 1),
  }, h.client.source.memberSourceComparisonAdapter).kind, "rejected");
  assert.equal(invoked, false);
  const invalidResults: BrowserSourceComparisonResult[] = [
    { ...results.source, version: 2 },
    { ...results.source, kind: 42 },
    { ...results.source, value: null },
    { ...results.source, reason: "user" },
    { ...results.source, kind: "Failed", value: null, failureKind: "Expected",
      error: "x".repeat(4_097), diagnostic: "too much text" },
    { ...results.source, value: sourceComparison({ failure: "x".repeat(engineOperationMaximumCharacters + 1) }) },
    Object.defineProperty({ ...results.source }, "value", {
      enumerable: true, get() { invoked = true; return results.source.value; },
    }),
  ];
  for (const result of invalidResults) {
    const handle = start(h, h.client.source.memberSourceComparisonAdapter, sourceRequest);
    await h.environment.flushAsync();
    h.calls.at(-1)!.result.resolve(result);
    await h.environment.flushAsync();
    assert.equal((await handle.outcome).kind, "failed");
    await handle.quiesced;
    assert.equal(h.host.snapshot().phase, "ready");
  }
  assert.equal(invoked, false);
  h.host.dispose();
});

test("a malformed exact-cancellation acknowledgment fails visibly without early physical release", async () => {
  const h = fixture({
    cancelMemberSourceComparison: () => ({ kind: "Requested", reason: "not-a-reason" }),
  });
  await h.environment.flushAsync();
  const handle = start(h, h.client.source.memberSourceComparisonAdapter, sourceRequest);
  await h.environment.flushAsync();
  let quiesced = false;
  void handle.quiesced.then(() => { quiesced = true; return undefined; });
  handle.cancel("user");
  await h.environment.flushAsync();
  assert.equal(h.host.snapshot().phase, "draining");
  assert.equal(quiesced, false);
  h.environment.advanceActive(21);
  await handle.quiesced;
  assert.equal(quiesced, true);
  h.host.dispose();
});
