import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlightPackageSearch,
  type SpotlightPackageSearchDependencies,
  type SpotlightPackageSearchState,
} from "../src/spotlight-package-search.ts";
import type { SpotlightPackageHit } from "../src/spotlight.ts";

interface ScheduledSearch {
  id: number;
  delay: number;
  callback: () => Promise<void>;
}

function searchState(
  overrides: Partial<SpotlightPackageSearchState> = {},
): SpotlightPackageSearchState {
  return {
    spotlightQuery: "",
    spotlightScope: "all",
    spotlightPkgHits: [],
    spotlightPkgQuery: "",
    spotlightPkgLoading: false,
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
  assert.equal(state.spotlightPkgLoading, true);
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
  assert.equal(state.spotlightPkgLoading, true);
});

test("rescheduling cancels the prior debounce before replacing it", () => {
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  state.spotlightQuery = "second";
  search.schedule();

  assert.deepEqual(harness.cancelled, [1]);
  assert.equal(harness.scheduled.length, 2);
  assert.equal(state.spotlightPkgLoading, true);
});

test("early-return transitions cancel a pending debounce", () => {
  const cases: readonly {
    name: string;
    transition: (state: SpotlightPackageSearchState) => void;
  }[] = [
    {
      name: "ineligible scope",
      transition: state => {
        state.spotlightScope = "types";
      },
    },
    {
      name: "short query",
      transition: state => {
        state.spotlightQuery = "x";
      },
    },
    {
      name: "resolved query",
      transition: state => {
        state.spotlightPkgQuery = "Example";
        state.spotlightPkgHits = [{ id: "Example", version: "1.0.0" }];
      },
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
    assert.equal(state.spotlightPkgLoading, false, scenario.name);
  }
});

test("ineligible scopes stop loading without clearing resolved results", () => {
  const hits = [{ id: "Existing", version: "1.0.0" }];
  const state = searchState({
    spotlightQuery: "Example",
    spotlightScope: "types",
    spotlightPkgHits: hits,
    spotlightPkgQuery: "Existing",
    spotlightPkgLoading: true,
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(state.spotlightPkgHits, hits);
  assert.equal(state.spotlightPkgQuery, "Existing");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(harness.scheduled.length, 0);
});

test("short queries clear package discovery state", () => {
  const state = searchState({
    spotlightQuery: "x",
    spotlightPkgHits: [{ id: "Existing", version: "1.0.0" }],
    spotlightPkgQuery: "Existing",
    spotlightPkgLoading: true,
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
});

test("already-resolved queries retain their results without another request", () => {
  const hits = [{ id: "Example", version: "1.0.0" }];
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPkgHits: hits,
    spotlightPkgQuery: "Example",
    spotlightPkgLoading: true,
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();

  assert.equal(state.spotlightPkgHits, hits);
  assert.equal(state.spotlightPkgLoading, false);
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

  assert.deepEqual(state.spotlightPkgHits, hits);
  assert.notEqual(state.spotlightPkgHits, hits);
  assert.equal(state.spotlightPkgQuery, "Example");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(harness.updates(), 1);
});

test("current package failures are visible and the same query can be retried", async () => {
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPkgHits: [{ id: "Old", version: "1.0.0" }],
  });
  const harness = searchDependencies(state, {
    queryPackages: async () => {
      throw new Error("NuGet unavailable");
    },
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  await harness.scheduled[0]?.callback();

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
  assert.match(state.spotlightPkgError ?? "", /NuGet unavailable/);
  assert.match(state.spotlightPkgError ?? "", /try again/);
  assert.equal(harness.updates(), 1);
  search.schedule();
  assert.equal(harness.scheduled.length, 2);
  assert.equal(state.spotlightPkgError, "");
  assert.equal(state.spotlightPkgLoading, true);
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
    assert.match(state.spotlightPkgError ?? "", /NuGet unavailable/);
    if (transition === "edit-and-undo") {
      state.spotlightQuery = "Example.more";
      search.schedule();
      state.spotlightQuery = "Example";
    } else {
      state.spotlightScope = "types";
      search.schedule();
      state.spotlightScope = "packages";
    }
    assert.equal(state.spotlightPkgError, "");
    search.schedule();

    assert.equal(state.spotlightPkgLoading, true);
    assert.equal(harness.scheduled.length, transition === "edit-and-undo" ? 3 : 2);
    assert.deepEqual(harness.cancelled, transition === "edit-and-undo" ? [2] : []);
    await harness.scheduled.at(-1)?.callback();
    assert.deepEqual(queries, ["Example", "Example"]);
    assert.deepEqual(state.spotlightPkgHits, hits);
    assert.equal(state.spotlightPkgError, "");
    assert.equal(state.spotlightPkgLoading, false);
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
  state.spotlightQuery = "Example.more";
  search.schedule();
  state.spotlightQuery = "Example";
  search.schedule();

  assert.deepEqual(queries, ["Example"]);
  assert.equal(harness.scheduled.length, 2);
  assert.deepEqual(harness.cancelled, [2]);
  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "Example");
  assert.equal(state.spotlightPkgError, "");
  assert.equal(state.spotlightPkgLoading, false);
});

test("input changes independently suppress stale package results", async () => {
  const query = deferred<readonly SpotlightPackageHit[]>();
  const state = searchState({ spotlightQuery: "first" });
  const harness = searchDependencies(state, {
    queryPackages: async () => query.promise,
  });
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  const request = harness.scheduled[0]?.callback();
  state.spotlightQuery = "second";
  query.resolve([{ id: "Stale", version: "1.0.0" }]);
  await request;

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, true);
  assert.equal(harness.updates(), 0);
});

test("newer generations suppress stale success while publishing the replacement", async () => {
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

  assert.deepEqual(state.spotlightPkgHits, [{
    id: "Current",
    version: "2.0.0",
  }]);
  assert.equal(state.spotlightPkgQuery, "second");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(harness.updates(), 1);
});

test("same-input generations independently suppress stale failures", async () => {
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

  assert.deepEqual(state.spotlightPkgHits, [{
    id: "Current",
    version: "2.0.0",
  }]);
  assert.equal(state.spotlightPkgQuery, "Example");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(state.spotlightPkgError, "");
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
  const request = harness.scheduled[0]?.callback();
  state.spotlightQuery = "second";
  query.reject(new Error("stale failure"));
  await request;

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, true);
  assert.equal(harness.updates(), 0);
});

test("leaving package scopes invalidates an in-flight request with unchanged input", async () => {
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

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
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

  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
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
  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(harness.updates(), 0);
});

test("reset cancels a pending debounce and clears discovery state", () => {
  const state = searchState({
    spotlightQuery: "Example",
    spotlightPkgHits: [{ id: "Old", version: "1.0.0" }],
    spotlightPkgQuery: "Old",
    spotlightPkgError: "Previous failure",
  });
  const harness = searchDependencies(state);
  const search = createSpotlightPackageSearch(harness.dependencies);

  search.schedule();
  search.reset();

  assert.deepEqual(harness.cancelled, [1]);
  assert.deepEqual(state.spotlightPkgHits, []);
  assert.equal(state.spotlightPkgQuery, "");
  assert.equal(state.spotlightPkgLoading, false);
  assert.equal(state.spotlightPkgError, "");
});
