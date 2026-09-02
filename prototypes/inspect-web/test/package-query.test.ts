import assert from "node:assert/strict";
import test from "node:test";

import {
  appendFailure,
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
});

test("withScopeQuery preserves facets and bounds while changing the prefix", () => {
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

test("appendRows and appendFailure accumulate without mutating prior outcome", () => {
  const start = emptyOutcome();
  const withRows = appendRows(start, [row("A"), row("B")]);
  const withBoth = appendFailure(withRows, "source X timed out");

  assert.equal(start.rows.length, 0);
  assert.equal(withRows.rows.length, 2);
  assert.equal(withBoth.failures.length, 1);
  assert.deepEqual(withBoth.rows.map(r => r.packageId), ["A", "B"]);
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
    async run(request, onPage, _onFailure, abortSignal) {
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
    async run(request, onPage, _onFailure, abortSignal) {
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

test("cancel() marks a streaming completion cancelled without clearing already-streamed rows", async () => {
  const state = initialQueryState();
  let releaseGate!: () => void;
  const gate = new Promise<void>(resolve => { releaseGate = resolve; });
  const controller = createPackageQueryController(
    state,
    {
      async run(_request, onPage, _onFailure, abortSignal) {
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
      async run(_request, onPage, _onFailure, abortSignal) {
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
