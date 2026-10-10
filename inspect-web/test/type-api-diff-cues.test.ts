import assert from "node:assert/strict";
import test from "node:test";
import { createBackgroundAnalysisQueue } from "../src/background-analysis.ts";
import type {
  BrowserLibraryApiDiffRequest,
  BrowserLibraryApiDiffResult,
} from "../src/facades/inspect-web-metadata.d.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import {
  createTypeApiDiffCues,
  renderTypeChangeStatus,
  type TypeApiDiffCueRequest,
} from "../src/type-api-diff-cues.ts";
import { inspection, withMembers } from "./library-api-diff-fixture.ts";

const settle = () => new Promise<void>(resolve => setTimeout(resolve, 0));

const request: TypeApiDiffCueRequest = {
  packageModel: {},
  baseline: {
    packageId: "Example.Package",
    currentVersion: "2.0.0",
    targetVersion: "1.0.0",
    targetFramework: "net11.0",
    compileAssetId: "lib/net11.0/Example.dll",
    axes: "ApiAndBody",
  },
  typeQueryIdentifier: "Example.Widget",
};

function echo(
  result: BrowserLibraryApiDiffResult,
  sent: BrowserLibraryApiDiffRequest,
): BrowserLibraryApiDiffResult {
  return {
    ...result,
    request: sent,
    inspection: inspection(undefined, { surface: "Type", views: "Changes", analyses: ["api"] }),
  };
}

function fixture(respond: (sent: BrowserLibraryApiDiffRequest) => unknown) {
  const sent: BrowserLibraryApiDiffRequest[] = [];
  const diagnostics: string[] = [];
  const cues = createTypeApiDiffCues({
    queue: createBackgroundAnalysisQueue({
      whenForegroundIdle: () => Promise.resolve(),
      reportError: error => { throw error; },
    }),
    operationAuthority: createOperationAuthorityPage(),
    query: (_operationId, body) => {
      sent.push(body);
      return Promise.resolve(respond(body));
    },
    cancel: () => undefined,
    describeError: error => String(error),
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic.kind);
      return undefined;
    },
    render: () => undefined,
  });
  return { cues, sent, diagnostics };
}

test("member cues ask for the API changes of the one selected Type", async () => {
  const state = fixture(body => echo(withMembers(), body));

  state.cues.ensure(request, () => true);
  await settle();

  assert.deepEqual(state.sent, [{
    schemaVersion: 3,
    packageId: "Example.Package",
    currentVersion: "2.0.0",
    targetVersion: "1.0.0",
    targetFramework: "net11.0",
    compileAssetId: "lib/net11.0/Example.dll",
    surface: "Type",
    analyses: ["api"],
    views: "Changes",
    typeNames: ["Example.Widget"],
    memberTargetIdentities: [],
    predicate: null,
  }]);
  const entry = state.cues.entry(request);
  assert.equal(entry?.status, "ready");
  const expected = new Set(
    withMembers().value?.types.flatMap(type =>
      type.members.flatMap(member => member.after ? [member.after.fingerprint] : [])));
  assert.ok(expected.size > 0);
  assert.deepEqual(entry?.status === "ready" ? entry.value : null, expected);
  assert.deepEqual(state.diagnostics, []);
});

test("a rejected Type comparison is a visible failure", async () => {
  const state = fixture(body => ({
    ...echo(withMembers(), body),
    kind: "Rejected",
    value: null,
    inspection: null,
    rejected: {
      kind: "CollectionEntryLimitExceeded",
      target: null,
      current: null,
      bound: 524288,
      observed: 524289,
    },
  }));

  state.cues.ensure(request, () => true);
  await settle();

  assert.deepEqual(state.cues.entry(request), {
    status: "failed",
    error: "The Type's API comparison is unavailable.",
  });
});

test("member cues are held per Type and baseline", async () => {
  const state = fixture(body => echo(withMembers(), body));

  state.cues.ensure(request, () => true);
  await settle();
  state.cues.ensure(request, () => true);
  state.cues.ensure({ ...request, typeQueryIdentifier: "Example.Options" }, () => false);
  await settle();

  assert.equal(state.sent.length, 1);
  assert.equal(
    state.cues.entry({
      ...request,
      baseline: { ...request.baseline, targetVersion: "1.5.0" },
    }),
    null);
});

test("every Type change cue names its Compare view", () => {
  const escape = (value: unknown) => String(value);
  const api = { description: "API changed since 9.0.20", api: true };
  const body = { description: "Implementation changed since 9.0.20", api: false };

  assert.match(
    renderTypeChangeStatus(api, { status: "loading" }, escape),
    /API changed since 9\.0\.20\.\s*<button type="button" data-type-change-compare="api">Compare API<\/button>/);
  assert.match(
    renderTypeChangeStatus(body, null, escape),
    /Implementation changed since 9\.0\.20\.\s*<button type="button" data-type-change-compare="member-body">Compare bodies<\/button>/);
  assert.match(
    renderTypeChangeStatus(api, { status: "ready", value: new Set() }, escape),
    /No member changed; Compare shows the Type-level change\./);
  assert.match(
    renderTypeChangeStatus(api, { status: "failed", error: "Unavailable." }, escape),
    /Member cues unavailable: Unavailable\. <button type="button" data-type-change-retry>Retry<\/button>/);
});
