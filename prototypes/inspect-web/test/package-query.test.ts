import assert from "node:assert/strict";
import test from "node:test";

import {
  appendFailure,
  appendRows,
  createPackageQueryController,
  createQueryRequest,
  emptyOutcome,
  initialQueryState,
  withCompletion,
  withFacet,
  withoutFacet,
  type PackageQueryDataSource,
  type QueryCompletion,
  type QueryFacetTerm,
  type QueryResultRow,
} from "../src/package-query.ts";

const TFM_FACET: QueryFacetTerm = {
  key: "tfm-out-of-support",
  label: "out-of-support only",
  tier: "nuspec",
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
  const base = createQueryRequest("Microsoft.*", "Microsoft.");
  const once = withFacet(base, TFM_FACET);
  const twice = withFacet(once, TFM_FACET);

  assert.equal(once.facets.length, 1);
  assert.equal(twice.facets.length, 1);
  assert.equal(withoutFacet(twice, TFM_FACET.key).facets.length, 0);
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
  completion: QueryCompletion,
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

  await controller.run(createQueryRequest("Microsoft.*", "Microsoft."));

  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A", "B"]);
  assert.deepEqual(state.outcome.failures, ["feed Y unreachable"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
  assert.ok(updates > 0);
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

  const firstRun = controller.run(createQueryRequest("slow", "slow"));
  await controller.run(createQueryRequest("fast", "fast"));
  releaseFirst();
  await firstRun;

  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["fresh"]);
  assert.equal(state.outcome.completion.kind, "exhausted");
});

test("toggleSelection and clearSelection manage the selected set", () => {
  const state = initialQueryState();
  let updates = 0;
  const controller = createPackageQueryController(
    state,
    stubSource([], { kind: "exhausted" }),
    () => { updates++; },
  );

  controller.toggleSelection("A");
  controller.toggleSelection("B");
  assert.deepEqual([...state.selected].sort(), ["A", "B"]);

  controller.toggleSelection("A");
  assert.deepEqual([...state.selected], ["B"]);

  controller.clearSelection();
  assert.equal(state.selected.size, 0);
  assert.ok(updates >= 3);
});

test("cancel() marks completion cancelled without clearing already-streamed rows", async () => {
  const state = initialQueryState();
  const controller = createPackageQueryController(
    state,
    stubSource([[row("A")]], { kind: "exhausted" }),
    () => {},
  );

  await controller.run(createQueryRequest("Microsoft.*", "Microsoft."));
  controller.cancel();

  assert.equal(state.outcome.completion.kind, "cancelled");
  assert.deepEqual(state.outcome.rows.map(r => r.packageId), ["A"]);
});
