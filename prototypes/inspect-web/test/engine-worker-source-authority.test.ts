import assert from "node:assert/strict";
import test from "node:test";

import {
  createOperationAuthorityPage,
  type OperationAuthorityPage,
  type OperationProducerAdapter,
} from "../src/operation-authority.ts";
import {
  registerEngineWorkerMemberSourceComparisonAdapter,
  registerEngineWorkerMethodBodyComparisonAdapter,
  registerEngineWorkerMethodBodyTargetsAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerMemberSourceComparisonOperation,
  engineWorkerMethodBodyComparisonOperation,
  engineWorkerMethodBodyTargetsOperation,
  registerEngineWorkerSourceAuthorityOperations,
  type EngineWorkerSourceAuthorityFacade,
} from "../src/engine-worker-source-authority.ts";
import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyTargets,
  BrowserSourceComparison,
  BrowserSourceComparisonEndpoint,
  BrowserSourceComparisonRequest,
} from "../src/facades/inspect-web-source.d.ts";
import type {
  MethodBodyComparisonContext,
} from "../src/method-body-comparison.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";

const before = {
  typeIdentity: "Example.Widget",
  memberName: "Run",
  selectorKey: "method:Run",
  metadataToken: 0x06000001,
  label: "Run()",
};
const context: MethodBodyComparisonContext = {
  packageId: "Example.Package",
  version: "1.0.0",
  framework: "net11.0",
  assembly: "Example.dll",
  ...before,
};
const targets: BrowserMethodBodyTargets = {
  packageId: context.packageId,
  version: context.version,
  framework: context.framework,
  assembly: context.assembly,
  moduleVersionId: "11111111-1111-1111-1111-111111111111",
  before,
  methods: [before],
};
const comparisonRequest: BrowserMethodBodyComparisonRequest = {
  packageId: targets.packageId,
  version: targets.version,
  framework: targets.framework,
  assembly: targets.assembly,
  moduleVersionId: targets.moduleVersionId,
  before,
  after: before,
};
const comparison: BrowserMethodBodyComparison = {
  request: comparisonRequest,
  stage: "Complete",
  outcome: "Equivalent",
  producers: [],
  diagnostics: [],
};
const sourceRequest: BrowserSourceComparisonRequest = {
  packageId: context.packageId,
  beforeVersion: context.version,
  afterVersion: "2.0.0",
  framework: context.framework,
  assembly: context.assembly,
  typeIdentity: context.typeIdentity,
  memberName: context.memberName,
  selectorKey: context.selectorKey,
  metadataToken: context.metadataToken,
};
const endpoint = (
  version: string,
): BrowserSourceComparisonEndpoint => ({
  packageId: context.packageId,
  version,
  framework: context.framework,
  assembly: context.assembly,
  assetPath: `lib/${context.framework}/${context.assembly}`,
  moduleVersionId: targets.moduleVersionId,
  assemblyIdentity: "Example, Version=1.0.0.0",
  memberIdentity: "Example.Widget.Run()",
  metadataToken: context.metadataToken,
  state: "Available",
  detail: null,
  text: "void Run() {}",
  sourceUrl: null,
  repositoryUrl: null,
  revision: null,
});
const sourceComparison: BrowserSourceComparison = {
  request: sourceRequest,
  status: "Compared",
  isExact: true,
  before: endpoint(sourceRequest.beforeVersion),
  after: endpoint(sourceRequest.afterVersion),
  lines: [],
  failure: null,
};

function succeeded<T>(value: T) {
  return {
    version: 1,
    kind: "Succeeded" as const,
    value,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function createFacade(
  calls: Array<{ operation: string; arguments: readonly unknown[] }>,
): EngineWorkerSourceAuthorityFacade {
  return {
    queryMethodBodyComparisonTargets: (...args) => {
      calls.push({ operation: "targets", arguments: args });
      return Promise.resolve(succeeded(targets));
    },
    queryMethodBodyComparison: (...args) => {
      calls.push({ operation: "comparison", arguments: args });
      return Promise.resolve(succeeded(comparison));
    },
    queryMemberSourceComparison: (...args) => {
      calls.push({ operation: "source", arguments: args });
      return Promise.resolve(succeeded(sourceComparison));
    },
    cancelMethodBodyComparison: () => ({
      kind: "NotActive",
      reason: null,
    }),
    cancelMemberSourceComparison: () => ({
      kind: "NotActive",
      reason: null,
    }),
  };
}

function createHarness(facade: EngineWorkerSourceAuthorityFacade) {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerSourceAuthorityOperations(
    operations,
    () => facade);
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
  return { environment, host };
}

async function startReady(
  harness: ReturnType<typeof createHarness>,
): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

async function runOperation<TInput, TValue>(
  harness: ReturnType<typeof createHarness>,
  page: OperationAuthorityPage,
  adapter: OperationProducerAdapter<
    TInput,
    TValue,
    string,
    never,
    WorkerRuntimePreparationError
  >,
  input: TInput,
) {
  const session = page.createSession<
    TInput,
    TValue,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  const started = session.start(input, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Source authority operation to start.");
  await harness.environment.flushAsync();
  const outcome = await started.handle.outcome;
  await started.handle.quiesced;
  session.dispose();
  return outcome;
}

test("Source authority Worker operations preserve page operation identity", async () => {
  const calls: Array<{
    operation: string;
    arguments: readonly unknown[];
  }> = [];
  const harness = createHarness(createFacade(calls));
  const targetsAdapter =
    registerEngineWorkerMethodBodyTargetsAdapter(harness.host);
  const comparisonAdapter =
    registerEngineWorkerMethodBodyComparisonAdapter(harness.host);
  const sourceAdapter =
    registerEngineWorkerMemberSourceComparisonAdapter(harness.host);
  const operationIds = [
    "targets-operation",
    "comparison-operation",
    "source-operation",
  ];
  const page = createOperationAuthorityPage({
    allocation: { createId: () => operationIds.shift() ?? "unexpected" },
  });
  await startReady(harness);

  assert.deepEqual(
    await runOperation(
      harness,
      page,
      targetsAdapter,
      context),
    { kind: "succeeded", value: targets });
  assert.deepEqual(
    await runOperation(
      harness,
      page,
      comparisonAdapter,
      comparisonRequest),
    { kind: "succeeded", value: comparison });
  assert.deepEqual(
    await runOperation(
      harness,
      page,
      sourceAdapter,
      sourceRequest),
    { kind: "succeeded", value: sourceComparison });

  assert.equal(calls[0]?.arguments[0], "targets-operation");
  assert.deepEqual(calls[0]?.arguments.slice(1), [
    context.packageId,
    context.version,
    context.framework,
    context.assembly,
    context.typeIdentity,
    context.memberName,
    context.selectorKey,
    context.metadataToken,
  ]);
  assert.equal(calls[1]?.arguments[0], "comparison-operation");
  const comparisonJson = calls[1]?.arguments[1];
  if (typeof comparisonJson !== "string") {
    assert.fail("comparison input was not JSON text");
  }
  assert.deepEqual(
    JSON.parse(comparisonJson),
    comparisonRequest);
  assert.equal(calls[2]?.arguments[0], "source-operation");
  const sourceJson = calls[2]?.arguments[1];
  if (typeof sourceJson !== "string") {
    assert.fail("Source comparison input was not JSON text");
  }
  assert.deepEqual(
    JSON.parse(sourceJson),
    sourceRequest);
  harness.host.dispose();
});

test("Source authority codecs reject malformed and oversized payloads", () => {
  assert.equal(
    engineWorkerMethodBodyTargetsOperation.input
      .decoder.decode({}).kind,
    "rejected");
  assert.equal(
    engineWorkerMethodBodyComparisonOperation.input
      .decoder.decode("x".repeat(1_048_577)).kind,
    "rejected");
  assert.equal(
    engineWorkerMemberSourceComparisonOperation.value
      .decoder.decode(JSON.stringify({
        ...sourceComparison,
        isExact: "yes",
      })).kind,
    "rejected");
});

test("invalid Source authority result is contained from its neighbor", async () => {
  let targetCall = 0;
  const facade = createFacade([]);
  facade.queryMethodBodyComparisonTargets = () =>
    Promise.resolve(targetCall++ === 0
      // This test deliberately crosses the generated facade with malformed data.
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      ? succeeded({ invalid: true } as never)
      : succeeded(targets));
  const harness = createHarness(facade);
  const adapter =
    registerEngineWorkerMethodBodyTargetsAdapter(harness.host);
  const operationIds = ["invalid-targets", "valid-targets"];
  const page = createOperationAuthorityPage({
    allocation: { createId: () => operationIds.shift() ?? "unexpected" },
  });
  await startReady(harness);

  const invalid = await runOperation(
    harness,
    page,
    adapter,
    context);
  const valid = await runOperation(
    harness,
    page,
    adapter,
    context);

  assert.equal(invalid.kind, "failed");
  if (invalid.kind === "failed") {
    assert.match(invalid.error, /invalid Worker boundary data/);
  }
  assert.deepEqual(valid, { kind: "succeeded", value: targets });
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.host.dispose();
});
