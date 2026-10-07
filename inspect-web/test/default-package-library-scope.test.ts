import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { createNuGetPackageSummaryModel, packageLibrariesForModel } from "../src/package-acquisition.ts";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

const selectDefault = stripTypeScriptTypes(
  sourceText(functionDeclaration("selectDefaultPackageSubject")),
);

test("fresh package navigation selects the declared default Library rather than all Libraries", () => {
  const state = { libraryScope: null as Set<string> | null, atLibraryRoot: false, atPackageRoot: true };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject(pkg);`, {
    state,
    packageLibrariesForModel,
    pkg: { assemblies: [{ id: "first" }, { id: "best" }], assemblyId: "best" },
  });
  assert.equal(state.atLibraryRoot, true);
  assert.equal(state.atPackageRoot, false);
  assert.deepEqual(Array.from(state.libraryScope ?? []), ["best"]);
});

test("a package without a default Library opens at Package", () => {
  const state = { libraryScope: new Set(["previous"]), atLibraryRoot: true, atPackageRoot: false };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject(pkg);`, {
    state,
    packageLibrariesForModel,
    pkg: { assemblies: [], assemblyId: "" },
  });
  assert.equal(state.atLibraryRoot, false);
  assert.equal(state.atPackageRoot, true);
  assert.equal(state.libraryScope, null);
});


test("summary-only package navigation selects its declared default without a broad surface", () => {
  const state = { libraryScope: null as Set<string> | null, atLibraryRoot: false, atPackageRoot: true };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject(pkg);`, {
    state,
    packageLibrariesForModel,
    pkg: {
      assemblies: [],
      assemblyId: "best",
      packageChildren: { content: { libraries: [
        { assetId: "first", assemblyName: "First", assetPath: "lib/First.dll" },
        { assetId: "best", assemblyName: "Best", assetPath: "lib/Best.dll" },
      ] } },
    },
  });
  assert.equal(state.atLibraryRoot, true);
  assert.deepEqual(Array.from(state.libraryScope ?? []), ["best"]);
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
  const children = {
    content: { packageId: "Sample", packageVersion: "1.0.0", targetFramework: "net8.0",
      status: "Available", isComplete: true, libraries: [
        { assetId: "first", assemblyName: "First", assetPath: "lib/First.dll" },
        { assetId: "best", assemblyName: "Best", assetPath: "lib/Best.dll" },
      ] },
  } as unknown as Parameters<typeof createNuGetPackageSummaryModel>[0];
  const model = createNuGetPackageSummaryModel(children, [], undefined, undefined, "best");
  assert.equal(model.assemblyId, "best");
  assert.equal(model.assembly, "Best");
  assert.equal(model.assemblies.length, 0);
  assert.equal(packageLibrariesForModel(model).length, 2);
});

test("a tool-only summary with no product compile default stays at Package", () => {
  const assetId = "tools/net11.0/any/Tool.Payload.dll";
  const children = {
    content: { packageId: "Tool.Payload", packageVersion: "1.0.0", targetFramework: "net11.0",
      status: "Available", isComplete: true, libraries: [
        { assetId, assemblyName: "Tool.Payload", assetPath: assetId, role: "ToolEntryPoint" },
      ] },
  } as unknown as Parameters<typeof createNuGetPackageSummaryModel>[0];
  const model = createNuGetPackageSummaryModel(children, [], undefined, undefined, null);
  const state = { libraryScope: new Set(["previous"]), atLibraryRoot: true, atPackageRoot: false };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject(pkg);`, {
    state,
    packageLibrariesForModel,
    pkg: model,
  });
  assert.equal(model.assemblyId, "");
  assert.equal(state.atLibraryRoot, false);
  assert.equal(state.atPackageRoot, true);
  assert.equal(state.libraryScope, null);
  assert.deepEqual(packageLibrariesForModel(model).map(library => library.id), [assetId]);
});

test("summary construction without a default field preserves its legacy fallback", () => {
  const children = {
    content: { packageId: "Sample", packageVersion: "1.0.0", targetFramework: "net8.0",
      status: "Available", isComplete: true, libraries: [
        { assetId: "first", assemblyName: "First", assetPath: "lib/First.dll" },
      ] },
  } as unknown as Parameters<typeof createNuGetPackageSummaryModel>[0];
  const model = createNuGetPackageSummaryModel(children, []);
  assert.equal(model.assemblyId, "first");
});
