import assert from "node:assert/strict";
import test from "node:test";

import {
  buildCloneCandidateRequest,
  createCloneCandidateInspectionCoordinator,
  createCloneCandidateInspectionState,
  type CloneCandidateInspectionDependencies,
  type CloneCandidateRequestInput,
} from "../src/clone-candidate-inspection.ts";
import type {
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
} from "../src/facades/inspect-web-analysis.d.ts";

const input: CloneCandidateRequestInput = {
  packages: [
    { id: "Seed.Package", version: "1.0.0", framework: "net11.0" },
    {
      id: "Platform",
      version: "11.0.0",
      framework: "net11.0",
      runtimePack: true,
    },
    { id: "Neighbor.Package", version: "2.0.0", framework: "net11.0" },
  ],
  selectedPackage: {
    id: "Seed.Package",
    version: "1.0.0",
    framework: "net11.0",
  },
  assembly: "lib/net11.0/Seed.Package.dll",
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
    body: null,
  },
};

function result(
  request: BrowserCloneCandidateRequest,
): BrowserCloneCandidateResult {
  return {
    schemaVersion: 1,
    request,
    kind: "Rejected",
    document: null,
    seedLibrary: null,
    openFailureKind: null,
    failure: null,
    presentationRejectionKind: null,
    subject: null,
    detail: "No candidate library was available.",
    metadataRootReason: null,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

test("defaults build Everything plus SimilarNames over package participants", () => {
  const state = createCloneCandidateInspectionState();
  assert.equal(state.breadth, "Everything");
  assert.equal(state.discovery, "SimilarNames");

  const available = buildCloneCandidateRequest(
    input,
    state.breadth,
    state.discovery);
  assert.equal(available.reason, "");
  assert.deepEqual(available.request, {
    schemaVersion: 1,
    packages: [
      {
        packageId: "Seed.Package",
        version: "1.0.0",
        targetFramework: "net11.0",
      },
      {
        packageId: "Neighbor.Package",
        version: "2.0.0",
        targetFramework: "net11.0",
      },
    ],
    selectedPackageIndex: 0,
    assembly: "lib/net11.0/Seed.Package.dll",
    seed: input.seed,
    breadth: "Everything",
    discovery: "SimilarNames",
  });
});

test("runtime and missing Workspace subjects remain visibly unavailable", () => {
  assert.match(buildCloneCandidateRequest({
    ...input,
    selectedPackage: { ...input.selectedPackage, runtimePack: true },
  }, "Everything", "SimilarNames").reason, /runtime libraries/);

  assert.match(buildCloneCandidateRequest({
    ...input,
    selectedPackage: {
      id: "Removed.Package",
      version: "1.0.0",
      framework: "net11.0",
    },
  }, "Everything", "SimilarNames").reason, /no longer present/);
});

test("control replacement suppresses a late result from the old request", async () => {
  const state = createCloneCandidateInspectionState();
  const pending = deferred<BrowserCloneCandidateResult>();
  let renders = 0;
  let current = true;
  const dependencies: CloneCandidateInspectionDependencies = {
    state,
    query: () => pending.promise,
    isCurrent: () => current,
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {
      renders++;
    },
  };
  const coordinator =
    createCloneCandidateInspectionCoordinator(dependencies);
  const loading = coordinator.load(input);
  const request = state.request;
  assert.ok(request);
  assert.equal(state.loading, true);

  assert.equal(coordinator.setBreadth("Self"), true);
  current = false;
  pending.resolve(result(request));
  await loading;

  assert.equal(state.result, null);
  assert.equal(state.loading, false);
  assert.equal(state.breadth, "Self");
  assert.ok(renders >= 1);
});

test("subject reconciliation clears stale evidence before replacement", () => {
  const state = createCloneCandidateInspectionState();
  const dependencies: CloneCandidateInspectionDependencies = {
    state,
    query: () => {
      throw new Error("Reconciliation must not start a query.");
    },
    isCurrent: () => true,
    describeError: String,
    render: () => undefined,
  };
  const coordinator =
    createCloneCandidateInspectionCoordinator(dependencies);
  const previous = buildCloneCandidateRequest(
    input,
    state.breadth,
    state.discovery).request;
  assert.ok(previous);
  state.request = previous;
  state.result = result(previous);

  const replacement = {
    ...input,
    seed: {
      ...input.seed,
      typeDefinitionId: "Example.Replacement",
    },
  };
  assert.equal(coordinator.reconcile(replacement, ""), true);
  assert.equal(state.request, null);
  assert.equal(state.result, null);
  assert.equal(state.loading, false);
});
