import assert from "node:assert/strict";
import test from "node:test";

import {
  createCatalogRequests,
  resetCatalogRequestLoading,
  type CatalogRequestDependencies,
  type CatalogRequestState,
  type DotnetRelease,
} from "../src/catalog-requests.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}

function createState(): CatalogRequestState {
  return {
    package: null,
    packages: [],
    dotnetReleases: null,
    dotnetReleasesLoading: false,
    packageVersions: {},
    packageVersionsLoading: {},
  };
}

function createHarness(
  state = createState(),
  overrides: Partial<Omit<CatalogRequestDependencies, "state">> = {},
) {
  let platformUpdates = 0;
  const packageUpdates: string[] = [];
  const platformUpdateSnapshots: Array<DotnetRelease[] | null> = [];
  const packageUpdateSnapshots: Array<{
    packageId: string;
    versions: string[] | undefined;
  }> = [];
  let releaseQueries = 0;
  const versionQueries: string[] = [];
  const dependencies: CatalogRequestDependencies = {
    state,
    queryDotnetReleases: async () => {
      releaseQueries++;
      return [];
    },
    queryPackageVersions: async packageId => {
      versionQueries.push(packageId);
      return [];
    },
    updatePlatformVersionSelect: () => {
      platformUpdates++;
      platformUpdateSnapshots.push(state.dotnetReleases);
    },
    updatePackageVersionSelect: packageId => {
      packageUpdates.push(packageId);
      packageUpdateSnapshots.push({
        packageId,
        versions: state.packageVersions[packageId],
      });
    },
    ...overrides,
  };
  return {
    requests: createCatalogRequests(dependencies),
    state,
    get packageUpdates() {
      return packageUpdates;
    },
    get packageUpdateSnapshots() {
      return packageUpdateSnapshots;
    },
    get platformUpdates() {
      return platformUpdates;
    },
    get platformUpdateSnapshots() {
      return platformUpdateSnapshots;
    },
    get releaseQueries() {
      return releaseQueries;
    },
    get versionQueries() {
      return versionQueries;
    },
  };
}

test("release requests cache rows and refresh the resident Platform selector", async () => {
  const state = createState();
  const runtime = { id: ".NET Platform", isRuntimePack: true };
  state.package = runtime;
  state.packages = [runtime];
  const rows: DotnetRelease[] = [
    { major: 10, tfm: "net10.0", version: "10.0.4" },
    { major: 9, tfm: "net9.0", version: "9.0.9" },
  ];
  const harness = createHarness(state, {
    queryDotnetReleases: async () => rows,
  });

  await harness.requests.ensureDotnetReleases();

  assert.deepEqual(state.dotnetReleases, rows);
  assert.notEqual(state.dotnetReleases, rows);
  assert.equal(state.dotnetReleasesLoading, false);
  assert.equal(harness.platformUpdates, 1);
  assert.deepEqual(harness.platformUpdateSnapshots, [rows]);
});

test("release completion does not refresh a non-Platform selector", async () => {
  const state = createState();
  state.package = { id: "Example.Package" };
  const harness = createHarness(state, {
    queryDotnetReleases: async () => [
      { major: 9, tfm: "net9.0", version: "9.0.9" },
    ],
  });

  await harness.requests.ensureDotnetReleases();

  assert.equal(harness.platformUpdates, 0);
  assert.equal(state.dotnetReleases?.length, 1);
});

test("release requests deduplicate in-flight work and reuse cached rows", async () => {
  const pending = deferred<readonly DotnetRelease[]>();
  let queryCount = 0;
  const harness = createHarness(createState(), {
    queryDotnetReleases: () => {
      queryCount++;
      return pending.promise;
    },
  });

  const first = harness.requests.ensureDotnetReleases();
  const second = harness.requests.ensureDotnetReleases();
  assert.equal(harness.state.dotnetReleasesLoading, true);
  assert.equal(queryCount, 1);

  pending.resolve([]);
  await Promise.all([first, second]);
  await harness.requests.ensureDotnetReleases();

  assert.equal(queryCount, 1);
});

test("release failures remain silent, clear loading, and allow retry", async () => {
  const state = createState();
  const runtime = { id: ".NET Platform", isRuntimePack: true };
  state.package = runtime;
  state.packages = [runtime];
  let queryCount = 0;
  const harness = createHarness(state, {
    queryDotnetReleases: async () => {
      queryCount++;
      if (queryCount === 1) throw new Error("offline");
      return [{ major: 8, tfm: "net8.0", version: "8.0.20" }];
    },
  });

  await harness.requests.ensureDotnetReleases();
  assert.equal(harness.state.dotnetReleases, null);
  assert.equal(harness.state.dotnetReleasesLoading, false);
  assert.equal(harness.platformUpdates, 0);

  await harness.requests.ensureDotnetReleases();
  assert.equal(queryCount, 2);
  assert.deepEqual(harness.state.dotnetReleases, [
    { major: 8, tfm: "net8.0", version: "8.0.20" },
  ]);
  assert.equal(harness.platformUpdates, 1);
});

test("package requests ignore missing and Platform packages", async () => {
  const harness = createHarness();

  await harness.requests.ensurePackageVersions(null);
  await harness.requests.ensurePackageVersions({
    id: ".NET Platform",
    isRuntimePack: true,
  });

  assert.deepEqual(harness.versionQueries, []);
});

test("package requests normalize identity, sort versions, and refresh by identity", async () => {
  const state = createState();
  state.packages = [{ id: "Example.Package" }];
  const harness = createHarness(state, {
    queryPackageVersions: async packageId => {
      assert.equal(packageId, "example.package");
      return ["2.0.0", "10.0.0", "1.9.0", "2.1.0"];
    },
  });

  await harness.requests.ensurePackageVersions({ id: "EXAMPLE.PACKAGE" });

  assert.deepEqual(
    state.packageVersions["example.package"],
    ["10.0.0", "2.1.0", "2.0.0", "1.9.0"],
  );
  assert.equal(state.packageVersionsLoading["example.package"], false);
  assert.deepEqual(harness.packageUpdates, ["example.package"]);
  assert.deepEqual(harness.packageUpdateSnapshots, [{
    packageId: "example.package",
    versions: ["10.0.0", "2.1.0", "2.0.0", "1.9.0"],
  }]);
});

test("package requests deduplicate in-flight work and reuse cached versions", async () => {
  const state = createState();
  const pkg = { id: "Example.Package" };
  state.packages = [pkg];
  const pending = deferred<readonly string[]>();
  let queryCount = 0;
  const harness = createHarness(state, {
    queryPackageVersions: () => {
      queryCount++;
      return pending.promise;
    },
  });

  const first = harness.requests.ensurePackageVersions(pkg);
  const second = harness.requests.ensurePackageVersions(pkg);
  assert.equal(state.packageVersionsLoading["example.package"], true);
  assert.equal(queryCount, 1);

  pending.resolve(["1.0.0"]);
  await Promise.all([first, second]);
  await harness.requests.ensurePackageVersions(pkg);

  assert.equal(queryCount, 1);
  assert.deepEqual(harness.packageUpdates, ["example.package"]);
});

test("package success is discarded when its package is no longer resident", async () => {
  const state = createState();
  const pkg = { id: "Example.Package" };
  state.packages = [pkg];
  const pending = deferred<readonly string[]>();
  const harness = createHarness(state, {
    queryPackageVersions: () => pending.promise,
  });

  const request = harness.requests.ensurePackageVersions(pkg);
  state.packages = [];
  pending.resolve(["1.0.0"]);
  await request;

  assert.equal(state.packageVersions["example.package"], undefined);
  assert.equal(state.packageVersionsLoading["example.package"], undefined);
  assert.deepEqual(harness.packageUpdates, []);
});

test("package failures stay silent and keep loading state for resident packages", async () => {
  const state = createState();
  const pkg = { id: "Example.Package" };
  state.packages = [pkg];
  let queryCount = 0;
  const harness = createHarness(state, {
    queryPackageVersions: async () => {
      queryCount++;
      throw new Error("offline");
    },
  });

  await harness.requests.ensurePackageVersions(pkg);
  assert.equal(state.packageVersions["example.package"], undefined);
  assert.equal(state.packageVersionsLoading["example.package"], false);
  assert.deepEqual(harness.packageUpdates, []);

  await harness.requests.ensurePackageVersions(pkg);
  assert.equal(queryCount, 2);
});

test("package rejection removes loading state after resident removal", async () => {
  const state = createState();
  const pkg = { id: "Example.Package" };
  state.packages = [pkg];
  const pending = deferred<readonly string[]>();
  const harness = createHarness(state, {
    queryPackageVersions: () => pending.promise,
  });

  const request = harness.requests.ensurePackageVersions(pkg);
  state.packages = [];
  pending.reject(new Error("offline"));
  await request;

  assert.equal(state.packageVersionsLoading["example.package"], undefined);
  assert.deepEqual(harness.packageUpdates, []);
});

test("rollback clears transient catalog loading markers so requests can restart", async () => {
  const state = createState();
  const pkg = { id: "Example.Package" };
  state.package = pkg;
  state.packages = [pkg];
  state.dotnetReleasesLoading = true;
  state.packageVersionsLoading["example.package"] = true;
  const harness = createHarness(state);

  resetCatalogRequestLoading(state);
  await harness.requests.ensureDotnetReleases();
  await harness.requests.ensurePackageVersions(pkg);

  assert.equal(harness.releaseQueries, 1);
  assert.deepEqual(harness.versionQueries, ["example.package"]);
  assert.equal(state.dotnetReleasesLoading, false);
  assert.equal(state.packageVersionsLoading["example.package"], false);
});
