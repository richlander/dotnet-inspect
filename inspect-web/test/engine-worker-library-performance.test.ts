import assert from "node:assert/strict";
import test from "node:test";

import {
  bindLibraryPerformanceFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerLibraryPerformanceAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerLibraryPerformanceCancellationIsRunning,
  engineWorkerLibraryPerformanceDurableEvent,
  engineWorkerLibraryPerformanceInput,
  mapEngineWorkerLibraryPerformanceResult,
  registerEngineWorkerLibraryPerformanceOperation,
  type EngineWorkerLibraryPerformanceFacade,
  type LibraryPerformanceLoadRequest,
} from "../src/engine-worker-library-performance.ts";
import type { BrowserPerformanceAnalysisResult } from "../src/facades/inspect-web-analysis.d.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { member, summary } from "./library-performance-fixture.ts";

const request: LibraryPerformanceLoadRequest = {
  packageId: "Example.Library",
  version: "1.0.0",
  targetFramework: "net8.0",
  assemblyName: "Example.Library.dll",
};

function succeeded(): BrowserPerformanceAnalysisResult {
  return {
    version: 1,
    kind: "Succeeded",
    summary: summary(),
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

test("Library Performance request is closed, paired, and exact", () => {
  assert.equal(
    engineWorkerLibraryPerformanceInput.decode(request).kind, "decoded");
  assert.equal(
    engineWorkerLibraryPerformanceInput.decode({
      ...request,
      extra: true,
    }).kind,
    "rejected");
  assert.equal(
    engineWorkerLibraryPerformanceInput.decode({
      ...request,
      packageId: 7,
    }).kind,
    "rejected");
  const oversized = engineWorkerLibraryPerformanceInput.decode({
    ...request,
    targetFramework: "n".repeat(300),
  });
  assert.equal(oversized.kind, "rejected");
  if (oversized.kind === "rejected")
    assert.equal(oversized.reason, "oversized");
});

test("Library Performance Item events decode a well-formed member and reject malformed payloads", () => {
  const decoded = engineWorkerLibraryPerformanceDurableEvent.decode({
    kind: "Item",
    item: member,
  });
  assert.equal(decoded.kind, "decoded");
  if (decoded.kind === "decoded") assert.deepEqual(decoded.value.item, member);

  assert.equal(
    engineWorkerLibraryPerformanceDurableEvent.decode({
      kind: "Progress",
      item: member,
    }).kind,
    "rejected");
  assert.equal(
    engineWorkerLibraryPerformanceDurableEvent.decode({
      kind: "Item",
      item: null,
    }).kind,
    "rejected");
  assert.equal(
    engineWorkerLibraryPerformanceDurableEvent.decode({
      kind: "Item",
      item: { ...member, bodyTargets: null },
      extra: true,
    }).kind,
    "rejected");
  assert.equal(
    engineWorkerLibraryPerformanceDurableEvent.decode({
      kind: "Item",
      item: { ...member, bodyTargets: null },
    }).kind,
    "decoded");
});

test("Library Performance result mapping is authoritative across every terminal kind", () => {
  const ok = mapEngineWorkerLibraryPerformanceResult(succeeded());
  assert.equal(ok.kind, "succeeded");
  if (ok.kind === "succeeded") assert.deepEqual(ok.value, summary());

  const failed = mapEngineWorkerLibraryPerformanceResult({
    version: 1,
    kind: "Failed",
    summary: null,
    failureKind: "Expected",
    error: "The package could not be located.",
    diagnostic: "not found",
    reason: null,
  });
  assert.equal(failed.kind, "failed");
  if (failed.kind === "failed") {
    assert.equal(failed.failureKind, "expected");
    assert.equal(failed.error.error, "The package could not be located.");
  }

  const canceled = mapEngineWorkerLibraryPerformanceResult({
    version: 1,
    kind: "Canceled",
    summary: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: "user",
  });
  assert.deepEqual(canceled, { kind: "canceled", reason: "user" });

  assert.equal(
    mapEngineWorkerLibraryPerformanceResult({
      ...succeeded(),
      version: 2,
    }).kind,
    "failed");
  assert.equal(
    mapEngineWorkerLibraryPerformanceResult({
      ...succeeded(),
      summary: null,
    }).kind,
    "failed");
});

test("Library Performance cancellation preserves the first managed reason", () => {
  assert.equal(
    engineWorkerLibraryPerformanceCancellationIsRunning(
      { kind: "Requested", reason: "superseded" },
      "superseded"),
    true);
  assert.equal(
    engineWorkerLibraryPerformanceCancellationIsRunning(
      { kind: "NotActive", reason: null },
      "user"),
    false);
  assert.throws(() =>
    engineWorkerLibraryPerformanceCancellationIsRunning(
      { kind: "Requested", reason: "user" },
      "superseded"));
});

test("Library Performance publishes Item events in order before settlement", async () => {
  const published: unknown[] = [];
  const facade: EngineWorkerLibraryPerformanceFacade = {
    async queryPackagePerformanceStreaming(
      _operationId,
      packageId,
      version,
      targetFramework,
      assemblyName,
      eventSink,
    ) {
      assert.deepEqual(
        { packageId, version, targetFramework, assemblyName },
        request);
      if ((typeof eventSink !== "object" && typeof eventSink !== "function")
        || eventSink === null) {
        throw new TypeError("Test event sink is unavailable.");
      }
      assert.equal(Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Item",
        item: member,
      })), true);
      return succeeded();
    },
    cancelLibraryPerformanceAnalysis() {
      return { kind: "NotActive", reason: null };
    },
  };
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerLibraryPerformanceOperation(operations, () => facade);
  const worker = new FakeWorkerRuntime<string, string>({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      bootstrap: () => undefined,
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({
      error: `Unknown operation: ${kind}`,
      diagnostic: `Unknown operation: ${kind}`,
    }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  const host = new WorkerRuntimeHost<string, string>({
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: engineWorkerText.decode,
      diagnostic: engineWorkerText,
    },
    diagnostic: engineWorkerText,
    callbacks: {
      failure: () => undefined,
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) =>
      `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    idleHeartbeatIntervalMilliseconds:
      engineWorkerPolicy.idleHeartbeatIntervalMilliseconds,
    schedulingToleranceMilliseconds:
      engineWorkerPolicy.schedulingToleranceMilliseconds,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  assert.equal(host.start("").kind, "started");
  await environment.flushAsync();
  const binding = bindLibraryPerformanceFacade(
    registerEngineWorkerLibraryPerformanceAdapter(host),
    () => undefined,
    createSharedEngineOperationAuthority(),
  );
  const eventSink = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      published.push(JSON.parse(String(value)));
    },
  });

  const pending = binding.queryPackagePerformanceStreaming(
    "library-performance-1",
    request.packageId,
    request.version,
    request.targetFramework,
    request.assemblyName,
    eventSink,
  );
  await environment.flushAsync();
  const result = await pending;

  assert.equal(result.kind, "Succeeded");
  assert.deepEqual(published, [{ kind: "Item", item: member }]);
  assert.deepEqual(result.summary, summary());

  binding.dispose();
  host.dispose();
});
