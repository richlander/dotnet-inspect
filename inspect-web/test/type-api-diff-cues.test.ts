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
  typeIdentifier: "after-widget",
};

// A second changed Type whose API change is only in its declaration.
function withDeclarationOnlyType(): BrowserLibraryApiDiffResult {
  const result = withMembers();
  const value = result.value;
  const widget = value?.types[0];
  if (!value || !widget) throw new Error("Expected a changed Type.");
  return {
    ...result,
    value: {
      ...value,
      types: [
        ...value.types,
        {
          ...widget,
          documentIdentifier: "Example.Gadget",
          display: "Example.Gadget",
          typeDefinitionChanged: true,
          changedMemberCount: 0,
          before: { ...widget.before!, identifier: "before-gadget", segments: ["Gadget"], display: "Example.Gadget" },
          after: { ...widget.after!, identifier: "after-gadget", segments: ["Gadget"], display: "Example.Gadget" },
          members: [],
        },
      ],
    },
  };
}

function echo(
  result: BrowserLibraryApiDiffResult,
  sent: BrowserLibraryApiDiffRequest,
): BrowserLibraryApiDiffResult {
  return {
    ...result,
    request: sent,
    inspection: inspection(undefined, {
      surface: "Type",
      views: "Changes",
      analyses: ["api", "api-attribute"],
    }),
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
    analyses: ["api", "api-attribute"],
    views: "Changes",
    typeNames: ["Example.Widget"],
    memberTargetIdentities: [],
    predicate: null,
  }]);
  const entry = state.cues.entry(request);
  assert.equal(entry?.status, "ready");
  const widget = withMembers().value?.types.find(type =>
    type.after?.identifier === "after-widget");
  const expected = new Set(widget?.members.flatMap(member =>
    member.after ? [member.after.fingerprint] : []));
  assert.ok(expected.size > 0);
  assert.deepEqual(entry?.status === "ready" ? entry.value : null, {
    found: true,
    declarationChanged: true,
    memberFingerprints: expected,
  });
  assert.deepEqual(state.diagnostics, []);
});

test("member cues keep only the selected Type's row of the Library-wide value", async () => {
  const state = fixture(body => echo(withDeclarationOnlyType(), body));
  const gadget = { ...request, typeQueryIdentifier: "Example.Gadget", typeIdentifier: "after-gadget" };
  const absent = { ...request, typeQueryIdentifier: "Example.Absent", typeIdentifier: "after-absent" };

  state.cues.ensure(gadget, () => true);
  state.cues.ensure(absent, () => true);
  await settle();

  const declarationOnly = state.cues.entry(gadget);
  assert.deepEqual(declarationOnly?.status === "ready" ? declarationOnly.value : null, {
    found: true,
    declarationChanged: true,
    memberFingerprints: new Set(),
  });
  assert.match(
    renderTypeChangeStatus(
      { description: "API changed since 1.0.0", api: true, body: false },
      declarationOnly,
      String),
    /The change is in the Type's declaration\./);
  const missing = state.cues.entry(absent);
  assert.match(
    renderTypeChangeStatus(
      { description: "API changed since 1.0.0", api: true, body: false },
      missing,
      String),
    /The complete API diff shows no change for this Type\./);
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
  const api = { description: "API changed since 9.0.20", api: true, body: false };
  const body = { description: "Implementation changed since 9.0.20", api: false, body: true };
  const both = { description: "API changed, implementation changed since 9.0.20", api: true, body: true };

  const apiLine = renderTypeChangeStatus(api, { status: "loading" }, escape);
  assert.match(apiLine, /class="item-achievement-glyph api-diff"/);
  assert.match(apiLine, /API changed since 9\.0\.20<\/span>\s*<button type="button" class="type-change-action" data-type-change-compare="api">Compare API<\/button><\/p>/);
  const bodyLine = renderTypeChangeStatus(body, null, escape);
  assert.match(bodyLine, /class="item-achievement-glyph body-diff"/);
  assert.match(bodyLine, /data-type-change-compare="member-body">Compare bodies<\/button>/);
  assert.doesNotMatch(bodyLine, /data-type-change-compare="api"/);
  const bothLine = renderTypeChangeStatus(both, null, escape);
  assert.match(bothLine, /data-type-change-compare="api">Compare API[\s\S]*data-type-change-compare="member-body">Compare bodies/);
  assert.doesNotMatch(
    renderTypeChangeStatus(api, {
      status: "ready",
      value: { found: true, declarationChanged: false, memberFingerprints: new Set(["m"]) },
    }, escape),
    /type-change-detail/);
  assert.match(
    renderTypeChangeStatus(api, { status: "failed", error: "Unavailable." }, escape),
    /Member cues unavailable: Unavailable\.<\/span>\s*<button type="button" class="type-change-action" data-type-change-retry>Retry<\/button>/);
});
