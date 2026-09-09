import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserCloneCandidateDocument,
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
} from "../src/facades/inspect-web-analysis.d.ts";
import type {
  OperationFeatureEvent,
  OperationHandle,
  OperationSession,
} from "../src/operation-authority.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import {
  registerEngineWorkerCloneCandidateAdapter,
} from "../src/engine-worker-client.ts";
import {
  engineWorkerCloneCandidateInput,
  engineWorkerCloneCandidateValue,
  registerEngineWorkerCloneCandidateOperation,
  type EngineWorkerCloneCandidateAdapter,
  type EngineWorkerCloneCandidateFacade,
} from "../src/engine-worker-analysis.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";

const request: BrowserCloneCandidateRequest = {
  schemaVersion: 1,
  packages: [{
    packageId: "Example.Package",
    version: "1.0.0",
    targetFramework: "net11.0",
  }],
  selectedPackageIndex: 0,
  assembly: "lib/net11.0/Example.Package.dll",
  seed: {
    kind: "Member",
    typeDefinitionId: "Example.Widget",
    member: {
      stableSelector: "M:Example.Widget.Build",
      canonicalSignature: "System.Void Example.Widget.Build()",
      fingerprint: "digest",
      typeFullName: "Example.Widget",
      memberName: "Build",
    },
    body: {
      memberName: "Build",
      selectorKey: "M:Example.Widget.Build",
      metadataToken: 0x06000001,
    },
  },
  breadth: "Everything",
  discovery: "SimilarNames",
};

const emptyDocument: BrowserCloneCandidateDocument = {
  schemaVersion: 1,
  seed: {
    kind: "Member",
    type: null,
    member: request.seed.member,
  },
  breadth: request.breadth,
  discovery: request.discovery,
  nameSimilarityThreshold: 0.72,
  limits: {
    maximumResults: 100,
    maximumSeedMethods: 500,
    maximumCandidateMethods: 5_000,
    maximumParticipants: 12,
    maximumRetrievalPairs: 50_000,
    maximumRetrievalChunkMethods: 500,
    maximumNameCharacters: 256,
    maximumNameComparisonWork: 1_000_000,
    maximumNameCacheCells: 10_000,
    comparisonLimits: null,
  },
  scopeChangedDuringSearch: false,
  coverageIsComplete: true,
  rows: [],
  seeds: [],
  libraries: [],
  receipt: {
    seedMethods: 0,
    candidateMethods: 0,
    discoveredMethods: 0,
    admittedLibraries: 0,
    excludedLibraries: 0,
    nameComparisonWork: 0,
    retrievalPairs: 0,
    retrievalCalls: 0,
    rankedPairs: 0,
    suppressedPairs: 0,
    returnedPairs: 0,
    resultLimitReached: false,
  },
  resultLimitReached: false,
  resultLimitOmittedPairs: 0,
};

function result(
  kind: "Available" | "Rejected" | "Failed" | "Unrepresentable",
): BrowserCloneCandidateResult {
  return {
    schemaVersion: 1,
    request,
    kind,
    document: kind === "Available" ? emptyDocument : null,
    seedLibrary: null,
    openFailureKind: null,
    failure: kind === "Failed"
      ? {
          kind: "MetadataInspectionFailed",
          subject: null,
          detail: "Candidate production failed.",
        }
      : null,
    presentationRejectionKind: kind === "Unrepresentable"
      ? "ParticipantCoverageMissing"
      : null,
    subject: null,
    detail: kind === "Rejected" ? "No candidates were available." : null,
    metadataRootReason: null,
  };
}

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  return {
    promise: new Promise<T>(resolve => {
      resolvePromise = resolve;
    }),
    resolve: value => resolvePromise?.(value),
  };
}

interface Harness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly worker: FakeWorkerRuntime<string, string>;
  readonly host: WorkerRuntimeHost<string, string>;
  readonly adapter: EngineWorkerCloneCandidateAdapter;
}

function createHarness(facade: EngineWorkerCloneCandidateFacade): Harness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerCloneCandidateOperation(operations, () => facade);
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
    adapter: registerEngineWorkerCloneCandidateAdapter(host),
  };
}

async function startReady(harness: Harness): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

function startClone(
  adapter: EngineWorkerCloneCandidateAdapter,
): {
  readonly handle: OperationHandle<BrowserCloneCandidateResult, string>;
  readonly events:
    OperationFeatureEvent<BrowserCloneCandidateResult, string, never>[];
  readonly session: OperationSession<
    string,
    BrowserCloneCandidateResult,
    string,
    never,
    WorkerRuntimePreparationError
  >;
} {
  const events:
    OperationFeatureEvent<BrowserCloneCandidateResult, string, never>[] = [];
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "clone-operation" },
  });
  const session = page.createSession<
    string,
    BrowserCloneCandidateResult,
    string,
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
  const started = session.start(JSON.stringify(request), adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Clone Candidates to start.");
  return { handle: started.handle, events, session };
}

for (const kind of [
  "Available",
  "Rejected",
  "Failed",
  "Unrepresentable",
] as const) {
  test(`Clone Candidates Worker transports ${kind}`, async () => {
    const calls: string[] = [];
    const expected = result(kind);
    const harness = createHarness({
      queryCloneCandidates(requestJson) {
        calls.push(requestJson);
        return Promise.resolve(expected);
      },
    });
    await startReady(harness);

    const { handle } = startClone(harness.adapter);
    await harness.environment.flushAsync();

    assert.deepEqual(await handle.outcome, {
      kind: "succeeded",
      value: expected,
    });
    await handle.quiesced;
    assert.deepEqual(calls, [JSON.stringify(request)]);
    harness.host.dispose();
  });
}

test("Clone Candidates rejects malformed and oversized boundary payloads", () => {
  assert.equal(engineWorkerCloneCandidateInput.decode({
    requestJson: "{",
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateInput.decode({
    requestJson: "x".repeat(512 * 1024 + 1),
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateValue.decode({
    ...result("Rejected"),
    extra: true,
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateValue.decode({
    ...result("Rejected"),
    detail: "x".repeat(8 * 1024 * 1024 + 1),
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateInput.decode({
    requestJson: JSON.stringify({
      ...request,
      packages: Array.from({ length: 13 }, () => request.packages[0]),
    }),
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateValue.decode({
    ...result("Available"),
    document: {
      ...emptyDocument,
      seeds: Array.from({ length: 1_001 }, () => null),
    },
  }).kind, "rejected");
  assert.equal(engineWorkerCloneCandidateValue.decode({
    ...result("Available"),
    document: {
      ...emptyDocument,
      libraries: Array.from({ length: 257 }, () => null),
    },
  }).kind, "rejected");
});

test("Clone Candidates cancellation suppresses publication without claiming managed cancellation", async () => {
  const pending = deferred<BrowserCloneCandidateResult>();
  let managedCompleted = false;
  const harness = createHarness({
    async queryCloneCandidates() {
      const value = await pending.promise;
      managedCompleted = true;
      return value;
    },
  });
  await startReady(harness);

  const { handle, events } = startClone(harness.adapter);
  await harness.environment.flushAsync();
  assert.equal(handle.cancel("superseded").kind, "applied");
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });

  pending.resolve(result("Rejected"));
  await harness.environment.flushAsync();
  await handle.quiesced;
  assert.equal(managedCompleted, true);
  assert.deepEqual(events.map(event => event.kind), ["started", "canceled"]);
  assert.equal(events.some(event => event.kind === "terminal"), false);
  harness.host.dispose();
});

test("Clone Candidates session disposal suppresses a late managed result", async () => {
  const pending = deferred<BrowserCloneCandidateResult>();
  const harness = createHarness({
    queryCloneCandidates: () => pending.promise,
  });
  await startReady(harness);

  const { handle, events, session } = startClone(harness.adapter);
  await harness.environment.flushAsync();
  assert.equal(session.dispose().kind, "applied");
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "disposed",
  });

  pending.resolve(result("Available"));
  await harness.environment.flushAsync();
  await handle.quiesced;
  assert.deepEqual(events.map(event => event.kind), ["started", "disposed"]);
  assert.equal(events.some(event => event.kind === "terminal"), false);
  harness.host.dispose();
});
