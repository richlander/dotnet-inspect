import assert from "node:assert/strict";
import test from "node:test";
import {
  dependencyCoordinateCandidates,
  dependencyGraphExternalKey,
  dependencyGraphPackageKey,
  ensureBoundedGraphNode,
  packageIdentityKey,
  removeAppendedNotice,
  replaceCurrentNavigationEntry,
} from "../src/data.ts";

import {
  packageAt,
} from "./composition-root-test-fixture.ts";
test("dependency candidates carry typed package provenance to the product engine", () => {
  const packageCandidate = packageAt("2.0.0", "net8.0");
  const runtimeCandidate = {
    ...packageAt("10.0.10", "net10.0"),
    id: "Microsoft.NETCore.App",
    isRuntimePack: true
  };

  assert.deepEqual(
    dependencyCoordinateCandidates([packageCandidate, runtimeCandidate]),
    [
      {
        key: packageIdentityKey(packageCandidate),
        provenance: "NuGetPackage",
        packageId: "Example.Package",
        version: "2.0.0",
        targetFramework: "net8.0"
      },
      {
        key: packageIdentityKey(runtimeCandidate),
        provenance: "PlatformRuntime",
        packageId: "Microsoft.NETCore.App",
        version: "10.0.10",
        targetFramework: "net10.0"
      }
    ]);
});
test("dependency graph keys preserve complete coordinates and declared ranges", () => {
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("1.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")));
  assert.notEqual(
    dependencyGraphPackageKey(packageAt("2.0.0", "net8.0")),
    dependencyGraphPackageKey(packageAt("2.0.0", "net9.0")));
  assert.notEqual(
    dependencyGraphExternalKey("Example.Package", "[1.0.0]"),
    dependencyGraphExternalKey("Example.Package", "2.*"));
});

test("dependency graph node insertion is bounded", () => {
  const nodes = new Map<string, { index: number }>();
  let truncated = false;
  for (let index = 0; index < 8000; index++) {
    const result = ensureBoundedGraphNode(
      nodes,
      `node-${index}`,
      () => ({ index }),
      80);
    truncated ||= result.truncated;
  }

  assert.equal(nodes.size, 80);
  assert.equal(truncated, true);
  assert.equal(
    ensureBoundedGraphNode(nodes, "node-1", () => null, 80).node,
    nodes.get("node-1"));
});

test("a successful retry removes only its appended failure notice", () => {
  const prior = "The workspace was truncated.";
  const failed = `${prior} Couldn’t load System.Net.Http: unavailable.`;

  assert.equal(removeAppendedNotice(failed, prior, failed), prior);
  assert.equal(
    removeAppendedNotice(
      `${failed} A later warning remains.`,
      prior,
      failed),
    `${prior} A later warning remains.`);
  assert.equal(
    removeAppendedNotice("A replacement warning.", prior, failed),
    "A replacement warning.");
});

test("normalizing a history entry keeps its consumed position and later entries", () => {
  const nav = {
    index: 1,
    stack: [
      { sig: "older", view: { id: "older" } },
      { sig: "stale", view: { id: "stale" } },
      { sig: "newer", view: { id: "newer" } },
    ],
  };

  replaceCurrentNavigationEntry(nav, {
    sig: "normalized",
    view: { id: "normalized" }
  });

  assert.equal(nav.index, 1);
  assert.deepEqual(nav.stack, [
    { sig: "older", view: { id: "older" } },
    { sig: "normalized", view: { id: "normalized" } },
    { sig: "newer", view: { id: "newer" } },
  ]);
});
