import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { createNuGetPackageSummaryModel, packageLibrariesForModel } from "../src/package-acquisition.ts";
import { parseWorkspaceRoute } from "../src/workspace-navigation.ts";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

const selectDefault = stripTypeScriptTypes(
  sourceText(functionDeclaration("selectDefaultPackageSubject")),
);
const selectLens = stripTypeScriptTypes(
  sourceText(functionDeclaration("selectLibraryLens")),
);
const applyLocation = stripTypeScriptTypes(
  sourceText(functionDeclaration("applyLocationView")),
);

type PackageChildren =
  Parameters<typeof createNuGetPackageSummaryModel>[0];
type PackageLibrary = PackageChildren["content"]["libraries"][number];

function packageChildren(
  packageId: string,
  libraries: readonly PackageLibrary[],
): PackageChildren {
  return {
    content: {
      kind: "PackageChildren",
      status: "Available",
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

test("fresh package navigation opens Package Overview and clears the previous Library selection", () => {
  const state = {
    workspaceSubjectOpen: true,
    libraryScope: new Set(["previous"]),
    atLibraryRoot: true,
    atPackageRoot: false,
    packageLens: "metadata",
    libraryLens: "api",
  };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject();`, { state });
  assert.equal(state.workspaceSubjectOpen, false);
  assert.equal(state.atLibraryRoot, false);
  assert.equal(state.atPackageRoot, true);
  assert.equal(state.libraryScope, null);
  assert.equal(state.packageLens, "overview");
  assert.equal(state.libraryLens, "overview");
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
    if (subject === "library") {
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
  runInNewContext(`${selectDefault}\n${applyLocation}\napplyLocationView(loc);`, { state, loc });
  assert.equal(state.atPackageRoot, false);
  assert.equal(state.atLibraryRoot, true);
  assert.equal(state.libraryLens, "metadata");
  assert.deepEqual(Array.from(state.libraryScope), ["first"]);
});

const autoLoad = stripTypeScriptTypes(
  sourceText(functionDeclaration("maybeAutoLoadPackageSurfaceForLibraryNavigation")),
);

for (const lens of ["overview", "api"]) {
  test(`Library ${lens} requests broad Type navigation only when needed`, () => {
    let loads = 0;
    runInNewContext(`${autoLoad}\nmaybeAutoLoadPackageSurfaceForLibraryNavigation();`, {
      state: { package: { id: "Sample" }, atLibraryRoot: true, libraryLens: lens, libraryScope: new Set(["best"]) },
      packageSurfaceCanLoadTypes: () => true,
      loadPackageSurface: () => { loads++; return Promise.resolve(true); },
      observeAsync: () => {},
    });
    assert.equal(loads, lens === "overview" ? 0 : 1);
  });
}


test("All Libraries Overview retains its broad Type navigation request", () => {
  let loads = 0;
  runInNewContext(`${autoLoad}\nmaybeAutoLoadPackageSurfaceForLibraryNavigation();`, {
    state: { package: { id: "Sample" }, atLibraryRoot: true, libraryLens: "overview", libraryScope: null },
    packageSurfaceCanLoadTypes: () => true,
    loadPackageSurface: () => { loads++; return Promise.resolve(true); },
    observeAsync: () => {},
  });
  assert.equal(loads, 1);
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
