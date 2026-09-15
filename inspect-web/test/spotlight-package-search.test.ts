import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlightPackageSearch,
  normalizeSpotlightPackageSearchSnapshot,
  spotlightPackageSearchError,
  spotlightPackageSearchIsLoading,
  visibleSpotlightPackageHits,
  type ReadySpotlightPackageSearch,
  type SpotlightPackageSearchDependencies,
  type SpotlightPackageSearchResultState,
  type SpotlightPackageSearchState,
} from "../src/spotlight-package-search.ts";
import type { SpotlightPackageHit } from "../src/spotlight.ts";

interface ScheduledSearch {
  id: number;
  delay: number;
  callback: () => Promise<void>;
}

function ready(
  query: string,
  hits: readonly SpotlightPackageHit[] = [],
): ReadySpotlightPackageSearch {
  return { status: "ready", query, hits };
}

function loading(
  query: string,
  cached: ReadySpotlightPackageSearch | null = null,
): SpotlightPackageSearchResultState {
  return { status: "loading", query, cached };
}

function searchState(
  overrides: Partial<SpotlightPackageSearchState> = {},
): SpotlightPackageSearchState {
  return {
    spotlightQuery: "",
    spotlightScope: "all",
    spotlightPackageSearch: { status: "idle" },
    ...overrides,
  };
}

function searchDependencies(
  state: SpotlightPackageSearchState,
  overrides:
    Partial<Omit<SpotlightPackageSearchDependencies<number>, "state">> = {},
) {
  let nextId = 0;
  const scheduled: ScheduledSearch[] = [];
  const cancelled: number[] = [];
  let updates = 0;
  const dependencies: SpotlightPackageSearchDependencies<number> = {
    state,
    queryPackages: async query => [{ id: query, version: "1.0.0" }],
    schedule: (callback, delay) => {
      const id = ++nextId;
      scheduled.push({ id, delay, callback });
      return id;
    },
    cancelScheduled: id => {
      cancelled.push(id);
    },
    updateResults: () => {
      updates++;
    },
    ...overrides,
  };
  return {
    dependencies,
    scheduled,
    cancelled,
    updates: () => updates,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

test("eligible package queries schedule one trimmed debounced request", async () => {
  let queries = 0;
  const state = searchState({ spotlightQuery: "  Example  " });
  const harness = searchDependencies(state, {
    queryPackages: async query => {
      assert.equal(query, "Example");
      queries++;
      return [];
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(queries, 0);
  assert.deepEqual(state.spotlightPackageSearch, loading("Example"));
  assert.equal(harness.scheduled.length, 1);
  assert.equal(harness.scheduled[0]?.delay, 220);
  await harness.scheduled[0]?.callback();
  assert.equal(queries, 1);
});

test("the dedicated Packages scope schedules NuGet discovery", () => {
  const state = searchState({
    spotlightQuery: "Example",
    spotlightScope: "packages",
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(harness.scheduled.length, 1);
  assert.equal(state.spotlightPackageSearch.status, "loading");
});

test("rescheduling cancels the prior debounce before replacing it", () => {
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const first = state.spotlightPackageSearch;
  state.spotlightQuery = "second";
  search.schedule();

  assert.deepEqual(harness.cancelled, [1]);
  assert.equal(harness.scheduled.length, 2);
  assert.deepEqual(state.spotlightPackageSearch, loading("second"));
  assert.notEqual(state.spotlightPackageSearch, first);
});

test("early-return transitions cancel a pending debounce", () => {
  const cases: readonly {
    name: string;
    transition: (state: SpotlightPackageSearchState) => void;
    expected: SpotlightPackageSearchResultState;
  }[] = [
    {
      name: "ineligible scope",
      transition: state => {
        state.spotlightScope = "types";
      },
      expected: { status: "idle" },
    },
    {
      name: "short query",
      transition: state => {
        state.spotlightQuery = "x";
      },
      expected: { status: "idle" },
    },
    {
      name: "resolved query",
      transition: state => {
        state.spotlightPackageSearch =
          ready("Example", [{ id: "Example", version: "1.0.0" }]);
      },
      expected: ready("Example", [{ id: "Example", version: "1.0.0" }]),
    },
  ];

  for (const scenario of cases) {
    const state = searchState({ spotlightQuery: "Example" });
    const harness = searchDependencies(state);
    const search = createSpotlightPackageSearch(harness.dependencies);

    search.schedule();
    scenario.transition(state);
    search.schedule();

    assert.deepEqual(harness.cancelled, [1], scenario.name);
    assert.equal(harness.scheduled.length, 1, scenario.name);
    assert.deepEqual(
      state.spotlightPackageSearch,
      scenario.expected,
      scenario.name,
    );
  }
});

test("ineligible scopes settle loading to its successful cache", () => {
  const cached = ready(
    "Existing",
    [{ id: "Existing", version: "1.0.0" }],
  );
  const state = searchState({
    spotlightQuery: "Example",
    spotlightScope: "types",
    spotlightPackageSearch: loading("Example", cached),
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(state.spotlightPackageSearch, cached);
  assert.equal(harness.scheduled.length, 0);
});

test("short queries discard package discovery state", () => {
  const state = searchState({
    spotlightQuery: "x",
    spotlightPackageSearch: loading(
      "Example",
      ready("Existing", [{ id: "Existing", version: "1.0.0" }]),
    ),
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
});

test("cached queries restore their exact ready value without another request", () => {
  const cached = ready("Example", [{ id: "Example", version: "1.0.0" }]);
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPackageSearch: loading("Other", cached),
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(state.spotlightPackageSearch, cached);
  assert.equal(harness.scheduled.length, 0);
});

test("current package results publish and refresh the mounted surface", async () => {
  const hits: SpotlightPackageHit[] = [{
    id: "Example.Package",
    version: "2.0.0",
  }];
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async query => {
      assert.equal(query, "Example");
      return hits;
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  await harness.scheduled[0]?.callback();

  assert.deepEqual(state.spotlightPackageSearch, ready("Example", hits));
  assert.equal(state.spotlightPackageSearch.status, "ready");
  assert.notEqual(state.spotlightPackageSearch.hits, hits);
  assert.equal(harness.updates(), 1);
});

test("current failures discard cache and the same query can be retried", async () => {
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPackageSearch: ready("Old", [{ id: "Old", version: "1.0.0" }]),
  });
  const harness = searchDependencies(state, {
    queryPackages: async () => {
      throw new Error("NuGet unavailable");
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  await harness.scheduled[0]?.callback();

  assert.equal(state.spotlightPackageSearch.status, "failed");
  assert.equal(state.spotlightPackageSearch.query, "Example");
  assert.match(state.spotlightPackageSearch.error, /NuGet unavailable/);
  assert.match(state.spotlightPackageSearch.error, /try again/);
  assert.equal(harness.updates(), 1);
  search.schedule();
  assert.equal(harness.scheduled.length, 2);
  assert.deepEqual(state.spotlightPackageSearch, loading("Example"));
});

for (const transition of ["edit-and-undo", "scope change"] as const) {
  test(`failed queries retry after ${transition} clears the displayed error`, async () => {
    const state = searchState({
      spotlightQuery: "Example",
      spotlightScope: "packages",
    });
    const queries: string[] = [];
    const hits = [{ id: "Example.Package", version: "1.0.0" }];
    const harness = searchDependencies(state, {
      queryPackages: async query => {
        queries.push(query);
        if (queries.length === 1) throw new Error("NuGet unavailable");
        return hits;
      },
    });
    const search = createSpotlightPackageSearch(harness.dependencies);

    search.schedule();
    await harness.scheduled[0]?.callback();
    assert.match(spotlightPackageSearchError(state.spotlightPackageSearch),
      /NuGet unavailable/);
    if (transition === "edit-and-undo") {
      state.spotlightQuery = "Example.more";
      search.schedule();
      state.spotlightQuery = "Example";
    } else {
      state.spotlightScope = "types";
      search.schedule();
      state.spotlightScope = "packages";
    }
    assert.equal(
      spotlightPackageSearchError(state.spotlightPackageSearch),
      "",
    );
    search.schedule();

    assert.equal(state.spotlightPackageSearch.status, "loading");
    assert.equal(harness.scheduled.length, transition === "edit-and-undo" ? 3 : 2);
    assert.deepEqual(harness.cancelled, transition === "edit-and-undo" ? [2] : []);
    await harness.scheduled.at(-1)?.callback();
    assert.deepEqual(queries, ["Example", "Example"]);
    assert.deepEqual(state.spotlightPackageSearch, ready("Example", hits));
  });
}

test("successful empty results remain cached after edit-and-undo", async () => {
  const state = searchState({ spotlightQuery: "Example" });
  const queries: string[] = [];
  const harness = searchDependencies(state, {
    queryPackages: async query => {
      queries.push(query);
      return [];
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  await harness.scheduled[0]?.callback();
  const cached = state.spotlightPackageSearch;
  assert.equal(cached.status, "ready");
  state.spotlightQuery = "Example.more";
  search.schedule();
  state.spotlightQuery = "Example";
  search.schedule();

  assert.deepEqual(queries, ["Example"]);
  assert.equal(harness.scheduled.length, 2);
  assert.deepEqual(harness.cancelled, [2]);
  assert.equal(state.spotlightPackageSearch, cached);
  assert.deepEqual(visibleSpotlightPackageHits(cached, "Example"), []);
});

test("input changes independently suppress stale package results", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const pending = state.spotlightPackageSearch;
  const request = harness.scheduled[0]?.callback();
  state.spotlightQuery = "second";
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.equal(state.spotlightPackageSearch, pending);
  assert.equal(spotlightPackageSearchIsLoading(state.spotlightPackageSearch), true);
  assert.equal(harness.updates(), 0);
});

test("newer loading identity suppresses stale success while publishing replacement", async () => {
  const first = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state, {
    queryPackages: async query =>
      query === "first"
        ? first.promise
        : [{ id: "Current", version: "2.0.0" }],
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const firstRequest = harness.scheduled[0]?.callback();
  state.spotlightQuery = "second";
  search.schedule();
  await harness.scheduled[1]?.callback();
  first.resolve([{ id: "Stale", version: "1.0.0" }]);
  await firstRequest;

  assert.deepEqual(
    state.spotlightPackageSearch,
    ready("second", [{ id: "Current", version: "2.0.0" }]),
  );
  assert.equal(harness.updates(), 1);
});

test("same-input loading identity independently suppresses stale failures", async () => {
  const first = deferred<readonly SpotlightPackageHit[]>();
  let queries = 0;
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async () => {
      queries++;
      if (queries === 1) return first.promise;
      return [{ id: "Current", version: "2.0.0" }];
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const firstRequest = harness.scheduled[0]?.callback();
  search.schedule();
  await harness.scheduled[1]?.callback();
  first.reject(new Error("stale failure"));
  await firstRequest;

  assert.deepEqual(
    state.spotlightPackageSearch,
    ready("Example", [{ id: "Current", version: "2.0.0" }]),
  );
  assert.equal(harness.updates(), 1);
});

test("input changes independently suppress stale failures", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const pending = state.spotlightPackageSearch;
  const request = harness.scheduled[0]?.callback();
  state.spotlightQuery = "second";
  query.reject(new Error("stale failure"));
  await request;

  assert.equal(state.spotlightPackageSearch, pending);
  assert.equal(harness.updates(), 0);
});

test("leaving package scopes invalidates an in-flight request", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const request = harness.scheduled[0]?.callback();
  state.spotlightScope = "types";
  search.schedule();
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
  assert.equal(harness.updates(), 0);
});

test("short-query cancellation stays effective if the prior input returns", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const request = harness.scheduled[0]?.callback();
  state.spotlightQuery = "x";
  search.schedule();
  state.spotlightQuery = "Example";
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
  assert.equal(harness.updates(), 0);
});

test("reset cancels scheduled and in-flight publication", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const request = harness.scheduled[0]?.callback();
  search.reset();
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.deepEqual(harness.cancelled, []);
  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
  assert.equal(harness.updates(), 0);
});

test("reset cancels a pending debounce and clears discovery state", () => {
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPackageSearch: {
      status: "failed",
      query: "Old",
      error: "Previous failure",
    },
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  search.reset();

  assert.deepEqual(harness.cancelled, [1]);
  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
});

test("snapshot normalization settles loading without mutating live state", () => {
  const cached = ready(
    "Existing",
    [{ id: "Existing", version: "1.0.0" }],
  );
  const withCache = loading("Pending", cached);
  const withoutCache = loading("Pending");

  assert.equal(normalizeSpotlightPackageSearchSnapshot(withCache), cached);
  assert.deepEqual(
    normalizeSpotlightPackageSearchSnapshot(withoutCache),
    { status: "idle" },
  );
  assert.deepEqual(withCache, loading("Pending", cached));
  assert.deepEqual(withoutCache, loading("Pending"));
});

test("a loading receipt cannot publish after snapshot settlement replaces it", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "Example" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const request = harness.scheduled[0]?.callback();
  state.spotlightPackageSearch =
    normalizeSpotlightPackageSearchSnapshot(state.spotlightPackageSearch);
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.deepEqual(state.spotlightPackageSearch, { status: "idle" });
  assert.equal(harness.updates(), 0);
});

test("result projections expose only evidence valid for the current variant", () => {
  const cached = ready(
    "Example",
    [{ id: "Example.Package", version: "1.0.0" }],
  );
  const pending = loading("Other", cached);
  const failed = {
    status: "failed",
    query: "Other",
    error: "Package search failed",
  } as const;

  assert.deepEqual(visibleSpotlightPackageHits(pending, "Example"), cached.hits);
  assert.deepEqual(visibleSpotlightPackageHits(pending, "Other"), []);
  assert.equal(spotlightPackageSearchIsLoading(pending), true);
  assert.equal(spotlightPackageSearchIsLoading(cached), false);
  assert.equal(spotlightPackageSearchError(failed), "Package search failed");
  assert.equal(spotlightPackageSearchError(pending), "");
});
