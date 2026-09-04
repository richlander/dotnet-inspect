import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { parseSync } from "oxc-parser";
import { packageIdentityKey } from "../src/data.ts";
import {
  createPackageRemoval,
  type PackageRemovalState,
} from "../src/package-removal.ts";

const alpha = { id: "Alpha", version: "1.0.0", activeFramework: "net10.0" };
const beta = { ...alpha, id: "Beta" };
const otherAlpha = { ...alpha, version: "2.0.0" };

function harness(packages = [alpha, beta, otherAlpha]) {
  const state: PackageRemovalState<typeof alpha> = {
    packages,
    package: packages[0] ?? null,
    recentPackages: [alpha, beta].map(pkg => ({
      id: pkg.id, version: pkg.version, framework: pkg.activeFramework,
    })),
  };
  let stored = JSON.stringify(state.recentPackages);
  let failStorage = false;
  const released: typeof alpha[] = [];
  const activated: (typeof alpha | null)[] = [];
  const removal = createPackageRemoval({
    state,
    persistRecent: entries => {
      if (failStorage) throw new Error("Storage is unavailable");
      stored = JSON.stringify(entries);
    },
    activate: next => {
      activated.push(next);
      state.package = next;
    },
    release: pkg => released.push(pkg),
  });
  return {
    state, removal, released, activated,
    persisted: () => JSON.parse(stored) as unknown,
    failStorage: () => { failStorage = true; },
  };
}

test("forgetting a recent ID persists without changing loaded packages", () => {
  const h = harness();
  h.removal.forgetRecent("aLPHa");
  assert.deepEqual(h.persisted(), [
    { id: "Beta", version: "1.0.0", framework: "net10.0" },
  ]);
  assert.deepEqual(h.state.packages, [alpha, beta, otherAlpha]);
  assert.equal(h.state.package, alpha);
  assert.deepEqual(h.released, []);
});

test("removing an inactive coordinate preserves active selection and other versions", () => {
  const h = harness();
  h.removal.removeLoaded(packageIdentityKey(otherAlpha));
  assert.deepEqual(h.state.packages, [alpha, beta]);
  assert.equal(h.state.package, alpha);
  assert.deepEqual(h.activated, []);
  assert.deepEqual(h.released, [otherAlpha]);
  assert.equal(h.state.recentPackages.some(entry => entry.id === "Alpha"), false);
});

test("active and last removal use the existing successor and release each removed model", () => {
  const h = harness([alpha, beta]);
  h.removal.removeLoaded(packageIdentityKey(alpha));
  assert.equal(h.state.package, beta);
  h.removal.removeLoaded(packageIdentityKey(beta));
  assert.equal(h.state.package, null);
  assert.deepEqual(h.state.packages, []);
  assert.deepEqual(h.persisted(), []);
  assert.deepEqual(h.released, [alpha, beta]);
});

test("storage failure preserves membership and history without running removal effects", () => {
  const h = harness();
  const before = structuredClone(h.state);
  h.failStorage();
  assert.throws(() => h.removal.forgetRecent("Alpha"), /Storage/);
  assert.throws(() => h.removal.removeLoaded(packageIdentityKey(alpha)), /Storage/);
  assert.deepEqual(h.state, before);
  assert.deepEqual(h.activated, []);
  assert.deepEqual(h.released, []);
});

test("Platform and missing coordinates fail visibly rather than removing another package", () => {
  const platform = { ...beta, isRuntimePack: true };
  const h = harness([alpha, platform]);
  assert.throws(() => h.removal.removeLoaded(packageIdentityKey(platform)), /no longer removable/);
  assert.throws(() => h.removal.removeLoaded("missing"), /no longer removable/);
  assert.deepEqual(h.state.packages, [alpha, platform]);
});

const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const app = parseSync("dotnet-inspect.ts", appSource);
const hostNames = new Set([
  "activateAfterPackageRemoval", "finishPackageRemoval", "activatePackage",
  "packageIdentityEquals", "defaultAccessibilityFilter",
]);
const hostDeclarations = app.program.body
  .filter(node => node.type === "FunctionDeclaration" && hostNames.has(node.id?.name ?? ""))
  .map(node => appSource.slice(node.start, node.end)).join("\n");

for (const home of [true, false]) {
  test(`original application removal handlers preserve ${home ? "Home" : "Workspace"} through last removal`, () => {
    const state = {
      home, packages: [alpha, beta], package: alpha as typeof alpha | null,
      workspaceSubjectOpen: !home, atPackageRoot: false,
      dependenciesGroupIndex: 2, selectedTypeId: "Old.Type",
      selectedMemberKey: "Old.Member", selectedOverloadIndex: 3,
      memberBrowseTypeId: "Old.Type", accessibilityFilter: new Set<string>(),
      workspaceShareBasis: { previous: true },
    };
    const locations: string[] = [];
    const released: typeof alpha[] = [];
    const effects: string[] = [];
    const context = {
      state, packageIdentityKey, next: beta, first: alpha,
      spotlightCache: {}, spotlightMemberCache: {},
      resetLocationFilters: () => effects.push("filters"),
      resetMemberFilters: () => {},
      resetMemberSectionState: () => effects.push("member"),
      navigationSequence: { begin: () => effects.push("cancel") },
      invalidateGraphMemberNavigation: () => {},
      clearWorkspaceOccurrenceView: () => effects.push("occurrences"),
      packageInspection: { invalidatePackageResults: () => effects.push("results") },
      releasePackageModelCaches: (pkg: typeof alpha) => released.push(pkg),
      workspaceLocation: { replace: (url: string) => locations.push(url) },
      render: () => effects.push("render"),
    };
    runInNewContext(stripTypeScriptTypes(`${hostDeclarations}
      state.packages = [next];
      activateAfterPackageRemoval(next);
      finishPackageRemoval(first);
      state.packages = [];
      activateAfterPackageRemoval(null);
      finishPackageRemoval(next);
    `), context);
    assert.equal(state.home, home);
    assert.equal(state.package, null);
    assert.equal(state.workspaceSubjectOpen, !home);
    assert.equal(state.dependenciesGroupIndex, null);
    assert.equal(state.selectedTypeId, "");
    assert.equal(state.selectedMemberKey, "");
    assert.equal(state.selectedOverloadIndex, null);
    assert.equal(state.workspaceShareBasis, null);
    assert.deepEqual(locations, home ? [] : ["/demos"]);
    assert.deepEqual(released, [alpha, beta]);
    assert.equal(effects.filter(effect => effect === "render").length, 2);
  });
}
