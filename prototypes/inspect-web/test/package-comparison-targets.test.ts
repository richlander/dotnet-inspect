import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageComparisonTargets,
  createPackageComparisonTargets,
  diffTargetDescription,
  renderPackageComparisonTargets,
  type ComparisonPackage,
} from "../src/package-comparison-targets.ts";
import type { PackageVersionState } from "../src/catalog-requests.ts";
import { fakeDom } from "./fake-dom.ts";

const pkg = (id = "Example.Package"): ComparisonPackage => ({
  id, version: "2.0.0", activeFramework: "net11.0", source: { kind: "nuget.org" },
});
const versions: PackageVersionState = {
  status: "available",
  inventory: {
    versions: ["2.0.0", "1.0.0"],
    previousVersion: "1.0.0",
    previousVersionUnavailableReason: null,
  },
};
const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;").replaceAll('"', "&quot;");

test("new Packages default to the previous version and live Workspace including self", () => {
  const current = pkg();
  const targets = createPackageComparisonTargets(() => [current]);
  assert.deepEqual(targets.get(current), {
    diff: { kind: "previous" }, clone: { kind: "workspace" },
  });
  assert.equal(diffTargetDescription(targets.get(current).diff, versions),
    "Compare against 1.0.0 (previous version).");
});

test("Library, Type, and Member readers inherit the same Package selection", () => {
  const current = pkg();
  const targets = createPackageComparisonTargets(() => [current]);
  targets.selectDiff(current, { kind: "exact", version: "2.0.0" }, versions);
  for (const subject of ["Library", "Type", "Member"]) {
    assert.deepEqual(targets.get(current).diff, { kind: "exact", version: "2.0.0" }, subject);
  }
  targets.selectDiff(current, { kind: "previous" }, versions);
  assert.deepEqual(targets.get(current).diff, { kind: "previous" });
});

test("separate same-coordinate models and replacements do not inherit choices", () => {
  const current = pkg();
  const replacement = pkg();
  const targets = createPackageComparisonTargets(() => [current, replacement]);
  targets.selectDiff(current, { kind: "exact", version: "2.0.0" }, versions);
  assert.deepEqual(targets.get(replacement).diff, { kind: "previous" });
  targets.forget(current);
  assert.deepEqual(targets.get(current).diff, { kind: "previous" });
});

test("explicit Clone selection remains visibly unavailable after its target is removed", () => {
  const current = pkg();
  const other = pkg("Other.Package");
  let packages = [current, other];
  const targets = createPackageComparisonTargets(() => packages);
  targets.selectClone(current, { kind: "package", package: other });
  packages = [current];
  const html = renderPackageComparisonTargets({
    package: current, packages, ...targets.get(current), versions,
  }, escapeHtml);
  assert.match(html, /Unavailable: Other\.Package/);
  assert.match(html, /no longer in this Workspace/);
  assert.equal(targets.get(current).clone.kind, "package");
  assert.throws(() => targets.selectClone(current, { kind: "package", package: other }), /no longer/);
  targets.selectClone(current, { kind: "workspace" });
  assert.deepEqual(targets.get(current).clone, { kind: "workspace" });
});

test("rollback copies both settings and their associations into the snapshot models", () => {
  const current = pkg();
  const other = pkg("Other.Package");
  const targets = createPackageComparisonTargets(() => [current, other]);
  targets.selectDiff(current, { kind: "exact", version: "2.0.0" }, versions);
  targets.selectClone(current, { kind: "package", package: other });
  const copiedCurrent = structuredClone(current);
  const copiedOther = structuredClone(other);
  targets.copyPackages(new Map([[current, copiedCurrent], [other, copiedOther]]));
  targets.forget(current);
  assert.deepEqual(targets.get(copiedCurrent).diff, { kind: "exact", version: "2.0.0" });
  const clone = targets.get(copiedCurrent).clone;
  assert.equal(clone.kind, "package");
  if (clone.kind === "package") assert.equal(clone.package, copiedOther);
});

test("invalid exact choices and non-Gallery origins cannot borrow an inventory", () => {
  const current = pkg();
  const other = { ...pkg(), source: { kind: "feed" } };
  const targets = createPackageComparisonTargets(() => [current, other]);
  assert.throws(() => targets.selectDiff(current, { kind: "exact", version: "9.0.0" }, versions), /available versions/);
  assert.throws(() => targets.selectDiff(other, { kind: "exact", version: "1.0.0" }, versions), /Gallery/);
});

test("no predecessor, listing uncertainty, and request failure stay distinct", () => {
  assert.equal(diffTargetDescription({ kind: "previous" }, {
    status: "available", inventory: { versions: [], previousVersion: null, previousVersionUnavailableReason: null },
  }), "No earlier listed version is available.");
  assert.equal(diffTargetDescription({ kind: "previous" }, {
    status: "available", inventory: { versions: ["1.0.0"], previousVersion: null, previousVersionUnavailableReason: "Listing unknown" },
  }), "Listing unknown");
  assert.equal(diffTargetDescription({ kind: "previous" }, { status: "failed", message: "Offline" }), "Offline");
});

test("form renders escaped coordinates, explicit limitations, and a retry action", () => {
  const current = pkg("<Package>");
  const html = renderPackageComparisonTargets({
    package: current, packages: [current],
    diff: { kind: "previous" }, clone: { kind: "workspace" },
    versions: { status: "failed", message: "<offline>" },
  }, escapeHtml);
  assert.match(html, /&lt;Package>/);
  assert.match(html, /&lt;offline>/);
  assert.match(html, /forthcoming Diff and Clone/);
  assert.match(html, /package-comparison-retry/);
  assert.match(html, /Workspace \(including self\)/);
});

test("bindings dispatch exact versions and captured Package objects without eager selection", () => {
  class Control {
    value = "";
    callback: (() => void) | null = null;
    addEventListener(_event: string, listener: () => void) { this.callback = listener; }
  }
  const diff = new Control();
  const clone = new Control();
  const retry = new Control();
  const controls = new Map([
    ["#package-diff-target", diff],
    ["#package-clone-target", clone],
    ["#package-comparison-retry", retry],
  ]);
  const current = pkg();
  const events: unknown[] = [];
  bindPackageComparisonTargets(fakeDom.parentNode({
    querySelector: (selector: string) => controls.get(selector) ?? null,
  }), [current], {
    selectDiff: target => events.push(target),
    selectClone: target => events.push(target),
    retry: () => events.push("retry"),
  });
  assert.deepEqual(events, []);
  diff.value = "exact:1.0.0";
  diff.callback?.();
  clone.value = "package:0";
  clone.callback?.();
  retry.callback?.();
  assert.deepEqual(events, [
    { kind: "exact", version: "1.0.0" },
    { kind: "package", package: current },
    "retry",
  ]);
});
