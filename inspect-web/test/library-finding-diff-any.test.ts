import assert from "node:assert/strict";
import test from "node:test";
import { createBackgroundAnalysisQueue } from "../src/background-analysis.ts";
import type {
  BrowserFindingDiffAnyType,
  BrowserLibraryFindingDiffAnyRequest,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createLibraryFindingDiffAny,
  libraryFindingDiffAnyAchievement,
  libraryFindingDiffAnyKey,
  renderLibraryFindingDiffAnyStatus,
  type LibraryFindingDiffAnyBaseline,
} from "../src/library-finding-diff-any.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

const baseline: LibraryFindingDiffAnyBaseline = {
  packageId: "Fixture",
  currentVersion: "2.0.1",
  targetVersion: "2.0.0",
  targetFramework: "net10.0",
  runtimeIdentifier: "win-x64",
  compileAssetId: "compile:lib/net10.0/Fixture.dll",
  axes: "ApiAndBody",
};

const settle = () => new Promise<void>(resolve => setTimeout(resolve, 0));
const escape = (value: unknown) => String(value);

function type(
  identifier: string,
  api: BrowserFindingDiffAnyType["api"],
  body: BrowserFindingDiffAnyType["body"],
): BrowserFindingDiffAnyType {
  return { identifier, fullName: identifier.replaceAll("+", "."), api, body };
}

function succeeded(request: BrowserLibraryFindingDiffAnyRequest, types: BrowserFindingDiffAnyType[]) {
  return {
    schemaVersion: 3,
    request,
    kind: "Succeeded",
    value: {
      targetAssetId: request.compileAssetId,
      currentAssetId: request.compileAssetId,
      comparedTypeCount: 10,
      types,
    },
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function fixture(respond: (request: BrowserLibraryFindingDiffAnyRequest) => unknown) {
  const requests: BrowserLibraryFindingDiffAnyRequest[] = [];
  const canceled: string[] = [];
  const diagnostics: string[] = [];
  let renders = 0;
  const queue = createBackgroundAnalysisQueue({
    whenForegroundIdle: () => Promise.resolve(),
    reportError: error => { throw error; },
  });
  const findingDiffAny = createLibraryFindingDiffAny({
    queue,
    operationAuthority: createOperationAuthorityPage(),
    query: (_operationId, request) => {
      requests.push(request);
      return Promise.resolve(respond(request));
    },
    cancel: operationId => { canceled.push(String(operationId)); },
    describeError: error => String(error),
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic.kind);
      return undefined;
    },
    render: () => { renders++; },
  });
  return { findingDiffAny, requests, canceled, diagnostics, renders: () => renders };
}

test("cues name the changed axis and the baseline version", () => {
  assert.equal(libraryFindingDiffAnyAchievement(undefined, "2.0.0"), null);
  assert.equal(
    libraryFindingDiffAnyAchievement(type("A", "Unchanged", "Unchanged"), "2.0.0"),
    null);
  assert.equal(
    libraryFindingDiffAnyAchievement(type("A", "Unchanged", "NotCompared"), "2.0.0"),
    null);
  assert.deepEqual(
    libraryFindingDiffAnyAchievement(type("A", "Changed", "Unchanged"), "2.0.0"),
    { kind: "diff", description: "API changed since 2.0.0" });
  assert.deepEqual(
    libraryFindingDiffAnyAchievement(type("A", "Unchanged", "Changed"), "2.0.0"),
    { kind: "diff", description: "Implementation changed since 2.0.0" });
  assert.deepEqual(
    libraryFindingDiffAnyAchievement(type("A", "Changed", "Changed"), "2.0.0"),
    { kind: "diff", description: "API changed, implementation changed since 2.0.0" });
  assert.deepEqual(
    libraryFindingDiffAnyAchievement(type("A", "Unchanged", "Indeterminate"), "2.0.0"),
    { kind: "diff", description: "Implementation undecided since 2.0.0" });
});

test("a baseline result is held per exact request and keyed by identifier", async () => {
  const state = fixture(request => succeeded(request, [
    type("Fixture.Outer+Inner", "Unchanged", "Changed"),
  ]));

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();
  state.findingDiffAny.ensure(baseline, () => true);
  await settle();

  assert.equal(state.requests.length, 1);
  assert.deepEqual(state.requests[0], { schemaVersion: 3, ...baseline });
  const entry = state.findingDiffAny.entry(baseline);
  assert.equal(entry?.status, "ready");
  assert.equal(
    entry?.status === "ready" ? entry.value.get("Fixture.Outer+Inner")?.body : null,
    "Changed");
  assert.equal(state.findingDiffAny.entry({ ...baseline, axes: "Api" }), null);
  assert.ok(state.renders() > 0);
  assert.deepEqual(state.diagnostics, []);
  assert.notEqual(
    libraryFindingDiffAnyKey(baseline),
    libraryFindingDiffAnyKey({
      ...baseline,
      runtimeIdentifier: "linux-x64",
    }),
  );
});

test("a rejected baseline is a visible failure that retry queues again", async () => {
  let reject = true;
  const state = fixture(request => reject
    ? {
        schemaVersion: 3,
        request,
        kind: "Rejected",
        value: null,
        error: "The target package has no matching Library.",
        diagnostic: null,
        reason: null,
      }
    : succeeded(request, []));

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();
  const failed = state.findingDiffAny.entry(baseline);
  assert.equal(failed?.status, "failed");
  assert.match(
    renderLibraryFindingDiffAnyStatus(failed, baseline.targetVersion, escape),
    /role="alert">Changes since 2\.0\.0 unavailable: The target package has no matching Library\.[\s\S]*data-library-finding-diff-any-retry/);
  assert.equal(
    renderLibraryFindingDiffAnyStatus({ status: "loading" }, baseline.targetVersion, escape),
    "");

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();
  assert.equal(state.requests.length, 1);

  reject = false;
  state.findingDiffAny.retry(baseline, () => true);
  await settle();
  assert.equal(state.requests.length, 2);
  assert.equal(state.findingDiffAny.entry(baseline)?.status, "ready");
});

test("a canceled baseline is not held", async () => {
  const state = fixture(request => ({
    schemaVersion: 3,
    request,
    kind: "Canceled",
    value: null,
    error: null,
    diagnostic: null,
    reason: "superseded",
  }));

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();

  assert.equal(state.findingDiffAny.entry(baseline), null);
});

test("a missing runtime counterpart is a visible typed failure", async () => {
  const state = fixture(request => ({
    schemaVersion: 3,
    request,
    kind: "MissingCounterpart",
    value: null,
    error: "The target endpoint cannot issue runtime population 'win-x64'.",
    diagnostic: null,
    reason: null,
    missingCounterpart: {
      endpoint: "Target",
      version: request.targetVersion,
      runtimeIdentifier: "win-x64",
      availableRuntimeIdentifiers: ["linux-x64"],
    },
  }));

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();

  const entry = state.findingDiffAny.entry(baseline);
  assert.equal(entry?.status, "failed");
  assert.match(
    renderLibraryFindingDiffAnyStatus(
      entry,
      baseline.targetVersion,
      escape,
    ),
    /cannot issue runtime population 'win-x64'/,
  );
});

test("a malformed result is a visible failure", async () => {
  const state = fixture(() => ({ schemaVersion: 1, kind: "Succeeded" }));

  state.findingDiffAny.ensure(baseline, () => true);
  await settle();

  const entry = state.findingDiffAny.entry(baseline);
  assert.equal(entry?.status, "failed");
  assert.deepEqual(state.diagnostics, ["producer-contract"]);
});
