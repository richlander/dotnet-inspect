import assert from "node:assert/strict";
import test from "node:test";

import {
  appendAssessment,
  appendFailure,
  appendProgress,
  appendRows,
  createPackageQueryController,
  createQueryRequest,
  emptyOutcome,
  initialQueryState,
  isLibraryLiteralQuery,
  shouldExecuteQuery,
  togglePreset,
  replaceTerm,
  withCompletion,
  withEditorDraft,
  withPreset,
  withTerm,
  withSourceSelection,
  withScopeQuery,
  withoutPreset,
  withoutTerm,
  type PackageQueryDataSource,
  type QueryAssemblyAssessment,
  type QueryCompletion,
  type QueryPreset,
  type QueryResultRow,
  type QueryTermDescriptor,
  type TerminalQueryCompletion,
} from "../src/package-query.ts";

const TFM_FACET: QueryPreset = {
  id: "readme:eq:true",
  key: "readme",
  operator: "eq",
  value: "true",
  label: "embedded README",
  tier: "nuspec",
  executionClass: "nuspec",
};

const HAS_DEPENDENCIES_FACET: QueryPreset = {
  id: "dependencies:eq:any",
  key: "dependencies",
  operator: "eq",
  value: "any",
  label: "Has dependencies",
  tier: "nuspec",
  executionClass: "nuspec",
  selectionGroupId: "dependencies",
};

const NO_DEPENDENCIES_FACET: QueryPreset = {
  id: "dependencies:eq:none",
  key: "dependencies",
  operator: "eq",
  value: "none",
  label: "No dependencies",
  tier: "nuspec",
  executionClass: "nuspec",
  selectionGroupId: "dependencies",
};

const SKILL_FACET: QueryPreset = {
  id: "skill:eq:true",
  key: "skill",
  operator: "eq",
  value: "true",
  label: "embedded SKILL.md",
  tier: "package-content",
  executionClass: "package-content",
};

const ANY_TOOL_FACET: QueryPreset = {
  id: "tool:eq:true",
  key: "tool",
  operator: "eq",
  value: "true",
  label: ".NET Tool",
  tier: "nuspec",
  executionClass: "nuspec",
  replacementGroupId: "dotnet-tool",
};

const TOOL_V1_FACET: QueryPreset = {
  id: "tool-format:eq:v1",
  key: "tool-format",
  operator: "eq",
  value: "v1",
  label: "v1",
  tier: "package-content",
  executionClass: "package-content",
  selectionGroupId: "tool-format",
  combinesWithinSelectionGroup: true,
  replacementGroupId: "dotnet-tool",
};

const TOOL_V2_FACET: QueryPreset = {
  id: "tool-format:eq:v2",
  key: "tool-format",
  operator: "eq",
  value: "v2",
  label: "v2",
  tier: "package-content",
  executionClass: "package-content",
  selectionGroupId: "tool-format",
  combinesWithinSelectionGroup: true,
  replacementGroupId: "dotnet-tool",
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
  multiline: false,
};

const CONTENT_TERM: QueryTermDescriptor = {
  ...DEPENDS_TERM,
  key: "contains-file",
  label: "Contains file",
  tier: "package-content",
  executionClass: "package-content",
};

const NUSPEC_EXPENSIVE_TERM: QueryTermDescriptor = {
  ...DEPENDS_TERM,
  key: "depends-transitive",
  label: "Transitive dependency",
  executionClass: "nuspec-expensive",
};

const LIBRARY_LITERAL_TERM: QueryTermDescriptor = {
  ...DEPENDS_TERM,
  key: "library-literal",
  label: "Library literal",
  summary: "Matches decoded string-literal uses.",
  weight: 650,
  tier: "package-content",
  executionClass: "metadata-expensive",
  valueKind: "decoded UTF-16 text",
  example: "Microsoft.Extensions.",
  multiline: true,
};

function row(packageId: string): QueryResultRow {
  return {
    packageId,
    version: "1.0.0",
    tier: "nuspec",
    answers: [],
    evidence: [{
      id: "test.package",
      scope: "package",
      summary: null,
      properties: [{ name: "value", value: "net45" }],
      number: null,
    }],
    totalDownloads: 100,
  };
}

const NO_MATCH_ASSESSMENT: QueryAssemblyAssessment = {
  packageId: "Contoso.Library",
  version: "1.2.3",
  disposition: "NoMatch",
  message: "No decoded string literal contained the requested operand.",
  assetPath: "lib/net10.0/Contoso.Library.dll",
  rootRequest: "{\"kind\":\"package\"}",
  libraries: [],
};

test("withPreset is idempotent by preset id and withoutPreset removes by id", () => {
  const base = createQueryRequest("Microsoft.");
  const once = withPreset(base, TFM_FACET);
  const twice = withPreset(once, TFM_FACET);

  assert.equal(once.presets.length, 1);
  assert.equal(twice.presets.length, 1);
  assert.equal(withoutPreset(twice, TFM_FACET.id).presets.length, 0);
});

test("createQueryRequest gives candidate and match limits independent defaults", () => {
  const defaults = createQueryRequest("Microsoft.");

  assert.equal(defaults.requestedLimit, 200);
  assert.equal(defaults.requestedMatchLimit, 100);
  assert.notEqual(defaults.requestedLimit, defaults.requestedMatchLimit);
  assert.equal(defaults.includePrerelease, false);
  assert.deepEqual(defaults.terms, []);
});

test("library-literal is an ordinary composable term with metadata-expensive bounds", () => {
  const ordinary = withTerm(
    withPreset(createQueryRequest("Contoso.Package"), TFM_FACET),
    DEPENDS_TERM,
    "eq",
    "Contoso.Dependency");
  const exact = withTerm(
    ordinary,
    LIBRARY_LITERAL_TERM,
    "eq",
    "shared-literal-use-marker");
  const prefix = withScopeQuery(exact, "Contoso.*");

  assert.equal(isLibraryLiteralQuery(exact), true);
  assert.deepEqual(exact.presets, [TFM_FACET]);
  assert.deepEqual(exact.terms.map(term => term.descriptor.key), [
    "depends",
    "library-literal",
  ]);
  assert.equal(exact.targetFramework, "net10.0");
  assert.equal(exact.requestedLimit, 5);
  assert.equal(exact.requestedMatchLimit, 100);
  assert.equal(prefix.requestedLimit, 5);
  assert.equal(prefix.requestedMatchLimit, 100);

  const whitespace = withTerm(
    ordinary,
    LIBRARY_LITERAL_TERM,
    "eq",
    " ");
  assert.equal(isLibraryLiteralQuery(whitespace), true);
  assert.deepEqual(whitespace.presets, [TFM_FACET]);
  assert.equal(whitespace.terms[1]?.value, " ");
  assert.equal(whitespace.requestedLimit, 5);
  assert.equal(whitespace.requestedMatchLimit, 100);

  const cleared = withoutTerm(prefix, 1);
  assert.equal(isLibraryLiteralQuery(cleared), false);
  assert.equal(cleared.requestedLimit, 200);
  assert.equal(cleared.requestedMatchLimit, 100);
  assert.equal(cleared.targetFramework, "net10.0");
});

test("presets and free terms compose with a library-literal term", () => {
  const literal = withTerm(
    {
      ...createQueryRequest("Contoso.*"),
      targetFramework: "net9.0",
    },
    LIBRARY_LITERAL_TERM,
    "eq",
    "shared-literal-use-marker");
  for (const request of [
    withPreset(literal, TFM_FACET),
    togglePreset(literal, SKILL_FACET),
    withTerm(literal, DEPENDS_TERM, "eq", "Contoso.Dependency"),
    withTerm(literal, CONTENT_TERM, "eq", "tools/"),
  ]) {
    assert.equal(isLibraryLiteralQuery(request), true);
    assert.equal(
      request.terms.find(term =>
        term.descriptor.key === "library-literal")?.value,
      "shared-literal-use-marker");
    assert.equal(request.targetFramework, "net9.0");
    assert.equal(request.scopeQuery, "Contoso.*");
    assert.equal(request.requestedLimit, 5);
    assert.equal(request.requestedMatchLimit, 100);
  }
});

test("operand-bearing terms retain exact repeated triples and edit by position", () => {
  const base = createQueryRequest("Microsoft.");
  const first = withTerm(base, DEPENDS_TERM, "eq", "Microsoft.Extensions.Hosting");
  const repeated = withTerm(
    first,
    DEPENDS_TERM,
    "eq",
    "Microsoft.Extensions.DependencyInjection");
  const edited = replaceTerm(
    repeated,
    0,
    "eq",
    "  Microsoft.Extensions.Hosting  ");

  assert.deepEqual(edited.terms.map(term => ({
    key: term.descriptor.key,
    operator: term.operator,
    value: term.value,
  })), [
    {
      key: "depends",
      operator: "eq",
      value: "  Microsoft.Extensions.Hosting  ",
    },
    {
      key: "depends",
      operator: "eq",
      value: "Microsoft.Extensions.DependencyInjection",
    },
  ]);
  assert.deepEqual(withoutTerm(edited, 0).terms.map(term => term.value), [
    "Microsoft.Extensions.DependencyInjection",
  ]);
});

test("operand-bearing terms participate in candidate bounds", () => {
  const content = withTerm(
    createQueryRequest("Contoso."),
    CONTENT_TERM,
    "eq",
    "tools/");
  assert.equal(content.requestedLimit, 20);
  assert.equal(withoutTerm(content, 0).requestedLimit, 200);

  const transitive = withTerm(
    createQueryRequest("Contoso."),
    NUSPEC_EXPENSIVE_TERM,
    "eq",
    "Contoso.Dependency");
  const transitiveWithContent = withPreset(transitive, SKILL_FACET);
  assert.equal(transitive.requestedLimit, 5);
  assert.equal(transitiveWithContent.requestedLimit, 5);
  assert.equal(withoutTerm(transitiveWithContent, 0).requestedLimit, 20);
});

test("package requests preserve editor spelling without resolving source defaults", () => {
  for (const text of ["", "  hosting dependency injection  ", "System.*"]) {
    assert.equal(createQueryRequest(text).scopeQuery, text);
  }
});

test("inspection changes retain prerelease selection and independent match limits", () => {
  const request = {
    ...createQueryRequest(" hosting libraries "),
    includePrerelease: true,
    requestedMatchLimit: 7,
  };
  const content = togglePreset(request, SKILL_FACET);
  const manifest = togglePreset(togglePreset(content, TFM_FACET), SKILL_FACET);
  const browse = withScopeQuery(manifest, "");

  assert.equal(content.requestedLimit, 20);
  assert.equal(manifest.requestedLimit, 200);
  for (const changed of [content, manifest, browse]) {
    assert.equal(changed.includePrerelease, true);
    assert.equal(changed.requestedMatchLimit, 7);
  }
  assert.deepEqual(browse.presets, [TFM_FACET]);
  assert.equal(browse.scopeQuery, "");
});

test("package-content presets lower the candidate bound until the last one is removed", () => {
  const base = createQueryRequest("Microsoft.");
  const withSkill = withPreset(base, SKILL_FACET);
  const withSkillAndManifest = withPreset(withSkill, TFM_FACET);
  const manifestOnly = withoutPreset(
    withSkillAndManifest,
    SKILL_FACET.id);

  assert.equal(withSkill.requestedLimit, 20);
  assert.equal(withSkillAndManifest.requestedLimit, 20);
  assert.equal(manifestOnly.requestedLimit, 200);
  assert.equal(withSkill.requestedMatchLimit, 100);
  assert.equal(withSkillAndManifest.requestedMatchLimit, 100);
  assert.equal(manifestOnly.requestedMatchLimit, 100);
});

test("only tool-format presets grant bounded package-content work", () => {
  const base = createQueryRequest("Azure.");

  assert.equal(withPreset(base, ANY_TOOL_FACET).requestedLimit, 200);
  for (const preset of [TOOL_V1_FACET, TOOL_V2_FACET]) {
    assert.equal(withPreset(base, preset).requestedLimit, 20);
  }
});

test("withScopeQuery preserves selected presets and bounds while changing search text", () => {
  const request = {
    ...withPreset(createQueryRequest("Microsoft."), TFM_FACET),
    requestedLimit: 25,
    requestedMatchLimit: 10,
  };

  assert.deepEqual(withScopeQuery(request, "System."), {
    ...request,
    scopeQuery: "System.",
  });
});

test("controller configures blank package input as idle while retaining presets", () => {
  const state = initialQueryState();
  let runs = 0;
  const controller = createPackageQueryController(
    state,
    {
      async run() {
        runs++;
        return { kind: "exhausted" };
      },
    },
    () => {},
  );
  const configured = withPreset(createQueryRequest(""), TFM_FACET);

  controller.configure(configured);

  assert.equal(runs, 0);
  assert.deepEqual(state.request?.presets, [TFM_FACET]);
  assert.equal(state.outcome.completion.kind, "idle");
});

test("blank package input stays idle while editor and prerelease controls retain presets", () => {
  const configuredPackage = withPreset(createQueryRequest(""), TFM_FACET);
  assert.equal(shouldExecuteQuery(configuredPackage), false);

  const drafted = withEditorDraft(configuredPackage, "Newtonsoft.Json");
  assert.equal(drafted.scopeQuery, "Newtonsoft.Json");
  assert.equal(shouldExecuteQuery(drafted), true);
  assert.deepEqual(togglePreset(drafted, SKILL_FACET).presets, [
    TFM_FACET,
    SKILL_FACET,
  ]);
  const prerelease = withSourceSelection(drafted, {
    includePrerelease: true,
  });
  assert.equal(prerelease.includePrerelease, true);
  assert.deepEqual(prerelease.presets, [TFM_FACET]);
});

test("togglePreset replaces an active preset in the same producer-owned selection group", () => {
  const withDependencies = togglePreset(
    createQueryRequest("Microsoft."),
    HAS_DEPENDENCIES_FACET);
  const withoutDependencies = togglePreset(
    withDependencies,
    NO_DEPENDENCIES_FACET);

  assert.deepEqual(
    withoutDependencies.presets.map(preset => preset.id),
    [NO_DEPENDENCIES_FACET.id]);
});

test("togglePreset applies product-owned tool replacement and union groups", () => {
  const withV1 = togglePreset(createQueryRequest("Microsoft."), TOOL_V1_FACET);
  const withBoth = togglePreset(withV1, TOOL_V2_FACET);
  const withAny = togglePreset(withBoth, ANY_TOOL_FACET);
  const backToV2 = togglePreset(withAny, TOOL_V2_FACET);

  assert.deepEqual(
    withBoth.presets.map(preset => preset.id),
    [TOOL_V1_FACET.id, TOOL_V2_FACET.id]);
  assert.deepEqual(
    withAny.presets.map(preset => preset.id),
    [ANY_TOOL_FACET.id]);
  assert.equal(withAny.requestedLimit, 200);
  assert.deepEqual(
    backToV2.presets.map(preset => preset.id),
    [TOOL_V2_FACET.id]);
  assert.equal(backToV2.requestedLimit, 20);
});

test("appendRows and appendFailure accumulate without mutating prior outcome", () => {
  const start = emptyOutcome();
  const withRows = appendRows(start, [row("A"), row("B")]);
  const withBoth = appendFailure(withRows, "source X timed out");

  assert.equal(start.rows.length, 0);
  assert.equal(withRows.rows.length, 2);
  assert.equal(withBoth.failures.length, 1);
  assert.deepEqual(withBoth.rows.map(r => r.packageId), ["A", "B"]);
});

test("appendAssessment retains semantic misses separately from rows and failures", () => {
  const start = emptyOutcome();
  const assessed = appendAssessment(start, NO_MATCH_ASSESSMENT);

  assert.deepEqual(start.assessments, []);
  assert.deepEqual(assessed.assessments, [NO_MATCH_ASSESSMENT]);
  assert.deepEqual(assessed.rows, []);
  assert.deepEqual(assessed.failures, []);
});

test("appendProgress replaces one phase while retaining rows and other phases", () => {
  const start = appendRows(emptyOutcome(), [row("A")]);
  const searched = appendProgress(start, {
    phase: "search",
    completed: 1,
    limit: 1,
  });
  const evaluated = appendProgress(searched, {
    phase: "manifest",
    completed: 2,
    limit: 20,
  });
  const advanced = appendProgress(evaluated, {
    phase: "manifest",
    completed: 3,
    limit: 20,
  });

  assert.deepEqual(advanced.rows.map(item => item.packageId), ["A"]);
  assert.deepEqual(advanced.progress, [
    { phase: "search", completed: 1, limit: 1 },
    { phase: "manifest", completed: 3, limit: 20 },
  ]);
});

test("withCompletion sets the honesty label without touching rows", () => {
  const outcome = appendRows(emptyOutcome(), [row("A")]);
  const bounded: QueryCompletion = { kind: "bounded", reason: "first 1,500 relevance-ranked ids" };
  const completed = withCompletion(outcome, bounded);

  assert.equal(completed.completion.kind, "bounded");
  assert.equal(completed.rows.length, 1);
});

function stubSource(
  pages: (readonly QueryResultRow[])[],
  completion: TerminalQueryCompletion,
  failures: string[] = [],
): PackageQueryDataSource {
  return {
    async run(_request, onPage, onFailure) {
      for (const page of pages) onPage(page);
      for (const failure of failures) onFailure(failure);
      return completion;
    },
  };
}

test("controller run() streams pages into state and applies final completion", async () => {
  const state = initialQueryState();
  let updates = 0;
  const controller = createPackageQueryController(
    state,
    stubSource([[row("A")], [row("B")]], { kind: "exhausted" }, ["feed Y unreachable"]),
    () => { updates++; },
  );

  await controller.run(createQueryRequest("Microsoft."));

  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A", "B"]);
  assert.deepEqual(state.outcome.failures, ["feed Y unreachable"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
  assert.ok(updates > 0);
});

test("controller replenishes only near the granted match-window edge", async () => {
  const state = initialQueryState();
  const requested: number[] = [];
  let publish!: (rows: readonly QueryResultRow[]) => void;
  let finish!: (completion: TerminalQueryCompletion) => void;
  const source: PackageQueryDataSource = {
    initialMatchCredit: 20,
    requestMore(additionalMatchCredit) {
      requested.push(additionalMatchCredit);
      return true;
    },
    async run(_request, onPage) {
      publish = onPage;
      return await new Promise<TerminalQueryCompletion>(
        resolve => { finish = resolve; });
    },
  };
  const controller = createPackageQueryController(state, source, () => {});
  const running = controller.run(createQueryRequest("Microsoft."));

  publish(Array.from({ length: 14 }, (_, index) => row(`P${index}`)));
  controller.requestMore();
  assert.deepEqual(requested, []);

  publish([row("P14")]);
  controller.requestMore();
  controller.requestMore();
  assert.deepEqual(requested, [10]);

  publish(Array.from({ length: 10 }, (_, index) => row(`Q${index}`)));
  controller.requestMore();
  assert.deepEqual(requested, [10, 10]);

  finish({ kind: "exhausted" });
  await running;
  controller.requestMore();
  assert.deepEqual(requested, [10, 10]);
});

test("controller does not count rejected replenishment as granted credit", async () => {
  const state = initialQueryState();
  let requests = 0;
  let publish!: (rows: readonly QueryResultRow[]) => void;
  let finish!: (completion: TerminalQueryCompletion) => void;
  const source: PackageQueryDataSource = {
    initialMatchCredit: 20,
    requestMore() {
      requests++;
      return false;
    },
    async run(_request, onPage) {
      publish = onPage;
      return await new Promise<TerminalQueryCompletion>(
        resolve => { finish = resolve; });
    },
  };
  const controller = createPackageQueryController(state, source, () => {});
  const running = controller.run(createQueryRequest("Microsoft."));

  publish(Array.from({ length: 15 }, (_, index) => row(`P${index}`)));
  controller.requestMore();
  controller.requestMore();

  assert.equal(requests, 2);
  finish({ kind: "cancelled" });
  await running;
});

test("controller publishes progress without clearing streamed rows", async () => {
  const state = initialQueryState();
  const source: PackageQueryDataSource = {
    async run(_request, onPage, _onFailure, onProgress) {
      onPage([row("A")]);
      onProgress({ phase: "manifest", completed: 1, limit: 20 });
      return { kind: "exhausted" };
    },
  };
  const controller = createPackageQueryController(state, source, () => {});

  await controller.run(createQueryRequest("Microsoft."));

  assert.deepEqual(state.outcome.rows.map(item => item.packageId), ["A"]);
  assert.deepEqual(state.outcome.progress, [
    { phase: "manifest", completed: 1, limit: 20 },
  ]);
});

test("a data source that rejects transitions to a visible 'failed' completion, not a stuck 'streaming' one", async () => {
  const state = initialQueryState();
  const rejectingSource: PackageQueryDataSource = {
    async run(_request, onPage) {
      onPage([row("A")]);
      throw new Error("feed unreachable");
    },
  };
  const controller = createPackageQueryController(state, rejectingSource, () => {});

  // run() itself must not reject past the controller — a caller awaiting it
  // should see a settled outcome, not an unhandled rejection.
  await assert.doesNotReject(controller.run(createQueryRequest("Microsoft.")));

  assert.notEqual(state.outcome.completion.kind, "streaming");
  assert.equal(state.outcome.completion.kind, "failed");
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A"]);
  // A whole-query rejection is the "Failed" state, not "Partial failure" —
  // the design doc's States table treats these as distinct (one source/page
  // failing vs. the request itself never reaching completion). It must not
  // also land in `failures`, or a total failure would render as if it were
  // merely partial: a "some sources failed" banner duplicating the same
  // reason the "Query failed" state already names.
  assert.deepEqual(state.outcome.failures, []);
});

test("starting a new run() aborts the previous generation's abortSignal, not just cancel()", async () => {
  const state = initialQueryState();
  let firstAborted = false;
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });

  const slowThenFast: PackageQueryDataSource = {
    async run(request, onPage, _onFailure, _onProgress, abortSignal) {
      if (request.scopeQuery === "slow") {
        abortSignal.addEventListener("abort", () => { firstAborted = true; });
        await firstGate;
        return { kind: "cancelled" };
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };

  const controller = createPackageQueryController(state, slowThenFast, () => {});

  const firstRun = controller.run(createQueryRequest("slow"));
  await controller.run(createQueryRequest("fast"));
  releaseFirst();
  await firstRun;

  assert.ok(firstAborted, "starting a newer run() should abort the superseded generation's signal");
});

test("each run() receives its own distinct abortSignal even when onUpdate() reentrantly starts another run()", async () => {
  const state = initialQueryState();
  const signals: AbortSignal[] = [];
  let triggeredReentrant = false;
  let releaseSlow!: () => void;
  const slowGate = new Promise<void>(resolve => { releaseSlow = resolve; });

  const slowThenFast: PackageQueryDataSource = {
    async run(request, onPage, _onFailure, _onProgress, abortSignal) {
      signals.push(abortSignal);
      if (request.scopeQuery === "slow") {
        await slowGate;
        return { kind: "cancelled" };
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };

  // onUpdate() is caller-supplied and may synchronously start another run()
  // in direct response to the request/outcome reset the first run() performs
  // — before that first run() has passed its own signal to the source. If
  // the controller reads its mutable `abortController` field late (at the
  // `source.run()` call site) rather than capturing it up front, the
  // reentrant run() reassigning that field mid-flight would silently hand
  // the first run someone else's signal instead of its own.
  const controller = createPackageQueryController(state, slowThenFast, () => {
    if (!triggeredReentrant && state.request?.scopeQuery === "slow") {
      triggeredReentrant = true;
      void controller.run(createQueryRequest("fast"));
    }
  });

  const slowRun = controller.run(createQueryRequest("slow"));
  releaseSlow();
  await slowRun;

  assert.equal(signals.length, 2);
  assert.notEqual(signals[0], signals[1], "the slow run must keep its own signal, not the reentrant run's");
});

test("a superseded run's late pages never land in the newer outcome", async () => {
  const state = initialQueryState();
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });

  const slowThenFast: PackageQueryDataSource = {
    async run(request, onPage) {
      if (request.scopeQuery === "slow") {
        await firstGate;
        onPage([row("stale")]);
        return { kind: "cancelled" };
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };

  const controller = createPackageQueryController(state, slowThenFast, () => {});

  const firstRun = controller.run(createQueryRequest("slow"));
  await controller.run(createQueryRequest("fast"));
  releaseFirst();
  await firstRun;

  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["fresh"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
});

test("changing only prerelease selection supersedes work without changing search text", async () => {
  const state = initialQueryState();
  let firstSignal: AbortSignal | undefined;
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });
  const source: PackageQueryDataSource = {
    async run(request, onPage, onFailure, onProgress, signal) {
      assert.equal(request.scopeQuery, "Contoso.");
      if (!request.includePrerelease) {
        firstSignal = signal;
        await firstGate;
        onPage([row("Stale")]);
        onFailure("Stale failure");
        onProgress({ phase: "search", completed: 1, limit: 1 });
        return { kind: "exhausted" };
      }
      onPage([row("Current")]);
      return { kind: "exhausted" };
    },
  };
  const controller = createPackageQueryController(state, source, () => {});
  const first = controller.run(createQueryRequest("Contoso."));
  await controller.run({
    ...createQueryRequest("Contoso."),
    includePrerelease: true,
  });
  assert.equal(firstSignal?.aborted, true);
  releaseFirst();
  await first;

  assert.deepEqual(state.outcome.rows.map(item => item.packageId), ["Current"]);
  assert.deepEqual(state.outcome.failures, []);
  assert.deepEqual(state.outcome.progress, []);
  assert.deepEqual(state.outcome.completion, {
    kind: "exhausted",
  });
});

test("a superseded run's late rejection never overwrites the newer outcome", async () => {
  const state = initialQueryState();
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });

  const slowRejectThenFast: PackageQueryDataSource = {
    async run(request, onPage) {
      if (request.scopeQuery === "slow") {
        await firstGate;
        throw new Error("stale feed error");
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };

  const controller = createPackageQueryController(state, slowRejectThenFast, () => {});

  const firstRun = controller.run(createQueryRequest("slow"));
  await controller.run(createQueryRequest("fast"));
  releaseFirst();
  await firstRun;

  // The first run's rejection resolves after the second run has already
  // completed; it must not clobber the newer, successful outcome.
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["fresh"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
  assert.deepEqual(state.outcome.failures, []);
});

test("a superseded run's late onFailure call never lands in the newer outcome", async () => {
  const state = initialQueryState();
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });

  const slowFailThenFast: PackageQueryDataSource = {
    async run(request, onPage, onFailure) {
      if (request.scopeQuery === "slow") {
        await firstGate;
        onFailure("stale source failure");
        return { kind: "exhausted" };
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };

  const controller = createPackageQueryController(state, slowFailThenFast, () => {});

  const firstRun = controller.run(createQueryRequest("slow"));
  await controller.run(createQueryRequest("fast"));
  releaseFirst();
  await firstRun;

  // The first run's late onFailure() call resolves after the second run has
  // already completed cleanly; it must not attach a stale failure to the
  // newer outcome (the design doc's race-safety claim covers this callback
  // too, not just late pages/rejections).
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["fresh"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
  assert.deepEqual(state.outcome.failures, []);
});

test("a superseded run's late progress never lands in the newer outcome", async () => {
  const state = initialQueryState();
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });
  const slowThenFast: PackageQueryDataSource = {
    async run(request, onPage, _onFailure, onProgress) {
      if (request.scopeQuery === "slow") {
        await firstGate;
        onProgress({ phase: "manifest", completed: 12, limit: 20 });
        return { kind: "exhausted" };
      }
      onPage([row("fresh")]);
      return { kind: "exhausted" };
    },
  };
  const controller = createPackageQueryController(
    state,
    slowThenFast,
    () => {});

  const firstRun = controller.run(createQueryRequest("slow"));
  await controller.run(createQueryRequest("fast"));
  releaseFirst();
  await firstRun;

  assert.deepEqual(state.outcome.rows.map(item => item.packageId), ["fresh"]);
  assert.deepEqual(state.outcome.progress, []);
  assert.equal(state.outcome.completion.kind, "exhausted");
});

test("cancel() marks a streaming completion cancelled without clearing already-streamed rows", async () => {
  const state = initialQueryState();
  let releaseGate!: () => void;
  const gate = new Promise<void>(resolve => { releaseGate = resolve; });
  const controller = createPackageQueryController(
    state,
    {
      async run(_request, onPage, _onFailure, _onProgress, abortSignal) {
        onPage([row("A")]);
        await gate;
        if (abortSignal.aborted) return { kind: "cancelled" };
        return { kind: "exhausted" };
      },
    },
    () => {},
  );

  const running = controller.run(createQueryRequest("Microsoft."));
  controller.cancel();
  releaseGate();
  await running;

  assert.equal(state.outcome.completion.kind, "cancelled");
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A"]);
});

test("cancel() is a no-op once the run has already reached a final completion", async () => {
  const state = initialQueryState();
  const controller = createPackageQueryController(
    state,
    stubSource([[row("A")]], { kind: "exhausted" }),
    () => {},
  );

  await controller.run(createQueryRequest("Microsoft."));
  controller.cancel();

  // A finished run's honesty label must not be overwritten by a later cancel().
  assert.equal(state.outcome.completion.kind, "exhausted");
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A"]);
});

test("cancel() signals the data source's abortSignal so in-flight work can stop", async () => {
  const state = initialQueryState();
  let observedAborted = false;
  const controller = createPackageQueryController(
    state,
    {
      async run(_request, onPage, _onFailure, _onProgress, abortSignal) {
        onPage([row("A")]);
        await new Promise<void>(resolve => {
          abortSignal.addEventListener("abort", () => { observedAborted = true; resolve(); });
        });
        return { kind: "cancelled" };
      },
    },
    () => {},
  );

  const running = controller.run(createQueryRequest("Microsoft."));
  controller.cancel();
  await running;

  assert.ok(observedAborted, "the source's abortSignal should fire when cancel() is called");
});

test("cancel() stays authoritative even against a source that ignores the abort signal", async () => {
  const state = initialQueryState();
  let releaseGate!: () => void;
  const gate = new Promise<void>(resolve => { releaseGate = resolve; });

  // This source never checks abortSignal.aborted at all — it keeps working
  // and eventually reports a page, a failure, and an "exhausted" completion
  // regardless of cancellation. The generation guard (not the source's
  // cooperation) is what must keep "cancelled" authoritative here.
  const uncooperativeSource: PackageQueryDataSource = {
    async run(_request, onPage, onFailure) {
      await gate;
      onPage([row("late")]);
      onFailure("late failure");
      return { kind: "exhausted" };
    },
  };

  const controller = createPackageQueryController(state, uncooperativeSource, () => {});

  const running = controller.run(createQueryRequest("Microsoft."));
  controller.cancel();
  releaseGate();
  await running;

  assert.equal(state.outcome.completion.kind, "cancelled");
  assert.deepEqual(state.outcome.rows, []);
  assert.deepEqual(state.outcome.failures, []);
});
