import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { functionDeclaration, sourceText } from "./composition-root-test-fixture.ts";

const selectDefault = stripTypeScriptTypes(
  sourceText(functionDeclaration("selectDefaultPackageSubject")),
);

test("fresh package navigation selects the declared default Library rather than all Libraries", () => {
  const state = { libraryScope: null as Set<string> | null, atLibraryRoot: false, atPackageRoot: true };
  runInNewContext(`${selectDefault}\nselectDefaultPackageSubject(pkg);`, {
    state,
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
    pkg: { assemblies: [], assemblyId: "" },
  });
  assert.equal(state.atLibraryRoot, false);
  assert.equal(state.atPackageRoot, true);
  assert.equal(state.libraryScope, null);
});
