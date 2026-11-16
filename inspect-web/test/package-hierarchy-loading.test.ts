import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { hierarchyShareTargetFramework } from "../src/workspace-navigation.ts";
import { functionSource } from "./composition-root-test-fixture.ts";

function hierarchyLoading({
  platforms = [{
    family: "runtime",
    framework: "net10.0",
    version: "10.0.12",
  }],
  unavailableVersions = [],
  traversalTargetFramework = "net10.0",
}: {
  platforms?: { family: string; framework: string; version: string }[];
  unavailableVersions?: string[];
  traversalTargetFramework?: string | null;
} = {}) {
  type Catalog = {
    defaultFramework: string;
    target(tfm: string, version?: string): {
      tfm: string;
      version: string;
      supplies: { family: string; package: string; version: string }[];
    } | null;
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
    platformSelection: null,
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
      definition: { traversalTargetFramework },
      platforms,
    },
    scope: () => "package",
    hierarchyShareTargetFramework,
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
        target: (tfm, version) => {
          const selected = version ?? (tfm === "net10.0" ? "10.0.12" : "11.0.0");
          return unavailableVersions.includes(selected) ? null : {
            tfm,
            version: selected,
            supplies: [{
              family: "Microsoft.NETCore.App",
              package: "System.Text.Json",
              version: selected,
            }, {
              family: "Microsoft.AspNetCore.App",
              package: "Microsoft.Extensions.Caching.Memory",
              version: selected,
            }],
          };
        },
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
  assert.equal(JSON.stringify(request[5]), JSON.stringify([{
    tfm: "net10.0",
    version: "10.0.12",
    supplies: [{
      family: "Microsoft.NETCore.App",
      package: "System.Text.Json",
      version: "10.0.12",
    }],
  }, {
    tfm: "net10.0",
    version: "10.0.12",
    supplies: [{
      family: "Microsoft.AspNetCore.App",
      package: "Microsoft.Extensions.Caching.Memory",
      version: "10.0.12",
    }],
  }]));
  assert.equal(loading.state.packageHierarchyError, "");
});

test("an unpinned Hierarchy uses the product governing target's catalog", async () => {
  const loading = hierarchyLoading({ platforms: [], traversalTargetFramework: null });
  loading.start();

  await loading.complete();

  const request = assertRequest(loading.requests);
  assert.deepEqual(request.slice(0, 5), [
    "Example.Application", "1.0.0", "netstandard2.0", null, "first",
  ]);
  assert.equal(JSON.stringify(request[5]), JSON.stringify([{
    tfm: "net11.0",
    version: "11.0.0",
    supplies: [{
      family: "Microsoft.NETCore.App",
      package: "System.Text.Json",
      version: "11.0.0",
    }],
  }, {
    tfm: "net11.0",
    version: "11.0.0",
    supplies: [{
      family: "Microsoft.AspNetCore.App",
      package: "Microsoft.Extensions.Caching.Memory",
      version: "11.0.0",
    }],
  }]));
});

test("package-focused Hierarchy binds each retained family's exact patch", async () => {
  const loading = hierarchyLoading({
    platforms: [{
      family: "runtime",
      framework: "net10.0",
      version: "10.0.11",
    }, {
      family: "aspnetcore",
      framework: "net10.0",
      version: "10.0.10",
    }],
  });
  loading.start();

  await loading.complete();

  assert.equal(JSON.stringify(assertRequest(loading.requests)[5]), JSON.stringify([{
    tfm: "net10.0",
    version: "10.0.11",
    supplies: [{
      family: "Microsoft.NETCore.App",
      package: "System.Text.Json",
      version: "10.0.11",
    }],
  }, {
    tfm: "net10.0",
    version: "10.0.10",
    supplies: [{
      family: "Microsoft.AspNetCore.App",
      package: "Microsoft.Extensions.Caching.Memory",
      version: "10.0.10",
    }],
  }]));
});

test("a missing retained patch never falls back to the catalog-default patch", async () => {
  const loading = hierarchyLoading({
    platforms: [{
      family: "runtime",
      framework: "net10.0",
      version: "10.0.11",
    }],
    unavailableVersions: ["10.0.11"],
  });
  loading.start();

  await loading.complete();

  assert.equal(JSON.stringify(assertRequest(loading.requests)[5]), JSON.stringify([{
    tfm: "net10.0",
    version: "10.0.11",
    supplies: null,
  }, {
    tfm: "net10.0",
    version: "10.0.12",
    supplies: [{
      family: "Microsoft.AspNetCore.App",
      package: "Microsoft.Extensions.Caching.Memory",
      version: "10.0.12",
    }],
  }]));
});

test("conflicting retained patches do not invent one family's pruning authority", async () => {
  const loading = hierarchyLoading({
    platforms: ["10.0.11", "10.0.12"].map(version => ({
      family: "runtime",
      framework: "net10.0",
      version,
    })),
  });
  loading.start();

  await loading.complete();

  assert.equal(JSON.stringify(assertRequest(loading.requests)[5]), JSON.stringify([{
    tfm: "net10.0",
    version: "",
    supplies: null,
  }, {
    tfm: "net10.0",
    version: "10.0.12",
    supplies: [{
      family: "Microsoft.AspNetCore.App",
      package: "Microsoft.Extensions.Caching.Memory",
      version: "10.0.12",
    }],
  }]));
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
    platforms: [],
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
