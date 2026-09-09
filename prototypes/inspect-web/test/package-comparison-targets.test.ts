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
    currentVersionInsertionIndex: 0,
    previousVersion: "1.0.0",
    previousVersionUnavailableReason: null,
  },
};
const escapeHtml = (value: unknown) => String(value).replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;").replaceAll('"', "&quot;");

test("new Packages default to the previous version", () => {
  const current = pkg();
  const targets = createPackageComparisonTargets(() => [current]);
  assert.deepEqual(targets.get(current), { diff: { kind: "previous" } });
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

test("rollback copies the Diff setting into the snapshot model", () => {
  const current = pkg();
  const other = pkg("Other.Package");
  const targets = createPackageComparisonTargets(() => [current, other]);
  targets.selectDiff(current, { kind: "exact", version: "2.0.0" }, versions);
  const copiedCurrent = structuredClone(current);
  const copiedOther = structuredClone(other);
  targets.copyPackages(new Map([[current, copiedCurrent], [other, copiedOther]]));
  targets.forget(current);
  assert.deepEqual(targets.get(copiedCurrent).diff, { kind: "exact", version: "2.0.0" });
  assert.deepEqual(targets.get(copiedOther).diff, { kind: "previous" });
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
    status: "available", inventory: { versions: [], currentVersionInsertionIndex: 0, previousVersion: null, previousVersionUnavailableReason: null },
  }), "No earlier listed version is available.");
  assert.equal(diffTargetDescription({ kind: "previous" }, {
    status: "available", inventory: { versions: ["1.0.0"], currentVersionInsertionIndex: 0, previousVersion: null, previousVersionUnavailableReason: "Listing unknown" },
  }), "Listing unknown");
  assert.equal(diffTargetDescription({ kind: "previous" }, { status: "failed", message: "Offline" }), "Offline");
});

test("form renders escaped failures, explicit limitations, and a retry action", () => {
  const current = pkg("<Package>");
  const html = renderPackageComparisonTargets({
    package: current,
    diff: { kind: "previous" },
    versions: { status: "failed", message: "<offline>" },
  }, escapeHtml);
  assert.match(html, /&lt;offline>/);
  assert.match(html, /forthcoming Diff inspector/);
  assert.match(html, /package-comparison-retry/);
  assert.doesNotMatch(html, /Clone across/);
});

test("failed inventory retry stays visible while preserving an exact selection", () => {
  const current = pkg();
  const html = renderPackageComparisonTargets({
    package: current,
    diff: { kind: "exact", version: "1.0.0" },
    versions: { status: "failed", message: "Network unavailable" },
  }, escapeHtml);
  assert.match(html, /Network unavailable/);
  assert.match(html, /value="exact:1\.0\.0" selected/);
  assert.doesNotMatch(html, /Compare against 1\.0\.0\./);
  assert.match(html, /package-comparison-retry/);
});

test("bindings dispatch exact versions without eager selection", () => {
  class Control {
    value = "";
    callback: (() => void) | null = null;
    addEventListener(_event: string, listener: () => void) { this.callback = listener; }
  }
  const diff = new Control();
  const retry = new Control();
  const controls = new Map([
    ["#package-diff-target", diff],
    ["#package-comparison-retry", retry],
  ]);
  const events: unknown[] = [];
  bindPackageComparisonTargets(fakeDom.parentNode({
    querySelector: (selector: string) => controls.get(selector) ?? null,
  }), {
    selectDiff: target => events.push(target),
    retry: () => events.push("retry"),
  });
  assert.deepEqual(events, []);
  diff.value = "exact:1.0.0";
  diff.callback?.();
  retry.callback?.();
  assert.deepEqual(events, [
    { kind: "exact", version: "1.0.0" },
    "retry",
  ]);
});
