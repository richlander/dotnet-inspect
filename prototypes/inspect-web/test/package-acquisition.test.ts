import assert from "node:assert/strict";
import test from "node:test";

import {
  createNuGetPackageModel,
  createPackageAcquisition,
  createRuntimePackageModel,
  type AppPackage,
  type PackageAcquisitionDependencies,
} from "../src/package-acquisition.ts";
import type {
  BrowserAssemblySurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "../src/inspect-web-engine.d.ts";

function assembly(
  id: string,
  name: string,
  publicTypes = 1,
): BrowserAssemblySurface {
  return {
    id,
    name,
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
    asset: `lib/net10.0/${name}.dll`,
    publicTypes,
    publicMembers: publicTypes * 2,
  };
}

function typeSurface(
  id: string,
  assemblyName = "Example.Core",
): BrowserTypeSurface {
  return {
    id,
    definitionId: id,
    queryId: id,
    metadataId: id,
    name: id.split(".").at(-1) ?? id,
    displayName: id,
    namespace: id.split(".").slice(0, -1).join("."),
    kind: "class",
    accessibility: "public",
    accessibilityId: "public",
    assembly: assemblyName,
    assemblyId: assemblyName,
    assemblyName,
    members: 2,
    signature: `public class ${id}`,
    api: [],
  };
}

function packageSurface(
  overrides: Partial<BrowserPackageSurface> = {},
): BrowserPackageSurface {
  const primary = assembly("example-core", "Example.Core", 3);
  return {
    package: "Example.Package",
    version: "1.2.3",
    frameworks: ["net9.0", "net10.0"],
    activeFramework: "net10.0",
    defaultAssemblyId: primary.id,
    assemblies: [primary],
    types: [typeSurface("Example.Widget")],
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 1,
    }],
    totalMembers: 2,
    documents: [],
    inspectionError: null,
    ...overrides,
  };
}

function runtimeSurface(
  assemblyId: string,
  assemblyName: string,
  typeId: string,
  totalMembers = 2,
): BrowserPackageSurface {
  const primary = assembly(assemblyId, assemblyName);
  return packageSurface({
    package: "Microsoft.NETCore.App",
    version: "10.0.0",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    defaultAssemblyId: primary.id,
    assemblies: [primary],
    types: [typeSurface(typeId, assemblyName)],
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 1,
    }],
    totalMembers,
  });
}

function runtimeSurfaceWithInvalidAssemblyIds(
  mode: "missing" | "empty" | "whitespace",
): BrowserPackageSurface {
  const result =
    runtimeSurface("json", "System.Text.Json", "System.Text.Json.JsonDocument");
  const selected = result.assemblies?.[0];
  assert.ok(selected);
  if (mode === "missing") {
    Reflect.deleteProperty(result, "defaultAssemblyId");
    Reflect.deleteProperty(selected, "id");
  } else {
    // A whitespace-only id is the case a length-only guard would accept: the
    // descriptor would match itself and produce a model with a blank identity.
    const blank = mode === "empty" ? "" : "   ";
    result.defaultAssemblyId = blank;
    selected.id = blank;
  }
  return result;
}

function acquisitionDependencies(
  overrides: Partial<PackageAcquisitionDependencies> = {},
): PackageAcquisitionDependencies {
  return {
    queryPackage: async () => packageSurface(),
    loadRuntimePack: async () => JSON.stringify(
      runtimeSurface("corelib", "System.Private.CoreLib", "System.Object")),
    loadRuntimePackAssembly: async () => JSON.stringify(
      runtimeSurface("json", "System.Text.Json", "System.Text.Json.JsonDocument")),
    parseRuntimeSurface: json => {
      const parsed: unknown = JSON.parse(json);
      // The test parses JSON emitted from the typed fixture immediately above.
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      return parsed as BrowserPackageSurface;
    },
    runtimePackage: () => null,
    retainPackage: () => {},
    recordRecentPackage: () => {},
    refreshPackageStats: () => {},
    beginRuntimeLoad: () => {},
    failRuntimeLoad: () => {},
    endRuntimeLoad: () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

test("NuGet projection selects the declared assembly and preserves package totals", () => {
  const secondary = assembly("secondary", "Example.Secondary", 4);
  const model = createNuGetPackageModel(packageSurface({
    defaultAssemblyId: secondary.id,
    assemblies: [assembly("primary", "Example.Primary", 3), secondary],
    inspectionError: "one assembly could not be inspected",
  }));

  assert.equal(model.assembly, "Example.Secondary");
  assert.equal(model.assemblyId, "secondary");
  assert.equal(model.totalTypes, 7);
  assert.equal(model.inspectionError, "one assembly could not be inspected");
  assert.deepEqual(model.source, { kind: "nuget.org" });
  assert.equal(model.isRuntimePack, false);

  assert.throws(
    () => createNuGetPackageModel(packageSurface({
      defaultAssemblyId: "missing",
    })),
    /did not return its selected assembly descriptor/);
});

test("runtime assembly acquisition reports a missing selected descriptor", async () => {
  const failures: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      defaultAssemblyId: "missing",
      assemblies: [],
      types: [],
    })),
    failRuntimeLoad: error =>
      failures.push(error instanceof Error ? error.message : String(error)),
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json",
    "netcore.app");

  assert.equal(result.packageModel, null);
  assert.match(
    result.error instanceof Error ? result.error.message : "",
    /platform assembly query did not return its selected assembly descriptor/);
  assert.deepEqual(failures, [
    "The platform assembly query did not return its selected assembly descriptor.",
  ]);
});

// Adversarial review (GPT-5.6 Sol) found that routing this path through
// `defaultAssembly` changed which descriptor gets projected: it used to take
// `assemblies[0]` while reporting `defaultAssemblyId` as the identity, so a surface whose
// declared default is not first produced a model that named one assembly and identified
// another. The two agree for every surface the engine currently emits -- a runtime-pack
// assembly load returns the one assembly it was asked for -- which is why nothing caught
// it. Pin the selection so the disagreement cannot come back silently.
test("runtime assembly acquisition projects the declared default, not the first", async () => {
  const first = assembly("first", "First.Assembly");
  const declared = assembly("second", "Second.Assembly");
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      activeFramework: "net10.0",
      defaultAssemblyId: declared.id,
      assemblies: [first, declared],
      types: [],
    })),
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "Second.Assembly",
    "netcore.app");

  assert.equal(result.error, null);
  assert.equal(result.packageModel?.assemblyId, "second");
  assert.equal(result.packageModel?.assembly, "Second.Assembly");
  assert.equal(
    result.packageModel?.assemblyAsset,
    "lib/net10.0/Second.Assembly.dll");
});

test("runtime models reject missing, empty, and whitespace selected assembly IDs", () => {
  for (const mode of ["missing", "empty", "whitespace"] as const) {
    assert.throws(
      () => createRuntimePackageModel(
        runtimeSurfaceWithInvalidAssemblyIds(mode)),
      /platform query did not return its selected assembly descriptor/,
      mode);
  }
});

// Adversarial review (Claude Opus 5) found that validating the selected descriptor
// *before* the merge branch regressed a surface the engine really emits.
// `InspectionEngine.cs` permits an empty `assemblies` list whenever extraction truncates,
// and then falls back to `coordinate.DefaultAsset.Id` -- an id with no matching
// descriptor. Such a surface still carries types, and `mergeRuntimePackageSurface` reads
// types, assemblies, accessibility, and counts but never the descriptor. Rejecting it
// pre-merge turned a partially-successful load into a total failure.
//
// So the validation now sits on the path that actually needs a descriptor, and these two
// tests pin both halves: a truncated surface merges, and a surface that needs a
// descriptor still fails visibly when it has none.
test("a truncated platform surface merges instead of failing the whole load", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  const failures: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      activeFramework: "net10.0",
      // What the engine emits when extraction truncates: no descriptors, and a default
      // id that matches none of them.
      defaultAssemblyId: "missing",
      assemblies: [],
      types: [typeSurface("System.Text.Json.JsonDocument", "System.Text.Json")],
    })),
    runtimePackage: () => resident,
    failRuntimeLoad: error =>
      failures.push(error instanceof Error ? error.message : String(error)),
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json",
    "netcore.app");

  assert.equal(result.error, null);
  assert.equal(result.packageModel, resident);
  assert.deepEqual(failures, []);
  assert.ok(
    resident.types.some(type => type.id === "System.Text.Json.JsonDocument"),
    "the truncated surface's types were merged into the resident package");
});

test("a non-merging platform load still fails visibly without a descriptor", async () => {
  const failures: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      activeFramework: "net10.0",
      defaultAssemblyId: "missing",
      assemblies: [],
      types: [],
    })),
    // No resident package, so there is nothing to merge into and the descriptor is
    // genuinely required.
    runtimePackage: () => null,
    failRuntimeLoad: error =>
      failures.push(error instanceof Error ? error.message : String(error)),
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json",
    "netcore.app");

  assert.equal(result.packageModel, null);
  assert.match(
    result.error instanceof Error ? result.error.message : "",
    /platform assembly query did not return its selected assembly descriptor/);
  assert.deepEqual(failures, [
    "The platform assembly query did not return its selected assembly descriptor.",
  ]);
});

test("package acquisition publishes only current results", async () => {
  const events: string[] = [];
  const replacedPackage = createNuGetPackageModel(packageSurface({
    package: "Example.Old",
    version: "1.0.0",
  }));
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    retainPackage: (packageModel, replaced) =>
      events.push(`retain:${packageModel.id}/${replaced?.id ?? "none"}`),
    recordRecentPackage: (id, version, framework) =>
      events.push(`recent:${id}@${version}/${framework}`),
    refreshPackageStats: () => events.push("stats"),
  }));

  const stale = await acquisition.loadPackage({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    isCurrent: () => false,
  });
  assert.equal(stale, null);
  assert.deepEqual(events, []);

  const current = await acquisition.loadPackage({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    replacePackage: replacedPackage,
  });
  assert.equal(current?.id, "Example.Package");
  assert.deepEqual(events, [
    "stats",
    "retain:Example.Package/Example.Old",
    "recent:Example.Package@1.2.3/net10.0",
  ]);
});

test("runtime acquisition serializes and merges full-pack and assembly requests", async () => {
  const fullPack = deferred<string>();
  const calls: string[] = [];
  const status: string[] = [];
  let resident: AppPackage | null = null;
  let retainCount = 0;
  let statsCount = 0;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async framework => {
      calls.push(`pack:${framework}`);
      return fullPack.promise;
    },
    loadRuntimePackAssembly: async (framework, assemblyName, pack) => {
      calls.push(`assembly:${framework}/${assemblyName}/${pack}`);
      const surface = runtimeSurface(
        "json",
        "System.Text.Json",
        "System.Text.Json.JsonDocument",
        3);
      surface.types.unshift(typeSurface("System.Object", "System.Private.CoreLib"));
      return JSON.stringify(surface);
    },
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      retainCount++;
      resident = packageModel;
    },
    refreshPackageStats: () => statsCount++,
    beginRuntimeLoad: () => status.push("begin"),
    endRuntimeLoad: () => status.push("end"),
  }));

  const packRequest = acquisition.loadRuntimePack("net10.0");
  const assemblyRequest = acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json",
    "netcore.app");
  assert.deepEqual(calls, ["pack:net10.0"]);
  assert.deepEqual(status, ["begin"]);

  fullPack.resolve(JSON.stringify(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object")));
  const [packResult, mergedResult] = await Promise.all([
    packRequest,
    assemblyRequest,
  ]);
  const packModel = packResult.packageModel;
  const mergedModel = mergedResult.packageModel;

  assert.ok(packModel);
  assert.ok(mergedModel);
  assert.equal(packResult.error, null);
  assert.equal(mergedResult.error, null);
  assert.equal(mergedModel, packModel);
  assert.equal(resident, packModel);
  assert.deepEqual(calls, [
    "pack:net10.0",
    "assembly:net10.0/System.Text.Json/netcore.app",
  ]);
  assert.deepEqual(status, ["begin", "end", "begin", "end"]);
  assert.equal(retainCount, 1);
  assert.equal(statsCount, 2);
  assert.deepEqual(
    mergedModel.assemblies.map(candidate => candidate.name),
    ["System.Private.CoreLib", "System.Text.Json"]);
  assert.deepEqual(
    mergedModel.types.map(candidate => candidate.id),
    ["System.Object", "System.Text.Json.JsonDocument"]);
  assert.equal(mergedModel.totalMembers, 5);
  const publicAccessibility = mergedModel.accessibility[0];
  assert.ok(publicAccessibility);
  assert.equal(publicAccessibility.count, 2);
});

test("queued runtime work rechecks cancellation before invoking the engine", async () => {
  const fullPack = deferred<string>();
  let assemblyCalls = 0;
  let current = true;
  let resident: AppPackage | null = null;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => fullPack.promise,
    loadRuntimePackAssembly: async () => {
      assemblyCalls++;
      return JSON.stringify(
        runtimeSurface("json", "System.Text.Json", "System.Text.Json.JsonDocument"));
    },
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      resident = packageModel;
    },
  }));

  const packRequest = acquisition.loadRuntimePack("net10.0");
  const queuedRequest = acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json",
    "netcore.app",
    () => current);
  current = false;
  fullPack.resolve(JSON.stringify(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object")));

  assert.ok((await packRequest).packageModel);
  assert.deepEqual(await queuedRequest, {
    packageModel: null,
    error: null,
  });
  assert.equal(assemblyCalls, 0);
});

test("stale runtime results do not publish after the engine returns", async () => {
  const fullPack = deferred<string>();
  const events: string[] = [];
  let current = true;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => fullPack.promise,
    retainPackage: () => events.push("retain"),
    refreshPackageStats: () => events.push("stats"),
    beginRuntimeLoad: () => events.push("begin"),
    endRuntimeLoad: () => events.push("end"),
  }));

  const request = acquisition.loadRuntimePack("net10.0", () => current);
  current = false;
  fullPack.resolve(JSON.stringify(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object")));

  assert.deepEqual(await request, {
    packageModel: null,
    error: null,
  });
  assert.deepEqual(events, ["begin", "end"]);
});

test("queued runtime retries preserve each request's failure", async () => {
  const status: string[] = [];
  let attempts = 0;
  let resident: AppPackage | null = null;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => {
      attempts++;
      if (attempts === 1) throw new Error("runtime feed unavailable");
      return JSON.stringify(
        runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
    },
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      resident = packageModel;
    },
    beginRuntimeLoad: () => status.push("begin"),
    failRuntimeLoad: error =>
      status.push(error instanceof Error ? `fail:${error.message}` : "fail"),
    endRuntimeLoad: () => status.push("end"),
  }));

  const failedRequest = acquisition.loadRuntimePack("net10.0");
  const retryRequest = acquisition.loadRuntimePack("net10.0");
  const [failed, retried] = await Promise.all([
    failedRequest,
    retryRequest,
  ]);
  assert.equal(failed.packageModel, null);
  assert.match(
    failed.error instanceof Error ? failed.error.message : "",
    /runtime feed unavailable/);
  assert.ok(retried.packageModel);
  assert.equal(retried.error, null);
  assert.equal(attempts, 2);
  assert.deepEqual(status, [
    "begin",
    "fail:runtime feed unavailable",
    "end",
    "begin",
    "end",
  ]);
});

test("resident runtime packs short-circuit without entering loading state", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  let engineCalls = 0;
  let loadingTransitions = 0;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => {
      engineCalls++;
      return "";
    },
    runtimePackage: () => resident,
    beginRuntimeLoad: () => loadingTransitions++,
  }));

  assert.deepEqual(await acquisition.loadRuntimePack("NET10.0"), {
    packageModel: resident,
    error: null,
  });
  assert.equal(engineCalls, 0);
  assert.equal(loadingTransitions, 0);
});
