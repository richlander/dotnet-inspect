import assert from "node:assert/strict";
import test from "node:test";

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  createPackageInspectionCoordinator,
  packageMetadataView,
  workspaceDependencyKey,
  type PackageInspectionDependencies,
  type PackageInspectionState,
  type PackagePerformance,
} from "../src/package-inspection.ts";
import type { AsyncResource } from "../src/data.ts";
import type { AppPackage } from "../src/package-acquisition.ts";
import type {
  BrowserPackageDependencies,
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "../src/inspect-web-engine.d.ts";
import type { PackageMetadata } from "../src/metadata-viewer.ts";

function packageModel(
  overrides: Partial<AppPackage> = {},
): AppPackage {
  return {
    id: "Example.Package",
    version: "1.2.3",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    assembly: "Example.Package",
    assemblyId: "example-package",
    assemblyAsset: "lib/net10.0/Example.Package.dll",
    source: { kind: "nuget.org" },
    assemblies: [],
    types: [],
    accessibility: [],
    totalTypes: 0,
    totalMembers: 0,
    documents: [],
    isRuntimePack: false,
    ...overrides,
  };
}

function inspectionState(
  overrides: Partial<PackageInspectionState> = {},
): PackageInspectionState {
  return {
    packages: [],
    atPackageRoot: false,
    packageLens: "overview",
    packageDependencies: { status: "idle" },
    workspaceDependencies: {},
    workspaceDependencyErrors: {},
    workspaceDependencyLoads: new Set<string>(),
    packageIntegrations: { status: "idle" },
    packageOpportunities: { status: "idle" },
    packagePerformance: { status: "idle" },
    packageMetadata: { status: "idle" },
    ...overrides,
  };
}

function dependencyResult(
  packageId = "Example.Package",
  error: string | null = null,
): BrowserPackageDependencies {
  return {
    package: packageId,
    version: "1.2.3",
    activeFramework: "net10.0",
    assembly: `${packageId}.dll`,
    dependencyGroups: [{
      index: 0,
      framework: "net10.0",
      isActive: true,
      dependencies: [{ id: "Example.Dependency", versionRange: "[1.0.0,)" }],
    }],
    assemblyReferences: [],
    dependencyGroupError: error,
    assemblyReferenceError: null,
  };
}

function integrationsResult(): BrowserPackageIntegrations {
  return {
    package: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    categories: [],
    totalSignals: 0,
    isComplete: true,
    inspectionError: null,
  };
}

function opportunitiesResult(): BrowserPackageOpportunities {
  return {
    package: "Example.Package",
    version: "1.2.3",
    activeFramework: "net10.0",
    categories: [],
    totalOpportunities: 0,
    isComplete: true,
    inspectionError: null,
  };
}

function performanceResult(): PackagePerformance {
  return {
    members: [],
    nonPublicOpportunities: 0,
    totalOpportunities: 0,
  };
}

function metadataResult(): PackageMetadata {
  return { assemblies: [] };
}

function inspectionDependencies(
  state: PackageInspectionState,
  overrides: Partial<Omit<PackageInspectionDependencies, "state">> = {},
): PackageInspectionDependencies {
  return {
    state,
    queryDependencies: async packageItem => dependencyResult(packageItem.id),
    queryPackageIntegrations: async () => integrationsResult(),
    queryPlatformIntegrations: async () => integrationsResult(),
    queryPackageOpportunities: async () => opportunitiesResult(),
    queryPlatformOpportunities: async () => opportunitiesResult(),
    queryPackagePerformance: async () => performanceResult(),
    queryPlatformPerformance: async () => performanceResult(),
    queryPackageMetadata: async () => metadataResult(),
    queryPlatformMetadata: async () => metadataResult(),
    platformPackForAssembly: assemblyName => `pack:${assemblyName}`,
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    refreshPackageStats: () => {},
    render: () => {},
    renderDependencyGraph: async () => {},
    ...overrides,
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

type PackageInspectionCoordinator =
  ReturnType<typeof createPackageInspectionCoordinator>;

interface PackageLensFixture<T> {
  name: string;
  result: T;
  createCoordinator:
    (
      state: PackageInspectionState,
      query: () => Promise<T>,
      render?: () => void,
    ) =>
      PackageInspectionCoordinator;
  load: (coordinator: PackageInspectionCoordinator, signature: string) =>
    Promise<void>;
  read: (state: PackageInspectionState) => AsyncResource<T>;
  write: (
    state: PackageInspectionState,
    resource: AsyncResource<T>,
  ) => void;
}

async function verifyPackageLensLifecycle<T>(
  fixture: PackageLensFixture<T>,
) {
  {
    const query = deferred<T>();
    const events: string[] = [];
    let queries = 0;
    const state = inspectionState();
    fixture.write(state, {
      status: "failed",
      key: "old",
      error: "old failure",
    });
    const coordinator = fixture.createCoordinator(
      state,
      async () => {
        queries++;
        return query.promise;
      },
      () => events.push("render"));

    const load = fixture.load(coordinator, "current");
    const duplicate = fixture.load(coordinator, "current");

    assert.deepEqual(fixture.read(state), {
      status: "loading",
      key: "current",
    }, `${fixture.name} started loading`);
    assert.equal(queries, 1, `${fixture.name} reused in-flight request`);
    assert.deepEqual(
      events,
      ["render", "render"],
      `${fixture.name} start and reuse renders`);

    // Not querying twice is only half of reuse. These loaders are `async`, so the
    // duplicate caller is awaiting a promise, and the first version of this guard
    // returned early -- which resolved that promise immediately and told the caller the
    // load had finished while the request was still in flight. Counting queries could
    // not see it, because the early return also queries zero times.
    //
    // So assert the duplicate has not settled while the original is still pending. A
    // reused request has to hand back the original's promise, not a resolved one.
    let duplicateSettled = false;
    void duplicate.then(() => { duplicateSettled = true; });
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(
      duplicateSettled,
      false,
      `${fixture.name} duplicate settled before the in-flight request completed`);

    query.resolve(fixture.result);
    await Promise.all([load, duplicate]);

    assert.deepEqual(fixture.read(state), {
      status: "ready",
      key: "current",
      data: fixture.result,
    }, `${fixture.name} current result`);
    assert.deepEqual(
      events,
      ["render", "render", "render"],
      `${fixture.name} completion render`);
  }

  {
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(state, async () => {
      throw new Error("current failure");
    });

    const load = fixture.load(coordinator, "current");
    const duplicate = fixture.load(coordinator, "current");
    await Promise.all([load, duplicate]);

    assert.deepEqual(fixture.read(state), {
      status: "failed",
      key: "current",
      error: "current failure",
    }, `${fixture.name} current failure`);
  }

  {
    // A render that throws must not be able to strand the in-flight entry. The window that
    // matters is the one where `state[lens]` is still the exact pending object the join
    // matches on, so a stranded entry is not merely a leak -- the next same-key caller is
    // handed a promise nobody will resolve, and the lens deadlocks for the rest of the
    // session.
    //
    // Rather than restate where the throw-safe boundary is, drive the loader with a render
    // that throws on the nth call for every n a clean run makes, and require that a
    // subsequent same-key call still settles. That derives the property from the loader's
    // own behavior, so a loader that grows another render site is covered without this
    // test being updated.
    let cleanRenders = 0;
    const counted = fixture.createCoordinator(
      inspectionState(),
      async () => fixture.result,
      () => { cleanRenders++; });
    await fixture.load(counted, "current");
    assert.ok(
      cleanRenders > 0,
      `${fixture.name} made no renders, so the throwing-render sweep would prove nothing`);

    for (let throwOn = 1; throwOn <= cleanRenders; throwOn++) {
      const state = inspectionState();
      let renders = 0;
      const coordinator = fixture.createCoordinator(
        state,
        async () => fixture.result,
        () => {
          renders++;
          if (renders === throwOn) throw new Error(`render ${throwOn} failed`);
        });

      await fixture.load(coordinator, "current").catch(() => {});

      const settled = await Promise.race([
        fixture.load(coordinator, "current").then(() => "settled", () => "settled"),
        new Promise(resolve => { setTimeout(() => resolve("deadlocked"), 250); }),
      ]);
      assert.equal(
        settled,
        "settled",
        `${fixture.name} deadlocked after render ${throwOn} of ${cleanRenders} threw`);
    }
  }

  {
    let queries = 0;
    const state = inspectionState();
    const cached: AsyncResource<T> = {
      status: "failed",
      key: "cached",
      error: "cached failure",
    };
    fixture.write(state, cached);
    const coordinator = fixture.createCoordinator(state, async () => {
      queries++;
      return fixture.result;
    });

    await fixture.load(coordinator, "cached");

    assert.equal(queries, 0, `${fixture.name} cached query`);
    assert.strictEqual(
      fixture.read(state),
      cached,
      `${fixture.name} cached failure`);
  }

  {
    const query = deferred<T>();
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(
      state,
      async () => query.promise);

    const load = fixture.load(coordinator, "first");
    const newer: AsyncResource<T> = {
      status: "failed",
      key: "second",
      error: "newer failure",
    };
    fixture.write(state, newer);
    query.reject(new Error("stale failure"));
    await load;

    assert.strictEqual(
      fixture.read(state),
      newer,
      `${fixture.name} stale failure`);
  }

  {
    // Every staleness case above changes the key between the two requests, so key
    // equality and object identity give the same answer and none of them can tell the
    // two mechanisms apart. Adversarial review exploited exactly that: replacing the
    // ownership checks with `status !== "idle" && key === signature` left the whole
    // suite green, even though a late completion could then publish into a newer
    // request that happens to share its key.
    //
    // This is the interleaving that separates them -- one scope requested, abandoned,
    // and requested again, which is a user navigating away and back. Both requests for
    // that scope carry the same key, so only object identity can reject the first.
    const first = deferred<T>();
    const second = deferred<T>();
    const pending = [first, second];
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(
      state,
      async () => (pending.shift() ?? first).promise);

    const abandoned = fixture.load(coordinator, "scope-a");

    // Writing a different scope's state releases scope-a's ownership, so the reload
    // below starts a genuinely new request rather than joining the in-flight one.
    fixture.write(state, {
      status: "failed",
      key: "scope-b",
      error: "other scope",
    });

    const reloaded = fixture.load(coordinator, "scope-a");
    second.resolve(fixture.result);
    await reloaded;

    const live = fixture.read(state);
    assert.deepEqual(live, {
      status: "ready",
      key: "scope-a",
      data: fixture.result,
    }, `${fixture.name} reloaded scope`);

    // The abandoned request lands last, carrying the same key as the live one. Identity
    // ownership rejects it; key equality would accept it and publish the stale result.
    first.resolve(fixture.result);
    await abandoned;
    assert.strictEqual(
      fixture.read(state),
      live,
      `${fixture.name} same-key stale completion replaced the live request`);
  }

  {
    // Joining takes both halves. Identity alone cannot reject a caller asking for a
    // *different* scope while the first request is still in flight: that request is
    // still the live one, so identity says join -- and the caller would then be handed
    // a request that will never describe the scope it asked about. Only the key rejects
    // it.
    const first = deferred<T>();
    const second = deferred<T>();
    const pending = [first, second];
    let queries = 0;
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(state, async () => {
      queries++;
      return (pending.shift() ?? first).promise;
    });

    const stale = fixture.load(coordinator, "first");
    const newer = fixture.load(coordinator, "second");
    assert.equal(
      queries,
      2,
      `${fixture.name} second scope joined the first scope's request`);

    second.resolve(fixture.result);
    await newer;

    const live = fixture.read(state);
    assert.deepEqual(live, {
      status: "ready",
      key: "second",
      data: fixture.result,
    }, `${fixture.name} second scope result`);

    first.resolve(fixture.result);
    await stale;
    assert.strictEqual(
      fixture.read(state),
      live,
      `${fixture.name} first scope overwrote the second`);
  }
}

test("workspace dependency keys normalize complete package coordinates", () => {
  assert.equal(
    workspaceDependencyKey({
      id: "Example.Package",
      version: "1.2.3-PREVIEW",
      activeFramework: "NET10.0",
    }),
    "example.package@1.2.3-preview@net10.0");
});

test("dependency results cache for a resident package after the foreground lens moves", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const events: string[] = [];
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
      refreshPackageStats: () => events.push("stats"),
      render: () => events.push("render"),
      renderDependencyGraph: async () => {
        events.push("graph");
      },
    }));

  const load = coordinator.loadDependencies(packageItem, "first");
  assert.deepEqual(state.packageDependencies, {
    status: "loading",
    key: "first",
  });
  state.packageDependencies = {
    status: "loading",
    key: "second",
  };
  request.resolve(dependencyResult(packageItem.id, "partial dependency data"));
  await load;

  assert.deepEqual(state.packageDependencies, {
    status: "loading",
    key: "second",
  });
  const key = workspaceDependencyKey(packageItem);
  const workspace = state.workspaceDependencies[key];
  assert.ok(workspace);
  assert.equal(
    workspace.dependencyGroups?.[0]?.framework,
    "net10.0");
  assert.equal(
    state.workspaceDependencyErrors[key],
    "partial dependency data");
  assert.deepEqual(events, ["render", "stats", "render", "graph"]);
});

test("foreground dependency success refreshes cached groups and clears a prior error", async () => {
  const packageItem = packageModel();
  const key = workspaceDependencyKey(packageItem);
  const state = inspectionState({
    packages: [packageItem],
    workspaceDependencyErrors: { [key]: "stale failure" },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state));

  await coordinator.loadDependencies(packageItem, "current");

  const workspace = state.workspaceDependencies[key];
  assert.ok(workspace);
  assert.equal(
    workspace.dependencyGroups?.[0]?.dependencies?.[0]?.id,
    "Example.Dependency");
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, key), false);
});

test("package lens loaders reuse cached results without querying or clearing them", async () => {
  const packageItem = packageModel();
  const dependencies = dependencyResult();
  const integrations = integrationsResult();
  const opportunities = opportunitiesResult();
  const performance = performanceResult();
  const metadata = metadataResult();
  const cases = [
    {
      name: "dependencies",
      cached: dependencies,
      state: inspectionState({
        packageDependencies: {
          status: "ready",
          key: "cached",
          data: dependencies,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageDependencies.status === "ready"
          ? state.packageDependencies.data
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadDependencies(packageItem, "cached"),
    },
    {
      name: "integrations",
      cached: integrations,
      state: inspectionState({
        packageIntegrations: {
          status: "ready",
          key: "cached",
          data: integrations,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageIntegrations.status === "ready"
          ? state.packageIntegrations.data
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadIntegrations(packageItem, "cached", null),
    },
    {
      name: "opportunities",
      cached: opportunities,
      state: inspectionState({
        packageOpportunities: {
          status: "ready",
          key: "cached",
          data: opportunities,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageOpportunities.status === "ready"
          ? state.packageOpportunities.data
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadOpportunities(packageItem, "cached", null),
    },
    {
      name: "performance",
      cached: performance,
      state: inspectionState({
        packagePerformance: {
          status: "ready",
          key: "cached",
          data: performance,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packagePerformance.status === "ready"
          ? state.packagePerformance.data
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadPerformance(packageItem, "cached", null),
    },
    {
      name: "metadata",
      cached: metadata,
      state: inspectionState({
        packageMetadata: {
          status: "ready",
          key: "cached",
          data: metadata,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageMetadata.status === "ready"
          ? state.packageMetadata.data
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadMetadata(packageItem, "cached", null),
    },
  ];

  for (const fixture of cases) {
    let queries = 0;
    let renders = 0;
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(fixture.state, {
        queryDependencies: async () => {
          queries++;
          return dependencyResult();
        },
        queryPackageIntegrations: async () => {
          queries++;
          return integrationsResult();
        },
        queryPackageOpportunities: async () => {
          queries++;
          return opportunitiesResult();
        },
        queryPackagePerformance: async () => {
          queries++;
          return performanceResult();
        },
        queryPackageMetadata: async () => {
          queries++;
          return metadataResult();
        },
        render: () => renders++,
      }));

    await fixture.load(coordinator);

    assert.equal(queries, 0, fixture.name);
    assert.equal(renders, 1, fixture.name);
    assert.strictEqual(fixture.read(fixture.state), fixture.cached, fixture.name);
  }
});

test("package lens loaders reuse cached failures without querying", async () => {
  const packageItem = packageModel();
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    packagePerformance: {
      status: "failed",
      key: "cached",
      error: "cached failure",
    },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackagePerformance: async () => {
        queries++;
        return performanceResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadPerformance(packageItem, "cached", null);

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.deepEqual(state.packagePerformance, {
    status: "failed",
    key: "cached",
    error: "cached failure",
  });
});

test("every package lens preserves its complete request lifecycle", async () => {
  const packageItem = packageModel();

  await verifyPackageLensLifecycle({
    name: "dependencies",
    result: dependencyResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryDependencies: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadDependencies(packageItem, signature),
    read: state => state.packageDependencies,
    write: (state, resource) => { state.packageDependencies = resource; },
  });
  await verifyPackageLensLifecycle({
    name: "integrations",
    result: integrationsResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageIntegrations: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadIntegrations(packageItem, signature, null),
    read: state => state.packageIntegrations,
    write: (state, resource) => { state.packageIntegrations = resource; },
  });
  await verifyPackageLensLifecycle({
    name: "opportunities",
    result: opportunitiesResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageOpportunities: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadOpportunities(packageItem, signature, null),
    read: state => state.packageOpportunities,
    write: (state, resource) => { state.packageOpportunities = resource; },
  });
  await verifyPackageLensLifecycle({
    name: "performance",
    result: performanceResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackagePerformance: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadPerformance(packageItem, signature, null),
    read: state => state.packagePerformance,
    write: (state, resource) => { state.packagePerformance = resource; },
  });
  await verifyPackageLensLifecycle({
    name: "metadata",
    result: metadataResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageMetadata: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadMetadata(packageItem, signature, null),
    read: state => state.packageMetadata,
    write: (state, resource) => { state.packageMetadata = resource; },
  });
});

test("opportunity requests occupy one explicit lifecycle state", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageOpportunities>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async () => {
        queries++;
        return request.promise;
      },
    }));

  const load = coordinator.loadOpportunities(packageItem, "current", null);
  assert.deepEqual(state.packageOpportunities, {
    status: "loading",
    key: "current",
  });

  // The duplicate must *join* the live request, not report a completion that has not
  // happened. Awaiting it here used to pass only because the coordinator returned early
  // while the request was still running.
  let duplicateSettled = false;
  const duplicate = coordinator
    .loadOpportunities(packageItem, "current", null)
    .then(() => { duplicateSettled = true; });
  assert.equal(queries, 1);
  await Promise.resolve();
  assert.equal(
    duplicateSettled,
    false,
    "a duplicate caller completed before the request it deduplicated");

  const result = opportunitiesResult();
  request.resolve(result);
  await Promise.all([load, duplicate]);
  assert.equal(duplicateSettled, true);
  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "current",
    data: result,
  });

  await coordinator.loadOpportunities(packageItem, "next", null);
  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "next",
    data: result,
  });
});

test("opportunity ownership survives initial and completion render failures", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageOpportunities>();
  const state = inspectionState();
  let renders = 0;
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async () => request.promise,
      render: () => {
        renders++;
        if (renders === 1) throw new Error("initial render failed");
        if (renders === 3) throw new Error("completion render failed");
      },
    }));

  await assert.rejects(
    coordinator.loadOpportunities(packageItem, "current", null),
    /initial render failed/);

  let duplicateSettled = false;
  const duplicate = coordinator
    .loadOpportunities(packageItem, "current", null)
    .then(
      () => { duplicateSettled = true; },
      (error: unknown) => {
        duplicateSettled = true;
        throw error;
      });
  await Promise.resolve();
  assert.equal(
    duplicateSettled,
    false,
    "a duplicate caller completed after the owner render failed but before the request");

  request.resolve(opportunitiesResult());
  await assert.rejects(duplicate, /completion render failed/);
  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "current",
    data: opportunitiesResult(),
  });
});

test("opportunity failures and stale results preserve request ownership", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageOpportunities>();
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async ({ id }) => {
        if (id === packageItem.id) return request.promise;
        throw new Error("newer failure");
      },
    }));

  const staleLoad =
    coordinator.loadOpportunities(packageItem, "first", null);
  const newerPackage = packageModel({ id: "Example.Newer" });
  await coordinator.loadOpportunities(newerPackage, "second", null);
  assert.deepEqual(state.packageOpportunities, {
    status: "failed",
    key: "second",
    error: "newer failure",
  });

  request.resolve(opportunitiesResult());
  await staleLoad;
  assert.deepEqual(state.packageOpportunities, {
    status: "failed",
    key: "second",
    error: "newer failure",
  });
});

// The staleness test above changes the key between the two requests, so key equality and
// object identity give the same answer and it cannot tell them apart. Adversarial review
// used exactly that gap: replacing both ownership checks with `status !== "idle" && key
// === signature` left all 23 tests green, even though a late completion could then publish
// into a newer request that happens to share its key.
//
// This is the interleaving that separates them. Scope A is requested, abandoned for B, and
// then requested again -- one user navigating away and back. Both A requests carry the same
// key, so only object identity can tell the first from the second.
test("a re-requested scope keeps the newest result when the abandoned one lands late", async () => {
  const packageItem = packageModel();
  const first = deferred<BrowserPackageOpportunities>();
  const second = deferred<BrowserPackageOpportunities>();
  const other = deferred<BrowserPackageOpportunities>();
  const pending = [first, second];
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async ({ id }) =>
        id === packageItem.id
          ? (pending.shift() ?? first).promise
          : other.promise,
    }));

  const abandoned = coordinator.loadOpportunities(packageItem, "scope-a", null);

  // Navigating to B releases A's ownership, so the re-request below is a genuinely new
  // request object rather than a reuse of the in-flight one.
  const otherPackage = packageModel({ id: "Example.Other" });
  const otherLoad = coordinator.loadOpportunities(otherPackage, "scope-b", null);
  other.resolve(opportunitiesResult());
  await otherLoad;

  const reloaded = coordinator.loadOpportunities(packageItem, "scope-a", null);
  second.resolve({ ...opportunitiesResult(), totalOpportunities: 99 });
  await reloaded;
  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "scope-a",
    data: { ...opportunitiesResult(), totalOpportunities: 99 },
  });

  // The abandoned request now lands, carrying the same key as the live one. Identity
  // ownership rejects it; key equality would accept it and show the stale count.
  first.resolve({ ...opportunitiesResult(), totalOpportunities: 1 });
  await abandoned;
  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "scope-a",
    data: { ...opportunitiesResult(), totalOpportunities: 99 },
  });
});

test("a re-requested scope keeps the newest result when the abandoned one fails late",
  async () => {
    // The reject path is a separate guard from the resolve path, and round 1 fixed only the
    // resolve one. The existing failure test changes the key between requests, so key
    // equality and identity give the same answer there and it cannot tell them apart. This
    // is the A->B->A shape, where the abandoned request carries the *same* key as the live
    // one: without identity ownership, a good current opportunity table is replaced by an
    // error banner from a scan the user already navigated away from -- and, because a
    // `failed` resource short-circuits the loader's dedupe, it never retries.
    const packageItem = packageModel();
    const first = deferred<BrowserPackageOpportunities>();
    const second = deferred<BrowserPackageOpportunities>();
    const other = deferred<BrowserPackageOpportunities>();
    const pending = [first, second];
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async ({ id }) =>
          id === packageItem.id
            ? (pending.shift() ?? first).promise
            : other.promise,
      }));

    const abandoned = coordinator.loadOpportunities(packageItem, "scope-a", null);

    const otherPackage = packageModel({ id: "Example.Other" });
    const otherLoad = coordinator.loadOpportunities(otherPackage, "scope-b", null);
    other.resolve(opportunitiesResult());
    await otherLoad;

    const reloaded = coordinator.loadOpportunities(packageItem, "scope-a", null);
    second.resolve({ ...opportunitiesResult(), totalOpportunities: 99 });
    await reloaded;

    first.reject(new Error("abandoned scan failed"));
    await abandoned;
    assert.deepEqual(state.packageOpportunities, {
      status: "ready",
      key: "scope-a",
      data: { ...opportunitiesResult(), totalOpportunities: 99 },
    });
  });

test("workspace dependency loading records failures and ignores runtime packs", async () => {
  const good = packageModel({ id: "Example.Good" });
  const partial = packageModel({ id: "Example.Partial" });
  const bad = packageModel({ id: "Example.Bad" });
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const events: string[] = [];
  const goodKey = workspaceDependencyKey(good);
  const state = inspectionState({
    packages: [good, partial, bad, runtime],
    atPackageRoot: true,
    packageLens: "dependencies",
    workspaceDependencyErrors: { [goodKey]: "stale failure" },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        events.push(`query:${packageItem.id}`);
        if (packageItem === bad) throw new Error("dependency feed unavailable");
        if (packageItem === partial) {
          return dependencyResult(
            packageItem.id,
            "no dependency group matches net10.0");
        }
        return dependencyResult(packageItem.id);
      },
      refreshPackageStats: () => events.push("stats"),
      render: () => events.push("render"),
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.deepEqual(events, [
    "query:Example.Good",
    "query:Example.Partial",
    "query:Example.Bad",
    "render",
    "stats",
  ]);
  const goodWorkspace = state.workspaceDependencies[goodKey];
  assert.ok(goodWorkspace);
  assert.equal(
    goodWorkspace.dependencyGroups?.[0]?.dependencies?.[0]?.id,
    "Example.Dependency");
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, goodKey), false);
  const partialKey = workspaceDependencyKey(partial);
  const partialWorkspace = state.workspaceDependencies[partialKey];
  assert.ok(partialWorkspace);
  assert.equal(
    partialWorkspace.dependencyGroupError,
    "no dependency group matches net10.0");
  assert.equal(
    state.workspaceDependencyErrors[partialKey],
    "no dependency group matches net10.0");
  const failedWorkspace = state.workspaceDependencies[workspaceDependencyKey(bad)];
  assert.ok(failedWorkspace);
  assert.deepEqual(failedWorkspace.dependencyGroups, []);
  assert.equal(
    state.workspaceDependencyErrors[workspaceDependencyKey(bad)],
    "dependency feed unavailable");
  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(runtime)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("workspace dependency loading skips keys already in flight", async () => {
  const packageItem = packageModel();
  const key = workspaceDependencyKey(packageItem);
  let queries = 0;
  let graphRenders = 0;
  const state = inspectionState({
    packages: [packageItem],
    workspaceDependencyLoads: new Set([key]),
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => {
        queries++;
        return dependencyResult();
      },
      renderDependencyGraph: async () => {
        graphRenders++;
      },
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.equal(queries, 0);
  assert.equal(graphRenders, 1);
  assert.deepEqual([...state.workspaceDependencyLoads], [key]);
});

test("workspace dependency loading is sequential and only renders the active lens", async () => {
  const first = packageModel({ id: "Example.First" });
  const second = packageModel({ id: "Example.Second" });
  const firstRequest = deferred<BrowserPackageDependencies>();
  const queries: string[] = [];
  let renders = 0;
  let stats = 0;
  const state = inspectionState({
    packages: [first, second],
    atPackageRoot: false,
    packageLens: "dependencies",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        queries.push(packageItem.id);
        return packageItem.id === first.id
          ? firstRequest.promise
          : dependencyResult(packageItem.id);
      },
      render: () => renders++,
      refreshPackageStats: () => stats++,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  assert.deepEqual(queries, [first.id]);
  firstRequest.resolve(dependencyResult(first.id));
  await load;

  assert.deepEqual(queries, [first.id, second.id]);
  assert.equal(renders, 0);
  assert.equal(stats, 1);
});

test("workspace dependency loading does not render another active package lens", async () => {
  const packageItem = packageModel();
  let renders = 0;
  const state = inspectionState({
    packages: [packageItem],
    atPackageRoot: true,
    packageLens: "metadata",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.equal(renders, 0);
});

test("removed packages cannot publish an in-flight workspace dependency result", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("coordinate replacement cannot publish an in-flight workspace dependency result", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [packageModel({ version: "2.0.0" })];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("packages removed before their workspace turn are not queried", async () => {
  const first = packageModel({ id: "Example.First" });
  const removed = packageModel({ id: "Example.Removed" });
  const firstRequest = deferred<BrowserPackageDependencies>();
  const queries: string[] = [];
  const state = inspectionState({ packages: [first, removed] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        queries.push(packageItem.id);
        return packageItem === first
          ? firstRequest.promise
          : dependencyResult(packageItem.id);
      },
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [first];
  firstRequest.resolve(dependencyResult(first.id));
  await load;

  assert.deepEqual(queries, [first.id]);
  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(removed)),
    false);
});

test("removed packages cannot publish rejected workspace dependency requests", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [];
  request.reject(new Error("stale failure"));
  await load;

  const key = workspaceDependencyKey(packageItem);
  assert.equal(Object.hasOwn(state.workspaceDependencies, key), false);
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, key), false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("removed packages cannot publish dependencies loaded by the foreground lens", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.loadDependencies(packageItem, "dependencies");
  state.packages = [];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
});

test("scoped package lenses route platform coordinates and suppress stale results", async () => {
  const packageItem = packageModel();
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const metadata = deferred<PackageMetadata>();
  const calls: string[] = [];
  const state = inspectionState({ packages: [packageItem, runtime] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageIntegrations: async model => {
        calls.push(`integrations:${model.id}`);
        return integrationsResult();
      },
      queryPlatformOpportunities: async (framework, assemblyName, pack) => {
        calls.push(`opportunities:${framework}/${assemblyName}/${pack}`);
        return opportunitiesResult();
      },
      queryPackagePerformance: async model => {
        calls.push(`performance:${model.id}`);
        throw new Error("analysis unavailable");
      },
      queryPackageMetadata: async model => {
        calls.push(`metadata:${model.id}`);
        return metadata.promise;
      },
    }));

  await coordinator.loadIntegrations(packageItem, "integrations", null);
  await coordinator.loadOpportunities(
    runtime,
    "opportunities",
    "System.Text.Json");
  await coordinator.loadPerformance(packageItem, "performance", null);
  const metadataLoad =
    coordinator.loadMetadata(packageItem, "metadata-first", null);
  state.packageMetadata = {
    status: "loading",
    key: "metadata-second",
  };
  metadata.resolve(metadataResult());
  await metadataLoad;

  assert.deepEqual(calls, [
    "integrations:Example.Package",
    "opportunities:net10.0/System.Text.Json.dll/pack:System.Text.Json",
    "performance:Example.Package",
    "metadata:Example.Package",
  ]);
  assert.equal(state.packageIntegrations.status, "ready");
  assert.equal(state.packageOpportunities.status, "ready");
  assert.deepEqual(state.packagePerformance, {
    status: "failed",
    key: "performance",
    error: "analysis unavailable",
  });
  assert.deepEqual(state.packageMetadata, {
    status: "loading",
    key: "metadata-second",
  });
});

test("all scoped runtime package lenses route exact platform coordinates", async () => {
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const calls: string[] = [];
  const state = inspectionState({ packages: [runtime] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatformIntegrations: async (framework, assemblyName, pack) => {
        calls.push(`integrations:${framework}/${assemblyName}/${pack}`);
        return integrationsResult();
      },
      queryPlatformOpportunities: async (framework, assemblyName, pack) => {
        calls.push(`opportunities:${framework}/${assemblyName}/${pack}`);
        return opportunitiesResult();
      },
      queryPlatformPerformance: async (framework, assemblyName, pack) => {
        calls.push(`performance:${framework}/${assemblyName}/${pack}`);
        return performanceResult();
      },
      queryPlatformMetadata: async (framework, assemblyName, pack) => {
        calls.push(`metadata:${framework}/${assemblyName}/${pack}`);
        return metadataResult();
      },
    }));

  await coordinator.loadIntegrations(runtime, "integrations", "System.Text.Json");
  await coordinator.loadOpportunities(runtime, "opportunities", "System.Text.Json");
  await coordinator.loadPerformance(runtime, "performance", "System.Text.Json");
  await coordinator.loadMetadata(runtime, "metadata", "System.Text.Json");

  assert.deepEqual(calls, [
    "integrations:net10.0/System.Text.Json.dll/pack:System.Text.Json",
    "opportunities:net10.0/System.Text.Json.dll/pack:System.Text.Json",
    "performance:net10.0/System.Text.Json.dll/pack:System.Text.Json",
    "metadata:net10.0/System.Text.Json.dll/pack:System.Text.Json",
  ]);
});

test("package lens results clear before a different request completes", async () => {
  const packageItem = packageModel();

  {
    const query = deferred<BrowserPackageDependencies>();
    const state = inspectionState({
      packageDependencies: {
        status: "ready",
        key: "old",
        data: dependencyResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryDependencies: async () => query.promise,
      }));
    const load = coordinator.loadDependencies(packageItem, "new");
      assert.deepEqual(state.packageDependencies, {
        status: "loading",
        key: "new",
      });
    query.resolve(dependencyResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageIntegrations>();
    const state = inspectionState({
      packageIntegrations: {
        status: "ready",
        key: "old",
        data: integrationsResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageIntegrations: async () => query.promise,
      }));
    const load = coordinator.loadIntegrations(packageItem, "new", null);
      assert.deepEqual(state.packageIntegrations, {
        status: "loading",
        key: "new",
      });
    query.resolve(integrationsResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState({
      packageOpportunities: {
        status: "ready",
        key: "old",
        data: opportunitiesResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "new", null);
      assert.deepEqual(state.packageOpportunities, {
        status: "loading",
        key: "new",
      });
    query.resolve(opportunitiesResult());
    await load;
  }

  {
    const query = deferred<PackagePerformance>();
    const state = inspectionState({
      packagePerformance: {
        status: "ready",
        key: "old",
        data: performanceResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackagePerformance: async () => query.promise,
      }));
    const load = coordinator.loadPerformance(packageItem, "new", null);
      assert.deepEqual(state.packagePerformance, {
        status: "loading",
        key: "new",
      });
    query.resolve(performanceResult());
    await load;
  }

  {
    const query = deferred<PackageMetadata>();
    const state = inspectionState({
      packageMetadata: {
        status: "ready",
        key: "old",
        data: metadataResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageMetadata: async () => query.promise,
      }));
    const load = coordinator.loadMetadata(packageItem, "new", null);
      assert.deepEqual(state.packageMetadata, {
        status: "loading",
        key: "new",
      });
    query.resolve(metadataResult());
    await load;
  }
});

test("package lens results require their current request key", async () => {
  const packageItem = packageModel();

  {
    const query = deferred<BrowserPackageIntegrations>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageIntegrations: async () => query.promise,
      }));
    const load = coordinator.loadIntegrations(packageItem, "first", null);
      state.packageIntegrations = {
        status: "loading",
        key: "second",
      };
    query.resolve(integrationsResult());
    await load;
    assert.deepEqual(state.packageIntegrations, {
      status: "loading",
      key: "second",
    });
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "first", null);
      state.packageOpportunities = {
        status: "loading",
        key: "second",
      };
      query.resolve(opportunitiesResult());
      await load;
      assert.deepEqual(state.packageOpportunities, {
        status: "loading",
        key: "second",
      });
  }

  {
    const query = deferred<PackagePerformance>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackagePerformance: async () => query.promise,
      }));
    const load = coordinator.loadPerformance(packageItem, "first", null);
      state.packagePerformance = {
        status: "loading",
        key: "second",
      };
    query.resolve(performanceResult());
    await load;
    assert.deepEqual(state.packagePerformance, {
      status: "loading",
      key: "second",
    });
  }
});

test("runtime package lenses wait for an explicit library scope", async () => {
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  let queries = 0;
  let renders = 0;
  const state = inspectionState({ packages: [runtime] });
  const dependencies = inspectionDependencies(state, {
    queryPlatformIntegrations: async () => {
      queries++;
      return integrationsResult();
    },
    queryPlatformOpportunities: async () => {
      queries++;
      return opportunitiesResult();
    },
    queryPlatformPerformance: async () => {
      queries++;
      return performanceResult();
    },
    queryPlatformMetadata: async () => {
      queries++;
      return metadataResult();
    },
    render: () => renders++,
  });
  const coordinator = createPackageInspectionCoordinator(dependencies);

  await coordinator.loadIntegrations(runtime, "integrations", null);
  await coordinator.loadOpportunities(runtime, "opportunities", null);
  await coordinator.loadPerformance(runtime, "performance", null);
  await coordinator.loadMetadata(runtime, "metadata", null);

  assert.equal(queries, 0);
  assert.equal(renders, 0);
  assert.equal(state.packageIntegrations.status, "idle");
  assert.equal(state.packageOpportunities.status, "idle");
  assert.equal(state.packagePerformance.status, "idle");
  assert.equal(state.packageMetadata.status, "idle");
});

// `packageMetadataView` is the only bridge from the metadata `AsyncResource` to the
// renderer's four flattened options, and round 2 review (GPT-5.6 Sol, Claude Opus 5) found
// it had no test at all: discarding `resource.error` in the `failed` arm, and deleting the
// staleness gate the README claims is gated, both left the whole suite green. Losing the
// error text is user-visible -- the renderer falls through to its generic "Loading…"
// placeholder instead of reporting that the metadata read failed.
const metadataProjections: readonly {
  status: string;
  resource: AsyncResource<PackageMetadata>;
  signature: string;
  expected: ReturnType<typeof packageMetadataView>;
}[] = [
  {
    status: "idle",
    resource: { status: "idle" },
    signature: "scope",
    expected: { fresh: false, loading: false, error: "", metadata: null },
  },
  {
    status: "loading",
    resource: { status: "loading", key: "scope" },
    signature: "scope",
    expected: { fresh: true, loading: true, error: "", metadata: null },
  },
  {
    status: "loading",
    resource: { status: "loading", key: "other" },
    signature: "scope",
    expected: { fresh: false, loading: false, error: "", metadata: null },
  },
  {
    status: "failed",
    resource: { status: "failed", key: "scope", error: "metadata failed" },
    signature: "scope",
    expected: { fresh: true, loading: false, error: "metadata failed", metadata: null },
  },
  {
    status: "failed",
    resource: { status: "failed", key: "other", error: "metadata failed" },
    signature: "scope",
    expected: { fresh: false, loading: false, error: "", metadata: null },
  },
  {
    status: "ready",
    resource: { status: "ready", key: "scope", data: metadataResult() },
    signature: "scope",
    expected: { fresh: true, loading: false, error: "", metadata: metadataResult() },
  },
  {
    status: "ready",
    resource: { status: "ready", key: "other", data: metadataResult() },
    signature: "scope",
    expected: { fresh: false, loading: false, error: "", metadata: null },
  },
];

test("packageMetadataView projects every request state", () => {
  for (const projection of metadataProjections) {
    const stale = projection.expected.fresh ? "fresh" : "stale";
    assert.deepEqual(
      packageMetadataView(projection.resource, projection.signature),
      projection.expected,
      `${stale} ${projection.status} projection`);
  }
});

test("the projection table covers every AsyncResource variant", () => {
  // Derived, not restated: a new variant added to the union is an untested projection
  // until it appears here, and this fails rather than passing quietly.
  const source = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), "..", "src", "data.ts"), "utf8");
  const declaration = /export type AsyncResource<T> =([\s\S]*?);\n/.exec(source)?.[1] ?? "";
  const variants = [...declaration.matchAll(/status\s*:\s*"([^"]+)"/g)]
    .map(match => match[1])
    .filter((status): status is string => status !== undefined);

  assert.ok(
    variants.length >= 4,
    "the AsyncResource anchor stopped matching, so this gate derives nothing");
  assert.deepEqual(
    [...new Set(metadataProjections.map(projection => projection.status))].sort(),
    [...new Set(variants)].sort(),
    "the metadata projection table no longer covers the AsyncResource union");
});
