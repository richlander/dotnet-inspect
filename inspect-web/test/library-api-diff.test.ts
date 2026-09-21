import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import type {
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffResult,
  InspectionShare,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  bindLibraryApiDiffRows,
  createLibraryApiDiffCoordinator,
  renderLibraryApiDiff,
  type LibraryApiDiffSelection,
  type LibraryApiDiffState,
  type LibraryApiDiffStateHost,
} from "../src/library-api-diff.ts";
import { fakeDom } from "./fake-dom.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import { metadataInertStringFixture } from "./inert-string-fixture.ts";

const appSource = readFileSync(
  new URL("../src/dotnet-inspect.ts", import.meta.url),
  "utf8",
);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

const endpoint = (
  version: string,
  compileAssetId = "lib/net11.0/Example.dll",
): BrowserLibraryApiDiffEndpoint => ({
  packageId: "Example.Package",
  version,
  framework: "net11.0",
  asset: {
    id: compileAssetId,
    path: compileAssetId,
    assemblyName: "Example",
  },
  assembly: {
    name: "Example",
    version,
    culture: null,
    publicKeyToken: null,
  },
  scope: "Public",
  isComplete: true,
  issues: [],
});

function succeeded(
  targetVersion: string,
  currentVersion = "2.0.0",
  compileAssetId = "lib/net11.0/Example.dll",
): BrowserLibraryApiDiffResult {
  return {
    schemaVersion: 1,
    request: {
      schemaVersion: 1,
      packageId: "Example.Package",
      currentVersion,
      targetVersion,
      targetFramework: "net11.0",
      compileAssetId,
    },
    kind: "Succeeded",
    value: {
      libraryIdentifier: "Example",
      libraryDisplay: "Example",
      target: endpoint(targetVersion, compileAssetId),
      current: endpoint(currentVersion, compileAssetId),
      aggregate: {
        changedTypeCount: 2,
        addedTypeCount: 1,
        removedTypeCount: 0,
        changedMemberCount: 3,
        breakingCount: 1,
        additiveCount: 2,
        potentiallyBreakingCount: 0,
      },
      types: [
        {
          documentIdentifier: "Example.Widget",
          display: "Example.Widget",
          state: "Diff",
          typeDefinitionChanged: true,
          changedMemberCount: 2,
          breakingCount: 1,
          additiveCount: 1,
          potentiallyBreakingCount: 0,
          before: {
            identifier: "before-widget",
            namespace: "Example",
            segments: ["Widget"],
            display: "Example.Widget",
          },
          after: {
            identifier: "after-widget",
            namespace: "Example",
            segments: ["Widget"],
            display: "Example.Widget",
          },
          members: [],
          changes: [],
        },
        {
          documentIdentifier: "Example.Options",
          display: "Example.Options",
          state: "Addition",
          typeDefinitionChanged: null,
          changedMemberCount: 1,
          breakingCount: 0,
          additiveCount: 1,
          potentiallyBreakingCount: 0,
          before: null,
          after: {
            identifier: "after-options",
            namespace: "Example",
            segments: ["Options"],
            display: "Example.Options",
          },
          members: [],
          changes: [],
        },
      ],
    },
    unavailable: null,
    rejected: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
    inspection: inspection(),
  };
}

function inspection(
  content: unknown = { outcome: "available", document: {} },
): NonNullable<BrowserLibraryApiDiffResult["inspection"]> {
  const share: InspectionShare = {
    kind: "nonProjectable",
    path: "comparison/endpoints",
    reason: metadataInertStringFixture(
      "Ordered endpoints are not shareable."),
    fullUrl: null,
    packet: null,
  };
  return {
    content,
    share,
    diagnostics: [],
  };
}

function selection(
  packageModel: object,
  targetVersion = "1.0.0",
): LibraryApiDiffSelection {
  return {
    packageModel,
    packageId: "Example.Package",
    currentVersion: "2.0.0",
    targetFramework: "net11.0",
    compileAssetId: "lib/net11.0/Example.dll",
    target: { kind: "available", version: targetVersion },
  };
}

test("unresolved Package targets remain distinct without starting managed work", () => {
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  let queries = 0;
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => {
      queries++;
      return Promise.resolve(succeeded("1.0.0"));
    },
    cancel: () => undefined,
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  coordinator.reconcile({
    ...selection({}),
    target: { kind: "loading", message: "Reading available versions..." },
  });
  assert.equal(state.libraryApiDiff.status, "target-loading");
  if (state.libraryApiDiff.status === "target-loading")
    assert.equal(state.libraryApiDiff.message, "Reading available versions...");
  coordinator.reconcile({
    ...selection({}),
    target: { kind: "unavailable", message: "No earlier version." },
  });
  assert.equal(state.libraryApiDiff.status, "target-unavailable");
  assert.equal(queries, 0);
});

test("replacement Package contexts cancel old work and suppress late publication", async () => {
  const packageOne = {};
  const packageTwo = {};
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const pending = new Map<string, ReturnType<typeof deferred<BrowserLibraryApiDiffResult>>>();
  const cancellations: string[] = [];
  const ids = ["one", "two"];
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => ids.shift() ?? "unexpected" },
    }),
    query: operationId => {
      const result = deferred<BrowserLibraryApiDiffResult>();
      pending.set(operationId, result);
      return result.promise;
    },
    cancel: operationId => {
      cancellations.push(operationId);
    },
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });

  coordinator.reconcile(selection(packageOne, "1.0.0"));
  coordinator.reconcile(selection(packageTwo, "1.5.0"));
  assert.deepEqual(cancellations, ["one"]);
  pending.get("one")?.resolve(succeeded("1.0.0"));
  await Promise.resolve();
  const replacedState = state.libraryApiDiff;
  assert.equal(replacedState.status, "loading");
  if (replacedState.status === "loading")
    assert.equal(replacedState.input.packageModel, packageTwo);

  pending.get("two")?.resolve(succeeded("1.5.0"));
  await Promise.resolve();
  const readState = (): LibraryApiDiffState => state.libraryApiDiff;
  const completedState = readState();
  assert.equal(completedState.status, "ready");
  if (completedState.status === "ready")
    assert.equal(completedState.input.packageModel, packageTwo);
});

test("result request mismatches fail at the Browser transport boundary", async () => {
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const diagnostics: unknown[] = [];
  const result = succeeded("9.0.0");
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => Promise.resolve(result),
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    reportOperationDiagnostic: diagnostic => {
      diagnostics.push(diagnostic);
      return undefined;
    },
    render: () => undefined,
  });
  coordinator.reconcile(selection({}));
  await Promise.resolve();
  assert.equal(state.libraryApiDiff.status, "failed");
  assert.match(
    state.libraryApiDiff.status === "failed"
      ? state.libraryApiDiff.error
      : "",
    /does not match/,
  );
  assert.equal(diagnostics.length, 1);
});

test("malformed success is recoverable and cannot enter ready rendering", async () => {
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  let queryCount = 0;
  const malformed = succeeded("1.0.0");
  const { value: _omitted, ...withoutValue } = malformed;
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => Promise.resolve(
      queryCount++ === 0 ? withoutValue : succeeded("1.0.0"),
    ),
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  const active = selection({});

  coordinator.reconcile(active);
  await Promise.resolve();
  assert.equal(state.libraryApiDiff.status, "failed");

  coordinator.retry(active);
  await Promise.resolve();
  assert.equal(state.libraryApiDiff.status, "ready");
});

test("missing or contradictory baselines cannot publish a successful comparison", async () => {
  const result = succeeded("1.0.0");
  const { inspection: _omitted, ...withoutInspection } = result;
  for (const malformed of [
    withoutInspection,
    { ...result, inspection: null },
    {
      ...result,
      inspection: inspection({
        outcome: "unavailable", kind: 0, before: {}, after: {},
      }),
    },
    { ...result, inspection: { ...inspection(), diagnostics: null } },
    { ...result, inspection: { ...inspection(), share: null } },
  ]) {
    const state: LibraryApiDiffStateHost = {
      libraryApiDiff: { status: "idle" },
    };
    const coordinator = createLibraryApiDiffCoordinator({
      state,
      operationAuthority: createOperationAuthorityPage(),
      query: () => Promise.resolve(malformed),
      cancel: () => undefined,
      describeError: String,
      reportOperationDiagnostic: () => undefined,
      render: () => undefined,
    });
    coordinator.reconcile(selection({}));
    await Promise.resolve();
    assert.equal(state.libraryApiDiff.status, "failed");
  }
});

test("ready state retains the baseline and conditionally discloses ordered diagnostics", async () => {
  const baseline = {
    ...inspection(),
    diagnostics: [
      { code: "first", severity: 1, summary: "<warning>", correspondence: "T:Widget" },
      { code: "second", severity: 0, summary: "information", correspondence: null },
    ],
  };
  const result = { ...succeeded("1.0.0"), inspection: baseline };
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => Promise.resolve(result),
    cancel: () => undefined,
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  coordinator.reconcile(selection({}));
  await Promise.resolve();
  assert.equal(state.libraryApiDiff.status, "ready");
  if (state.libraryApiDiff.status !== "ready")
    throw new Error("Expected ready state.");
  assert.equal(state.libraryApiDiff.result.inspection, baseline);
  const html = renderLibraryApiDiff(
    state.libraryApiDiff,
    text => String(text).replaceAll("<", "&lt;").replaceAll(">", "&gt;"),
  );
  assert.match(html, /Inspection diagnostics \(2\)/);
  assert.match(html, /Warning first: &lt;warning&gt; \(T:Widget\)/);
  assert.ok(html.indexOf("first") < html.indexOf("second"));
  assert.doesNotMatch(
    renderLibraryApiDiff({ ...state.libraryApiDiff, result: succeeded("1.0.0") }, String),
    /Inspection diagnostics/,
  );
});

test("transport rejection has no partial baseline", async () => {
  for (const baseline of [null, inspection()]) {
    const result: BrowserLibraryApiDiffResult = {
      ...succeeded("1.0.0"),
      kind: "Rejected",
      value: null,
      inspection: baseline,
      rejected: {
        kind: "CollectionEntryLimitExceeded",
        target: null,
        current: null,
        bound: 524288,
        observed: 524289,
      },
    };
    const state: LibraryApiDiffStateHost = { libraryApiDiff: { status: "idle" } };
    const coordinator = createLibraryApiDiffCoordinator({
      state,
      operationAuthority: createOperationAuthorityPage(),
      query: () => Promise.resolve(result),
      cancel: () => undefined,
      describeError: String,
      reportOperationDiagnostic: () => undefined,
      render: () => undefined,
    });
    coordinator.reconcile(selection({}));
    await Promise.resolve();
    assert.equal(state.libraryApiDiff.status, baseline === null ? "ready" : "failed");
  }
});

test("leaving Compare cancels delayed work and suppresses its completion", async () => {
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const pending = deferred<BrowserLibraryApiDiffResult>();
  const cancellations: string[] = [];
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => "route-operation" },
    }),
    query: () => pending.promise,
    cancel: operationId => {
      cancellations.push(operationId);
    },
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });

  coordinator.reconcile(selection({}));
  coordinator.reconcile(null);
  assert.deepEqual(cancellations, ["route-operation"]);
  assert.equal(state.libraryApiDiff.status, "idle");

  pending.resolve(succeeded("1.0.0"));
  await Promise.resolve();
  assert.equal(state.libraryApiDiff.status, "idle");
});

test("application admission closes on every non-Compare route", () => {
  const selectionSource = appSource.match(
    /function currentCompareSubject\(\)[\s\S]*?\n}\n\nfunction currentCompareMode/,
  )?.[0] ?? "";
  assert.match(
    appSource,
    /function currentLibraryApiDiffSelection\(\)[\s\S]*?const subject = currentCompareSubject\(\);\s*if \(!subject \|\| currentCompareMode\(\) !== "diff"\) return null;/,
    "Diff work is admitted only through the shared Compare subject gate and the retained Diff mode");
  for (const condition of [
    "state.home",
    "state.credits",
    "state.packageQueryOpen",
    "isDiagnosticsPath(location.pathname)",
    "!state.engineReady",
    "state.loading",
    "Boolean(state.error)",
  ]) {
    assert.ok(
      selectionSource.includes(condition),
      `expected Library API Diff admission to reject ${condition}`,
    );
  }
});

test("successful rendering preserves producer order and exact nullable Type identities", () => {
  const html = renderLibraryApiDiff({
    status: "ready",
    input: {
      packageModel: {},
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    result: succeeded("1.0.0"),
  }, value => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll('"', "&quot;"));

  assert.match(html, /1\.0\.0 → 2\.0\.0/);
  assert.match(html, /2 changed Types/);
  assert.match(html, /data-before-type-id="before-widget"/);
  assert.match(html, /data-after-type-id="after-widget"/);
  assert.match(html, /data-before-type-id="" data-after-type-id="after-options"/);
  assert.ok(html.indexOf("Example.Widget") < html.indexOf("Example.Options"));
  assert.doesNotMatch(html, /<button[^>]+data-(?:before|after)-type-id/);
});

test("successful rendering preserves carriage returns in Type identity attributes", () => {
  const result = succeeded("1.0.0");
  if (result.value === null) throw new Error("Expected success.");
  const [template] = result.value.types;
  if (template === undefined || template.before === null
      || template.after === null) {
    throw new Error("Expected a paired Type result.");
  }
  const html = renderLibraryApiDiff({
    status: "ready",
    input: {
      packageModel: {},
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    result: {
      ...result,
      value: {
        ...result.value,
        aggregate: {
          ...result.value.aggregate,
          changedTypeCount: 1,
        },
        types: [{
          ...template,
          before: {
            ...template.before,
            identifier: "Example.A\rB",
          },
          after: {
            ...template.after,
            identifier: "Example.A\nB",
          },
        }],
      },
    },
  }, value => String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll('"', "&quot;"));

  assert.match(html, /data-before-type-id="Example\.A&#13;B"/);
  assert.match(html, /data-after-type-id="Example\.A\nB"/);
});

test("successful empty results stay distinct from target and endpoint unavailability", () => {
  const empty = succeeded("2.0.0");
  if (empty.value === null) throw new Error("Expected success.");
  const result = {
    ...empty,
    value: {
      ...empty.value,
      types: [],
      aggregate: {
        ...empty.value.aggregate,
        changedTypeCount: 0,
        changedMemberCount: 0,
        breakingCount: 0,
        additiveCount: 0,
      },
    },
  };
  const html = renderLibraryApiDiff({
    status: "ready",
    input: {
      packageModel: {},
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "2.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    result,
  }, String);
  assert.match(html, /No public API changes/);
  assert.match(html, /Comparison complete/);
  assert.doesNotMatch(html, /unavailable/i);
});

test("unavailable rendering discloses retained endpoint failure evidence", () => {
  const result: BrowserLibraryApiDiffResult = {
    ...succeeded("1.0.0"),
    kind: "Unavailable",
    inspection: inspection({
      outcome: "unavailable", kind: 0, before: {}, after: {},
    }),
    value: null,
    unavailable: {
      kind: "TargetIncomplete",
      target: {
        ...endpoint("1.0.0"),
        isComplete: false,
        issues: [{
          kind: "InspectionFailures",
          truncation: null,
          openFailureKind: null,
          detail: null,
          metadataRootReason: null,
          count: 1,
          inspectionFailures: [{
            operation: "generic-constraint",
            subjectToken: 0x02000001,
            mechanism: "Signature",
            kind: "MalformedSignature",
            detail: "constraint failed",
            subjectAssembly: endpoint("1.0.0").assembly,
            dependencyAssembly: {
              name: "Dependency",
              version: "2.0.0.0",
              culture: null,
              publicKeyToken: null,
            },
          }],
        }],
      },
      current: endpoint("2.0.0"),
    },
  };
  const html = renderLibraryApiDiff({
    status: "ready",
    input: {
      packageModel: {},
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    result,
  }, String);

  assert.match(html, /Target endpoint evidence/);
  assert.match(html, /generic-constraint at 0x02000001/);
  assert.match(html, /MalformedSignature/);
  assert.match(html, /Dependency: Dependency, 2\.0\.0\.0/);
});

function readyState(result: BrowserLibraryApiDiffResult): LibraryApiDiffState {
  return {
    status: "ready",
    input: {
      packageModel: {},
      packageId: "Example.Package",
      currentVersion: "2.0.0",
      targetVersion: "1.0.0",
      targetFramework: "net11.0",
      compileAssetId: "lib/net11.0/Example.dll",
    },
    result,
  };
}

function withMembers(): BrowserLibraryApiDiffResult {
  const result = succeeded("1.0.0");
  if (result.value === null) throw new Error("Expected success.");
  const [widget, options] = result.value.types;
  if (widget === undefined || options === undefined)
    throw new Error("Expected two Types.");
  const identity = (
    fingerprint: string,
    canonicalSignature: string,
    display: string,
  ) => ({
    declaringTypeIdentifier: "after-widget",
    stableSelector: "Run",
    canonicalSignature,
    fingerprint,
    typeFullName: "Example.Widget",
    memberName: "Run",
    display,
  });
  return {
    ...result,
    value: {
      ...result.value,
      types: [
        {
          ...widget,
          members: [
            {
              documentIdentifier: "relation-changed",
              pairKind: "Changed",
              role: "Both",
              before: identity("digest-before", "void Run(int)", "Run(int)"),
              after: identity("digest-run", "void Run(long)", "Run(long)"),
              match: null,
              changes: [
                {
                  kind: "MemberSignatureChanged",
                  classification: "Breaking",
                  category: "Signature",
                  message: "Parameter type changed from int to long.",
                  oldValue: "void Run(int)",
                  newValue: "void Run(long)",
                },
                {
                  kind: "MemberAttributeAdded",
                  classification: "PotentiallyBreaking",
                  category: "Attribute",
                  message: "[Obsolete] was added.",
                  oldValue: null,
                  newValue: "Obsolete",
                },
              ],
            },
            {
              documentIdentifier: "relation-added",
              pairKind: "Added",
              role: "After",
              before: null,
              after: identity("digest-new", "void New()", "New()"),
              match: null,
              changes: [{
                kind: "MemberAdded",
                classification: "Additive",
                category: "Signature",
                message: "Member New() was added.",
                oldValue: null,
                newValue: null,
              }],
            },
            {
              documentIdentifier: "relation-removed",
              pairKind: "Removed",
              role: "Before",
              before: identity("digest-gone", "void Gone()", "Gone()"),
              after: null,
              match: null,
              changes: [],
            },
          ],
          changes: [{
            kind: "SealedAdded",
            classification: "Breaking",
            category: "Signature",
            message: "Type became sealed.",
            oldValue: null,
            newValue: "sealed",
          }],
        },
        // A removed Type keeps Before-side evidence and no current identity.
        {
          ...options,
          state: "Deletion",
          before: {
            identifier: "before-options",
            namespace: "Example",
            segments: ["Options"],
            display: "Example.Options",
          },
          after: null,
          changes: [{
            kind: "TypeRemoved",
            classification: "Breaking",
            category: "Signature",
            message: "Type Example.Options was removed.",
            oldValue: null,
            newValue: null,
          }],
        },
      ],
    },
  };
}

test("Type Diff lists Type-level changes first and classifies each Member row from producer changes", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: { kind: "type", typeIdentifier: "after-widget" },
    activatableMembers: new Set(["digest-run", "digest-new"]),
  });
  assert.match(html, /<ol class="library-api-diff-changes" aria-label="Type-level changes">[\s\S]*?<strong>sealed added<\/strong>\s*<span>Type became sealed\.<\/span>[\s\S]*?<code>—<\/code> → <code>sealed<\/code>/);
  assert.ok(html.indexOf('aria-label="Type-level changes"') < html.indexOf('aria-label="Changed Members"'));
  assert.match(html, /library-api-diff-change-chip library-api-diff-change-breaking">Breaking · member signature changed</);
  assert.match(html, /library-api-diff-change-chip library-api-diff-change-potentiallybreaking">Potentially breaking · member attribute added</);
  assert.match(html, /library-api-diff-change-chip library-api-diff-change-additive">Additive · member added</);
  // The removed Member carries no classified change of its own and gets no chip.
  const removedRow = html.match(/<li class="library-api-diff-member library-api-diff-member-inert"[\s\S]*?<\/li>/)?.[0] ?? "";
  assert.doesNotMatch(removedRow, /library-api-diff-change-chip/);
});

test("Member Diff renders the producer's change rows with message, values, and category", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-run",
    },
  });
  assert.match(html, /<h2 id="library-api-diff-changes-title">What changed<\/h2>/);
  const rows = [...html.matchAll(/<li class="library-api-diff-change">/g)];
  assert.equal(rows.length, 2);
  assert.match(html, /<strong>member signature changed<\/strong>\s*<span>Parameter type changed from int to long\.<\/span>\s*<span class="library-api-diff-change-values"><code>void Run\(int\)<\/code> → <code>void Run\(long\)<\/code><\/span>\s*<span class="library-api-diff-change-category">Signature<\/span>/);
  assert.match(html, /<strong>member attribute added<\/strong>[\s\S]*?<code>—<\/code> → <code>Obsolete<\/code>[\s\S]*?Attribute<\/span>/);
  assert.match(html, /<span>Breaking · member signature changed<\/span>/);

  // A Member inside a removed Type has no change of its own; say so instead of
  // showing an empty table.
  const carried = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-gone",
    },
  });
  assert.match(carried, /No classified compatibility change is recorded for this Member\./);
  assert.doesNotMatch(carried, /<li class="library-api-diff-change">/);
});

test("malformed change rows are rejected at the transport boundary", async () => {
  const result = withMembers();
  if (result.value === null) throw new Error("Expected success.");
  const [widget] = result.value.types;
  if (widget === undefined) throw new Error("Expected a Type.");
  const malformed = {
    ...result,
    value: {
      ...result.value,
      types: [{
        ...widget,
        changes: [{ ...widget.changes[0], classification: "Catastrophic" }],
      }],
    },
  };
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const coordinator = createLibraryApiDiffCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: () => Promise.resolve(malformed),
    cancel: () => undefined,
    describeError: error => error instanceof Error ? error.message : String(error),
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });
  coordinator.reconcile(selection({}));
  await Promise.resolve();
  await Promise.resolve();
  const settled = (): LibraryApiDiffState => state.libraryApiDiff;
  const outcome = settled();
  assert.equal(outcome.status, "failed");
  if (outcome.status === "failed")
    assert.match(outcome.error, /types\[0\]\.changes\[0\]\.classification is unsupported/);
});

test("Library rows activate only joined current-side Types; removed Types stay visible and inert", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: { kind: "library" },
    subjectLabel: "Example",
    activatableTypes: new Set(["after-widget"]),
    targetText: "1.0.0 → 2.0.0",
  });
  assert.match(html, /<h1 id="compare-title">Example<\/h1>/);
  assert.match(html, /data-compare-mode="diff" aria-selected="true"/);
  assert.match(html, /data-compare-mode="clone" aria-selected="false"/);
  assert.match(html, /Diff baseline<\/span>\s*<span class="compare-target-value">1\.0\.0 → 2\.0\.0/);
  assert.match(html, /<button type="button" class="library-api-diff-row" data-compare-type-id="after-widget"/);
  assert.doesNotMatch(html, /data-compare-type-id="before-options"/);
  assert.match(html, /data-before-type-id="before-options" data-after-type-id=""><div class="library-api-diff-row" aria-disabled="true">/);
  assert.match(html, /Removed in the current version; Before-side evidence only/);
  // No selected-Type detail pane and no Library Explore action.
  assert.doesNotMatch(html, /Explore/);
  assert.doesNotMatch(html, /Whole type diff/);
});

test("a current-side Type that is not joined to a loaded subject stays inert with its reason", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: { kind: "library" },
    activatableTypes: new Set(),
  });
  assert.doesNotMatch(html, /data-compare-type-id=/);
  assert.match(html, /Not joined to a loaded Type/);
});

test("Type Diff projects the exact Type's Members from the Library-root document", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: { kind: "type", typeIdentifier: "after-widget" },
    subjectLabel: "Example.Widget",
    activatableMembers: new Set(["digest-run", "digest-new"]),
  });
  assert.match(html, /compare-surface-type/);
  assert.match(html, /Comparison complete\. 3 changed Members\./);
  assert.match(html, /2 changed Members<\/span>/);
  assert.match(html, /Type definition changed/);
  const rows = [...html.matchAll(/<li class="library-api-diff-member([^"]*)"/g)]
    .map(match => match[1]);
  assert.deepEqual(rows, ["", "", " library-api-diff-member-inert"]);
  assert.match(html, /data-compare-member-fingerprint="digest-run"/);
  assert.match(html, /data-compare-member-fingerprint="digest-new"/);
  assert.doesNotMatch(html, /data-compare-member-fingerprint="digest-gone"/);
  assert.match(html, /data-member-before-fingerprint="digest-gone"><div class="library-api-diff-row" aria-disabled="true">/);
  assert.match(html, /<code>void Run\(int\)<\/code> → <code>void Run\(long\)<\/code>/);
  // No whole-Type destination is owner-issued, so no row advertises one.
  assert.doesNotMatch(html, /Whole type diff/);
});

test("Type Diff on an unchanged Type is a successful empty result inside the same frame", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: { kind: "type", typeIdentifier: "after-unrelated" },
  });
  assert.match(html, /Comparison complete\. No changed Members\./);
  assert.match(html, /This Type is unchanged between these versions\./);
  assert.match(html, /data-compare-mode="clone"/);
});

test("Member Diff presents the exact Member relation evidence and no Explore action", () => {
  const html = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-run",
    },
    subjectLabel: "Example.Widget.Run",
  });
  assert.match(html, /compare-surface-member/);
  assert.match(html, /Comparison complete\. Member changed\./);
  assert.match(html, /<h2>Before<\/h2>\s*<p><code>Run\(int\)<\/code>/);
  assert.match(html, /<h2>After<\/h2>\s*<p><code>Run\(long\)<\/code>/);
  assert.match(html, /<dt>Digest<\/dt><dd><code>digest-run<\/code>/);
  assert.match(html, /Correspondence identifier <code>relation-changed<\/code>/);
  assert.doesNotMatch(html, /Explore/);

  const removed = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-gone",
    },
  });
  assert.match(removed, /Member removed\./);
  assert.match(removed, /<h2>After<\/h2>\s*<p class="library-api-diff-absent">Not present on this side\./);

  const unchanged = renderLibraryApiDiff(readyState(withMembers()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-unrelated",
    },
  });
  assert.match(unchanged, /This Member is unchanged between these versions\./);
});

test("failure, loading, and unavailable states keep the mode control and target row at every subject", () => {
  for (const subject of [
    { kind: "library" } as const,
    { kind: "type", typeIdentifier: "after-widget" } as const,
    {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-run",
    } as const,
  ]) {
    const input = readyState(withMembers());
    if (input.status !== "ready") throw new Error("Expected ready state.");
    for (const state of [
      { status: "loading", input: input.input } as const,
      { status: "failed", input: input.input, error: "Boom" } as const,
      {
        status: "target-unavailable",
        selection: selection({}),
        message: "No earlier listed version is available.",
      } as const,
    ]) {
      const html = renderLibraryApiDiff(state, String, { subject, mode: "diff" });
      assert.match(html, /role="tablist" aria-label="Compare modes"/, subject.kind);
      assert.match(html, /id="compare-change-target"/, subject.kind);
      assert.doesNotMatch(html, /library-api-diff-row/, subject.kind);
      if (state.status === "failed")
        assert.match(html, /id="compare-retry"/, subject.kind);
    }
  }
});

test("row bindings dispatch owner-issued identities and ignore inert rows", () => {
  const activated: string[] = [];
  const buttons = (attribute: string, values: string[]) => values.map(value => {
    let handler: (() => void) | undefined;
    return {
      dataset: { [attribute]: value },
      addEventListener: (type: string, listener: () => void) => {
        if (type === "click") handler = listener;
      },
      click: () => handler?.(),
    };
  });
  const typeButtons = buttons("compareTypeId", ["after-widget", ""]);
  const memberButtons = buttons("compareMemberFingerprint", ["digest-run"]);
  bindLibraryApiDiffRows(fakeDom.parentNode({
    querySelectorAll: (selector: string) =>
      selector === "[data-compare-type-id]" ? typeButtons
        : selector === "[data-compare-member-fingerprint]" ? memberButtons
          : [],
  }), {
    activateType: identifier => activated.push(`type:${identifier}`),
    activateMember: fingerprint => activated.push(`member:${fingerprint}`),
  });
  for (const button of [...typeButtons, ...memberButtons]) button.click();
  assert.deepEqual(activated, ["type:after-widget", "member:digest-run"]);
});

function withMovedMember(): BrowserLibraryApiDiffResult {
  const result = withMembers();
  if (result.value === null) throw new Error("Expected success.");
  const [widget, options] = result.value.types;
  if (widget === undefined || options === undefined)
    throw new Error("Expected two Types.");
  const moved = (declaringTypeIdentifier: string, typeFullName: string) => ({
    declaringTypeIdentifier,
    stableSelector: "Transform",
    canonicalSignature: "int Transform(int)",
    fingerprint: "digest-transform",
    typeFullName,
    memberName: "Transform",
    display: "Transform(int)",
  });
  const relation = (role: "Before" | "After") => ({
    documentIdentifier: "relation-moved",
    pairKind: "Changed" as const,
    role,
    before: moved("before-options", "Example.Options"),
    after: moved("after-widget", "Example.Widget"),
    changes: [],
    match: { tier: "signature", confidence: 80 },
  });
  return {
    ...result,
    value: {
      ...result.value,
      types: [
        { ...widget, members: [...widget.members, relation("After")] },
        {
          ...options,
          state: "Diff",
          after: {
            identifier: "after-options",
            namespace: "Example",
            segments: ["Options"],
            display: "Example.Options",
          },
          members: [relation("Before")],
        },
      ],
    },
  };
}

test("a moved Member shows its counterpart placement from exact declaring-Type identity", () => {
  // Before-role placement: no current subject here; the counterpart Type is
  // activatable when it is loaded.
  const before = renderLibraryApiDiff(readyState(withMovedMember()), String, {
    subject: { kind: "type", typeIdentifier: "after-options" },
    activatableMembers: new Set(["digest-transform"]),
    activatableTypes: new Set(["after-widget"]),
  });
  const beforeRow = before.match(/<li class="library-api-diff-member[^"]*" data-member-fingerprint="digest-transform"[\s\S]*?<\/li>/)?.[0] ?? "";
  assert.match(beforeRow, /library-api-diff-member-inert/);
  assert.doesNotMatch(beforeRow, /data-compare-member-fingerprint/);
  assert.match(beforeRow, /Now declared on Example\.Widget/);
  assert.match(beforeRow, /<button type="button" class="library-api-diff-counterpart" data-compare-type-id="after-widget">Open Example\.Widget<\/button>/);
  assert.doesNotMatch(beforeRow, /Moved from/);

  // Without a loaded counterpart there is no activation, only the fact.
  const unloaded = renderLibraryApiDiff(readyState(withMovedMember()), String, {
    subject: { kind: "type", typeIdentifier: "after-options" },
    activatableMembers: new Set(["digest-transform"]),
  });
  assert.doesNotMatch(unloaded, /library-api-diff-counterpart/);
  assert.match(unloaded, /Now declared on Example\.Widget/);

  // After-role placement: activatable here, with provenance from the producer.
  const after = renderLibraryApiDiff(readyState(withMovedMember()), String, {
    subject: { kind: "type", typeIdentifier: "after-widget" },
    activatableMembers: new Set(["digest-transform"]),
    activatableTypes: new Set(["after-options"]),
  });
  const afterRow = after.match(/<li class="library-api-diff-member" data-member-fingerprint="digest-transform"[\s\S]*?<\/li>/)?.[0] ?? "";
  assert.match(afterRow, /data-compare-member-fingerprint="digest-transform"/);
  assert.match(afterRow, /<span class="library-api-diff-moved">Moved from Example\.Options · Matched by signature at 80% confidence<\/span>/);
  assert.doesNotMatch(afterRow, /library-api-diff-counterpart/);

  // A same-Type relation shows no move evidence.
  assert.doesNotMatch(
    after.match(/data-member-fingerprint="digest-run"[\s\S]*?<\/li>/)?.[0] ?? "",
    /Moved from|Now declared on/);
});

test("Member Diff states the correspondence and the move when the producer issued them", () => {
  const html = renderLibraryApiDiff(readyState(withMovedMember()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-transform",
    },
  });
  assert.match(html, /<p class="library-api-diff-note library-api-diff-correspondence">Matched by signature at 80% confidence · Moved from Example\.Options to Example\.Widget<\/p>/);
  const plain = renderLibraryApiDiff(readyState(withMovedMember()), String, {
    subject: {
      kind: "member",
      typeIdentifier: "after-widget",
      memberFingerprint: "digest-run",
    },
  });
  assert.doesNotMatch(plain, /library-api-diff-correspondence/);
});

test("malformed match provenance is rejected at the transport boundary", async () => {
  const result = withMovedMember();
  if (result.value === null) throw new Error("Expected success.");
  const [widget] = result.value.types;
  if (widget === undefined) throw new Error("Expected a Type.");
  const state: LibraryApiDiffStateHost = {
    libraryApiDiff: { status: "idle" },
  };
  const settled = (): LibraryApiDiffState => state.libraryApiDiff;
  for (const [match, message] of [
    [{ tier: "signature", confidence: 100 }, /match\.confidence must be between 1 and 99/],
    [{ tier: "", confidence: 50 }, /match\.tier must not be empty/],
  ] as const) {
    const malformed = {
      ...result,
      value: {
        ...result.value,
        types: [{
          ...widget,
          members: widget.members.map(member =>
            member.documentIdentifier === "relation-moved"
              ? { ...member, match }
              : member),
        }],
      },
    };
    const coordinator = createLibraryApiDiffCoordinator({
      state,
      operationAuthority: createOperationAuthorityPage(),
      query: () => Promise.resolve(malformed),
      cancel: () => undefined,
      describeError: error => error instanceof Error ? error.message : String(error),
      reportOperationDiagnostic: () => undefined,
      render: () => undefined,
    });
    coordinator.reconcile(selection({}));
    await Promise.resolve();
    await Promise.resolve();
    const outcome = settled();
    assert.equal(outcome.status, "failed");
    if (outcome.status === "failed") assert.match(outcome.error, message);
    coordinator.cancelCurrentRequest();
  }
});
