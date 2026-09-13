import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserSource,
  BrowserTypeSourceResult,
} from "../src/facades/inspect-web-source.d.ts";
import type {
  OperationFeatureEvent,
  OperationHandle,
} from "../src/operation-authority.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import {
  bindTypeSourceFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerTypeSourceAdapter,
  type EngineWorkerTypeSourceAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerTypeSourceCancellationIsRunning,
  engineWorkerTypeSourceInput,
  engineWorkerTypeSourceKind,
  engineWorkerTypeSourceProgress,
  engineWorkerTypeSourceValue,
  mapEngineWorkerTypeSourceResult,
  registerEngineWorkerTypeSourceOperation,
  type EngineWorkerTypeSourceFailure,
  type EngineWorkerTypeSourceFacade,
} from "../src/engine-worker-source.ts";
import type { TypeSourceLoadRequest } from "../src/source-inspection.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";
import type {
  ManagedOperationSettlement,
} from "../src/worker-runtime-protocol.ts";

const source: BrowserSource = {
  provider: "decompiled",
  provenance: "dotnet-inspect fixture",
  url: null,
  pdbSourceLimitation: "PDB unavailable",
  text: "public sealed class Widget {}",
};

const request: TypeSourceLoadRequest = {
  packageId: "Example.Package",
  version: "1.2.3",
  framework: "net11.0",
  assembly: "Example.dll",
  type: "Example.Widget",
  taste: "[\"readable-locals\"]",
  signature: "page-only-signature",
  isVisible: () => true,
};

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>(resolve => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
  };
}

function ownData(value: unknown, name: string): unknown {
  if (typeof value !== "object" || value === null) return undefined;
  const property = Object.getOwnPropertyDescriptor(value, name);
  return property !== undefined && "value" in property
    ? property.value
    : undefined;
}

function succeeded(value: BrowserSource = source): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Succeeded",
    value,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function canceled(reason: string): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Canceled",
    value: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason,
  };
}

function failed(
  failureKind: "Expected" | "Unexpected",
  error: string,
  diagnostic: string,
): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Failed",
    value: null,
    failureKind,
    error,
    diagnostic,
    reason: null,
  };
}

interface Harness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly worker: FakeWorkerRuntime<string, string>;
  readonly host: WorkerRuntimeHost<string, string>;
  readonly adapter: EngineWorkerTypeSourceAdapter;
}

function createHarness(
  registerWorker: (operations: FakeWorkerOperationCatalog) => void,
): Harness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerWorker(operations);
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
  return {
    environment,
    worker,
    host,
    adapter: registerEngineWorkerTypeSourceAdapter(host),
  };
}

async function startReady(harness: Harness): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

function startSource(
  adapter: EngineWorkerTypeSourceAdapter,
): {
  readonly handle: OperationHandle<BrowserSource, EngineWorkerTypeSourceFailure>;
  readonly events: OperationFeatureEvent<
    BrowserSource,
    EngineWorkerTypeSourceFailure,
    never
  >[];
} {
  const events: OperationFeatureEvent<
    BrowserSource,
    EngineWorkerTypeSourceFailure,
    never
  >[] = [];
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "source-operation" },
  });
  const session = page.createSession<
    TypeSourceLoadRequest,
    BrowserSource,
    EngineWorkerTypeSourceFailure,
    never,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        events.push(event);
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const started = session.start(request, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Type Source to start.");
  return { handle: started.handle, events };
}

test("Type Source Worker adapter projects clone-safe input and returns source", async () => {
  const calls: unknown[][] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: (...args) => {
      calls.push(args);
      return Promise.resolve(succeeded());
    },
    cancelTypeSourceQuery: () => ({ kind: "NotActive", reason: null }),
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, { kind: "succeeded", value: source });
  await handle.quiesced;
  assert.deepEqual(calls, [[
    "source-operation",
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.taste,
  ]]);
  const start = harness.worker.receivedMessages.find(message =>
    ownData(message, "kind") === "start");
  assert.notEqual(start, undefined);
  const payload = ownData(start, "payload");
  assert.deepEqual(payload, {
    packageId: request.packageId,
    version: request.version,
    framework: request.framework,
    assembly: request.assembly,
    type: request.type,
    taste: request.taste,
  });
  assert.doesNotThrow(() => structuredClone(payload));

  harness.host.dispose();
});

test("Type Source binding preserves caller identity and expected diagnostics", async () => {
  const calls: unknown[][] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: (...args) => {
      calls.push(args);
      return Promise.resolve(failed(
        "Expected",
        "source unavailable",
        "restore the package before requesting source",
      ));
    },
    cancelTypeSourceQuery: () => ({ kind: "NotActive", reason: null }),
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);
  const binding = bindTypeSourceFacade(
    harness.adapter,
    () => undefined,
    createSharedEngineOperationAuthority(),
  );

  assert.deepEqual(await binding.queryTypeSource(
    "caller-source-operation",
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.taste,
  ), failed(
    "Expected",
    "source unavailable",
    "restore the package before requesting source",
  ));
  assert.equal(calls[0]?.[0], "caller-source-operation");

  binding.dispose();
  harness.host.dispose();
});

test("Type Source Worker operation forwards keyed cancellation", async () => {
  const result = deferred<BrowserTypeSourceResult>();
  const cancellations: unknown[][] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: () => result.promise,
    cancelTypeSourceQuery: (...args) => {
      cancellations.push(args);
      return { kind: "Requested", reason: args[1] };
    },
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();
  assert.equal(handle.cancel("superseded").kind, "applied");
  await harness.environment.flushAsync();
  assert.deepEqual(cancellations, [["source-operation", "superseded"]]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });

  result.resolve(canceled("superseded"));
  await Promise.resolve();
  await harness.environment.flushAsync();
  await handle.quiesced;
  harness.host.dispose();
});

test("Type Source managed terminal results map without losing failure kind", () => {
  assert.deepEqual(mapEngineWorkerTypeSourceResult(succeeded()), {
    kind: "succeeded",
    value: source,
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Expected",
    error: "source unavailable",
    diagnostic: "expected detail",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "expected",
    error: {
      failureKind: "Expected",
      error: "source unavailable",
      diagnostic: "expected detail",
    },
    diagnostic: "expected detail",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Unexpected",
    error: "source crashed",
    diagnostic: "stack",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "source crashed",
      diagnostic: "stack",
    },
    diagnostic: "stack",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult(canceled("timeout")), {
    kind: "canceled",
    reason: "timeout",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult(succeeded({
    ...source,
    provenance: "P".repeat(64 * 1024),
  })), {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Type Source exceeded Worker transport limits.",
      diagnostic: "Type Source metadata exceeds 65536 characters.",
    },
    diagnostic: "Type Source metadata exceeds 65536 characters.",
  });
});

test("Type Source managed result and cancellation validators reject drift", () => {
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...succeeded(),
      value: { ...source, text: undefined },
    }),
    /Expected Type Source text/,
  );
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...canceled("future-reason"),
    }),
    /cancellation reason is invalid/,
  );
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...succeeded(),
      future: true,
    }),
    /Expected a version 1 Type Source result/,
  );

  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "Requested", reason: "disposed" },
    "disposed",
  ), true);
  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "AlreadyRequested", reason: "user" },
    "superseded",
  ), true);
  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "NotActive", reason: null },
    "user",
  ), false);
  assert.throws(
    () => engineWorkerTypeSourceCancellationIsRunning(
      { kind: "Requested", reason: "user" },
      "timeout",
    ),
    /changed the requested reason/,
  );
});

test("Type Source codecs enforce request, result, and no-progress bounds", () => {
  const oversizedInput = engineWorkerTypeSourceInput.decode({
    packageId: request.packageId,
    version: request.version,
    framework: request.framework,
    assembly: request.assembly,
    type: "T".repeat(64 * 1024),
    taste: request.taste,
  });
  assert.equal(oversizedInput.kind, "rejected");
  if (oversizedInput.kind === "rejected")
    assert.equal(oversizedInput.reason, "oversized");
  assert.equal(engineWorkerTypeSourceInput.decode(request).kind, "rejected");

  const oversizedValue = engineWorkerTypeSourceValue.decode({
    ...source,
    provenance: "P".repeat(64 * 1024),
  });
  assert.equal(oversizedValue.kind, "rejected");
  if (oversizedValue.kind === "rejected")
    assert.equal(oversizedValue.reason, "oversized");

  const oversizedText = engineWorkerTypeSourceValue.decode({
    ...source,
    text: "S".repeat(32_000_001),
  });
  assert.equal(oversizedText.kind, "rejected");
  if (oversizedText.kind === "rejected")
    assert.equal(oversizedText.reason, "oversized");

  assert.deepEqual(engineWorkerTypeSourceProgress.decode("unexpected"), {
    kind: "rejected",
    reason: "invalid",
    message: "Type Source does not publish progress payloads.",
  });
});

test("Page adapter rejects a malformed Worker settlement as boundary failure", async () => {
  const harness = createHarness(operations => {
    operations.register({
      kind: engineWorkerTypeSourceKind,
      allowance: { kind: "unbounded" },
      input: engineWorkerTypeSourceInput,
      rejectInvalidPayload: failure => ({
        error: failure.message,
        diagnostic: failure.message,
      }),
      invoke: (): ManagedOperationSettlement<unknown, string, string> => ({
        kind: "succeeded",
        value: { ...source, text: 42 },
      }),
    });
  });
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: {
      failureKind: "Unexpected",
      error: "Worker protocol failed.",
      diagnostic: "Worker protocol failed.",
    },
  });
  harness.environment.advanceActive(21);
  await handle.quiesced;
  harness.host.dispose();
});

test("Worker adapter contains malformed generated results to one operation", async () => {
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: () => Promise.resolve({
      ...succeeded(),
      version: 2,
    }),
    cancelTypeSourceQuery: () => ({ kind: "NotActive", reason: null }),
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: {
      failureKind: "Unexpected",
      error: "Type Source returned invalid Worker boundary data.",
      diagnostic: "Expected a version 1 Type Source result.",
    },
  });
  await handle.quiesced;
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.host.dispose();
});
