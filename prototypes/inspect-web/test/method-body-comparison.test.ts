import assert from "node:assert/strict";
import test from "node:test";

import {
  createMethodBodyComparisonCoordinator,
  createMethodBodyDiffState,
  methodBodyComparisonPackageId,
  filterMethodBodyChoices,
  methodBodyChoices,
  methodBodySelectionKey,
  type MethodBodyComparisonContext,
  type MethodBodyComparisonDependencies,
  type MethodBodyDiffState,
} from "../src/method-body-comparison.ts";
import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonResult,
  BrowserMethodBodySelection,
  BrowserMethodBodyTargets,
  BrowserMethodBodyTargetsResult,
} from "../src/facades/inspect-web-source.d.ts";
import type { OperationId } from "../src/operation-authority.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

function selection(
  overrides: Partial<BrowserMethodBodySelection> = {},
): BrowserMethodBodySelection {
  return {
    typeIdentity: "Example.Widget",
    memberName: "Build",
    selectorKey: "method:Build()",
    metadataToken: 0x06000001,
    label: "Example.Widget.Build()",
    ...overrides,
  };
}

const before = selection();
const after = selection({
  memberName: "Rebuild",
  selectorKey: "method:Rebuild()",
  metadataToken: 0x06000002,
  label: "Example.Widget.Rebuild()",
});
const bodyless = selection({
  typeIdentity: "Example.IWidget",
  memberName: "Accept",
  selectorKey: "method:Accept()",
  metadataToken: 0x06000003,
  label: "Example.IWidget.Accept()",
});

function targets(
  overrides: Partial<BrowserMethodBodyTargets> = {},
): BrowserMethodBodyTargets {
  return {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
    before,
    methods: [after, bodyless],
    ...overrides,
  };
}

function targetsResult(
  value: BrowserMethodBodyTargets | null,
  overrides: Partial<BrowserMethodBodyTargetsResult> = {},
): BrowserMethodBodyTargetsResult {
  return {
    version: 1,
    kind: "Succeeded",
    value,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    ...overrides,
  };
}

function comparison(
  request: BrowserMethodBodyComparison["request"],
): BrowserMethodBodyComparison {
  return {
    request,
    stage: "Research",
    outcome: "Completed",
    producers: [],
    diagnostics: [],
  };
}

function comparisonResult(
  value: BrowserMethodBodyComparison | null,
  overrides: Partial<BrowserMethodBodyComparisonResult> = {},
): BrowserMethodBodyComparisonResult {
  return {
    version: 1,
    kind: "Succeeded",
    value,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    ...overrides,
  };
}

const context: MethodBodyComparisonContext = {
  packageId: "Example.Package",
  version: "1.2.3",
  framework: "net10.0",
  assembly: "Example.Package",
  typeIdentity: "Example.Widget",
  memberName: "Build",
  selectorKey: "method:Build()",
  metadataToken: 0x06000001,
  label: "Widget Build()",
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

interface Harness {
  state: MethodBodyDiffState;
  dependencies: MethodBodyComparisonDependencies;
  targetQueries: Array<{
    operationId: OperationId;
    resolve: (value: BrowserMethodBodyTargetsResult) => void;
  }>;
  comparisonQueries: Array<{
    operationId: OperationId;
    requestJson: string;
    resolve: (value: BrowserMethodBodyComparisonResult) => void;
  }>;
  cancellations: Array<{ operationId: string; reason: string }>;
  diagnostics: unknown[];
  renders: number;
}

function harness(): Harness {
  const state = createMethodBodyDiffState();
  const targetQueries: Harness["targetQueries"] = [];
  const comparisonQueries: Harness["comparisonQueries"] = [];
  const cancellations: Harness["cancellations"] = [];
  const diagnostics: unknown[] = [];
  const counters = { renders: 0 };
  let nextOperation = 1;
  const dependencies: MethodBodyComparisonDependencies = {
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: {
        createId: () => `method-body-operation-${nextOperation++}`,
      },
    }),
    queryTargets: operationId => {
      const pending = deferred<BrowserMethodBodyTargetsResult>();
      targetQueries.push({ operationId, resolve: pending.resolve });
      return pending.promise;
    },
    queryComparison: (operationId, requestJson) => {
      const pending = deferred<BrowserMethodBodyComparisonResult>();
      comparisonQueries.push({
        operationId,
        requestJson,
        resolve: pending.resolve,
      });
      return pending.promise;
    },
    cancelMethodBodyComparison: (operationId, reason) => {
      cancellations.push({ operationId, reason });
    },
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic);
      return undefined;
    },
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {
      counters.renders++;
    },
  };
  return {
    state,
    dependencies,
    targetQueries,
    comparisonQueries,
    cancellations,
    diagnostics,
    get renders() {
      return counters.renders;
    },
  };
}

async function settle(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
}

test("the inventory loads once and choosing a candidate never compares", async () => {
  const context7 = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(context7.dependencies);

  const opening = coordinator.open(context, "#compare-method-bodies");
  assert.equal(context7.state.open, true);
  assert.equal(context7.state.targetsLoading, true);
  assert.equal(context7.targetQueries.length, 1);
  context7.targetQueries[0]!.resolve(targetsResult(targets()));
  await opening;

  assert.equal(context7.state.targetsLoading, false);
  assert.deepEqual(context7.state.targets, targets());

  coordinator.setFilter("Rebuild");
  assert.equal(coordinator.selectCandidate(methodBodySelectionKey(after)), true);
  assert.equal(coordinator.selectCandidate(methodBodySelectionKey(after)), false);
  assert.equal(context7.comparisonQueries.length, 0);

  const comparing = coordinator.compare();
  assert.equal(context7.comparisonQueries.length, 1);
  const submitted: unknown =
    JSON.parse(context7.comparisonQueries[0]!.requestJson);
  assert.deepEqual(submitted, {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
    before,
    after,
  });
  context7.comparisonQueries[0]!.resolve(
    comparisonResult(comparison({
      packageId: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
      assembly: "Example.Package",
      moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
      before,
      after,
    })));
  await comparing;

  assert.equal(context7.state.comparisonLoading, false);
  assert.equal(context7.state.comparison?.outcome, "Completed");
  assert.deepEqual(context7.state.submittedRequest?.after, after);
  assert.equal(context7.diagnostics.length, 0);
});

test("a same-method pair is choosable and submitted unchanged", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  const opening = coordinator.open(context, "#compare-method-bodies");
  fixture.targetQueries[0]!.resolve(
    targetsResult(targets({ methods: [after] })));
  await opening;

  const choices = methodBodyChoices(fixture.state.targets!);
  assert.deepEqual(choices.map(choice => choice.memberName), ["Build", "Rebuild"]);
  coordinator.selectCandidate(methodBodySelectionKey(before));
  void coordinator.compare();

  const submitted: unknown =
    JSON.parse(fixture.comparisonQueries[0]!.requestJson);
  assert.deepEqual(submitted, {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
    before,
    after: before,
  });
});

test("changing the pair clears the old result and cancels its operation", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  const opening = coordinator.open(context, "#compare-method-bodies");
  fixture.targetQueries[0]!.resolve(targetsResult(targets()));
  await opening;

  coordinator.selectCandidate(methodBodySelectionKey(after));
  void coordinator.compare();
  const running = fixture.comparisonQueries[0]!;
  assert.equal(fixture.state.comparisonLoading, true);

  coordinator.selectCandidate(methodBodySelectionKey(bodyless));
  assert.equal(fixture.state.comparisonLoading, false);
  assert.equal(fixture.state.comparison, null);
  assert.equal(fixture.state.submittedRequest, null);
  assert.deepEqual(
    fixture.cancellations,
    [{ operationId: running.operationId, reason: "superseded" }]);

  running.resolve(comparisonResult(comparison({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
    before,
    after,
  })));
  await settle();

  assert.equal(fixture.state.comparison, null);
  assert.equal(fixture.state.candidateKey, methodBodySelectionKey(bodyless));
});

test("closing the dialog cancels both lanes and suppresses late results", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  const opening = coordinator.open(context, "#compare-method-bodies");
  fixture.targetQueries[0]!.resolve(targetsResult(targets()));
  await opening;
  coordinator.selectCandidate(methodBodySelectionKey(after));
  void coordinator.compare();
  const running = fixture.comparisonQueries[0]!;

  const dismissal = coordinator.close();
  assert.equal(dismissal.handled, true);
  assert.equal(dismissal.returnFocusSelector, "#compare-method-bodies");
  assert.deepEqual(
    fixture.cancellations,
    [{ operationId: running.operationId, reason: "disposed" }]);
  assert.equal(fixture.state.open, false);
  assert.equal(fixture.state.targets, null);

  running.resolve(comparisonResult(comparison({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    moduleVersionId: "0f5f6a4a-6d59-4b0c-9e9e-2b7d1a6c1234",
    before,
    after,
  })));
  await settle();

  assert.equal(fixture.state.open, false);
  assert.equal(fixture.state.comparison, null);
  assert.equal(fixture.state.comparisonLoading, false);
  assert.equal(fixture.diagnostics.length, 0);
});

test("navigation replacement releases a running inventory query", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  void coordinator.open(context, "#compare-method-bodies");
  const running = fixture.targetQueries[0]!;

  assert.equal(coordinator.dispose(), true);
  assert.deepEqual(
    fixture.cancellations,
    [{ operationId: running.operationId, reason: "disposed" }]);

  running.resolve(targetsResult(targets()));
  await settle();

  assert.equal(fixture.state.open, false);
  assert.equal(fixture.state.targets, null);
  assert.equal(coordinator.dispose(), false);
});

test("an expected managed failure stays visible for its own lane", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  const opening = coordinator.open(context, "#compare-method-bodies");
  fixture.targetQueries[0]!.resolve(targetsResult(null, {
    kind: "Failed",
    failureKind: "Expected",
    error: "The implementation assembly is unavailable.",
    diagnostic: "context-unavailable",
  }));
  await opening;

  assert.equal(fixture.state.targetsLoading, false);
  assert.equal(
    fixture.state.targetsError,
    "The implementation assembly is unavailable.");
  assert.equal(fixture.state.targets, null);
  assert.equal(fixture.diagnostics.length, 0);
});

test("a transported comparison failure never becomes an empty success", async () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);
  const opening = coordinator.open(context, "#compare-method-bodies");
  fixture.targetQueries[0]!.resolve(targetsResult(targets()));
  await opening;
  coordinator.selectCandidate(methodBodySelectionKey(bodyless));
  const comparing = coordinator.compare();
  fixture.comparisonQueries[0]!.resolve(comparisonResult(null, {
    kind: "Failed",
    failureKind: "Expected",
    error: "The designation is ambiguous.",
    diagnostic: "ambiguous-designation",
  }));
  await comparing;

  assert.equal(fixture.state.comparison, null);
  assert.equal(fixture.state.comparisonError, "The designation is ambiguous.");
  assert.deepEqual(fixture.state.submittedRequest?.after, bodyless);
});

test("an unavailable context opens with its visible reason and no query", () => {
  const fixture = harness();
  const coordinator =
    createMethodBodyComparisonCoordinator(fixture.dependencies);

  coordinator.openUnavailable(
    "Select one accessor or body of this member before comparing method bodies.",
    "#compare-method-bodies");

  assert.equal(fixture.state.open, true);
  assert.equal(
    fixture.state.unavailableReason,
    "Select one accessor or body of this member before comparing method bodies.");
  assert.equal(fixture.targetQueries.length, 0);
  assert.equal(coordinator.close().handled, true);
});

test("filtering keeps the chosen candidate selectable", () => {
  const choices = methodBodyChoices(targets());
  const filtered = filterMethodBodyChoices(
    choices,
    "Accept",
    methodBodySelectionKey(after));

  assert.deepEqual(
    filtered.map(choice => choice.memberName),
    ["Rebuild", "Accept"]);
  assert.deepEqual(
    filterMethodBodyChoices(choices, "", "").map(choice => choice.memberName),
    ["Build", "Rebuild", "Accept"]);
});

test("a platform selection keeps its resident coordinates without a package id", () => {
  assert.equal(
    methodBodyComparisonPackageId({ id: "Example.Package" }),
    "Example.Package");
  assert.equal(
    methodBodyComparisonPackageId({
      id: "Microsoft.NETCore.App",
      isRuntimePack: false,
    }),
    "Microsoft.NETCore.App");
  // The retained platform workspace is not an acquired package: the managed target
  // signature discriminates it by an empty package id, and version, framework and the
  // resident assembly still identify the implementation exactly.
  assert.equal(
    methodBodyComparisonPackageId({
      id: "Microsoft.NETCore.App",
      isRuntimePack: true,
    }),
    "");
});
