import assert from "node:assert/strict";
import test from "node:test";

import {
  createAppMemberSurface,
  createNuGetPackageModel,
  createPackageAcquisition,
  createRuntimePackageModel,
  graphOnlyImplementationBody,
  mergeRuntimePackageSurface,
  retainGraphOnlyImplementationBody,
  runtimeAssemblyIsResident,
  type AppPackage,
  type PackageAcquisitionDependencies,
} from "../src/package-acquisition.ts";
import type {
  BrowserAssemblySurface,
  BrowserMemberSurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "../src/facades/inspect-web-package.d.ts";

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
    platformPack: null,
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
    platformPack: null,
  };
}

function memberSurface(): BrowserMemberSurface {
  return {
    name: "Value",
    kind: "property",
    signature: "int Value { get; set; }",
    accessibility: "public",
    isStatic: false,
    isUnsafe: false,
    isVirtual: false,
    isAbstract: false,
    isOverride: false,
    isExtension: false,
    isObsolete: false,
    genericArity: 0,
    metadataToken: null,
    returnType: "int",
    parameters: [],
    documentationId: "P:Example.Widget.Value",
    summary: null,
    returns: null,
    exceptions: [],
    stableSelector: "Value",
    anchorDigest: "value",
    canonicalSignature: "int Example.Widget.Value",
    graphSelectorKey: "Value",
    bodySelectors: [{
      token: 0x06000001,
      memberName: "get_Value",
      selectorKey: "getter",
    }, {
      token: 0x06000002,
      memberName: "set_Value",
      selectorKey: "setter",
    }],
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
    compileLibrary: { status: "Selected", targetFramework: "net10.0", message: null },
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
    icon: null,
    inspectionErrors: [],
    inspectionError: null,
    ...overrides,
  };
}

function generatedPackageSurfaceRejectsMutation(
  surface: BrowserPackageSurface,
): void {
  // @ts-expect-error Generated wire properties are producer-owned snapshots.
  surface.version = "application state";
  // @ts-expect-error Generated wire collections are readonly.
  surface.types[0] = typeSurface("Application.Type");
  const type = surface.types[0];
  if (!type) return;
  const member = type.api[0];
  if (!member) return;
  // @ts-expect-error Nested generated wire collections are readonly.
  type.api[0] = member;
}
void generatedPackageSurfaceRejectsMutation;

test("root-only package surfaces preserve typed unavailability at the UI boundary", () => {
  assert.throws(
    () => createNuGetPackageModel(packageSurface({
      defaultAssemblyId: null,
      compileLibrary: {
        status: "NoCompileAssets",
        targetFramework: null,
        message: null,
      },
      assemblies: [],
      types: [],
      accessibility: [],
      totalMembers: 0,
    })),
    /NoCompileAssets/,
  );
});

test("NuGet package models retain the product-issued icon descriptor", () => {
  const icon = {
    mediaType: "image/png",
    base64: "iVBORw0KGgo=",
  } as const;

  const model = createNuGetPackageModel(packageSurface({ icon }));

  assert.deepEqual(model.icon, icon);
});

test("graph-only implementation bodies select, switch, and clear", () => {
  const overload = {
    ...createAppMemberSurface(memberSurface()),
    graphOnly: true,
  };
  const navigationTarget = {
    assembly: "Example.dll",
    assemblyVersion: "1.2.3.4",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "stale",
    selectorKey: "stale",
    metadataToken: 0x06000003,
  };

  const getter = retainGraphOnlyImplementationBody(overload, {
    ...navigationTarget,
    memberName: "get_Value",
    selectorKey: "getter",
  });
  assert.equal(graphOnlyImplementationBody(overload)?.token, 0x06000001);
  assert.equal(getter?.assembly, "Example.dll");
  assert.equal(getter?.typeDefinitionId, "T:Example.Widget");
  assert.equal(getter?.metadataToken, 0x06000001);

  const setter = retainGraphOnlyImplementationBody(overload, {
    ...navigationTarget,
    memberName: "set_Value",
    selectorKey: "setter",
  });
  assert.equal(graphOnlyImplementationBody(overload)?.token, 0x06000002);
  assert.equal(setter?.selectorKey, "setter");
  assert.equal(setter?.metadataToken, 0x06000002);

  const unmatched = retainGraphOnlyImplementationBody(
    overload,
    navigationTarget);
  assert.equal(unmatched, navigationTarget);
  assert.equal(graphOnlyImplementationBody(overload), undefined);

  retainGraphOnlyImplementationBody(overload, {
    ...navigationTarget,
    memberName: "get_Value",
    selectorKey: "getter",
  });
  assert.ok(graphOnlyImplementationBody(overload));
  assert.equal(retainGraphOnlyImplementationBody(overload, null), null);
  assert.equal(graphOnlyImplementationBody(overload), undefined);
});

function runtimeSurface(
  assemblyId: string,
  assemblyName: string,
  typeId: string,
  totalMembers = 2,
  platformPack = "netcore.app",
): BrowserPackageSurface {
  const primary = {
    ...assembly(assemblyId, assemblyName),
    platformPack,
  };
  const type = {
    ...typeSurface(typeId, assemblyName),
    platformPack,
  };
  return packageSurface({
    package: "Microsoft.NETCore.App",
    version: "10.0.0",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    defaultAssemblyId: primary.id,
    assemblies: [primary],
    types: [type],
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
    return {
      ...result,
      defaultAssemblyId: blank,
      assemblies: result.assemblies.map(candidate =>
        candidate === selected ? { ...candidate, id: blank } : candidate),
    };
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
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
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

test("package projection copies only application-owned mutable collections", () => {
  const surface = packageSurface();
  const model = createNuGetPackageModel(surface);

  assert.notEqual(model.frameworks, surface.frameworks);
  assert.notEqual(model.assemblies, surface.assemblies);
  assert.equal(model.assemblies[0], surface.assemblies[0]);
  assert.notEqual(model.types, surface.types);
  assert.notEqual(model.types[0], surface.types[0]);
  assert.notEqual(model.types[0]?.api, surface.types[0]?.api);
  assert.notEqual(model.accessibility, surface.accessibility);
  assert.equal(model.accessibility[0], surface.accessibility[0]);
  assert.notEqual(model.documents, surface.documents);
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
    /platform query returned no descriptor for System\.Text\.Json/);
  assert.deepEqual(failures, [
    "The platform query returned no descriptor for System.Text.Json.",
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

test("runtime surface merging validates identity before mutating the resident model", () => {
  for (const mode of ["missing", "empty", "whitespace"] as const) {
    const resident = createRuntimePackageModel(
      runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
    const originalTypes = resident.types.length;
    assert.throws(
      () => mergeRuntimePackageSurface(
        resident,
        runtimeSurfaceWithInvalidAssemblyIds(mode)),
      /platform query did not return its selected assembly identity/,
      mode);
    assert.equal(resident.types.length, originalTypes, mode);
  }
});

test("runtime surface merging rejects an unmatched nonempty descriptor list", () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  const originalTypes = resident.types.length;
  const json = assembly("json", "System.Text.Json");
  assert.throws(
    () => mergeRuntimePackageSurface(resident, packageSurface({
      package: "Microsoft.NETCore.App",
      defaultAssemblyId: "missing",
      assemblies: [json],
      types: [typeSurface("System.Text.Json.JsonDocument", json.name)],
    })),
    /platform query returned no descriptor for missing/);
  assert.equal(resident.types.length, originalTypes);
});

test("runtime surface merging rejects types without an assembly descriptor", () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  const originalTypes = resident.types.length;
  const originalMembers = resident.totalMembers;
  const originalAccessibility = resident.accessibility[0]?.count;
  assert.throws(
    () => mergeRuntimePackageSurface(resident, packageSurface({
      package: "Microsoft.NETCore.App",
      defaultAssemblyId: "missing",
      assemblies: [],
      types: [typeSurface("System.Text.Json.JsonDocument", "System.Text.Json")],
    })),
    /platform query returned no descriptor for missing/);
  assert.equal(resident.types.length, originalTypes);
  assert.equal(resident.totalMembers, originalMembers);
  assert.equal(resident.accessibility[0]?.count, originalAccessibility);
});

// Adversarial review (Claude Opus 5) found that validating the selected descriptor
// *before* the merge branch regressed a surface the engine really emits.
// `InspectionEngine.cs` permits an empty `assemblies` list whenever extraction truncates,
// and then falls back to `coordinate.DefaultAsset.Id` -- an id with no matching
// descriptor. The projection commits a participant's descriptor and types atomically, so
// this shape carries neither, but its inspection notice is still partial evidence.
// Rejecting it pre-merge turned that visible partial result into a total failure.
//
// The merge path accepts that descriptor-free truncated surface so it can preserve the
// partial inspection evidence. The non-merging path still requires a descriptor. These
// tests pin both halves.
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
      types: [],
      accessibility: [{
        id: "public",
        label: "Public",
        order: 0,
        isDefault: true,
        count: 12,
      }],
      totalMembers: 0,
      inspectionErrors: ["System.Text.Json: extraction truncated."],
      inspectionError: "System.Text.Json: extraction truncated.",
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
  assert.deepEqual(
    resident.types.map(type => type.id),
    ["System.Object"]);
  assert.equal(resident.accessibility[0]?.count, 1);
  assert.equal(
    resident.inspectionError,
    "System.Text.Json: extraction truncated.");
});

test("a truncated full-pack surface merges into a compatible resident", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface(
      "aspnet",
      "Microsoft.AspNetCore.Http.Abstractions",
      "Microsoft.AspNetCore.Http.HttpContext",
      2,
      "aspnetcore.app"));
  const failures: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      activeFramework: "net10.0",
      defaultAssemblyId: "missing",
      assemblies: [],
      types: [],
      accessibility: [{
        id: "public",
        label: "Public",
        order: 0,
        isDefault: true,
        count: 12,
      }],
      totalMembers: 0,
      inspectionErrors: ["System.Private.CoreLib: extraction truncated."],
      inspectionError: "System.Private.CoreLib: extraction truncated.",
    })),
    runtimePackage: () => resident,
    failRuntimeLoad: error =>
      failures.push(error instanceof Error ? error.message : String(error)),
  }));

  const result = await acquisition.loadRuntimePack("net10.0");

  assert.equal(result.error, null);
  assert.equal(result.packageModel, resident);
  assert.deepEqual(failures, []);
  assert.deepEqual(
    resident.types.map(type => type.id),
    ["Microsoft.AspNetCore.Http.HttpContext"]);
  assert.equal(resident.accessibility[0]?.count, 1);
  assert.equal(resident.assemblyId, "aspnet");
  assert.equal(
    resident.inspectionError,
    "System.Private.CoreLib: extraction truncated.");
});

test("repeating a partial surface merge does not inflate resident evidence", () => {
  const residentNotices = [
    "System.Private.CoreLib: extraction truncated; "
      + "0 assembly(ies) were not projected.",
    "System.Text.Json: extraction truncated; "
      + "0 assembly(ies) were not projected.",
  ];
  const residentSurface = {
    ...runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"),
    inspectionErrors: residentNotices,
    inspectionError: residentNotices.join("; "),
  };
  const resident = createRuntimePackageModel(residentSurface);
  const partialNotice = residentNotices[1];
  assert.ok(partialNotice);
  const partial = {
    ...runtimeSurface(
      "json",
      "System.Text.Json",
      "System.Text.Json.JsonDocument"),
    inspectionErrors: [partialNotice],
    inspectionError: partialNotice,
  };

  mergeRuntimePackageSurface(resident, partial);
  mergeRuntimePackageSurface(resident, partial);

  assert.deepEqual(
    resident.types.map(type => type.id),
    ["System.Object", "System.Text.Json.JsonDocument"]);
  assert.equal(resident.totalMembers, 4);
  assert.equal(resident.accessibility[0]?.count, 2);
  assert.equal(
    resident.inspectionError,
    residentSurface.inspectionError);
});

// Round 6 review split the two reviewers. GPT-5.6 Sol found that the resident-merge path
// returned before any identity check, so a surface with an absent, empty, or
// whitespace-only `defaultAssemblyId` succeeded -- and mutated the resident package --
// whenever a same-framework runtime package happened to be resident. Claude Opus 5 agreed
// on the fact and disagreed on the severity: `main` behaves identically, and the merged
// model keeps the *resident's* valid identity, so no blank-identity model is produced.
//
// Both are right about what they measured, and the disagreement is really about which
// check belongs where. A blank identity is never legitimate -- the field is declared
// non-optional -- while an unmatched one is, because truncation makes the engine fall
// back to an id matching no descriptor. So identity is now required on every path and a
// matching descriptor only where one is read, which rejects these three inputs without
// re-rejecting the truncated surface the test above pins.
// `defaultAssemblyId` is declared non-optional, so `absent` cannot be expressed as an
// override -- it is what the wire payload looks like when the engine violates that
// contract, which is precisely the case the check exists for. Deleting the key from the
// serialized form is the only faithful way to model it.
for (const [mode, corrupt] of [
  ["absent", (surface: Record<string, unknown>) => {
    delete surface["defaultAssemblyId"];
  }],
  ["empty", (surface: Record<string, unknown>) => {
    surface["defaultAssemblyId"] = "";
  }],
  ["whitespace", (surface: Record<string, unknown>) => {
    surface["defaultAssemblyId"] = "   ";
  }],
] as const) {
  test(`a resident merge rejects a surface whose assembly identity is ${mode}`, async () => {
    const resident = createRuntimePackageModel(
      runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
    const residentTypes = resident.types.length;
    const failures: string[] = [];
    const acquisition = createPackageAcquisition(acquisitionDependencies({
      loadRuntimePackAssembly: async () => {
        const surface: Record<string, unknown> = { ...packageSurface({
          package: "Microsoft.NETCore.App",
          activeFramework: "net10.0",
          assemblies: [],
          types: [typeSurface("System.Text.Json.JsonDocument", "System.Text.Json")],
        }) };
        corrupt(surface);
        return JSON.stringify(surface);
      },
      runtimePackage: () => resident,
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
      /did not return its selected assembly identity/);
    assert.deepEqual(failures, [
      "The platform query did not return its selected assembly identity.",
    ]);
    // And it is rejected *before* the merge, so the resident package is untouched. A
    // check that ran after the merge would report the failure and still have mutated it.
    assert.equal(
      resident.types.length,
      residentTypes,
      "a rejected surface must not have already been merged into the resident package");
  });
}

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
    /platform query returned no descriptor for System\.Text\.Json/);
  assert.deepEqual(failures, [
    "The platform query returned no descriptor for System.Text.Json.",
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
    loadRuntimePackAssembly: async (
      framework,
      _platformVersion,
      assemblyName,
      pack,
    ) => {
      calls.push(`assembly:${framework}/${assemblyName}/${pack}`);
      const surface = runtimeSurface(
        "json",
        "System.Text.Json",
        "System.Text.Json.JsonDocument",
        3);
      return JSON.stringify({
        ...surface,
        inspectionError: "System.Text.Json: omitted 2 metadata rows.",
        types: [
          typeSurface("System.Object", "System.Private.CoreLib"),
          ...surface.types,
        ],
      });
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
    "System.Text.Json.dll",
    "netcore.app");
  assert.deepEqual(calls, ["pack:net10.0"]);
  assert.deepEqual(status, ["begin"]);

  const coreSurface = {
    ...runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"),
    inspectionError: "System.Private.CoreLib: omitted 1 metadata row.",
  };
  fullPack.resolve(JSON.stringify(coreSurface));
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
    "assembly:net10.0/System.Text.Json.dll/netcore.app",
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
  assert.equal(mergedModel.totalMembers, 4);
  // Main's added assertions, with this slice's checked-index guard on the one indexed
  // read among them.
  const publicAccessibility = mergedModel.accessibility[0];
  assert.ok(publicAccessibility);
  assert.equal(publicAccessibility.count, 2);
  assert.equal(mergedModel.assembly, "System.Private.CoreLib");
  assert.equal(mergedModel.assemblyId, "corelib");
  assert.equal(
    mergedModel.assemblyAsset,
    "lib/net10.0/System.Private.CoreLib.dll");
  assert.equal(
    mergedModel.inspectionError,
    "System.Private.CoreLib: omitted 1 metadata row.; "
      + "System.Text.Json: omitted 2 metadata rows.");
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

test("multiple queued runtime assemblies execute one at a time", async () => {
  const fullPack = deferred<string>();
  const firstAssembly = deferred<string>();
  const secondAssembly = deferred<string>();
  const secondAssemblyStarted = deferred<void>();
  const calls: string[] = [];
  const status: string[] = [];
  let resident: AppPackage | null = null;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => {
      calls.push("pack");
      return fullPack.promise;
    },
    loadRuntimePackAssembly: async (
      _framework,
      _platformVersion,
      assemblyFileName,
    ) => {
      calls.push(assemblyFileName);
      if (assemblyFileName === "System.B.dll") secondAssemblyStarted.resolve();
      return assemblyFileName === "System.A.dll"
        ? firstAssembly.promise
        : secondAssembly.promise;
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

  const packRequest = acquisition.loadRuntimePack("net10.0");
  const firstRequest = acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.A.dll",
    "netcore.app");
  const secondRequest = acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.B.dll",
    "netcore.app");

  assert.deepEqual(calls, ["pack"]);
  fullPack.resolve(JSON.stringify(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object")));
  assert.ok((await packRequest).packageModel);
  assert.deepEqual(calls, ["pack", "System.A.dll"]);

  firstAssembly.reject(new Error("first assembly failed"));
  const firstResult = await firstRequest;
  assert.match(
    firstResult.error instanceof Error
      ? String(firstResult.error)
      : "",
    /first assembly failed/);
  await secondAssemblyStarted.promise;
  assert.deepEqual(calls, ["pack", "System.A.dll", "System.B.dll"]);
  assert.deepEqual(status, [
    "begin",
    "end",
    "begin",
    "fail:first assembly failed",
    "end",
    "begin",
  ]);

  secondAssembly.resolve(JSON.stringify(
    runtimeSurface("b", "System.B", "System.B.Widget")));
  assert.ok((await secondRequest).packageModel);
  assert.equal(status.at(-1), "end");
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

test("stale runtime assembly failures do not publish after navigation changes", async () => {
  const response = deferred<string>();
  const events: string[] = [];
  let current = true;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => response.promise,
    beginRuntimeLoad: () => events.push("begin"),
    failRuntimeLoad: error =>
      events.push(error instanceof Error ? `fail:${error.message}` : "fail"),
    endRuntimeLoad: () => events.push("end"),
  }));

  const request = acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Text.Json.dll",
    "",
    () => current);
  current = false;
  response.reject(new Error("stale feed failure"));

  const result = await request;
  assert.equal(result.packageModel, null);
  assert.match(
    result.error instanceof Error ? result.error.message : "",
    /stale feed failure/);
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

test("an exact platform version bypasses a different resident patch", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  const calls: string[] = [];
  const retainedVersions: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async (framework, platformVersion) => {
      calls.push(`${framework}@${platformVersion}`);
      const surface =
        runtimeSurface("corelib", "System.Private.CoreLib", "System.Object");
      return JSON.stringify({
        ...surface,
        version: platformVersion,
      });
    },
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      retainedVersions.push(packageModel.version);
    },
  }));

  const result = await acquisition.loadRuntimePack(
    "net10.0",
    () => true,
    "10.0.1");

  assert.deepEqual(calls, ["net10.0@10.0.1"]);
  assert.equal(result.packageModel?.version, "10.0.1");
  assert.deepEqual(retainedVersions, ["10.0.1"]);
});

test("the latest platform sentinel remains a floating acquisition", async () => {
  const calls: string[] = [];
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async (framework, platformVersion) => {
      calls.push(`${framework}@${platformVersion}`);
      return JSON.stringify(
        runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
    },
    runtimePackage: () => null,
  }));

  const result = await acquisition.loadRuntimePack(
    "net10.0",
    () => true,
    "latest");

  assert.deepEqual(calls, ["net10.0@"]);
  assert.ok(result.packageModel);
});

test("platform assembly residency includes the requested pack", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface(
      "shared-runtime",
      "Shared",
      "Shared.RuntimeType",
      2,
      "netcore.app"));
  let engineCalls = 0;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => {
      engineCalls++;
      throw new Error("cross-family duplicate rejected");
    },
    runtimePackage: () => resident,
  }));

  assert.equal(
    runtimeAssemblyIsResident(resident, "Shared.dll", "netcore.app"),
    true);
  assert.equal(
    runtimeAssemblyIsResident(resident, "Shared.dll", "aspnetcore.app"),
    false);
  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "Shared.dll",
    "aspnetcore.app");

  assert.equal(engineCalls, 1);
  assert.equal(result.packageModel, null);
  assert.match(
    result.error instanceof Error ? result.error.message : "",
    /cross-family duplicate rejected/);
});

test("runtime pack acquisition fills the core family after an ASP.NET-first load", async () => {
  const calls: string[] = [];
  let resident: AppPackage | null = null;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => {
      calls.push("aspnet");
      return JSON.stringify(runtimeSurface(
        "aspnet-http",
        "Microsoft.AspNetCore.Http.Abstractions",
        "Microsoft.AspNetCore.Http.IHeaderDictionary",
        2,
        "aspnetcore.app"));
    },
    loadRuntimePack: async () => {
      calls.push("runtime");
      return JSON.stringify(runtimeSurface(
        "corelib",
        "System.Private.CoreLib",
        "System.Object"));
    },
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      resident = packageModel;
    },
  }));

  const aspNet = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "Microsoft.AspNetCore.Http.Abstractions.dll",
    "aspnetcore.app");
  const runtime = await acquisition.loadRuntimePack("net10.0");

  assert.ok(aspNet.packageModel);
  assert.equal(runtime.packageModel, aspNet.packageModel);
  assert.deepEqual(calls, ["aspnet", "runtime"]);
  assert.deepEqual(
    runtime.packageModel?.assemblies.map(candidate => candidate.name),
    ["Microsoft.AspNetCore.Http.Abstractions", "System.Private.CoreLib"]);
  assert.equal(runtime.packageModel?.assembly, "System.Private.CoreLib");
});

test("assembly-specific CoreLib acquisition promotes an ASP.NET-first model", async () => {
  let resident: AppPackage | null = null;
  let fullRuntimeCalls = 0;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePack: async () => {
      fullRuntimeCalls++;
      return JSON.stringify(runtimeSurface(
        "corelib-full",
        "System.Private.CoreLib",
        "System.Object"));
    },
    loadRuntimePackAssembly: async (
      _framework,
      _platformVersion,
      assemblyName,
    ) =>
      JSON.stringify(assemblyName.startsWith("System.Private.CoreLib")
        ? runtimeSurface(
          "corelib",
          "System.Private.CoreLib",
          "System.Object")
        : runtimeSurface(
          "aspnet-http",
          "Microsoft.AspNetCore.Http.Abstractions",
          "Microsoft.AspNetCore.Http.IHeaderDictionary",
          2,
          "aspnetcore.app")),
    runtimePackage: () => resident,
    retainPackage: packageModel => {
      resident = packageModel;
    },
  }));

  const aspNet = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "Microsoft.AspNetCore.Http.Abstractions.dll",
    "aspnetcore.app");
  const core = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Private.CoreLib.dll",
    "netcore.app");
  const reused = await acquisition.loadRuntimePack("net10.0");

  assert.ok(aspNet.packageModel);
  assert.equal(core.packageModel, aspNet.packageModel);
  assert.equal(reused.packageModel, aspNet.packageModel);
  assert.equal(fullRuntimeCalls, 0);
  assert.equal(core.packageModel?.assembly, "System.Private.CoreLib");
  assert.equal(core.packageModel?.assemblyId, "corelib");
  assert.equal(
    core.packageModel?.assemblyAsset,
    "lib/net10.0/System.Private.CoreLib.dll");
});

test("resident primary promotion uses the declared default descriptor", async () => {
  const resident = createRuntimePackageModel(runtimeSurface(
    "aspnet-http",
    "Microsoft.AspNetCore.Http.Abstractions",
    "Microsoft.AspNetCore.Http.IHeaderDictionary",
    2,
    "aspnetcore.app"));
  const json = assembly("json", "System.Text.Json");
  const corelib = assembly("corelib", "System.Private.CoreLib");
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => JSON.stringify(packageSurface({
      package: "Microsoft.NETCore.App",
      activeFramework: "net10.0",
      defaultAssemblyId: corelib.id,
      assemblies: [json, corelib],
      types: [],
    })),
    runtimePackage: () => resident,
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Private.CoreLib.dll",
    "netcore.app");

  assert.equal(result.error, null);
  assert.equal(result.packageModel, resident);
  assert.equal(resident.assemblyId, corelib.id);
  assert.equal(resident.assembly, corelib.name);
  assert.equal(resident.assemblyAsset, corelib.asset);
});

test("resident runtime assemblies match the requested dll name", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("json", "System.Text.Json", "System.Text.Json.JsonDocument"));
  let engineCalls = 0;
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => {
      engineCalls++;
      return "";
    },
    runtimePackage: () => resident,
  }));

  assert.deepEqual(
    await acquisition.loadRuntimePackAssembly(
      "NET10.0",
      "System.Text.Json.dll",
      "netcore.app"),
    { packageModel: resident, error: null });
  assert.equal(engineCalls, 0);
});

test("runtime assembly acquisition exposes an empty surface failure", async () => {
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => {
      const surface = runtimeSurface(
        "missing",
        "System.Missing",
        "System.Missing.Type");
      return JSON.stringify({
        ...surface,
        assemblies: [],
        types: [],
        inspectionError: "System.Missing was rejected.",
      });
    },
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Missing.dll",
    "netcore.app");

  assert.equal(result.packageModel, null);
  assert.match(
    result.error instanceof Error ? result.error.message : "",
    /System\.Missing was rejected/);
});

test("resident runtime model retains an empty assembly result as inspection evidence", async () => {
  const resident = createRuntimePackageModel(
    runtimeSurface("corelib", "System.Private.CoreLib", "System.Object"));
  const acquisition = createPackageAcquisition(acquisitionDependencies({
    loadRuntimePackAssembly: async () => {
      const surface = runtimeSurface(
        "missing",
        "System.Missing",
        "System.Missing.Type");
      return JSON.stringify({
        ...surface,
        assemblies: [],
        types: [],
        inspectionError: "System.Missing was rejected.",
      });
    },
    runtimePackage: () => resident,
  }));

  const result = await acquisition.loadRuntimePackAssembly(
    "net10.0",
    "System.Missing.dll",
    "netcore.app");

  assert.equal(result.packageModel, resident);
  assert.equal(result.error, null);
  assert.equal(resident.assembly, "System.Private.CoreLib");
  assert.equal(resident.inspectionError, "System.Missing was rejected.");
});
