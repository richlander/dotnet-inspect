import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import type {
  BrowserLibraryApiDiffEndpoint,
  BrowserLibraryApiDiffResult,
  InspectionPortableProjection,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createLibraryApiDiffCoordinator,
  renderLibraryApiDiff,
  type LibraryApiDiffSelection,
  type LibraryApiDiffState,
  type LibraryApiDiffStateHost,
} from "../src/library-api-diff.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

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
  const portableProjection: InspectionPortableProjection = {
    kind: "nonProjectable",
    path: "comparison/endpoints",
    reason: "notSupported",
    explanation: "Ordered endpoints are not shareable.",
    fullUrl: null,
    packet: null,
  };
  return {
    contentKind: "outcome",
    content,
    portableProjection,
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
    { ...result, inspection: { ...inspection(), portableProjection: null } },
    { ...result, inspection: { ...inspection(), contentKind: "document" } },
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
    /function currentLibraryApiDiffSelection\(\)[\s\S]*?\n}\n\nfunction scopedPlatformLibrary/,
  )?.[0] ?? "";
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
