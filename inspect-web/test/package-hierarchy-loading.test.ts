import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { functionSource } from "./composition-root-test-fixture.ts";

function hierarchyLoading() {
  type Catalog = {
    defaultFramework: string;
    target(tfm: string, version?: string): {
      tfm: string;
      version: string;
      supplies: { family: string; package: string; version: string }[];
    };
  };
  let resolveCatalog!: (value: Catalog) => void;
  const catalog = new Promise<Catalog>(resolve => { resolveCatalog = resolve; });
  const requests: unknown[][] = [];
  const pending: Promise<unknown>[] = [];
  const state = {
    package: {
      id: "Example.Application",
      version: "1.0.0",
      activeFramework: "netstandard2.0",
      source: { kind: "nuget.org" },
    },
    atPackageRoot: true,
    packageLens: "hierarchy",
    platformSelection: { tfm: "net10.0", version: "10.0.12" },
    platformIndex: null,
    packageHierarchyKey: "",
    packageHierarchy: null,
    packageHierarchyLoading: false,
    packageHierarchyError: "",
    workspaceShareBasis: null,
  };
  const context = {
    state,
    activeRetainedWorkspacePosting: {
      realizationId: "first",
      publicationOrdinal: 1,
      definition: { traversalTargetFramework: "net10.0" },
    },
    scope: () => "package",
    currentPackage: () => state.package,
    loadPlatformIndex: () => catalog,
    inspectPackageHierarchy: async (...args: unknown[]) => {
      requests.push(args);
      return { package: args[0] };
    },
    observeAsync: (promise: Promise<unknown>) => pending.push(promise),
    errorMessage: String,
    render() {},
    renderPackageHierarchyGraph: async () => {},
  };
  runInNewContext(stripTypeScriptTypes([
    "workspaceTraversalTargetFramework",
    "packageHierarchySignature",
    "packageHierarchyTargetFramework",
    "maybeAutoLoadPackageHierarchy",
  ].map(functionSource).join("\n")), context);

  return {
    context,
    state,
    requests,
    start: () => { runInNewContext("maybeAutoLoadPackageHierarchy()", context); },
    async complete() {
      resolveCatalog({
        defaultFramework: "net11.0",
        target: (tfm, version) => ({
          tfm,
          version: version ?? "11.0.0",
          supplies: [{
            family: "Microsoft.NETCore.App",
            package: "System.Text.Json",
            version: "10.0.12",
          }],
        }),
      });
      await Promise.all(pending);
    },
  };
}

test("Hierarchy loads inventory for its governing target, not its root asset", async () => {
  const loading = hierarchyLoading();
  loading.start();

  await loading.complete();

  const request = assertRequest(loading.requests);
  assert.deepEqual(request.slice(0, 5), [
    "Example.Application", "1.0.0", "netstandard2.0", "net10.0", "first",
  ]);
  assert.equal(JSON.stringify(request[5]), JSON.stringify({
    tfm: "net10.0",
    version: "10.0.12",
    supplies: [{
      family: "Microsoft.NETCore.App",
      package: "System.Text.Json",
      version: "10.0.12",
    }],
  }));
  assert.equal(loading.state.packageHierarchyError, "");
});

test("a superseded Hierarchy does not submit its stale Workspace request", async () => {
  const loading = hierarchyLoading();
  loading.start();
  loading.state.packageHierarchyKey = "another-view";

  await loading.complete();

  assert.equal(loading.requests.length, 0);
});

test("a new retained Workspace invalidates an identical package Hierarchy", async () => {
  const loading = hierarchyLoading();
  loading.start();
  loading.context.activeRetainedWorkspacePosting = {
    realizationId: "runtime-free",
    publicationOrdinal: 2,
    definition: { traversalTargetFramework: "net10.0" },
  };
  loading.start();

  await loading.complete();

  assert.equal(assertRequest(loading.requests)[4], "runtime-free");
});

function assertRequest(requests: unknown[][]): unknown[] {
  assert.equal(requests.length, 1);
  const request = requests[0];
  assert.ok(request);
  return request;
}
