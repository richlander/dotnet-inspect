import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageQueryDataSource,
  packageQueryAssemblyPatterns,
  packageQueryFacets,
  type BrowserPackageQueryEngine,
} from "../src/package-query-source.ts";
import {
  createAssemblyQueryRequest,
  createQueryRequest,
  withFacet,
  type QueryAssemblyAssessment,
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
    estimatedTotalHits: null,
    semanticMisses: null,
    notApplicable: null,
    scope: null,
    kind: "Exhausted",
  },
};

const assemblyCompletionEvent = {
  kind: "Completed" as const,
  row: null,
  failure: null,
  progress: null,
  assessment: null,
  completion: {
    prefix: "",
    producer: "package-query.assembly",
    candidateLimit: 2,
    matchLimit: 2,
    candidates: 2,
    matches: 1,
    failures: 0,
    sourceCandidates: null,
    estimatedTotalHits: null,
    semanticMisses: 1 as number | null,
    notApplicable: 0 as number | null,
    scope: "selector-issued primary implementation assemblies" as string | null,
    kind: "ExplicitCandidatesComplete" as const,
  },
};

function succeeded(
  value: BrowserPackageQueryEvent,
): BrowserPackageQueryResult {
  return {
    version: 1,
    kind: "Succeeded",
    value,
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
  inputKind: "package" | "gallery" = "gallery",
) {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded({ ...completionEvent, completion });
    },
  };
  return createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("", inputKind),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);
}

test("Browser source dispatches package and explicit Gallery input with unchanged K", async () => {
  for (const [searchText, inputKind, discovery] of [
    ["Newtonsoft.Json", "package", false],
    ["Newtonsoft.*", "package", false],
    [`${"a".repeat(100)}*`, "package", false],
    ["", "gallery", true],
  ] as const) {
    for (const matchLimit of [100, 7]) {
      const request = {
        ...withFacet(createQueryRequest(searchText, inputKind), {
          key: "producer.inspection.facet",
          label: "Producer inspection",
          tier: "nuspec",
        }),
        packageType: "Producer.CustomType",
        sourceOrderId: "producer.order.custom",
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
          assert.deepEqual(args.slice(8), [
            "Producer.CustomType", "producer.order.custom", discovery,
          ]);
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

test("Browser source leaves automatic source selections unresolved in either mode", async () => {
  for (const [searchText, inputKind, discovery] of [
    ["Newtonsoft.Json", "package", false],
    ["", "gallery", true],
  ] as const) {
    const engine: BrowserPackageQueryEngine = {
      ...defaultControls,
      async run(...args) {
        assert.equal(args[1], searchText);
        assert.equal(args[5], false);
        assert.deepEqual(args.slice(8), [null, null, discovery]);
        return succeeded(completionEvent);
      },
    };
    await createBrowserPackageQueryDataSource(engine).run(
      createQueryRequest(searchText, inputKind),
      () => {}, () => {}, () => {}, new AbortController().signal);
  }
});

test("Gallery metadata rows preserve unknown downloads and source-authored evidence", async () => {
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
            "producer.source-selection",
            "Source order: producer ranking; package type: Producer.Type")],
        },
      };
      Reflect.set(
        event.row!,
        "rootRequest",
        "Gallery rows must continue opening by ID and version.");
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
        createQueryRequest("", "gallery"),
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
          id: "producer.source-selection",
          text: "Source order: producer ranking; package type: Producer.Type",
          scope: "query",
          summary: null,
        }],
      }]);
    }
  }
});

test("Gallery row descriptions are projected unchanged from the producer", async () => {
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
    createQueryRequest("", "gallery"),
    page => rows.push(...page),
    () => {}, () => {}, new AbortController().signal);

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.description, description);
});

test("Gallery completions retain capacity and full acquired response independently of local matches", async () => {
  const completion = await runCompletion({
    ...completionEvent.completion!,
    kind: "MatchLimitReached",
    candidateLimit: 200,
    matchLimit: 7,
    candidates: 7,
    matches: 7,
    sourceCandidates: 200,
    estimatedTotalHits: 42000,
  });

  assert.deepEqual(completion, {
    kind: "bounded",
    reason: "one finite Gallery response (capacity 200 candidates); acquired 200 candidates; local match limit 7 reached; estimated total hits: 42,000 (estimate only)",
  });
});

test("empty and short Gallery responses stay finite even with absent or zero estimates", async () => {
  for (const sourceCandidates of [0, 3]) {
    for (const estimatedTotalHits of [null, 0, 400]) {
      const completion = await runCompletion({
        ...completionEvent.completion!,
        kind: "GalleryResponseComplete",
        candidateLimit: 200,
        sourceCandidates,
        estimatedTotalHits,
        candidates: sourceCandidates,
        matches: 0,
        failures: 0,
      });
      assert.equal(completion.kind, "bounded");
      assert.ok(completion.kind === "bounded");
      assert.match(completion.reason, /one finite Gallery response/);
      assert.match(completion.reason, /capacity 200 candidates/);
      assert.ok(completion.reason.includes(`acquired ${sourceCandidates} candidates`));
      assert.ok(completion.reason.includes(estimatedTotalHits === null
        ? "estimated total hits: unavailable"
        : `estimated total hits: ${estimatedTotalHits} (estimate only)`));
      assert.doesNotMatch(completion.reason, /exhausted|all matches|local match limit/);
    }
  }
});

test("Gallery completion requires received-count evidence and known completion kind", async () => {
  await assert.rejects(runCompletion({
    ...completionEvent.completion!,
    kind: "GalleryResponseComplete",
  }), /no source candidate count/);
  await assert.rejects(runCompletion({
    ...completionEvent.completion!,
    kind: 99,
  }), /Unknown package-query completion/);
  for (const property of ["sourceCandidates", "estimatedTotalHits"]) {
    const incomplete = { ...completionEvent.completion! };
    Reflect.deleteProperty(incomplete, property);
    await assert.rejects(runCompletion(incomplete), /not a finite number/);
  }
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
      estimatedTotalHits: null,
      kind: "ExactPackageComplete",
    }, "package"), { kind: "exact" });
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
        createQueryRequest("", "gallery"),
        () => {}, () => {}, () => {}, new AbortController().signal),
      /Unsupported package-query row tier|not a finite number|not a non-negative integer|not a boolean|not text|no evidence|Unknown package-query evidence scope|evidence preview was not an array/);
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

test("packageQueryAssemblyPatterns preserves only engine-issued descriptors", () => {
  assert.deepEqual(packageQueryAssemblyPatterns([{
    id: "package.query.assembly.ldstr-contains",
    label: "Decoded string literal contains",
    summary: "Ordinal substring over decoded IL ldstr occurrences.",
    maximumOperandLength: 256,
    maximumPackages: 5,
  }]), [{
    id: "package.query.assembly.ldstr-contains",
    label: "Decoded string literal contains",
    summary: "Ordinal substring over decoded IL ldstr occurrences.",
    maximumOperandLength: 256,
    maximumPackages: 5,
  }]);
});

test("Browser source dispatches assembly requests without Gallery parameters", async () => {
  const operand = "  Literal * value  ";
  let galleryRuns = 0;
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      galleryRuns++;
      return succeeded(completionEvent);
    },
    async runAssembly(...args) {
      assert.deepEqual(args.slice(1, 6), [
        "package.query.assembly.ldstr-contains",
        operand,
        '["Contoso.One@1.2.3","Contoso.Two@4.5.6"]',
        "net10.0",
        20,
      ]);
      assert.ok(typeof args[6] === "object" && args[6] !== null);
      return succeeded(assemblyCompletionEvent);
    },
  };
  const completion = await createBrowserPackageQueryDataSource(engine).run(
    createAssemblyQueryRequest(
      "package.query.assembly.ldstr-contains",
      operand,
      ["Contoso.One@1.2.3", "Contoso.Two@4.5.6"],
      "net10.0"),
    () => {},
    () => {},
    () => {},
    new AbortController().signal);

  assert.equal(galleryRuns, 0);
  assert.deepEqual(completion, {
    kind: "bounded",
    reason:
      "2 explicit candidates; 1 match; 1 semantic no-match; 0 not applicable; 0 failures; scope: selector-issued primary implementation assemblies",
  });
});

test("explicit candidate completion requires finite complete accounting and scope", async () => {
  const runAssemblyCompletion = async (
    completion: typeof assemblyCompletionEvent.completion,
  ) => {
    const engine: BrowserPackageQueryEngine = {
      ...defaultControls,
      async run() {
        return succeeded(completionEvent);
      },
      async runAssembly() {
        return succeeded({ ...assemblyCompletionEvent, completion });
      },
    };
    return await createBrowserPackageQueryDataSource(engine).run(
      createAssemblyQueryRequest(
        "package.query.assembly.ldstr-contains",
        "literal",
        ["Contoso.One@1.2.3", "Contoso.Two@4.5.6"],
        "net10.0"),
      () => {},
      () => {},
      () => {},
      new AbortController().signal);
  };

  await assert.rejects(
    runAssemblyCompletion({
      ...assemblyCompletionEvent.completion,
      semanticMisses: null,
    }),
    /omitted its accounting or scope/);
  await assert.rejects(
    runAssemblyCompletion({
      ...assemblyCompletionEvent.completion,
      scope: " ",
    }),
    /omitted its accounting or scope/);
  await assert.rejects(
    runAssemblyCompletion({
      ...assemblyCompletionEvent.completion,
      failures: 1,
    }),
    /did not account for every candidate/);
});

test("missing Browser assembly export fails visibly instead of falling back to Gallery", async () => {
  let galleryRuns = 0;
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      galleryRuns++;
      return succeeded(completionEvent);
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createAssemblyQueryRequest(
        "package.query.assembly.ldstr-contains",
        "literal",
        ["Contoso.Library@1.2.3"],
        "net10.0"),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /Assembly-pattern package queries are unavailable/);
  assert.equal(galleryRuns, 0);
});

test("Browser source keeps assembly matches and assessments distinct", async () => {
  const rootRequest = "{\"kind\":\"package\",\"id\":\"Contoso.Match\"}";
  const assessmentRoot =
    "{\"kind\":\"package\",\"id\":\"Contoso.NoMatch\"}";
  const rows: QueryResultRow[] = [];
  const assessments: QueryAssemblyAssessment[] = [];
  const callbackOrder: string[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded(completionEvent);
    },
    async runAssembly(
      _operationId,
      _pattern,
      _operand,
      _coordinates,
      _framework,
      _credit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Progress",
        row: null,
        failure: null,
        completion: null,
        progress: {
          phase: "Assembly",
          completed: 1,
          limit: 2,
        },
        assessment: null,
      }));
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Match",
        row: {
          packageId: "Contoso.Match",
          version: "1.2.3",
          tier: "Assembly",
          evidence: [packageEvidence(
            "package.query.assembly.ldstr-contains",
            "lib/net10.0/Contoso.Match.dll: M (IL_0001)")],
          totalDownloads: null,
          verified: null,
          producer: "analysis.ldstr",
          description: null,
          rootRequest,
        },
        failure: null,
        completion: null,
        progress: null,
        assessment: null,
      }));
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Assessment",
        row: null,
        failure: null,
        completion: null,
        progress: null,
        assessment: {
          packageId: "Contoso.NoMatch",
          version: "4.5.6",
          disposition: "NoMatch",
          message:
            "No decoded string literal contained the requested operand in the selected assembly.",
          assetPath: "lib/net10.0/Contoso.NoMatch.dll",
          rootRequest: assessmentRoot,
        },
      }));
      return succeeded(assemblyCompletionEvent);
    },
  };

  await createBrowserPackageQueryDataSource(engine).run(
    createAssemblyQueryRequest(
      "package.query.assembly.ldstr-contains",
      "literal",
      ["Contoso.Match@1.2.3", "Contoso.NoMatch@4.5.6"],
      "net10.0"),
    page => {
      callbackOrder.push(`page:${page[0]?.packageId ?? "empty"}`);
      rows.push(...page);
    },
    () => {},
    progress => {
      callbackOrder.push(`progress:${progress.completed}`);
      assert.deepEqual(progress, {
        phase: "assembly",
        completed: 1,
        limit: 2,
      });
    },
    new AbortController().signal,
    assessment => {
      callbackOrder.push(`assessment:${assessment.packageId}`);
      assessments.push(assessment);
    });
  callbackOrder.push("completed");

  assert.deepEqual(rows, [{
    packageId: "Contoso.Match",
    version: "1.2.3",
    tier: "assembly",
    evidence: [{
      id: "package.query.assembly.ldstr-contains",
      text: "lib/net10.0/Contoso.Match.dll: M (IL_0001)",
      scope: "package",
      summary: null,
    }],
    totalDownloads: null,
    description: null,
    producer: "analysis.ldstr",
    rootRequest,
  }]);
  assert.deepEqual(assessments, [{
    packageId: "Contoso.NoMatch",
    version: "4.5.6",
    disposition: "NoMatch",
    message:
      "No decoded string literal contained the requested operand in the selected assembly.",
    assetPath: "lib/net10.0/Contoso.NoMatch.dll",
    rootRequest: assessmentRoot,
  }]);
  assert.deepEqual(callbackOrder, [
    "progress:1",
    "page:Contoso.Match",
    "assessment:Contoso.NoMatch",
    "completed",
  ]);
});

test("assembly match rows require the opaque Root request", async () => {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return succeeded(completionEvent);
    },
    async runAssembly(
      _operationId,
      _pattern,
      _operand,
      _coordinates,
      _framework,
      _credit,
      sink,
    ) {
      assert.ok(typeof sink === "object" && sink !== null);
      Reflect.set(sink, "event", JSON.stringify({
        kind: "Match",
        row: {
          packageId: "Contoso.Match",
          version: "1.2.3",
          tier: "Assembly",
          evidence: [packageEvidence("literal", "Occurrence")],
          totalDownloads: null,
          verified: null,
          producer: "analysis.ldstr",
          description: null,
          rootRequest: null,
        },
        failure: null,
        completion: null,
        progress: null,
        assessment: null,
      }));
      return succeeded(assemblyCompletionEvent);
    },
  };

  await assert.rejects(
    createBrowserPackageQueryDataSource(engine).run(
      createAssemblyQueryRequest(
        "package.query.assembly.ldstr-contains",
        "literal",
        ["Contoso.Match@1.2.3"],
        "net10.0"),
      () => {},
      () => {},
      () => {},
      new AbortController().signal),
    /assembly package-query row contained no Root request/);
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
  assert.deepEqual(receivedArguments.slice(8), [null, null, false]);
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
  assert.equal(source.requestMore?.(10), true);
  assert.equal(source.requestMore?.(5), false);
  assert.deepEqual(requested, [
    ["credit-operation", 10],
    ["credit-operation", 5],
  ]);
  release();
  await running;
  assert.equal(source.requestMore?.(10), false);
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
  assert.equal(source.requestMore?.(10), true);
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
        version: 1,
        kind: "Failed",
        value: null,
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
        version: 1,
        kind: "Canceled",
        value: null,
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
    version: 1,
    kind: "Failed",
    value: null,
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
