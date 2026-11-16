import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { createNuGetPackageSummaryModel, packageLibrariesForModel } from "../src/package-acquisition.ts";
import { parseWorkspaceRoute } from "../src/workspace-navigation.ts";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

const selectDefault = stripTypeScriptTypes(
  `${sourceText(functionDeclaration("firstLibraryScope"))}\n`
  + sourceText(functionDeclaration("selectDefaultPackageSubject")),
);
const selectLens = stripTypeScriptTypes(
  sourceText(functionDeclaration("selectLibraryLens")),
);
const applyLocation = stripTypeScriptTypes(
  sourceText(functionDeclaration("applyLocationView")),
);
const installSurface = stripTypeScriptTypes(
  sourceText(functionDeclaration("installPackageSurfacePopulation")),
);
const prepareSpotlight = stripTypeScriptTypes(
  sourceText(functionDeclaration("prepareSpotlightResults")),
);

type PackageChildren =
  Parameters<typeof createNuGetPackageSummaryModel>[0];
type PackageLibrary = PackageChildren["content"]["libraries"][number];

function packageChildren(
  packageId: string,
  libraries: readonly PackageLibrary[],
  status: PackageChildren["content"]["status"] = "Available",
): PackageChildren {
  return {
    content: {
      kind: "PackageChildren",
      status,
      packageId,
      packageVersion: "1.0.0",
      targetFramework: "net8.0",
      libraries,
      runtimeIdentifierPackages: [],
      detail: null,
      isComplete: true,
    },
    share: {
      kind: "NonProjectable",
      fullUrl: null,
      packet: null,
      path: null,
      reason: "Test fixture",
    },
    diagnostics: [],
  };
}

test("summary model retains the explicit no-compile status", () => {
  const model = createNuGetPackageSummaryModel(
    packageChildren("MetadataOnly", [], "NoCompileAssets"),
    [],
  );
  assert.equal(model.compileLibraryStatus, "NoCompileAssets");
});

for (const [childrenStatus, compileStatus] of [
  ["SelectedEmpty", "EmptyCompileGroup"],
  ["NoApplicableTarget", "NoMatchingTargetFramework"],
] as const) {
  test(`summary model retains ${compileStatus} from compile selection`, () => {
    const model = createNuGetPackageSummaryModel(
      packageChildren("ReferencePack", [], childrenStatus),
      [],
      undefined,
      undefined,
      undefined,
      compileStatus,
    );
    assert.equal(model.compileLibraryStatus, compileStatus);
  });
}

test("tool summary does not infer compile status from package-child status", () => {
  const model = createNuGetPackageSummaryModel(
    packageChildren("Tool.Package", [], "NoApplicableTarget"),
    [],
    undefined,
    undefined,
    undefined,
    null,
  );
  assert.equal(model.compileLibraryStatus, undefined);
});

test("fresh package navigation opens Package Overview with First Library selected", () => {
  const state = {
    package: {
      assemblies: [{ id: "first" }, { id: "best" }],
      assemblyId: "best",
    },
    workspaceSubjectOpen: true,
    libraryScope: new Set(["previous"]),
    atLibraryRoot: true,
    atPackageRoot: false,
    packageLens: "metadata",
    libraryLens: "api",
  };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject();`, {
    state,
    packageLibrariesForModel,
  });
  assert.equal(state.workspaceSubjectOpen, false);
  assert.equal(state.atLibraryRoot, false);
  assert.equal(state.atPackageRoot, true);
  assert.deepEqual(Array.from(state.libraryScope ?? []), ["best"]);
  assert.equal(state.packageLens, "overview");
  assert.equal(state.libraryLens, "overview");
});

test("fresh package navigation without an admitted First Library selects no Library", () => {
  const state = {
    package: { assemblies: [{ id: "first" }], assemblyId: "missing" },
    libraryScope: new Set(["previous"]) as Set<string> | null,
  };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject();`, {
    state,
    packageLibrariesForModel,
  });
  assert.equal(state.libraryScope, null);
});

for (const hasWorkspaceState of [false, true]) {
  test(`${hasWorkspaceState ? "restored" : "routed"} Package Overview selects First Library for Library entry`, () => {
    const loc = {
      ...parseWorkspaceRoute(new URL(
        "https://inspect.example/?package=Sample&version=1.0.0#pkg",
      )).visible,
      hasWorkspaceState,
    };
    const state = {
      package: {
        assemblies: [{ id: "first" }, { id: "best" }],
        assemblyId: "best",
      },
      rootKind: "package",
      atLibraryRoot: false,
      atPackageRoot: true,
      libraryScope: null as Set<string> | null,
    };
    runInNewContext(`${selectDefault}\n${applyLocation}\napplyLocationView(loc);`, {
      state,
      loc,
      packageLibrariesForModel,
    });
    assert.equal(state.atPackageRoot, true);
    assert.deepEqual(Array.from(state.libraryScope ?? []), ["best"]);
  });
}

test("restored All libraries navigation stays the explicit aggregate", () => {
  const loc = {
    ...parseWorkspaceRoute(new URL(
      "https://inspect.example/?package=Sample&version=1.0.0#library",
    )).visible,
    hasWorkspaceState: true,
  };
  const state = {
    package: {
      assemblies: [{ id: "first" }, { id: "best" }],
      assemblyId: "best",
    },
    atLibraryRoot: false,
    atPackageRoot: false,
    libraryScope: null as Set<string> | null,
  };
  runInNewContext(`${selectDefault}\n${applyLocation}\napplyLocationView(loc);`, {
    state,
    loc,
    packageLibrariesForModel,
  });
  assert.equal(state.atLibraryRoot, true);
  assert.equal(state.libraryScope, null);
});

for (const [hash, subject, lens] of [
  ["", "package", "overview"],
  ["#pkg", "package", "overview"],
  ["#library", "library", "overview"],
  ["#library:metadata", "library", "metadata"],
] as const) {
  test(`package route ${hash || "(implicit)"} opens ${subject} ${lens}`, () => {
    const loc = parseWorkspaceRoute(new URL(
      `https://inspect.example/?package=Sample&version=1.0.0${hash}`,
    )).visible;
    const state = {
      package: {
        assemblies: [{ id: "first" }, { id: "best" }],
        assemblyId: "best",
      },
      atLibraryRoot: true,
      atPackageRoot: false,
      libraryScope: new Set(["previous"]),
      packageLens: "metadata",
      libraryLens: "api",
    };
    runInNewContext(`${selectDefault}\n${applyLocation}\napplyLocationView(loc);`, {
      state,
      loc,
      packageLibrariesForModel,
    });
    assert.equal(state.atPackageRoot, subject === "package");
    assert.equal(state.atLibraryRoot, subject === "library");
    assert.equal(subject === "package" ? state.packageLens : state.libraryLens, lens);
    if (subject === "library" || hash === "") {
      assert.deepEqual(Array.from(state.libraryScope), ["best"]);
    }
  });
}

test("restored Library navigation retains its exact selection and inspector", () => {
  const loc = {
    ...parseWorkspaceRoute(new URL(
      "https://inspect.example/?package=Sample&version=1.0.0#library:metadata",
    )).visible,
    hasWorkspaceState: true,
  };
  const state = {
    package: {
      assemblies: [{ id: "first" }, { id: "best" }],
      assemblyId: "best",
    },
    atLibraryRoot: false,
    atPackageRoot: true,
    libraryScope: new Set(["first"]),
    libraryLens: "overview",
  };
  runInNewContext(`${selectDefault}\n${applyLocation}\napplyLocationView(loc);`, {
    state,
    loc,
    packageLibrariesForModel,
  });
  assert.equal(state.atPackageRoot, false);
  assert.equal(state.atLibraryRoot, true);
  assert.equal(state.libraryLens, "metadata");
  assert.deepEqual(Array.from(state.libraryScope), ["first"]);
});

const autoLoad = stripTypeScriptTypes(
  sourceText(functionDeclaration("maybeAutoLoadPackageSurfaceForLibraryNavigation")),
);
const deepLoad = stripTypeScriptTypes(
  sourceText(functionDeclaration("loadDeepPackageSurface")),
);

for (const lens of ["overview", "api"]) {
  test(`Library ${lens} requests its exact Type surface only when needed`, () => {
    const loads: (string | null)[] = [];
    runInNewContext(`${autoLoad}\nmaybeAutoLoadPackageSurfaceForLibraryNavigation();`, {
      state: { package: { id: "Sample" }, atLibraryRoot: true, libraryLens: lens, libraryScope: new Set(["best"]) },
      packageSurfaceIsEligible: () => true,
      loadPackageSurface: (_pkg: unknown, libraryId: string | null) => {
        loads.push(libraryId);
        return Promise.resolve(true);
      },
      observeAsync: () => {},
    });
    assert.deepEqual(loads, lens === "overview" ? [] : ["best"]);
  });
}


test("All Libraries Overview retains its broad Type navigation request", () => {
  const loads: (string | null)[] = [];
  runInNewContext(`${autoLoad}\nmaybeAutoLoadPackageSurfaceForLibraryNavigation();`, {
    state: { package: { id: "Sample" }, atLibraryRoot: true, libraryLens: "overview", libraryScope: null },
    packageSurfaceIsEligible: () => true,
    loadPackageSurface: (_pkg: unknown, libraryId: string | null) => {
      loads.push(libraryId);
      return Promise.resolve(true);
    },
    observeAsync: () => {},
  });
  assert.deepEqual(loads, [null]);
});

test("deep Library Type navigation requests only its exact Library surface", async () => {
  const loads: (string | null)[] = [];
  await runInNewContext(
    `${deepLoad}\nloadDeepPackageSurface(pkg, deep, undefined);`,
    {
      pkg: { id: "Sample" },
      deep: { atLibraryRoot: true, library: "sibling", type: "Sibling.Type" },
      packageSurfaceIsEligible: () => true,
      packageLibrariesForModel: () => [{ id: "sibling-id" }],
      resolvePackageLibrary: () => ({ id: "sibling-id" }),
      resolveReplacementPackageLibrary: () => null,
      loadPackageSurface: (_pkg: unknown, libraryId: string | null) => {
        loads.push(libraryId);
        return Promise.resolve(true);
      },
    },
  );
  assert.deepEqual(loads, ["sibling-id"]);
});

test("package-scoped deep Type navigation retains aggregate loading", async () => {
  const loads: (string | null)[] = [];
  await runInNewContext(
    `${deepLoad}\nloadDeepPackageSurface(pkg, deep, undefined);`,
    {
      pkg: { id: "Sample" },
      deep: { type: "Sibling.Type" },
      packageSurfaceIsEligible: () => true,
      packageLibrariesForModel: () => [{ id: "first" }, { id: "sibling" }],
      resolvePackageLibrary: () => null,
      resolveReplacementPackageLibrary: () => null,
      loadPackageSurface: (_pkg: unknown, libraryId: string | null) => {
        loads.push(libraryId);
        return Promise.resolve(true);
      },
    },
  );
  assert.deepEqual(loads, [null]);
});


test("summary model follows the declared default even when it is not the first Library", () => {
  const children = packageChildren("Sample", [
    {
      assetId: "first",
      assemblyName: "First",
      assetPath: "lib/First.dll",
      role: "Compile",
    },
    {
      assetId: "best",
      assemblyName: "Best",
      assetPath: "lib/Best.dll",
      role: "Compile",
    },
  ]);
  const model = createNuGetPackageSummaryModel(children, [], undefined, undefined, "best");
  assert.equal(model.assemblyId, "best");
  assert.equal(model.assembly, "Best");
  assert.equal(model.assemblies.length, 0);
  assert.equal(packageLibrariesForModel(model).length, 2);
});

test("a tool-only summary retains the owner-issued First Library for explicit Library navigation", () => {
  const assetId = "tools/net11.0/any/Tool.Payload.dll";
  const children = packageChildren("Tool.Payload", [
    {
      assetId,
      assemblyName: "Tool.Payload",
      assetPath: assetId,
      role: "ToolEntryPoint",
    },
  ]);
  const model = createNuGetPackageSummaryModel(
    children,
    [],
    undefined,
    undefined,
    assetId,
  );
  assert.equal(model.assemblyId, assetId);
  assert.deepEqual(packageLibrariesForModel(model).map(library => library.id), [assetId]);
});

test("an exact-only Library lens consumes the owner-issued First Library", () => {
  const selected: string[] = [];
  const state = {
    libraryLens: "overview",
    package: { assemblyId: "owner-first" },
  };
  runInNewContext(`${selectLens}\nselectLibraryLens("api");`, {
    state,
    libraryLensRequiresExactLibrary: () => true,
    aggregateLibrarySubjectIsActive: () => true,
    packageLibraries: () => [
      { id: "host-preferred" },
      { id: "owner-first" },
    ],
    selectLibrarySubject: (id: string) => selected.push(id),
    render: () => {},
  });
  assert.deepEqual(selected, ["owner-first"]);
  assert.equal(state.libraryLens, "api");
});

test("installing an exact sibling surface retains the owner-issued First Library", () => {
  const ownerChildren = { content: { libraries: [] } };
  const pkg = {
    assembly: "Owner.First",
    assemblyId: "owner-first",
    assemblyAsset: "ref/net8.0/Owner.First.dll",
    packageChildren: ownerChildren,
    packageInfo: { content: "owner-info" },
    versionSettlement: { content: "owner-version" },
    types: [],
  };
  const expanded = {
    assembly: "Sibling",
    assemblyId: "sibling",
    assemblyAsset: "ref/net8.0/Sibling.dll",
    packageChildren: { content: { libraries: ["sibling"] } },
    packageInfo: undefined,
    versionSettlement: undefined,
    types: [{ name: "Sibling.Type" }],
  };
  runInNewContext(
    `${installSurface}\ninstallPackageSurfacePopulation(pkg, expanded, true);`,
    { pkg, expanded },
  );
  assert.equal(pkg.assembly, "Owner.First");
  assert.equal(pkg.assemblyId, "owner-first");
  assert.equal(pkg.assemblyAsset, "ref/net8.0/Owner.First.dll");
  assert.equal(pkg.packageChildren, ownerChildren);
  assert.deepEqual(pkg.types, [{ name: "Sibling.Type" }]);
});

test("Spotlight queues aggregate loading while an exact request is in flight", async () => {
  const pkg = { id: "Sample" };
  const loads: (string | null)[] = [];
  let releaseExact!: () => void;
  const exactSettled = new Promise<void>(resolve => {
    releaseExact = resolve;
  });
  let observed!: Promise<unknown>;
  let updates = 0;
  runInNewContext(`${prepareSpotlight}\nprepareSpotlightResults();`, {
    state: {
      spotlightOpen: true,
      spotlightQuery: "Sibling.Type",
      spotlightScope: "types",
      packages: [pkg],
      package: pkg,
    },
    packageSurfaceIsEligible: () => true,
    packageSurfaceIsAvailable: () => false,
    packageSurfaceFailed: () => false,
    loadPackageSurface: async (_pkg: unknown, libraryId: string | null = null) => {
      loads.push(libraryId);
      await exactSettled;
      return true;
    },
    observeAsync: (operation: Promise<unknown>) => {
      observed = operation;
    },
    spotlight: {
      updateResults: () => {
        updates++;
      },
    },
  });
  assert.deepEqual(loads, [null]);
  releaseExact();
  await observed;
  assert.equal(updates, 1);
});

test("summary construction without a default field preserves its legacy fallback", () => {
  const children = packageChildren("Sample", [
    {
      assetId: "first",
      assemblyName: "First",
      assetPath: "lib/First.dll",
      role: "Compile",
    },
  ]);
  const model = createNuGetPackageSummaryModel(children, []);
  assert.equal(model.assemblyId, "first");
});
