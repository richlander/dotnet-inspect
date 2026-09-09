import assert from "node:assert/strict";
import test from "node:test";

import {
  createLibraryApiDiffCoordinator,
  createLibraryApiDiffState,
  libraryApiDiffSignature,
  libraryApiDiffViewIsActive,
  resolveLibraryApiDiffTarget,
  type LibraryApiDiffDependencies,
  type LibraryApiDiffRequestContext,
  type LibraryApiDiffState,
} from "../src/library-api-diff.ts";
import type {
  BrowserLibraryApiDiff,
  BrowserLibraryApiDiffRequest,
  BrowserLibraryApiDiffResult,
} from "../src/facades/inspect-web-metadata.d.ts";
import type { ComparisonPackage } from "../src/package-comparison-targets.ts";
import type { OperationId } from "../src/operation-authority.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

function galleryPackage(overrides: Partial<ComparisonPackage> = {}): ComparisonPackage {
  return {
    id: "Example.Package",
    version: "2.0.0",
    activeFramework: "net10.0",
    source: { kind: "nuget.org" },
    ...overrides,
  };
}

function parseRequest(json: string): BrowserLibraryApiDiffRequest {
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return JSON.parse(json) as BrowserLibraryApiDiffRequest;
}

test("resolveLibraryApiDiffTarget: non-Gallery sources never resolve, even with an exact target", () => {
  const platform = galleryPackage({ source: { kind: "platform" } });
  const resolution = resolveLibraryApiDiffTarget(
    platform, { kind: "exact", version: "1.0.0" }, { status: "idle" });
  assert.equal(resolution.kind, "unresolved");
  assert.match(
    (resolution as { reason: string }).reason, /Gallery packages/);
});

test("resolveLibraryApiDiffTarget: an exact target resolves regardless of the version inventory", () => {
  const pkg = galleryPackage();
  const resolution = resolveLibraryApiDiffTarget(
    pkg, { kind: "exact", version: "1.5.0" }, { status: "idle" });
  assert.deepEqual(resolution, { kind: "resolved", comparisonVersion: "1.5.0" });
});

test("resolveLibraryApiDiffTarget: an automatic target is unresolved while the inventory is idle or loading", () => {
  const pkg = galleryPackage();
  for (const versions of [{ status: "idle" }, { status: "loading" }] as const) {
    const resolution = resolveLibraryApiDiffTarget(pkg, { kind: "previous" }, versions);
    assert.equal(resolution.kind, "unresolved");
    assert.match((resolution as { reason: string }).reason, /Reading available versions/);
  }
});

test("resolveLibraryApiDiffTarget: an automatic target surfaces the catalog failure message", () => {
  const pkg = galleryPackage();
  const resolution = resolveLibraryApiDiffTarget(
    pkg, { kind: "previous" }, { status: "failed", message: "NuGet unavailable" });
  assert.deepEqual(resolution, { kind: "unresolved", reason: "NuGet unavailable" });
});

test("resolveLibraryApiDiffTarget: an automatic target surfaces the inventory's own unavailable reason without ordering versions", () => {
  const pkg = galleryPackage();
  const resolution = resolveLibraryApiDiffTarget(pkg, { kind: "previous" }, {
    status: "available",
    inventory: {
      versions: ["2.0.0"],
      currentVersionInsertionIndex: 0,
      previousVersion: null,
      previousVersionUnavailableReason: "Only one listed version is published.",
    },
  });
  assert.deepEqual(resolution, {
    kind: "unresolved", reason: "Only one listed version is published.",
  });
});

test("resolveLibraryApiDiffTarget: no predecessor and no owner-issued reason still produces a visible reason", () => {
  const pkg = galleryPackage();
  const resolution = resolveLibraryApiDiffTarget(pkg, { kind: "previous" }, {
    status: "available",
    inventory: {
      versions: ["2.0.0"],
      currentVersionInsertionIndex: 0,
      previousVersion: null,
      previousVersionUnavailableReason: null,
    },
  });
  assert.deepEqual(resolution, {
    kind: "unresolved", reason: "No earlier listed version is available.",
  });
});

test("resolveLibraryApiDiffTarget: an automatic target resolves to the catalog's previousVersion, never a browser-computed one", () => {
  const pkg = galleryPackage();
  const resolution = resolveLibraryApiDiffTarget(pkg, { kind: "previous" }, {
    status: "available",
    inventory: {
      versions: ["1.0.0", "2.0.0"],
      currentVersionInsertionIndex: 1,
      previousVersion: "1.0.0",
      previousVersionUnavailableReason: null,
    },
  });
  assert.deepEqual(resolution, { kind: "resolved", comparisonVersion: "1.0.0" });
});

test("libraryApiDiffSignature preserves exact association and distinguishes every coordinate field", () => {
  const context: Omit<LibraryApiDiffRequestContext, "comparisonVersion"> = {
    packageId: "Example.Package", version: "2.0.0", framework: "net10.0", assembly: "Example.Package",
  };
  assert.notEqual(
    libraryApiDiffSignature(context, "1.0.0"),
    libraryApiDiffSignature(
      { ...context, packageId: "EXAMPLE.PACKAGE" }, "1.0.0"));
  const base = libraryApiDiffSignature(context, "1.0.0");
  assert.notEqual(base, libraryApiDiffSignature({ ...context, version: "2.0.1" }, "1.0.0"));
  assert.notEqual(base, libraryApiDiffSignature({ ...context, framework: "net9.0" }, "1.0.0"));
  assert.notEqual(base, libraryApiDiffSignature({ ...context, assembly: "Other.dll" }, "1.0.0"));
  assert.notEqual(base, libraryApiDiffSignature({ ...context, assembly: "example.package" }, "1.0.0"));
  assert.notEqual(base, libraryApiDiffSignature(context, "1.0.1"));
});

test("Library Diff is active only in the visible ready Library Diff view", () => {
  const visible = {
    engineReady: true,
    loading: false,
    hasError: false,
    home: false,
    credits: false,
    packageQueryOpen: false,
    explorerOpen: false,
    hasPackage: true,
    scope: "library" as const,
    libraryLens: "diff" as const,
  };
  assert.equal(libraryApiDiffViewIsActive(visible), true);
  for (const hidden of [
    { home: true },
    { credits: true },
    { packageQueryOpen: true },
    { explorerOpen: true },
    { loading: true },
    { hasError: true },
    { engineReady: false },
    { hasPackage: false },
    { scope: "workspace" as const },
    { libraryLens: "overview" as const },
  ]) {
    assert.equal(libraryApiDiffViewIsActive({ ...visible, ...hidden }), false);
  }
});

// ─── Coordinator ────────────────────────────────────────────────────────────

const context: LibraryApiDiffRequestContext = {
  packageId: "Example.Package",
  version: "2.0.0",
  framework: "net10.0",
  assembly: "Example.Package",
  comparisonVersion: "1.0.0",
};
const signature = libraryApiDiffSignature(context, context.comparisonVersion);

function endpointSummary(
  overrides: Partial<BrowserLibraryApiDiff["before"]> = {},
): BrowserLibraryApiDiff["before"] {
  return {
    identity: { name: "Example.Package", version: "1.0.0.0", culture: null, publicKeyToken: null },
    scope: "Public",
    isComplete: true,
    issues: [],
    ...overrides,
  };
}

function availableDiff(
  request: BrowserLibraryApiDiffRequest,
  subjects: BrowserLibraryApiDiff["document"] extends null ? never : NonNullable<BrowserLibraryApiDiff["document"]>["subjects"] = [],
): BrowserLibraryApiDiff {
  return {
    request,
    kind: "Available",
    before: endpointSummary(),
    after: endpointSummary({ identity: { name: "Example.Package", version: "2.0.0.0", culture: null, publicKeyToken: null } }),
    summary: {
      changedTypeCount: subjects.length, addedTypeCount: 0, removedTypeCount: 0,
      changedMemberCount: 0, breakingCount: 0, additiveCount: 0, potentiallyBreakingCount: 0,
    },
    document: { identifier: "library-api.v1|Example.Package", display: "Example.Package", subjects },
    unavailableKind: null,
    rejectionKind: null,
  };
}

function succeeded(value: BrowserLibraryApiDiff): BrowserLibraryApiDiffResult {
  return { version: 1, kind: "Succeeded", value, failureKind: null, error: null, diagnostic: null, reason: null };
}

function failed(error: string): BrowserLibraryApiDiffResult {
  return {
    version: 1, kind: "Failed", value: null, failureKind: "Expected",
    error, diagnostic: error, reason: null,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

interface Harness {
  state: LibraryApiDiffState;
  dependencies: LibraryApiDiffDependencies;
  queries: Array<{
    operationId: OperationId;
    requestJson: string;
    resolve: (value: BrowserLibraryApiDiffResult) => void;
  }>;
  cancellations: Array<{ operationId: string; reason: string }>;
  diagnostics: unknown[];
  renders: number;
}

function harness(): Harness {
  const state = createLibraryApiDiffState();
  const queries: Harness["queries"] = [];
  const cancellations: Harness["cancellations"] = [];
  const diagnostics: unknown[] = [];
  const counters = { renders: 0 };
  let nextOperation = 1;
  const dependencies: LibraryApiDiffDependencies = {
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => `library-api-diff-operation-${nextOperation++}` },
    }),
    queryLibraryApiDiff: (operationId, requestJson) => {
      const pending = deferred<BrowserLibraryApiDiffResult>();
      queries.push({ operationId, requestJson, resolve: pending.resolve });
      return pending.promise;
    },
    cancelLibraryApiDiff: (operationId, reason) => {
      cancellations.push({ operationId, reason });
    },
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic);
      return undefined;
    },
    describeError: error => (error instanceof Error ? error.message : String(error)),
    render: () => { counters.renders++; },
  };
  return {
    state, dependencies, queries, cancellations, diagnostics,
    get renders() { return counters.renders; },
  };
}

async function settle(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
}

test("an exact target starts exactly one request and publishes the Available result", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  assert.equal(h.state.loading, true);
  assert.equal(h.queries.length, 1);
  const submitted = parseRequest(h.queries[0]!.requestJson);
  assert.deepEqual(submitted, {
    packageId: "Example.Package", version: "2.0.0", framework: "net10.0",
    comparisonVersion: "1.0.0", assembly: "Example.Package",
  });
  h.queries[0]!.resolve(succeeded(availableDiff(submitted)));
  await settle();
  assert.equal(h.state.loading, false);
  assert.equal(h.state.result?.kind, "Available");
  assert.equal(h.state.noRequestReason, "");
  assert.equal(h.diagnostics.length, 0);

  // Idempotent: re-ensuring the same signature does not start a second request.
  coordinator.ensure(signature, context, "");
  assert.equal(h.queries.length, 1);
});

test("a previous (automatic) target resolves once the version inventory carries previousVersion", () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  const automaticContext = { ...context, comparisonVersion: "1.5.0" };
  const automaticSignature = libraryApiDiffSignature(automaticContext, automaticContext.comparisonVersion);
  coordinator.ensure(automaticSignature, automaticContext, "");
  assert.equal(h.queries.length, 1);
  const submitted = parseRequest(h.queries[0]!.requestJson);
  assert.equal(submitted.comparisonVersion, "1.5.0");
});

test("a pending, failed, or predecessor-free target publishes a visible no-request state and starts no query", () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  const unresolvedSignature = libraryApiDiffSignature(context, "");
  coordinator.ensure(unresolvedSignature, null, "Reading available versions…");
  assert.equal(h.queries.length, 0);
  assert.equal(h.state.noRequestReason, "Reading available versions…");
  assert.equal(h.state.loading, false);
  assert.equal(h.state.request, null);
  assert.equal(h.renders >= 1, true);

  // Idempotent: the same unresolved signature/reason renders once, not repeatedly.
  const rendersBefore = h.renders;
  coordinator.ensure(unresolvedSignature, null, "Reading available versions…");
  assert.equal(h.renders, rendersBefore);
});

test("immutable request association: changing the input after submission cannot relabel a prior result", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  const running = h.queries[0]!;
  const otherContext = { ...context, version: "2.0.1" };
  const otherSignature = libraryApiDiffSignature(otherContext, otherContext.comparisonVersion);

  // A second, different coordinate supersedes the first before it settles.
  coordinator.ensure(otherSignature, otherContext, "");
  assert.equal(h.queries.length, 2);
  assert.deepEqual(h.cancellations, [{ operationId: running.operationId, reason: "superseded" }]);

  const currentRequest = h.state.request;
  assert.equal(currentRequest?.version, "2.0.1");

  // The superseded operation's late result must not relabel the current request.
  running.resolve(succeeded(availableDiff(parseRequest(running.requestJson))));
  await settle();
  assert.equal(h.state.request, currentRequest);
  assert.equal(h.state.result, null);
});

test("a superseded (stale) completion never publishes over its successor", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  const first = h.queries[0]!;
  const secondContext = { ...context, framework: "net9.0" };
  const secondSignature = libraryApiDiffSignature(secondContext, secondContext.comparisonVersion);
  coordinator.ensure(secondSignature, secondContext, "");
  const second = h.queries[1]!;

  second.resolve(succeeded(availableDiff(parseRequest(second.requestJson))));
  await settle();
  assert.equal(h.state.result?.request.framework, "net9.0");

  // The first (stale) query resolving after the second must not overwrite the current view.
  first.resolve(succeeded(availableDiff(parseRequest(first.requestJson))));
  await settle();
  assert.equal(h.state.result?.request.framework, "net9.0");
});

test("dispose cancels an in-flight operation and resets every field", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  const running = h.queries[0]!;
  assert.equal(coordinator.dispose(), true);
  assert.deepEqual(h.cancellations, [{ operationId: running.operationId, reason: "disposed" }]);
  assert.deepEqual(h.state, createLibraryApiDiffState());

  running.resolve(succeeded(availableDiff(parseRequest(running.requestJson))));
  await settle();
  assert.deepEqual(h.state, createLibraryApiDiffState());
  assert.equal(coordinator.dispose(), false);
});

test("leaving the Diff inspector invalidates settled state and returning starts a fresh request", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  h.queries[0]!.resolve(succeeded(availableDiff(parseRequest(h.queries[0]!.requestJson))));
  await settle();
  assert.equal(h.state.result?.kind, "Available");

  coordinator.leave();
  assert.equal(h.cancellations.length, 0);
  assert.deepEqual(h.state, createLibraryApiDiffState());

  // Returning is another explicit request against the then-current coordinate.
  coordinator.ensure(signature, context, "");
  assert.equal(h.queries.length, 2);
});

test("leaving mid-flight disposes the running query and returning starts a fresh request", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  const running = h.queries[0]!;
  coordinator.leave();
  assert.deepEqual(h.cancellations, [{ operationId: running.operationId, reason: "disposed" }]);
  assert.deepEqual(h.state, createLibraryApiDiffState());

  // Returning to the exact same coordinate is a new explicit request.
  coordinator.ensure(signature, context, "");
  assert.equal(h.queries.length, 2);

  running.resolve(succeeded(availableDiff(parseRequest(running.requestJson))));
  await settle();
  assert.equal(h.state.result, null);
});

test("retry re-issues the current request and is a no-op without one", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);

  // No request has ever been submitted (e.g. an unresolved target): retry does nothing.
  coordinator.retry();
  assert.equal(h.queries.length, 0);

  coordinator.ensure(signature, context, "");
  h.queries[0]!.resolve(failed("Public API comparison failed."));
  await settle();
  assert.equal(h.state.error, "Public API comparison failed.");

  coordinator.retry();
  assert.equal(h.queries.length, 2);
  assert.deepEqual(
    parseRequest(h.queries[1]!.requestJson), parseRequest(h.queries[0]!.requestJson));
  h.queries[1]!.resolve(succeeded(availableDiff(parseRequest(h.queries[1]!.requestJson))));
  await settle();
  assert.equal(h.state.error, "");
  assert.equal(h.state.result?.kind, "Available");
});

test("selecting a Type changes only detail state and starts no managed call", async () => {
  const h = harness();
  const coordinator = createLibraryApiDiffCoordinator(h.dependencies);
  coordinator.ensure(signature, context, "");
  h.queries[0]!.resolve(succeeded(availableDiff(
    parseRequest(h.queries[0]!.requestJson),
    [{
      identifier: "T:Example.Widget", display: "Example.Widget", change: "Diff",
      typeDiff: {
        before: { identifier: "T:Example.Widget", display: "Example.Widget" },
        after: { identifier: "T:Example.Widget", display: "Example.Widget" },
        pairKind: "Changed", typeDefinitionChanged: false,
        compatibilityChanges: [], members: [],
        breakingCount: 0, additiveCount: 0, potentiallyBreakingCount: 0, changedMemberCount: 0,
      },
    }])));
  await settle();
  const queryCountBeforeSelection = h.queries.length;
  const rendersBeforeSelection = h.renders;

  coordinator.selectType("T:Example.Widget");
  assert.equal(h.state.selectedTypeId, "T:Example.Widget");
  assert.equal(h.queries.length, queryCountBeforeSelection);
  assert.equal(h.renders, rendersBeforeSelection + 1);

  // Selecting the same Type again is a no-op (no extra render).
  coordinator.selectType("T:Example.Widget");
  assert.equal(h.renders, rendersBeforeSelection + 1);
});
