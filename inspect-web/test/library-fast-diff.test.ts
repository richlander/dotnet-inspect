import assert from "node:assert/strict";
import test from "node:test";
import { createBackgroundAnalysisQueue } from "../src/background-analysis.ts";
import type {
  BrowserFastDiffType,
  BrowserLibraryFastDiffRequest,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createLibraryFastDiff,
  libraryFastDiffAchievement,
  renderLibraryFastDiffStatus,
  type LibraryFastDiffBaseline,
} from "../src/library-fast-diff.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

const baseline: LibraryFastDiffBaseline = {
  packageId: "Fixture",
  currentVersion: "2.0.1",
  targetVersion: "2.0.0",
  targetFramework: "net10.0",
  compileAssetId: "compile:lib/net10.0/Fixture.dll",
  axes: "ApiAndBody",
};

const settle = () => new Promise<void>(resolve => setTimeout(resolve, 0));
const escape = (value: unknown) => String(value);

function type(
  identifier: string,
  api: BrowserFastDiffType["api"],
  body: BrowserFastDiffType["body"],
): BrowserFastDiffType {
  return { identifier, fullName: identifier.replaceAll("+", "."), api, body };
}

function succeeded(request: BrowserLibraryFastDiffRequest, types: BrowserFastDiffType[]) {
  return {
    schemaVersion: 2,
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

function fixture(respond: (request: BrowserLibraryFastDiffRequest) => unknown) {
  const requests: BrowserLibraryFastDiffRequest[] = [];
  const canceled: string[] = [];
  const diagnostics: string[] = [];
  let renders = 0;
  const queue = createBackgroundAnalysisQueue({
    whenForegroundIdle: () => Promise.resolve(),
    reportError: error => { throw error; },
  });
  const fastDiff = createLibraryFastDiff({
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
  return { fastDiff, requests, canceled, diagnostics, renders: () => renders };
}

test("cues name the changed axis and the baseline version", () => {
  assert.equal(libraryFastDiffAchievement(undefined, "2.0.0"), null);
  assert.equal(
    libraryFastDiffAchievement(type("A", "Unchanged", "Unchanged"), "2.0.0"),
    null);
  assert.equal(
    libraryFastDiffAchievement(type("A", "Unchanged", "NotCompared"), "2.0.0"),
    null);
  assert.deepEqual(
    libraryFastDiffAchievement(type("A", "Changed", "Unchanged"), "2.0.0"),
    { kind: "api-diff", description: "API changed since 2.0.0" });
  assert.deepEqual(
    libraryFastDiffAchievement(type("A", "Unchanged", "Changed"), "2.0.0"),
    { kind: "body-diff", description: "Implementation changed since 2.0.0" });
  assert.deepEqual(
    libraryFastDiffAchievement(type("A", "Changed", "Changed"), "2.0.0"),
    { kind: "api-diff", description: "API changed, implementation changed since 2.0.0" });
  assert.deepEqual(
    libraryFastDiffAchievement(type("A", "Unchanged", "Indeterminate"), "2.0.0"),
    { kind: "body-diff", description: "Implementation undecided since 2.0.0" });
});

test("a baseline result is held per exact request and keyed by identifier", async () => {
  const state = fixture(request => succeeded(request, [
    type("Fixture.Outer+Inner", "Unchanged", "Changed"),
  ]));

  state.fastDiff.ensure(baseline, () => true);
  await settle();
  state.fastDiff.ensure(baseline, () => true);
  await settle();

  assert.equal(state.requests.length, 1);
  assert.deepEqual(state.requests[0], { schemaVersion: 2, ...baseline });
  const entry = state.fastDiff.entry(baseline);
  assert.equal(entry?.status, "ready");
  assert.equal(
    entry?.status === "ready" ? entry.types.get("Fixture.Outer+Inner")?.body : null,
    "Changed");
  assert.equal(state.fastDiff.entry({ ...baseline, axes: "Api" }), null);
  assert.ok(state.renders() > 0);
  assert.deepEqual(state.diagnostics, []);
});

test("a rejected baseline is a visible failure that retry queues again", async () => {
  let reject = true;
  const state = fixture(request => reject
    ? {
        schemaVersion: 2,
        request,
        kind: "Rejected",
        value: null,
        error: "The target package has no matching Library.",
        diagnostic: null,
        reason: null,
      }
    : succeeded(request, []));

  state.fastDiff.ensure(baseline, () => true);
  await settle();
  const failed = state.fastDiff.entry(baseline);
  assert.equal(failed?.status, "failed");
  assert.match(
    renderLibraryFastDiffStatus(failed, baseline.targetVersion, escape),
    /role="alert">Changes since 2\.0\.0 unavailable: The target package has no matching Library\.[\s\S]*data-library-fast-diff-retry/);
  assert.equal(
    renderLibraryFastDiffStatus({ status: "loading" }, baseline.targetVersion, escape),
    "");

  state.fastDiff.ensure(baseline, () => true);
  await settle();
  assert.equal(state.requests.length, 1);

  reject = false;
  state.fastDiff.retry(baseline, () => true);
  await settle();
  assert.equal(state.requests.length, 2);
  assert.equal(state.fastDiff.entry(baseline)?.status, "ready");
});

test("a canceled baseline is not held", async () => {
  const state = fixture(request => ({
    schemaVersion: 2,
    request,
    kind: "Canceled",
    value: null,
    error: null,
    diagnostic: null,
    reason: "superseded",
  }));

  state.fastDiff.ensure(baseline, () => true);
  await settle();

  assert.equal(state.fastDiff.entry(baseline), null);
});

test("a malformed result is a visible failure", async () => {
  const state = fixture(() => ({ schemaVersion: 1, kind: "Succeeded" }));

  state.fastDiff.ensure(baseline, () => true);
  await settle();

  const entry = state.fastDiff.entry(baseline);
  assert.equal(entry?.status, "failed");
  assert.deepEqual(state.diagnostics, ["producer-contract"]);
});
