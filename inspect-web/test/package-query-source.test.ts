import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageQueryDataSource,
  packageQueryFacets,
  type BrowserPackageQueryEngine,
} from "../src/package-query-source.ts";
import {
  createPackageQueryController,
  createQueryRequest,
  initialQueryState,
  withFacet,
  type QueryResultRow,
} from "../src/package-query.ts";
import type {
  BrowserPackageQueryCancellation,
  BrowserPackageQueryCompletion,
  BrowserPackageQueryEvent,
  BrowserPackageQueryFacetCatalog,
  BrowserPackageQueryMatchCreditResponse,
  BrowserPackageQueryResult,
} from "../src/facades/inspect-web-package.d.ts";

const completionEvent: BrowserPackageQueryEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  progress: null,
  assessment: null,
  completion: {
    prefix: "Microsoft.",
    producer: "nuget.org",
    candidateLimit: 200,
    matchLimit: 100,
    candidates: 1,
    matches: 1,
    failures: 1,
    sourceCandidates: null,
    semanticMisses: null,
    notApplicable: null,
    scope: null,
    kind: "Exhausted",
  },
};

function succeeded(
  value: BrowserPackageQueryEvent,
): BrowserPackageQueryResult {
  return {
    version: 3,
    kind: "Succeeded",
    value: null,
    inspection: {
      content: {
        results: [],
        failures: [],
        completion: value.completion!,
      },
      share: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        path: "package-query/share",
        reason: "No canonical Workspace packet.",
      },
      diagnostics: [],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function cancellation(
  kind: BrowserPackageQueryCancellation["kind"] = "Requested",
  reason: string | null = "user",
): BrowserPackageQueryCancellation {
  return { kind, reason };
}

function credit(
  additionalMatchCredit: number,
  granted = true,
): BrowserPackageQueryMatchCreditResponse {
  return granted
    ? { kind: "Granted", additionalMatchCredit }
    : { kind: "NotActive", additionalMatchCredit: null };
}

const defaultControls = {
  cancel() {
    return cancellation();
  },
  requestMatches(_operationId: string, additionalMatchCredit: number) {
    return credit(additionalMatchCredit);
  },
};

function packageEvidence(
  id: string,
  text: string,
  summary: { count: number; preview: string[] } | null = null,
) {
  return { id, text, scope: "Package" as const, summary };
}

function queryEvidence(id: string, text: string) {
  return { id, text, scope: "Query" as const, summary: null };
}

async function runCompletion(
  completion: BrowserPackageQueryCompletion,
) {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded({ ...completionEvent, completion });
    },
  };
  return createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft.*"),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);
}

test("Browser source dispatches exact and prefix package input with unchanged K", async () => {
  for (const searchText of [
    "Newtonsoft.Json",
    "Newtonsoft.*",
    `${"a".repeat(100)}*`,
  ]) {
    for (const matchLimit of [100, 7]) {
      const request = {
        ...withFacet(createQueryRequest(searchText), {
          key: "producer.inspection.facet",
          label: "Producer inspection",
          tier: "nuspec",
        }),
        includePrerelease: true,
        requestedMatchLimit: matchLimit,
      };
      const engine: BrowserPackageQueryEngine = {
        ...defaultControls,
        async run(...args) {
          assert.deepEqual(args.slice(0, 7), [
            "package-query-operation",
            searchText, '["producer.inspection.facet"]', 200, matchLimit, true, 20,
          ]);
          assert.ok(typeof args[7] === "object" && args[7] !== null);
          assert.equal(args.length, 8);
          return succeeded(completionEvent);
        },
      };
      await createBrowserPackageQueryDataSource(engine, {
        createOperationId: () => "package-query-operation",
      }).run(
        request, () => {}, () => {}, () => {}, new AbortController().signal);
    }
  }
});

test("Browser source preserves the default stable-only selection", async () => {
  for (const searchText of ["Newtonsoft.Json", "Newtonsoft.*"]) {
    const engine: BrowserPackageQueryEngine = {
      ...defaultControls,
      async run(...args) {
        assert.equal(args[1], searchText);
        assert.equal(args[5], false);
        assert.equal(args.length, 8);
        return succeeded(completionEvent);
      },
    };
    await createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest(searchText),
      () => {}, () => {}, () => {}, new AbortController().signal);
  }
});

test("Browser source retains the Package Query inspection envelope", async () => {
  const inspections: Array<
    BrowserPackageQueryResult["inspection"]
  > = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded(completionEvent);
    },
  };

  const completion = await createBrowserPackageQueryDataSource(engine, {
    onInspection: inspection => inspections.push(inspection),
  }).run(
    createQueryRequest("Contoso."),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(completion, { kind: "exhausted" });
  assert.equal(inspections.length, 2);
  assert.equal(inspections[0], null);
  assert.equal(inspections[1]?.content.completion.kind, "Exhausted");
  assert.deepEqual(inspections[1]?.share, {
    kind: "NonProjectable",
    fullUrl: null,
    packet: null,
    path: "package-query/share",
    reason: "No canonical Workspace packet.",
  });
});

test("Browser source retains only validated current-generation inspection", async () => {
  let retained: BrowserPackageQueryResult["inspection"] = null;
  const malformedCompletion = {
    ...completionEvent,
    completion: {
      ...completionEvent.completion!,
      kind: "ExplicitCandidatesComplete" as const,
      sourceCandidates: 2,
      candidates: 1,
      scope: "selector-issued candidates",
    },
  };
  const malformedEngine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded(malformedCompletion);
    },
  };
  await assert.rejects(
    createBrowserPackageQueryDataSource(malformedEngine, {
      onInspection: inspection => { retained = inspection; },
    }).run(
      createQueryRequest("Contoso."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /omitted its accounting or scope/);
  assert.equal(retained, null);

  const source = createBrowserPackageQueryDataSource({
    ...defaultControls,
    async run() {
      return succeeded(completionEvent);
    },
  }, {
    onInspection: inspection => { retained = inspection; },
  });
  const state = initialQueryState();
  const controller = createPackageQueryController(
    state,
    source,
    updateKind => {
      if (updateKind === "reset") retained = null;
    });
  await controller.run(createQueryRequest("Contoso."));
  assert.notEqual(retained, null);

  controller.configure(createQueryRequest("Fabrikam."));
  assert.equal(retained, null);
});

test("V3 metadata rows preserve unknown downloads and source-authored evidence", async () => {
  for (const totalDownloads of [null, 0, 9876]) {
    for (const verified of [null, false, true]) {
      const event: BrowserPackageQueryEvent = {
        ...toolMatchEvent,
        row: {
          ...toolMatchEvent.row!,
          tier: "SearchMetadata",
          totalDownloads,
          verified,
          evidence: [queryEvidence(
            "package.query.scope.prefix",
            "Package ID matches prefix \"Contoso.\".")],
        },
      };
      Reflect.set(
        event.row!,
        "rootRequest",
        "V3 rows must continue opening by ID and version.");
      const rows: QueryResultRow[] = [];
      const engine: BrowserPackageQueryEngine = {
        ...defaultControls,
        async run(...args) {
          assert.ok(typeof args[7] === "object" && args[7] !== null);
          Reflect.set(args[7], "event", JSON.stringify(event));
          return succeeded(completionEvent);
        },
      };
      await createBrowserPackageQueryDataSource(engine).run(
        createQueryRequest("Contoso.*"),
        page => rows.push(...page),
        () => {}, () => {}, new AbortController().signal);
      assert.deepEqual(rows, [{
        packageId: "Contoso.Tool",
        version: "2.0.0",
        tier: "search-metadata",
        totalDownloads,
        description: null,
        producer: "nuget.org",
        evidence: [{
          id: "package.query.scope.prefix",
          text: "Package ID matches prefix \"Contoso.\".",
          scope: "query",
          summary: null,
        }],
      }]);
    }
  }
});

test("V3 row descriptions are projected unchanged from the producer", async () => {
  const description = "  Tools for <format> packages & templates.  ";
  const rows: QueryResultRow[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(...args) {
      assert.ok(typeof args[7] === "object" && args[7] !== null);
      Reflect.set(args[7], "event", JSON.stringify({
        ...toolMatchEvent,
        row: {
          ...toolMatchEvent.row!,
          tier: "SearchMetadata",
          description,
        },
      } satisfies BrowserPackageQueryEvent));
      return succeeded(completionEvent);
    },
  };

  await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso.*"),
    page => rows.push(...page),
    () => {}, () => {}, new AbortController().signal);

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.description, description);
});

test("completion rejects unknown kinds and missing source candidate fields", async () => {
  await assert.rejects(runCompletion({
    ...completionEvent.completion!,
    kind: 99,
  }), /Unknown package-query completion/);
  const incomplete = { ...completionEvent.completion! };
  Reflect.deleteProperty(incomplete, "sourceCandidates");
  await assert.rejects(runCompletion(incomplete), /not a finite number/);
});

test("legacy match completion and failed package input retain distinct outcomes", async () => {
  assert.deepEqual(await runCompletion({
    ...completionEvent.completion!,
    kind: "MatchLimitReached",
  }), { kind: "bounded", reason: "first 100 matches" });
  assert.deepEqual(await runCompletion({
    ...completionEvent.completion!,
    kind: "Failed",
  }), {
    kind: "failed",
    reason: "Package source work failed before the query completed.",
  });
});

test("exact package completion remains distinct for zero or one source candidate", async () => {
  for (const sourceCandidates of [0, 1]) {
    assert.deepEqual(await runCompletion({
      ...completionEvent.completion!,
      prefix: "Missing.Package",
      candidateLimit: 1,
      candidates: sourceCandidates,
      matches: sourceCandidates,
      failures: 0,
      sourceCandidates,
      kind: "ExactPackageComplete",
    }), { kind: "exact" });
  }
});

test("streamed metadata admission rejects unknown tiers, malformed metadata, and empty evidence", async () => {
  const invalidRows = [
    { ...toolMatchEvent.row!, tier: "UnknownTier" },
    { ...toolMatchEvent.row!, totalDownloads: "unavailable" },
    { ...toolMatchEvent.row!, verified: "unknown" },
    { ...toolMatchEvent.row!, totalDownloads: undefined },
    { ...toolMatchEvent.row!, verified: undefined },
    { ...toolMatchEvent.row!, description: undefined },
    { ...toolMatchEvent.row!, description: 123 },
    { ...toolMatchEvent.row!, owners: "Contoso" },
    { ...toolMatchEvent.row!, manifest: {} },
    { ...toolMatchEvent.row!, tier: "SearchMetadata", evidence: [] },
    {
      ...toolMatchEvent.row!,
      tier: "SearchMetadata",
      evidence: [queryEvidence("producer.source", " ")],
    },
    {
      ...toolMatchEvent.row!,
      evidence: [{
        ...packageEvidence("package.summary", "Matched."),
        scope: "Unknown",
      }],
    },
    {
      ...toolMatchEvent.row!,
      evidence: [{
        ...packageEvidence("package.summary", "Matched."),
        summary: { count: -1, preview: [] },
      }],
    },
    {
      ...toolMatchEvent.row!,
      evidence: [{
        ...packageEvidence("package.summary", "Matched."),
        summary: { count: 1, preview: "not an array" },
      }],
    },
  ];
  for (const row of invalidRows) {
    const engine: BrowserPackageQueryEngine = {
      ...defaultControls,
      async run(...args) {
        assert.ok(typeof args[7] === "object" && args[7] !== null);
        Reflect.set(args[7], "event", JSON.stringify({ ...toolMatchEvent, row }));
        return succeeded(completionEvent);
      },
    };
    await assert.rejects(
      createBrowserPackageQueryDataSource(engine).run(
        createQueryRequest("Contoso.*"),
        () => {}, () => {}, () => {}, new AbortController().signal),
      /Unsupported package-query row tier|not a finite number|not a non-negative integer|not a boolean|not text|no evidence|Unknown package-query evidence scope|evidence preview was not an array|owners were not an array|manifest package types were not an array/);
  }
});

const toolMatchEvent: BrowserPackageQueryEvent = {
  kind: "Match",
  failure: null,
  progress: null,
  completion: null,
  assessment: null,
  row: {
    packageId: "Contoso.Tool",
    version: "2.0.0",
    tier: "PackageContent",
    evidence: [packageEvidence(
      "package.query.dotnet-tool-v2",
      "2 skill documents: skills/SKILL.md, skills/build/SKILL.md.",
      {
        count: 2,
        preview: ["skills/SKILL.md", "skills/build/SKILL.md"],
      })],
    totalDownloads: 12,
    description: null,
    verified: false,
    producer: "nuget.org",
    rootRequest: null,
    owners: ["Contoso"],
    manifest: {
      packageId: "contoso.tool",
      version: "2.0.0",
      manifestVersion: "nuspec",
      description: "Contoso tool package.",
      authors: "Contoso",
      repository: "https://example.test/contoso/tool",
      repositoryType: "git",
      repositoryCommit: "0123456789abcdef",
      license: "MIT",
      licenseUrl: "https://example.test/licenses/mit",
      packageTypes: ["DotnetTool"],
      isToolPackage: true,
      readmeFile: "README.md",
      dependencyGroups: [{
        targetFramework: "net10.0",
        dependencies: [{
          id: "Contoso.Dependency",
          versionRange: "[1.0.0,2.0.0)",
        }],
        isImplicitManifestGroup: false,
      }],
      iconFile: "icon.png",
      iconUrl: "https://example.test/icon.png",
      identityProvenance: "ExpectedCoordinate",
    },
  },
};

test("packageQueryFacets preserves product descriptors and producer ordering", () => {
  const catalog: BrowserPackageQueryFacetCatalog = {
    facets: [
      {
        id: "package.query.no-dependencies",
        label: "No dependencies",
        summary: "Packages with no dependency groups.",
        weight: 20,
        tier: "Nuspec",
        selectionGroupId: "package.query.dependencies",
        combinesWithinSelectionGroup: false,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        id: "package.query.source-verified",
        label: "Verified source",
        summary: "Packages with repository provenance.",
        weight: 10,
        tier: "Nuspec",
        selectionGroupId: null,
        combinesWithinSelectionGroup: false,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        id: "package.query.dotnet-tool-v2",
        label: "v2",
        summary: "RID-specific .NET tool format.",
        weight: 30,
        tier: "PackageContent",
        selectionGroupId: "package.query.dotnet-tool-format",
        combinesWithinSelectionGroup: true,
        displayGroupId: "package.query.display.dotnet-tool",
        displayGroupLabel: ".NET tool format",
      },
    ],
  };

  assert.deepEqual(packageQueryFacets(catalog), [
    {
      key: "package.query.no-dependencies",
      label: "No dependencies",
      summary: "Packages with no dependency groups.",
      weight: 20,
      tier: "nuspec",
      selectionGroupId: "package.query.dependencies",
      combinesWithinSelectionGroup: false,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      key: "package.query.source-verified",
      label: "Verified source",
      summary: "Packages with repository provenance.",
      weight: 10,
      tier: "nuspec",
      selectionGroupId: null,
      combinesWithinSelectionGroup: false,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      key: "package.query.dotnet-tool-v2",
      label: "v2",
      summary: "RID-specific .NET tool format.",
      weight: 30,
      tier: "package-content",
      selectionGroupId: "package.query.dotnet-tool-format",
      combinesWithinSelectionGroup: true,
      displayGroupId: "package.query.display.dotnet-tool",
      displayGroupLabel: ".NET tool format",
    },
  ]);
});

test("Browser data source maps package-content rows and visible failures", async () => {
  const failureEvent: BrowserPackageQueryEvent = {
    kind: "Failure",
    row: null,
    progress: null,
    completion: null,
    assessment: null,
    failure: {
      packageId: "Contoso.Bad",
      version: "1.0.0",
      producer: "nuget.org",
      kind: "PackageContentEvaluation",
      message: "package content could not be evaluated",
      manifestFailureReason: null,
    },
  };
  let candidateLimit = 0;
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      candidateLimit = candidates;
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      Reflect.set(sink, "event", JSON.stringify(failureEvent));
      return succeeded(completionEvent);
    },
  };
  const rows: QueryResultRow[] = [];
  const failures: string[] = [];
  const request = withFacet(createQueryRequest("Contoso."), {
    key: "package.query.dotnet-tool-v2",
    label: "v2",
    tier: "package-content",
  });

  await createBrowserPackageQueryDataSource(engine).run(
    request,
    page => rows.push(...page),
    failure => failures.push(failure),
    () => {},
    new AbortController().signal);

  assert.equal(candidateLimit, 20);
  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.packageId, "Contoso.Tool");
  assert.equal(rows[0]?.tier, "package-content");
  assert.deepEqual(rows[0]?.evidence, [{
    id: "package.query.dotnet-tool-v2",
    text: "2 skill documents: skills/SKILL.md, skills/build/SKILL.md.",
    scope: "package",
    summary: {
      count: 2,
      preview: ["skills/SKILL.md", "skills/build/SKILL.md"],
    },
  }]);
  assert.deepEqual(
    failures,
    ["Contoso.Bad@1.0.0: package content could not be evaluated"]);
});

test("Browser data source streams matches and failures before terminal completion", async () => {
  let receivedArguments: readonly unknown[] = [];
  const progressEvent: BrowserPackageQueryEvent = {
    kind: "Progress",
    row: null,
    failure: null,
    completion: null,
    assessment: null,
    progress: {
      phase: "Manifest",
      completed: 1,
      limit: 200,
    },
  };
  const matchEvent: BrowserPackageQueryEvent = {
    kind: "Match",
    failure: null,
    progress: null,
    completion: null,
    assessment: null,
    row: {
      packageId: "Microsoft.Extensions.Hosting",
      version: "10.0.0",
      tier: "Nuspec",
      evidence: [packageEvidence(
        "package.query.source-verified",
        "Verified source")],
      totalDownloads: 1234,
      description: null,
      verified: true,
      producer: "nuget.org",
      rootRequest: null,
      owners: ["Microsoft"],
      manifest: null,
    },
  };
  const failureEvent: BrowserPackageQueryEvent = {
    kind: "Failure",
    row: null,
    progress: null,
    completion: null,
    assessment: null,
    failure: {
      packageId: "Microsoft.Extensions.Bad",
      version: "1.0.0",
      producer: "nuget.org",
      kind: "ManifestAcquisition",
      message: "manifest unavailable",
      manifestFailureReason: null,
    },
  };
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(...args) {
      receivedArguments = args;
      const eventSink = args[7];
      assert.ok(typeof eventSink === "object" && eventSink !== null);
      Reflect.set(eventSink, "event", JSON.stringify(progressEvent));
      Reflect.set(eventSink, "event", JSON.stringify(matchEvent));
      Reflect.set(eventSink, "event", JSON.stringify(failureEvent));
      return succeeded(completionEvent);
    },
  };
  const rows: string[] = [];
  const failures: string[] = [];
  const progress: string[] = [];
  const request = withFacet(
    createQueryRequest("Microsoft."),
    {
      key: "package.query.source-verified",
      label: "Verified source",
      tier: "nuspec",
    });

  const completion = await createBrowserPackageQueryDataSource(engine).run(
    request,
    page => rows.push(...page.map(row => row.packageId)),
    failure => failures.push(failure),
    checkpoint => progress.push(
      `${checkpoint.phase}:${checkpoint.completed}/${checkpoint.limit}`),
    new AbortController().signal);

  assert.equal(typeof receivedArguments[0], "string");
  assert.deepEqual(receivedArguments.slice(1, 7), [
    "Microsoft.",
    '["package.query.source-verified"]',
    200,
    100,
    false,
    20,
  ]);
  assert.equal(receivedArguments.length, 8);
  assert.deepEqual(rows, ["Microsoft.Extensions.Hosting"]);
  assert.deepEqual(
    failures,
    ["Microsoft.Extensions.Bad@1.0.0: manifest unavailable"]);
  assert.deepEqual(progress, ["manifest:1/200"]);
  assert.deepEqual(completion, { kind: "exhausted" });
});

test("Browser data source counts only exact acknowledged match credit", async () => {
  const requested: Array<[string, number]> = [];
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    requestMatches(operationId, additionalMatchCredit) {
      requested.push([operationId, additionalMatchCredit]);
      return credit(
        additionalMatchCredit,
        additionalMatchCredit === 10);
    },
    async run() {
      await gate;
      return succeeded(completionEvent);
    },
  };
  const source = createBrowserPackageQueryDataSource(engine, {
    createOperationId: () => "credit-operation",
  });
  const running = source.run(
    createQueryRequest("Contoso."),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);

  assert.equal(source.initialMatchCredit, 20);
  const granted = source.requestMore?.(10);
  const overlapping = source.requestMore?.(5);
  assert.equal(await overlapping, false);
  assert.equal(await granted, true);
  assert.deepEqual(requested, [
    ["credit-operation", 10],
    ["credit-operation", 5],
  ]);
  release();
  await running;
  assert.equal(await source.requestMore?.(10), false);
});

test("old-run controls cannot target the replacement operation", async () => {
  const ids = ["old-operation", "replacement-operation"];
  const releases = new Map<string, () => void>();
  const cancellations: Array<[string, string]> = [];
  const credits: Array<[string, number]> = [];
  const engine: BrowserPackageQueryEngine = {
    cancel(operationId, reason) {
      cancellations.push([operationId, reason]);
      releases.get(operationId)?.();
      return cancellation("Requested", reason);
    },
    requestMatches(operationId, additionalMatchCredit) {
      credits.push([operationId, additionalMatchCredit]);
      return credit(additionalMatchCredit);
    },
    async run(operationId) {
      await new Promise<void>(resolve => {
        releases.set(operationId, resolve);
      });
      return succeeded(completionEvent);
    },
  };
  const source = createBrowserPackageQueryDataSource(engine, {
    createOperationId: () => ids.shift() ?? "",
  });
  const oldAbort = new AbortController();
  const replacementAbort = new AbortController();
  const oldRun = source.run(
    createQueryRequest("Old."),
    () => {},
    () => {},
    () => {},
    oldAbort.signal);
  const replacementRun = source.run(
    createQueryRequest("Replacement."),
    () => {},
    () => {},
    () => {},
    replacementAbort.signal);

  oldAbort.abort("superseded");
  assert.equal(await source.requestMore?.(10), true);
  assert.deepEqual(cancellations, [
    ["old-operation", "superseded"],
  ]);
  assert.deepEqual(credits, [
    ["replacement-operation", 10],
  ]);
  assert.deepEqual(await oldRun, { kind: "cancelled" });

  releases.get("replacement-operation")?.();
  assert.deepEqual(await replacementRun, { kind: "exhausted" });
});

test("Browser source decodes managed failure and cancellation results", async () => {
  const diagnostics: Array<[string, string, string | null]> = [];
  const failedEngine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return {
        version: 3,
        kind: "Failed",
        value: null,
        inspection: null,
        failureKind: "Unexpected",
        error: "managed package query failed",
        diagnostic: "diagnostic",
        reason: null,
      };
    },
  };
  await assert.rejects(
    createBrowserPackageQueryDataSource(failedEngine, {
      createOperationId: () => "active-failed-operation",
      reportUnexpectedFailure: (operationId, error, diagnostic) => {
        diagnostics.push([operationId, error.message, diagnostic]);
      },
    }).run(
      createQueryRequest("Failure."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /managed package query failed/);
  const failedAbort = new AbortController();
  const failedRun = createBrowserPackageQueryDataSource(failedEngine, {
    createOperationId: () => "failed-operation",
    reportUnexpectedFailure: (operationId, error, diagnostic) => {
      diagnostics.push([operationId, error.message, diagnostic]);
    },
  }).run(
    createQueryRequest("Failure."),
    () => {},
    () => {},
    () => {},
    failedAbort.signal);
  failedAbort.abort("superseded");
  assert.deepEqual(await failedRun, { kind: "cancelled" });
  assert.deepEqual(diagnostics, [
    [
      "active-failed-operation",
      "managed package query failed",
      "diagnostic",
    ],
    [
      "failed-operation",
      "managed package query failed",
      "diagnostic",
    ],
  ]);

  const canceledEngine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return {
        version: 3,
        kind: "Canceled",
        value: null,
        inspection: null,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: "worker-restarted",
      };
    },
  };
  assert.deepEqual(
    await createBrowserPackageQueryDataSource(canceledEngine).run(
      createQueryRequest("Canceled."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    { kind: "cancelled" });
});

test("Browser source reports unexpected failure after a superseded observer failure", async () => {
  const diagnostics: Array<[string, string, string | null]> = [];
  let settleManagedResult:
    ((result: BrowserPackageQueryResult) => void) | undefined;
  const managedResult = new Promise<BrowserPackageQueryResult>(resolve => {
    settleManagedResult = resolve;
  });
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(...args) {
      const eventSink = args[7];
      assert.ok(typeof eventSink === "object" && eventSink !== null);
      Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Progress",
        row: null,
        failure: null,
        progress: {
          phase: "Manifest",
          completed: 1,
          limit: 200,
        },
        assessment: null,
        completion: null,
      } satisfies BrowserPackageQueryEvent));
      return managedResult;
    },
  };
  const abort = new AbortController();
  const running = createBrowserPackageQueryDataSource(engine, {
    createOperationId: () => "observer-failure-operation",
    reportUnexpectedFailure: (operationId, error, diagnostic) => {
      diagnostics.push([operationId, error.message, diagnostic]);
    },
  }).run(
    createQueryRequest("Failure."),
    () => {},
    () => {},
    () => {
      throw new Error("package-query observer failed");
    },
    abort.signal);

  await Promise.resolve();
  abort.abort("superseded");
  settleManagedResult?.({
    version: 3,
    kind: "Failed",
    value: null,
    inspection: null,
    failureKind: "Unexpected",
    error: "managed package query failed",
    diagnostic: "managed diagnostic",
    reason: null,
  });

  await assert.rejects(running, /package-query observer failed/);
  assert.deepEqual(diagnostics, [[
    "observer-failure-operation",
    "managed package query failed",
    "managed diagnostic",
  ]]);
});

test("Browser data source batches consecutive matches into one controller page", async () => {
  const secondMatch = {
    ...toolMatchEvent,
    row: {
      ...toolMatchEvent.row!,
      packageId: "Contoso.Tool.Next",
    },
  } satisfies BrowserPackageQueryEvent;
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      Reflect.set(sink, "event", JSON.stringify(secondMatch));
      return succeeded(completionEvent);
    },
  };
  const pages: string[][] = [];

  await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    page => pages.push(page.map(row => row.packageId)),
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(
    pages,
    [["Contoso.Tool", "Contoso.Tool.Next"]]);
});

test("Browser progress is delivered while later engine work remains pending", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const received: string[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Progress",
        row: null,
        failure: null,
        completion: null,
        progress: {
          phase: "Manifest",
          completed: 4,
          limit: 20,
        },
        assessment: null,
      } satisfies BrowserPackageQueryEvent));
      assert.deepEqual(
        received,
        [],
        "the synchronous managed callback must not perform UI work");
      await engineGate;
      return succeeded(completionEvent);
    },
  };

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    progress => received.push(
      `${progress.phase}:${progress.completed}/${progress.limit}`),
    new AbortController().signal);
  await Promise.resolve();

  assert.deepEqual(received, ["manifest:4/20"]);
  releaseEngine();
  assert.deepEqual(await running, { kind: "exhausted" });
});

test("established durable events flush before producer failure is reported", async () => {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      throw new Error("producer failed");
    },
  };
  const rows: string[] = [];

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Contoso."),
      page => rows.push(...page.map(row => row.packageId)),
      () => {},
      () => {},
      new AbortController().signal),
    /producer failed/);

  assert.deepEqual(rows, ["Contoso.Tool"]);
});

test("established durable events reach the generation guard before cancellation returns", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    cancel() {
      releaseEngine();
      return cancellation();
    },
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      await engineGate;
      return succeeded(completionEvent);
    },
  };
  const abort = new AbortController();
  const rows: string[] = [];

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    page => rows.push(...page.map(row => row.packageId)),
    () => {},
    () => {},
    abort.signal);
  abort.abort();

  assert.deepEqual(await running, { kind: "cancelled" });
  assert.deepEqual(rows, ["Contoso.Tool"]);
});

test("durable-event delivery failure remains visible during cancellation", async () => {
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => { releaseEngine = resolve; });
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    cancel() {
      releaseEngine();
      return cancellation();
    },
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(toolMatchEvent));
      await engineGate;
      return succeeded(completionEvent);
    },
  };
  const abort = new AbortController();

  const running = createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso."),
    () => { throw new Error("view delivery failed"); },
    () => {},
    () => {},
    abort.signal);
  abort.abort();

  await assert.rejects(running, /view delivery failed/);
});

test("Browser data source maps product bounds without calling them exhaustive", async () => {
  const boundedEvent: BrowserPackageQueryEvent = {
    ...completionEvent,
    completion: {
      ...completionEvent.completion!,
      kind: "CandidateLimitReached",
      candidateLimit: 200,
    },
  };
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded(boundedEvent);
    },
  };

  const completion = await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(completion, {
    kind: "bounded",
    reason: "first 200 candidates",
  });
});

test("aborting Browser query work invokes the engine cancellation export", async () => {
  const cancellations: Array<[string, string]> = [];
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    cancel(operationId, reason) {
      cancellations.push([operationId, reason]);
      release();
      return cancellation("Requested", reason);
    },
    async run() {
      await gate;
      return succeeded(completionEvent);
    },
  };
  const abort = new AbortController();

  const running = createBrowserPackageQueryDataSource(engine, {
    createOperationId: () => "cancel-operation",
  }).run(
    createQueryRequest("Microsoft."),
    () => {},
    () => {},
    () => {},
    abort.signal);
  abort.abort("superseded");

  assert.deepEqual(await running, { kind: "cancelled" });
  assert.deepEqual(cancellations, [
    ["cancel-operation", "superseded"],
  ]);
});

test("malformed streamed events fail visibly instead of becoming empty output", async () => {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", "{}");
      return succeeded(completionEvent);
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Microsoft."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /Unknown Browser package-query event/);
});

test("terminal completion is rejected on the nonterminal callback channel", async () => {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _prefix,
      _facets,
      _candidates,
      _matches,
      _prerelease,
      _initialMatchCredit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify(completionEvent));
      return succeeded(completionEvent);
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest("Microsoft."),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /callback carried a terminal event/);
});
