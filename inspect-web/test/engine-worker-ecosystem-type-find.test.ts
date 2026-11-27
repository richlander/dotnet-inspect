import assert from "node:assert/strict";
import test from "node:test";

import {
  bindEcosystemTypeFindFacade,
  createSharedEngineOperationAuthority,
  registerEngineWorkerEcosystemTypeFindAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  createEngineWorkerEcosystemTypeFindHostRegistration,
  engineWorkerEcosystemTypeFindCancellationIsRunning,
  engineWorkerEcosystemTypeFindInput,
  mapEngineWorkerEcosystemTypeFindResult,
  registerEngineWorkerEcosystemTypeFindOperation,
  type EcosystemTypeFindEvent,
  type EngineWorkerEcosystemTypeFindFacade,
} from "../src/engine-worker-ecosystem-type-find.ts";
import type {
  BrowserEcosystemTypeFindFinding,
  BrowserEcosystemTypeFindInspection,
  BrowserEcosystemTypeFindRequest,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";

const request: BrowserEcosystemTypeFindRequest = {
  text: "JsonSerializer",
  ecosystems: ["dotnet"],
  prefixDemand: ["dotnet"],
  rowStart: 2,
  rowEnd: 4,
  maximumPrefixPackages: 25,
  targetFramework: "net11.0",
};

const finding: BrowserEcosystemTypeFindFinding = {
  identity: "package-type:System.Text.Json:JsonSerializer",
  scope: "Ecosystem",
  memberships: [{
    ecosystem: "dotnet",
    basis: "CorePackage",
    platformFamily: null,
    packageId: "System.Text.Json",
    packageVersion: "11.0.0",
    packageFramework: "net11.0",
    packageRuntimeIdentifier: null,
    packagePrefix: null,
  }],
  pattern: {
    ordinal: 0,
    text: "JsonSerializer",
    kind: "Ordinary",
    exactNamespaceFallback: null,
    namespace: null,
    namespaceMatch: null,
  },
  effectivePattern: "JsonSerializer",
  matchKind: "Exact",
  similarity: 1,
  name: "JsonSerializer",
  namespace: "System.Text.Json",
  fullName: "System.Text.Json.JsonSerializer",
  source: {
    kind: "Package",
    label: "System.Text.Json@11.0.0",
    packageId: "System.Text.Json",
    packageVersion: "11.0.0",
    platformFamily: null,
    library: {
      name: "System.Text.Json",
      version: "11.0.0.0",
      culture: null,
      publicKeyToken: "cc7b13ffcd2ddd51",
    },
    provenance: {
      kind: "Package",
      packageId: "System.Text.Json",
      packageVersion: "11.0.0",
      targetFramework: "net11.0",
      runtimeIdentifier: null,
      assetPath: "lib/net11.0/System.Text.Json.dll",
      framework: null,
      frameworkVersion: null,
      project: null,
      resolverSource: null,
      contentReference: null,
      digest: null,
      declaredName: null,
    },
    requestedTargetFramework: "net11.0",
    requestedRuntimeIdentifier: null,
    contextOrder: 0,
    memberOrder: 0,
  },
  moduleVersionId: "11111111-1111-1111-1111-111111111111",
  declarationOrder: 10,
  declarationKind: "Definition",
  definitionKind: "Class",
  isEditorBrowsableNever: false,
  isObsolete: false,
};

const progressEvent: EcosystemTypeFindEvent = {
  kind: "Progress",
  progress: { phase: "Indexed", source: "dotnet" },
  finding: null,
  itemFailure: null,
};

const findingEvent: EcosystemTypeFindEvent = {
  kind: "Finding",
  progress: null,
  finding: {
    identity: finding.identity,
    finding,
    activation: {
      status: "Available",
      action: "activate-retained-workspace",
      reason: null,
    },
  },
  itemFailure: null,
};

const failureEvent: EcosystemTypeFindEvent = {
  kind: "ItemFailure",
  progress: null,
  finding: null,
  itemFailure: {
    phase: "Prefix",
    source: "System.",
    message: "NuGet source unavailable.",
  },
};

const inspection: BrowserEcosystemTypeFindInspection = {
  requestIdentity: "22222222-2222-2222-2222-222222222222",
  settlement: {
    rowStart: 2,
    rowEnd: 4,
    findings: [finding],
    completion: "Partial",
    failures: [failureEvent.itemFailure],
    unsearched: ["Microsoft."],
    incomplete: true,
  },
};

test("Ecosystem Type Find Worker accepts one closed bounded request", () => {
  assert.equal(
    engineWorkerEcosystemTypeFindInput.decode({
      retainedDefinitionId: "definition",
      realizationId: "realization",
      resultGeneration: 3,
      ...request,
    }).kind,
    "decoded",
  );
  assert.equal(
    engineWorkerEcosystemTypeFindInput.decode({
      retainedDefinitionId: "definition",
      realizationId: "realization",
      resultGeneration: 3,
      ...request,
      prefixDemand: ["aspnet"],
    }).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerEcosystemTypeFindInput.decode({
      retainedDefinitionId: "definition",
      realizationId: "realization",
      resultGeneration: 3,
      ...request,
      rowStart: 5,
      rowEnd: 4,
    }).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerEcosystemTypeFindInput.decode({
      retainedDefinitionId: "definition",
      realizationId: "realization",
      resultGeneration: 3,
      ...request,
      rowEnd: 10_001,
    }).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerEcosystemTypeFindInput.decode({
      retainedDefinitionId: "definition",
      realizationId: "realization",
      resultGeneration: 3,
      ...request,
      indexed: true,
    }).kind,
    "rejected",
  );
});

test("Ecosystem Type Find Worker validates exact durable event unions", () => {
  const registration =
    createEngineWorkerEcosystemTypeFindHostRegistration();
  assert.ok(registration.durable);
  assert.deepEqual(
    registration.durable.decode(progressEvent),
    { kind: "decoded", value: progressEvent },
  );
  assert.deepEqual(
    registration.durable.decode(findingEvent),
    { kind: "decoded", value: findingEvent },
  );
  assert.deepEqual(
    registration.durable.decode(failureEvent),
    { kind: "decoded", value: failureEvent },
  );
  assert.equal(
    registration.durable.decode({
      ...progressEvent,
      finding: findingEvent.finding,
    }).kind,
    "rejected",
  );
  assert.equal(
    registration.durable.decode({
      ...findingEvent,
      finding: {
        ...findingEvent.finding,
        identity: "changed",
      },
    }).kind,
    "rejected",
  );
  assert.equal(
    registration.durable.decode({
      ...findingEvent,
      finding: {
        ...findingEvent.finding,
        finding: {
          ...finding,
          pattern: {
            ...finding.pattern,
            namespace: "System.Text.Json",
          },
        },
      },
    }).kind,
    "rejected",
  );
  assert.equal(
    registration.durable.decode({
      kind: "Candidate",
      progress: null,
      finding: findingEvent.finding,
      itemFailure: null,
    }).kind,
    "rejected",
  );
});

test("Ecosystem Type Find Worker validates terminal settlement", () => {
  const registration =
    createEngineWorkerEcosystemTypeFindHostRegistration();
  assert.deepEqual(
    registration.value.decode(inspection),
    { kind: "decoded", value: inspection },
  );
  assert.equal(
    registration.value.decode({
      ...inspection,
      settlement: {
        ...inspection.settlement,
        completion: "Complete",
      },
    }).kind,
    "rejected",
  );
  assert.equal(
    mapEngineWorkerEcosystemTypeFindResult({
      kind: "Succeeded",
      inspection,
      failureKind: null,
      error: null,
      diagnostic: null,
      reason: null,
    }).kind,
    "succeeded",
  );
  assert.equal(
    mapEngineWorkerEcosystemTypeFindResult({
      kind: "Succeeded",
      inspection,
      failureKind: "Expected",
      error: null,
      diagnostic: null,
      reason: null,
    }).kind,
    "failed",
  );
});

test("Ecosystem Type Find cancellation preserves the first managed reason", () => {
  assert.equal(
    engineWorkerEcosystemTypeFindCancellationIsRunning(
      { status: "Requested", reason: "superseded" },
      "superseded",
    ),
    true,
  );
  assert.equal(
    engineWorkerEcosystemTypeFindCancellationIsRunning(
      { status: "NotActive", reason: null },
      "user",
    ),
    false,
  );
  assert.throws(() =>
    engineWorkerEcosystemTypeFindCancellationIsRunning(
      { status: "Requested", reason: "user" },
      "superseded",
    )
  );
});

test("Ecosystem Type Find publishes ordered events before settlement", async () => {
  const published: unknown[] = [];
  const facade: EngineWorkerEcosystemTypeFindFacade = {
    async findEcosystemTypes(
      _operationId,
      retainedDefinitionId,
      realizationId,
      resultGeneration,
      receivedRequest,
      eventSink,
    ) {
      assert.equal(retainedDefinitionId, "definition");
      assert.equal(realizationId, "realization");
      assert.equal(resultGeneration, 3);
      assert.deepEqual(receivedRequest, request);
      if ((typeof eventSink !== "object" && typeof eventSink !== "function")
        || eventSink === null) {
        throw new TypeError("Test event sink is unavailable.");
      }
      for (const event of [progressEvent, findingEvent, failureEvent]) {
        assert.equal(
          Reflect.set(eventSink, "event", JSON.stringify(event)),
          true,
        );
      }
      return {
        kind: "Succeeded",
        inspection,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: null,
      };
    },
    cancelEcosystemTypeFind() {
      return { status: "NotActive", reason: null };
    },
  };
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerEcosystemTypeFindOperation(operations, () => facade);
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
  const binding = bindEcosystemTypeFindFacade(
    registerEngineWorkerEcosystemTypeFindAdapter(host),
    () => undefined,
    createSharedEngineOperationAuthority(),
  );
  const eventSink = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      published.push(JSON.parse(String(value)));
    },
  });

  const pending = binding.findEcosystemTypes(
    "ecosystem-type-find-1",
    "definition",
    "realization",
    3,
    request,
    eventSink,
  );
  await environment.flushAsync();
  const result = await pending;

  assert.equal(result.kind, "Succeeded");
  assert.deepEqual(published, [progressEvent, findingEvent, failureEvent]);
  assert.deepEqual(result.inspection, inspection);

  binding.dispose();
  host.dispose();
});
