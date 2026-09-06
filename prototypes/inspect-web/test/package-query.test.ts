import assert from "node:assert/strict";
import test from "node:test";

import {
  appendFailure,
  appendProgress,
  appendRows,
  createPackageQueryController,
  createQueryRequest,
  emptyOutcome,
  initialQueryState,
  toggleFacet,
  withCompletion,
  withFacet,
  withScopeQuery,
  withoutFacet,
  type PackageQueryDataSource,
  type QueryCompletion,
  type QueryFacetTerm,
  type QueryResultRow,
  type TerminalQueryCompletion,
} from "../src/package-query.ts";

const TFM_FACET: QueryFacetTerm = {
  key: "tfm-out-of-support",
  label: "out-of-support only",
  tier: "nuspec",
};

const HAS_DEPENDENCIES_FACET: QueryFacetTerm = {
  key: "package.query.has-dependencies",
  label: "Has dependencies",
  tier: "nuspec",
  selectionGroupId: "package.query.dependencies",
};

const NO_DEPENDENCIES_FACET: QueryFacetTerm = {
  key: "package.query.no-dependencies",
  label: "No dependencies",
  tier: "nuspec",
  selectionGroupId: "package.query.dependencies",
};

const SKILL_FACET: QueryFacetTerm = {
  key: "package.query.embedded-skill",
  label: "embedded SKILL.md",
  tier: "package-content",
};

const ANY_TOOL_FACET: QueryFacetTerm = {
  key: "package.query.dotnet-tool",
  label: ".NET Tool",
  tier: "nuspec",
  selectionGroupId: "package.query.dotnet-tool-format",
};

const TOOL_V1_FACET: QueryFacetTerm = {
  key: "package.query.dotnet-tool-v1",
  label: "v1",
  tier: "package-content",
  selectionGroupId: "package.query.dotnet-tool-format",
  combinesWithinSelectionGroup: true,
};

const TOOL_V2_FACET: QueryFacetTerm = {
  key: "package.query.dotnet-tool-v2",
  label: "v2",
  tier: "package-content",
  selectionGroupId: "package.query.dotnet-tool-format",
  combinesWithinSelectionGroup: true,
};

function row(packageId: string): QueryResultRow {
  return {
    packageId,
    version: "1.0.0",
    tier: "nuspec",
    evidence: ["net45"],
    totalDownloads: 100,
  };
}

test("withFacet is idempotent by key and withoutFacet removes by key", () => {
  const base = createQueryRequest("Microsoft.");
  const once = withFacet(base, TFM_FACET);
  const twice = withFacet(once, TFM_FACET);

  assert.equal(once.facets.length, 1);
  assert.equal(twice.facets.length, 1);
  assert.equal(withoutFacet(twice, TFM_FACET.key).facets.length, 0);
});

test("createQueryRequest gives candidate and match limits independent defaults", () => {
  const defaults = createQueryRequest("Microsoft.");

  assert.equal(defaults.requestedLimit, 200);
  assert.equal(defaults.requestedMatchLimit, 100);
  assert.notEqual(defaults.requestedLimit, defaults.requestedMatchLimit);
  assert.equal(defaults.packageType, null);
  assert.equal(defaults.sourceOrderId, null);
  assert.equal(defaults.includePrerelease, false);
});

test("browse and free-text requests preserve source intent without resolving source defaults", () => {
  for (const text of ["", "  hosting dependency injection  ", "System.*"]) {
    assert.equal(createQueryRequest(text).scopeQuery, text);
    assert.equal(createQueryRequest(text).sourceOrderId, null);
  }
});

test("inspection changes retain opaque source selections and independent match limits", () => {
  const request = {
    ...createQueryRequest(" hosting libraries "),
    packageType: "Producer.CustomType",
    sourceOrderId: "producer.order.custom",
    includePrerelease: true,
    requestedMatchLimit: 7,
  };
  const content = toggleFacet(request, SKILL_FACET);
  const manifest = toggleFacet(toggleFacet(content, TFM_FACET), SKILL_FACET);
  const browse = withScopeQuery(manifest, "");

  assert.equal(content.requestedLimit, 20);
  assert.equal(manifest.requestedLimit, 200);
  for (const changed of [content, manifest, browse]) {
    assert.equal(changed.packageType, request.packageType);
    assert.equal(changed.sourceOrderId, request.sourceOrderId);
    assert.equal(changed.includePrerelease, true);
    assert.equal(changed.requestedMatchLimit, 7);
  }
  assert.deepEqual(browse.facets, [TFM_FACET]);
  assert.equal(browse.scopeQuery, "");
});

test("package-content facets lower the candidate bound until the last one is removed", () => {
  const base = createQueryRequest("Microsoft.");
  const withSkill = withFacet(base, SKILL_FACET);
  const withSkillAndManifest = withFacet(withSkill, TFM_FACET);
  const manifestOnly = withoutFacet(
    withSkillAndManifest,
    SKILL_FACET.key);

  assert.equal(withSkill.requestedLimit, 20);
  assert.equal(withSkillAndManifest.requestedLimit, 20);
  assert.equal(manifestOnly.requestedLimit, 200);
  assert.equal(withSkill.requestedMatchLimit, 100);
  assert.equal(withSkillAndManifest.requestedMatchLimit, 100);
  assert.equal(manifestOnly.requestedMatchLimit, 100);
});

test("withScopeQuery preserves facets and bounds while changing search text", () => {
  const request = {
    ...withFacet(createQueryRequest("Microsoft."), TFM_FACET),
    requestedLimit: 25,
    requestedMatchLimit: 10,
  };

  assert.deepEqual(withScopeQuery(request, "System."), {
    ...request,
    scopeQuery: "System.",
  });
});

test("controller runs blank browse with source selection and nullable metadata", async () => {
  const state = initialQueryState();
  const request = {
    ...createQueryRequest(""),
    packageType: "Producer.Type",
    sourceOrderId: "producer.order",
    includePrerelease: true,
  };
  const source: PackageQueryDataSource = {
    async run(received, onPage) {
      assert.deepEqual(received, request);
      onPage([{
        ...row("Browse.Result"),
        tier: "search-metadata",
        evidence: ["Producer source selection and order"],
        totalDownloads: null,
      }]);
      return { kind: "bounded", reason: "one finite Gallery response" };
    },
  };
  const controller = createPackageQueryController(state, source, () => {});

  await controller.run(request);

  assert.equal(state.request?.scopeQuery, "");
  assert.equal(state.outcome.rows[0]?.tier, "search-metadata");
  assert.equal(state.outcome.rows[0]?.totalDownloads, null);
  assert.equal(state.outcome.completion.kind, "bounded");
});

test("toggleFacet replaces an active facet in the same producer-owned selection group", () => {
  const withDependencies = toggleFacet(
    createQueryRequest("Microsoft."),
    HAS_DEPENDENCIES_FACET);
  const withoutDependencies = toggleFacet(
    withDependencies,
    NO_DEPENDENCIES_FACET);

  assert.deepEqual(
    withoutDependencies.facets.map(facet => facet.key),
    [NO_DEPENDENCIES_FACET.key]);
});

test("toggleFacet unions combining tool versions while any-tool remains exclusive", () => {
  const withV1 = toggleFacet(createQueryRequest("Microsoft."), TOOL_V1_FACET);
  const withBoth = toggleFacet(withV1, TOOL_V2_FACET);
  const withAny = toggleFacet(withBoth, ANY_TOOL_FACET);
  const backToV2 = toggleFacet(withAny, TOOL_V2_FACET);

  assert.deepEqual(
    withBoth.facets.map(facet => facet.key),
    [TOOL_V1_FACET.key, TOOL_V2_FACET.key]);
  assert.deepEqual(
    withAny.facets.map(facet => facet.key),
    [ANY_TOOL_FACET.key]);
  assert.deepEqual(
    backToV2.facets.map(facet => facet.key),
    [TOOL_V2_FACET.key]);
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

test("changing only source selection supersedes browse work without changing search text", async () => {
  const state = initialQueryState();
  let firstSignal: AbortSignal | undefined;
  let releaseFirst!: () => void;
  const firstGate = new Promise<void>(resolve => { releaseFirst = resolve; });
  const source: PackageQueryDataSource = {
    async run(request, onPage, onFailure, onProgress, signal) {
      assert.equal(request.scopeQuery, "");
      if (request.sourceOrderId === null) {
        firstSignal = signal;
        await firstGate;
        onPage([row("Stale")]);
        onFailure("Stale failure");
        onProgress({ phase: "search", completed: 1, limit: 1 });
        return { kind: "exhausted" };
      }
      onPage([row("Current")]);
      return { kind: "bounded", reason: "one finite Gallery response" };
    },
  };
  const controller = createPackageQueryController(state, source, () => {});
  const first = controller.run(createQueryRequest(""));
  await controller.run({
    ...createQueryRequest(""),
    sourceOrderId: "producer.order.custom",
  });
  assert.equal(firstSignal?.aborted, true);
  releaseFirst();
  await first;

  assert.deepEqual(state.outcome.rows.map(item => item.packageId), ["Current"]);
  assert.deepEqual(state.outcome.failures, []);
  assert.deepEqual(state.outcome.progress, []);
  assert.deepEqual(state.outcome.completion, {
    kind: "bounded",
    reason: "one finite Gallery response",
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
