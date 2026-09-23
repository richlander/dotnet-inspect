import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserTypeExplorerInspection,
  BrowserTypeExplorerRequest,
  BrowserTypeExplorerResult,
} from "../src/facades/inspect-web-source.d.ts";
import {
  bindTypeExplorerFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerTypeExplorerAdapter,
  type EngineWorkerTypeExplorerAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerTypeExplorerCancellationIsRunning,
  engineWorkerTypeExplorerInput,
  engineWorkerTypeExplorerRequest,
  mapEngineWorkerTypeExplorerResult,
  registerEngineWorkerTypeExplorerOperation,
  type EngineWorkerTypeExplorerFacade,
} from "../src/engine-worker-type-explorer.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { inertStringFixture } from "./inert-string-fixture.ts";

const projection = {
  bodyMode: "Bodies",
  selectedDeclarationId: null,
  documentRevision: null,
  placement: "All",
  accessibilities: [
    "Unknown",
    "Private",
    "PrivateProtected",
    "Protected",
    "Internal",
    "ProtectedInternal",
    "Public",
  ],
  includeGenerated: false,
  includeDocumentation: true,
  includeAttributes: true,
} as const satisfies BrowserTypeExplorerRequest;

const inspection = {
  outcome: "Available",
  reason: null,
  bodyProjectionsAttempted: 0,
  failedBodyIds: [],
  document: null,
  share: {
    kind: "nonProjectable",
    fullUrl: null,
    packet: null,
    path: "type-explorer/share",
    reason: inertStringFixture(
      "No portable Workspace Share representation."),
  },
  diagnostics: [],
} as const satisfies BrowserTypeExplorerInspection;

function result(
  value: Partial<BrowserTypeExplorerResult>,
): BrowserTypeExplorerResult {
  return {
    version: 1,
    kind: "Succeeded",
    value: inspection,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    ...value,
  };
}

test("Type Explorer Worker decodes bounded complete requests", () => {
  assert.deepEqual(
    engineWorkerTypeExplorerRequest.decode(projection),
    { kind: "decoded", value: projection });
  const input = {
    packageId: "System.Text.Json",
    version: "11.0.0",
    framework: "net11.0",
    assembly: "System.Text.Json.dll",
    type: "System.Text.Json.JsonNamingPolicy",
    taste: "[]",
    projection,
  };
  assert.deepEqual(
    engineWorkerTypeExplorerInput.decode(input),
    { kind: "decoded", value: input });
});

test("Type Explorer Worker rejects malformed selection currency and duplicate access", () => {
  const malformed = engineWorkerTypeExplorerRequest.decode({
    ...projection,
    selectedDeclarationId: 1,
    documentRevision: null,
  });
  assert.equal(malformed.kind, "rejected");

  const staleRevision = engineWorkerTypeExplorerRequest.decode({
    ...projection,
    selectedDeclarationId: 1,
    documentRevision: "stale",
  });
  assert.equal(staleRevision.kind, "rejected");

  const duplicate = engineWorkerTypeExplorerRequest.decode({
    ...projection,
    accessibilities: ["Public", "Public"],
  });
  assert.equal(duplicate.kind, "rejected");
});

test("Type Explorer Worker maps managed terminal settlements", () => {
  assert.deepEqual(
    mapEngineWorkerTypeExplorerResult(result({})),
    { kind: "succeeded", value: inspection });
  assert.deepEqual(
    mapEngineWorkerTypeExplorerResult(result({
      kind: "Failed",
      value: null,
      failureKind: "Expected",
      error: "Document unavailable.",
      diagnostic: "No exact Type document.",
    })),
    {
      kind: "failed",
      failureKind: "expected",
      error: {
        failureKind: "Expected",
        error: "Document unavailable.",
        diagnostic: "No exact Type document.",
      },
      diagnostic: "No exact Type document.",
    });
  assert.deepEqual(
    mapEngineWorkerTypeExplorerResult(result({
      kind: "Canceled",
      value: null,
      reason: "superseded",
    })),
    { kind: "canceled", reason: "superseded" });
});

test("Type Explorer Worker validates keyed cancellation results", () => {
  assert.equal(
    engineWorkerTypeExplorerCancellationIsRunning(
      { kind: "Requested", reason: "superseded" },
      "superseded"),
    true);
  assert.equal(
    engineWorkerTypeExplorerCancellationIsRunning(
      { kind: "NotActive", reason: null },
      "disposed"),
    false);
  assert.throws(
    () => engineWorkerTypeExplorerCancellationIsRunning(
      { kind: "Requested", reason: "disposed" },
      "superseded"),
    /changed the requested reason/u);
});

test("Type Explorer Worker binding settles through the runtime", async () => {
  const facade: EngineWorkerTypeExplorerFacade = {
    queryTypeExplorer: () => Promise.resolve(result({})),
    cancelTypeExplorerQuery: () => ({
      kind: "NotActive",
      reason: null,
    }),
  };
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerTypeExplorerOperation(operations, () => facade);
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
  const adapter: EngineWorkerTypeExplorerAdapter =
    registerEngineWorkerTypeExplorerAdapter(host);
  const binding = bindTypeExplorerFacade(
    adapter,
    () => undefined,
    createSharedEngineOperationAuthority());

  const settlement = binding.queryTypeExplorer(
    "type-explorer-operation",
    "System.Text.Json",
    "11.0.0",
    "net11.0",
    "System.Text.Json.dll",
    "System.Text.Json.JsonNamingPolicy",
    "[]",
    projection);
  await environment.flushAsync();
  assert.deepEqual(await settlement, result({}));

  binding.dispose();
  host.dispose();
});
