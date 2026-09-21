import assert from "node:assert/strict";
import test from "node:test";

import {
  createBrowserPackageQueryDataSource,
  packageQueryCatalog,
  type BrowserPackageQueryEngine,
} from "../src/package-query-source.ts";
import {
  createPackageQueryController,
  createQueryRequest,
  initialQueryState,
  withLibraryLiteralDraft,
  withPreset,
  withTerm,
  type QueryResultRow,
  type QueryTermDescriptor,
} from "../src/package-query.ts";
import type {
  BrowserPackageAssemblySemanticCandidateOutcome,
  BrowserPackageQueryCancellation,
  BrowserPackageQueryCompletion,
  BrowserPackageQueryEvent,
  BrowserPackageQueryCatalog,
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
    occurrences: null,
    notEvaluated: null,
    kind: "Exhausted",
  },
};

const DEPENDS_TERM: QueryTermDescriptor = {
  key: "depends",
  label: "Direct dependency",
  summary: "Matches a direct dependency in any group.",
  weight: 10,
  tier: "nuspec",
  executionClass: "nuspec",
  operators: ["eq"],
  valueKind: "package-id",
  example: "Microsoft.Extensions.Hosting",
};
function succeeded(
  value: BrowserPackageQueryEvent,
): BrowserPackageQueryResult {
  return {
    version: 3,
    kind: "Succeeded",
    value: null,
    inspection: {
      resourcePath: "package-query",
      contentKind: "document",
      content: {
        hasPackages: false,
        results: [],
        failures: [],
        completion: value.completion!,
        assemblySemantic: null,
      },
      portableProjection: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        location: null,
        reason: "notSupported",
        explanation: "No canonical Workspace packet.",
      },
      diagnostics: [],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function semanticSucceeded(): BrowserPackageQueryResult {
  const selectedAsset = {
    path: "lib/net10.0/Contoso.Package.dll",
    assemblyName: "Contoso.Package",
    targetFramework: "net10.0",
    sequence: "Implementation",
    ordinal: 0,
    unevaluatedSiblings: 1,
    rootRequest: "opaque-root",
  };
  const occurrences = [{
    moduleVersionId: "00000000-0000-0000-0000-000000000001",
    methodDefinitionToken: 0x06000001,
    ilOffset: 4,
    userStringToken: 0x70000001,
    literalCharacterCount: 25,
    literalText: "shared-literal-use-marker",
  }, {
    moduleVersionId: "00000000-0000-0000-0000-000000000001",
    methodDefinitionToken: 0x06000002,
    ilOffset: 8,
    userStringToken: 0x70000002,
    literalCharacterCount: 25,
    literalText: "shared-literal-use-marker two",
  }];
  const semanticResult = {
    candidateOrdinal: 1,
    packageId: "contoso.package",
    version: "2.0.0",
    producer: "nuget.org",
    selectedAsset,
    occurrences,
  };
  const assemblySemantic = {
    population: {
      requestedCandidates: 1,
      candidates: 1,
      completion: "ExactPackageComplete" as const,
      isRequestedPopulationComplete: true,
      failures: [],
    },
    results: [semanticResult],
    candidateOutcomes: [{
      kind: "Matched" as const,
      candidateOrdinal: 1,
      packageId: "contoso.package",
      version: "2.0.0",
      producer: "nuget.org",
      result: semanticResult,
      selectedAsset,
      rootRequest: "opaque-root",
      notApplicableReason: null,
      failureKind: null,
      failureStage: null,
      nonEvaluationKind: null,
      timeoutKind: null,
      timeoutSeconds: null,
      message: null,
    }],
    candidateCount: 1,
    evaluatedCandidateCount: 1,
    notEvaluatedCount: 0,
    matchedPackageCount: 1,
    occurrenceCount: 2,
    semanticMissCount: 0,
    notApplicableCount: 0,
    failureCount: 0,
    completion: {
      population: "ExactPackageComplete" as const,
      isRequestedPopulationComplete: true,
      allCandidatesHaveTerminalOutcomes: true,
      hasFailures: false,
      isSemanticEvaluationComplete: true,
      isOperationDeadlineExpired: false,
    },
  };
  return {
    version: 3,
    kind: "Succeeded",
    value: null,
    inspection: {
      resourcePath: "package-query",
      contentKind: "document",
      content: {
        hasPackages: true,
        results: [{
          packageId: "contoso.package",
          version: "2.0.0",
          tier: "Assembly",
          answers: [],
          evidence: [{
            id: "selected-assembly",
            scope: "Package",
            summary: {
              count: 2,
              preview: ["first occurrence", "second occurrence"],
            },
            properties: [
              { name: "path", value: "lib/net10.0/Contoso.Package.dll" },
              { name: "literal-use-count", value: "2" },
              { name: "unevaluated-sibling-count", value: "0" },
            ],
            number: null,
            term: null,
          }],
          totalDownloads: null,
          verified: null,
          producer: "nuget.org",
          description: "Matched the decoded library-literal query.",
          rootRequest: "opaque-root",
          owners: [],
          manifest: null,
        }],
        failures: [],
        completion: {
          prefix: "Contoso.Package",
          producer: "nuget.org",
          candidateLimit: 1,
          matchLimit: 1,
          candidates: 1,
          matches: 1,
          failures: 0,
          kind: "ExactPackageComplete",
          sourceCandidates: 1,
          semanticMisses: 0,
          notApplicable: 0,
          scope: "Selected primary implementation libraries only.",
          occurrences: 2,
          notEvaluated: 0,
        },
        assemblySemantic,
      },
      portableProjection: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        location: null,
        reason: "notSupported",
        explanation: "No canonical Workspace packet.",
      },
      diagnostics: [],
    },
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function semanticAssessmentSucceeded(
  kind: "NoMatch" | "NotApplicable" | "Failure" | "NotEvaluated",
): BrowserPackageQueryResult {
  const base = semanticSucceeded();
  if (base.inspection === null
      || base.inspection.content.assemblySemantic === null) {
    throw new Error("Expected the semantic test inspection.");
  }
  const selectedAsset =
    base.inspection.content.assemblySemantic.results[0]!.selectedAsset;
  const outcome: BrowserPackageAssemblySemanticCandidateOutcome = {
    kind,
    candidateOrdinal: 1,
    packageId: "contoso.package",
    version: "2.0.0",
    producer: "nuget.org",
    result: null,
    selectedAsset: kind === "NotApplicable" || kind === "NotEvaluated"
      ? null
      : selectedAsset,
    rootRequest: kind === "NotEvaluated"
      ? null
      : "opaque-root",
    notApplicableReason: kind === "NotApplicable"
      ? "NoImplementationCounterpart"
      : null,
    failureKind: kind === "Failure" ? "Evaluation" : null,
    failureStage: kind === "Failure" ? "DecodeMethodBody" : null,
    nonEvaluationKind: kind === "NotEvaluated"
      ? "OperationDeadline"
      : null,
    timeoutKind: kind === "NotEvaluated" ? "Operation" : null,
    timeoutSeconds: kind === "NotEvaluated" ? 25 : null,
    message: `${kind} candidate`,
  };
  const notEvaluated = kind === "NotEvaluated" ? 1 : 0;
  const failureCount = kind === "Failure" ? 1 : 0;
  const hasOperationDeadline = kind === "NotEvaluated";
  const semanticMisses = kind === "NoMatch" ? 1 : 0;
  const notApplicable = kind === "NotApplicable" ? 1 : 0;
  return {
    ...base,
    inspection: {
      ...base.inspection,
      content: {
        ...base.inspection.content,
        hasPackages: false,
        results: [],
        failures: kind === "Failure"
          ? [{
              packageId: "contoso.package",
              version: "2.0.0",
              producer: "nuget.org",
              kind: "AssemblyEvaluation",
              message: "Failure candidate",
              manifestFailureReason: null,
            }]
          : hasOperationDeadline
            ? [{
                packageId: null,
                version: null,
                producer: "nuget.org",
                kind: "Search",
                message: "The package source operation deadline expired.",
                manifestFailureReason: null,
              }]
            : [],
        completion: {
          ...base.inspection.content.completion,
          matches: 0,
          failures: failureCount + (hasOperationDeadline ? 1 : 0),
          semanticMisses,
          notApplicable,
          occurrences: 0,
          notEvaluated,
        },
        assemblySemantic: {
          ...base.inspection.content.assemblySemantic,
          population: {
            ...base.inspection.content.assemblySemantic.population,
            failures: hasOperationDeadline
              ? [{
                  candidateOrdinal: null,
                  packageId: null,
                  version: null,
                  authority: "nuget.org",
                  kind: "Timeout",
                  message: "The package source operation deadline expired.",
                  timeoutKind: "Operation",
                  timeoutSeconds: 25,
                }]
              : [],
          },
          results: [],
          candidateOutcomes: [outcome],
          evaluatedCandidateCount: 1 - notEvaluated,
          notEvaluatedCount: notEvaluated,
          matchedPackageCount: 0,
          occurrenceCount: 0,
          semanticMissCount: semanticMisses,
          notApplicableCount: notApplicable,
          failureCount,
          completion: {
            ...base.inspection.content.assemblySemantic.completion,
            hasFailures: failureCount > 0 || hasOperationDeadline,
            isSemanticEvaluationComplete:
              notEvaluated === 0 && failureCount === 0,
            isOperationDeadlineExpired: hasOperationDeadline,
          },
        },
      },
    },
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
  return {
    id,
    scope: "Package" as const,
    summary,
    properties: [{ name: "value", value: text }],
    number: null,
    term: null,
  };
}

function queryEvidence(id: string, text: string) {
  return {
    id,
    scope: "Query" as const,
    summary: null,
    properties: [{ name: "value", value: text }],
    number: null,
    term: null,
  };
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
        ...withTerm(
          withPreset(createQueryRequest(searchText), {
            id: "readme:eq:true",
            key: "readme",
            operator: "eq",
            value: "true",
            label: "Embedded README",
            tier: "nuspec",
            executionClass: "nuspec",
          }),
          DEPENDS_TERM,
          "eq",
          "Microsoft.Extensions.Hosting"),
        includePrerelease: true,
        requestedMatchLimit: matchLimit,
      };
      const engine: BrowserPackageQueryEngine = {
        ...defaultControls,
        async run(...args) {
          assert.deepEqual(args.slice(0, 7), [
            "package-query-operation",
            searchText,
            '[{"key":"readme","operator":"eq","value":"true"},{"key":"depends","operator":"eq","value":"Microsoft.Extensions.Hosting"}]',
            200, matchLimit, true, 20,
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

test("Browser source uses progress-only callbacks and terminal semantic Document truth", async () => {
  const rows: QueryResultRow[][] = [];
  const failures: string[] = [];
  const progress: unknown[] = [];
  const assessments: unknown[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      assert.fail("Library-literal qualification must use its shared operation.");
    },
    async runAssemblySemantic(...args) {
      assert.deepEqual(args.slice(0, 7), [
        "package-query-operation",
        "Contoso.Package",
        "shared-literal-use-marker",
        "net10.0",
        1,
        true,
        20,
      ]);
      const eventSink: unknown = args[7];
      if (typeof eventSink !== "object" || eventSink === null)
        throw new Error("Expected Package Query event sink.");
      Reflect.set(eventSink, "event", JSON.stringify({
        kind: "Progress",
        row: null,
        failure: null,
        completion: null,
        progress: {
          phase: "Assembly",
          completed: 1,
          limit: 1,
        },
        assessment: null,
      }));
      return semanticSucceeded();
    },
  };
  const request = {
    ...withLibraryLiteralDraft(
      createQueryRequest("Contoso.Package"),
      "shared-literal-use-marker",
      "net10.0"),
    includePrerelease: true,
  };

  const completion = await createBrowserPackageQueryDataSource(engine, {
    createOperationId: () => "package-query-operation",
  }).run(
    request,
    page => rows.push([...page]),
    failure => failures.push(failure),
    item => progress.push(item),
    new AbortController().signal,
    assessment => assessments.push(assessment));

  assert.deepEqual(rows.map(page => page.map(row => ({
    packageId: row.packageId,
    tier: row.tier,
    rootRequest: row.rootRequest,
    occurrenceCount: row.evidence[0]?.summary?.count,
  }))), [[{
    packageId: "contoso.package",
    tier: "assembly",
    rootRequest: "opaque-root",
    occurrenceCount: 2,
  }]]);
  assert.deepEqual(failures, []);
  assert.deepEqual(progress, [{
    phase: "assembly",
    completed: 1,
    limit: 1,
  }]);
  assert.deepEqual(assessments, []);
  assert.deepEqual(completion, {
    kind: "library-literal",
    population: "ExactPackageComplete",
    candidateCount: 1,
    evaluatedCandidateCount: 1,
    notEvaluatedCount: 0,
    matchedPackageCount: 1,
    occurrenceCount: 2,
    semanticMissCount: 0,
    notApplicableCount: 0,
    failureCount: 0,
    complete: true,
  });
});

test("Browser source preserves typed semantic non-match, applicability, failure, and deadline outcomes", async () => {
  for (const kind of [
    "NoMatch",
    "NotApplicable",
    "Failure",
    "NotEvaluated",
  ] as const) {
    const assessments: unknown[] = [];
    const failures: string[] = [];
    const engine: BrowserPackageQueryEngine = {
      ...defaultControls,
      async run() {
        assert.fail("Library-literal qualification must use its shared operation.");
      },
      async runAssemblySemantic() {
        return semanticAssessmentSucceeded(kind);
      },
    };

    const completion = await createBrowserPackageQueryDataSource(engine).run(
      withLibraryLiteralDraft(
        createQueryRequest("Contoso.Package"),
        "shared-literal-use-marker",
        "net10.0"),
      () => {},
      failure => failures.push(failure),
      () => {},
      new AbortController().signal,
      assessment => assessments.push(assessment));

    assert.deepEqual(assessments, [{
      packageId: "contoso.package",
      version: "2.0.0",
      disposition: kind,
      message: `${kind} candidate`,
      assetPath:
        kind === "NoMatch" || kind === "Failure"
          ? "lib/net10.0/Contoso.Package.dll"
          : null,
      rootRequest: kind === "NotEvaluated" ? null : "opaque-root",
    }]);
    assert.equal(
      failures.length,
      kind === "Failure" || kind === "NotEvaluated" ? 1 : 0);
    assert.equal(completion.kind, "library-literal");
    if (completion.kind !== "library-literal")
      throw new Error("Expected library-literal completion.");
    assert.equal(completion.semanticMissCount, kind === "NoMatch" ? 1 : 0);
    assert.equal(
      completion.notApplicableCount,
      kind === "NotApplicable" ? 1 : 0);
    assert.equal(completion.failureCount, kind === "Failure" ? 1 : 0);
    assert.equal(
      completion.notEvaluatedCount,
      kind === "NotEvaluated" ? 1 : 0);
    assert.equal(
      completion.complete,
      kind === "NoMatch" || kind === "NotApplicable");
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

test("Browser source forwards repeated active terms as exact generic triples", async () => {
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(
      _operationId,
      _searchText,
      termsJson,
    ) {
      assert.deepEqual(JSON.parse(termsJson), [
        {
          key: "depends",
          operator: "eq",
          value: "Microsoft.Extensions.Hosting",
        },
        {
          key: "depends",
          operator: "eq",
          value: "  Microsoft.Extensions.DependencyInjection  ",
        },
      ]);
      return succeeded(completionEvent);
    },
  };
  const request = withTerm(
    withTerm(
      createQueryRequest("Microsoft.*"),
      DEPENDS_TERM,
      "eq",
      "Microsoft.Extensions.Hosting"),
    DEPENDS_TERM,
    "eq",
    "  Microsoft.Extensions.DependencyInjection  ");

  await createBrowserPackageQueryDataSource(engine).run(
    request,
    () => {},
    () => {},
    () => {},
    new AbortController().signal);
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
  assert.deepEqual(inspections[1]?.portableProjection, {
    kind: "NonProjectable",
    fullUrl: null,
    packet: null,
    location: null,
    reason: "notSupported",
    explanation: "No canonical Workspace packet.",
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
        answers: [],
        evidence: [{
          id: "package.query.scope.prefix",
          scope: "query",
          summary: null,
          properties: [{
            name: "value",
            value: "Package ID matches prefix \"Contoso.\".",
          }],
          number: null,
        }],
      }]);
    }
  }
});

test("V3 rows preserve structured product term attribution", async () => {
  const rows: QueryResultRow[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run(...args) {
      assert.ok(typeof args[7] === "object" && args[7] !== null);
      Reflect.set(args[7], "event", JSON.stringify({
        ...toolMatchEvent,
        row: {
          ...toolMatchEvent.row!,
          tier: "Nuspec",
          evidence: [{
            ...packageEvidence(
              "depends",
              "Direct dependency matches Microsoft.Extensions.Hosting."),
            term: {
              key: "depends",
              operator: "eq",
              value: "Microsoft.Extensions.Hosting",
            },
          }],
        },
      } satisfies BrowserPackageQueryEvent));
      return succeeded(completionEvent);
    },
  };

  await createBrowserPackageQueryDataSource(engine).run(
    createQueryRequest("Contoso.*"),
    page => rows.push(...page),
    () => {},
    () => {},
    new AbortController().signal);

  assert.deepEqual(rows[0]?.evidence, [{
    id: "depends",
    scope: "package",
    summary: null,
    properties: [{
      name: "value",
      value: "Direct dependency matches Microsoft.Extensions.Hosting.",
    }],
    number: null,
    term: {
      key: "depends",
      operator: "eq",
      value: "Microsoft.Extensions.Hosting",
    },
  }]);
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

test("streamed metadata admission rejects unknown tiers and malformed typed data", async () => {
  const invalidRows = [
    { ...toolMatchEvent.row!, tier: "UnknownTier" },
    { ...toolMatchEvent.row!, totalDownloads: "unavailable" },
    { ...toolMatchEvent.row!, verified: "unknown" },
    { ...toolMatchEvent.row!, totalDownloads: undefined },
    { ...toolMatchEvent.row!, verified: undefined },
    { ...toolMatchEvent.row!, description: undefined },
    { ...toolMatchEvent.row!, description: 123 },
    { ...toolMatchEvent.row!, answers: "not an array" },
    { ...toolMatchEvent.row!, owners: "Contoso" },
    { ...toolMatchEvent.row!, manifest: {} },
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
    {
      ...toolMatchEvent.row!,
      evidence: [{
        ...packageEvidence("package.summary", "Matched."),
        properties: "not an array",
      }],
    },
    {
      ...toolMatchEvent.row!,
      evidence: [{
        ...packageEvidence("package.summary", "Matched."),
        number: "not a number",
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
      /Unsupported package-query row tier|not a finite number|not a non-negative integer|not a boolean|not text|Unknown package-query evidence scope|evidence preview was not an array|answers were not an array|evidence properties were not an array|owners were not an array|manifest package types were not an array/);
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
    answers: [],
    evidence: [packageEvidence(
      "tool-format",
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

test("packageQueryCatalog preserves product descriptors and producer ordering", () => {
  const catalog: BrowserPackageQueryCatalog = {
    presets: [
      {
        key: "dependencies",
        operator: "eq",
        value: "none",
        label: "No dependencies",
        summary: "Packages with no dependency groups.",
        weight: 20,
        tier: "Nuspec",
        executionClass: "Nuspec",
        selectionGroupId: null,
        combinesWithinSelectionGroup: false,
        replacementGroupId: null,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        key: "readme",
        operator: "eq",
        value: "true",
        label: "Embedded README",
        summary: "Packages that declare an embedded README.",
        weight: 10,
        tier: "Nuspec",
        executionClass: "Nuspec",
        selectionGroupId: null,
        combinesWithinSelectionGroup: false,
        replacementGroupId: null,
        displayGroupId: null,
        displayGroupLabel: null,
      },
      {
        key: "tool-format",
        operator: "eq",
        value: "v2",
        label: "v2",
        summary: "RID-specific .NET tool format.",
        weight: 30,
        tier: "PackageContent",
        executionClass: "PackageContent",
        selectionGroupId: "tool-format",
        combinesWithinSelectionGroup: true,
        replacementGroupId: "package.query.replacement.dotnet-tool",
        displayGroupId: "package.query.display.dotnet-tool",
        displayGroupLabel: ".NET tool format",
      },
    ],
    terms: [{
      key: "references",
      label: "Assembly reference",
      summary: "Matches an AssemblyRef simple name.",
      weight: 10,
      tier: "PackageContent",
      executionClass: "Metadata",
      operators: ["eq"],
      valueKind: "assembly simple name",
      example: "Windows",
    }],
  };

  const projected = packageQueryCatalog(catalog);
  assert.deepEqual(projected.presets, [
    {
      id: "dependencies:eq:none",
      key: "dependencies",
      operator: "eq",
      value: "none",
      label: "No dependencies",
      summary: "Packages with no dependency groups.",
      weight: 20,
      tier: "nuspec",
      executionClass: "nuspec",
      selectionGroupId: null,
      combinesWithinSelectionGroup: false,
      replacementGroupId: null,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      id: "readme:eq:true",
      key: "readme",
      operator: "eq",
      value: "true",
      label: "Embedded README",
      summary: "Packages that declare an embedded README.",
      weight: 10,
      tier: "nuspec",
      executionClass: "nuspec",
      selectionGroupId: null,
      combinesWithinSelectionGroup: false,
      replacementGroupId: null,
      displayGroupId: null,
      displayGroupLabel: null,
    },
    {
      id: "tool-format:eq:v2",
      key: "tool-format",
      operator: "eq",
      value: "v2",
      label: "v2",
      summary: "RID-specific .NET tool format.",
      weight: 30,
      tier: "package-content",
      executionClass: "package-content",
      selectionGroupId: "tool-format",
      combinesWithinSelectionGroup: true,
      replacementGroupId: "package.query.replacement.dotnet-tool",
      displayGroupId: "package.query.display.dotnet-tool",
      displayGroupLabel: ".NET tool format",
    },
  ]);
  assert.deepEqual(projected.terms, [{
    key: "references",
    label: "Assembly reference",
    summary: "Matches an AssemblyRef simple name.",
    weight: 10,
    tier: "package-content",
    executionClass: "metadata",
    operators: ["eq"],
    valueKind: "assembly simple name",
    example: "Windows",
  }]);
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
      _terms,
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
  const request = withPreset(createQueryRequest("Contoso."), {
    id: "tool-format:eq:v2",
    key: "tool-format",
    operator: "eq",
    value: "v2",
    label: "v2",
    tier: "package-content",
    executionClass: "package-content",
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
    id: "tool-format",
    scope: "package",
    summary: {
      count: 2,
      preview: ["skills/SKILL.md", "skills/build/SKILL.md"],
    },
    properties: [{
      name: "value",
      value: "2 skill documents: skills/SKILL.md, skills/build/SKILL.md.",
    }],
    number: null,
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
      phase: "DependencyTraversal",
      completed: 1,
      limit: 5,
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
      answers: [],
      evidence: [packageEvidence(
        "readme",
        "Embedded README")],
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
      kind: "DependencyTraversal",
      message: "dependency traversal incomplete",
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
  const request = withPreset(
    createQueryRequest("Microsoft."),
    {
      id: "dependency-depth:eq:2",
      key: "dependency-depth",
      operator: "eq",
      value: "2",
      label: "Depth 2",
      tier: "nuspec",
      executionClass: "nuspec-expensive",
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
    '[{"key":"dependency-depth","operator":"eq","value":"2"}]',
    5,
    100,
    false,
    20,
  ]);
  assert.equal(receivedArguments.length, 8);
  assert.deepEqual(rows, ["Microsoft.Extensions.Hosting"]);
  assert.deepEqual(
    failures,
    ["Microsoft.Extensions.Bad@1.0.0: dependency traversal incomplete"]);
  assert.deepEqual(progress, ["dependency-traversal:1/5"]);
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

test("Browser source surfaces expected planning rejection without source failure or diagnostics", async () => {
  const diagnostics: string[] = [];
  const engine: BrowserPackageQueryEngine = {
    ...defaultControls,
    async run() {
      return {
        version: 3,
        kind: "Failed",
        value: null,
        inspection: null,
        failureKind: "Expected",
        error: "A package-query term value is invalid.",
        diagnostic: null,
        reason: null,
      };
    },
  };

  const failures: string[] = [];
  assert.deepEqual(
    await createBrowserPackageQueryDataSource(engine, {
      reportUnexpectedFailure: () => diagnostics.push("unexpected"),
    }).run(
      withTerm(
        createQueryRequest("Microsoft.*"),
        DEPENDS_TERM,
        "eq",
        "not a package id"),
      () => {},
      failure => failures.push(failure),
      () => {},
      new AbortController().signal),
    {
      kind: "failed",
      reason: "A package-query term value is invalid.",
    });
  assert.deepEqual(failures, []);
  assert.deepEqual(diagnostics, []);
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
      _terms,
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
      _terms,
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
      _terms,
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
      _terms,
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
      _terms,
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
      _terms,
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
      _terms,
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
