import assert from "node:assert/strict";
import test from "node:test";
import {
  createPublishedSourceComparisonBridge,
  installPublishedSourceComparisonBridge,
  type PublishedSourceComparisonTarget,
} from "../src/published-source-comparison-bridge.ts";

function unused(): never {
  throw new Error("The operation is not invoked by this contract test.");
}

const client = {
  package: { queryPackage: unused },
  source: {
    cancelMemberSourceComparison: unused,
    queryMemberSourceComparison: unused,
  },
};

test("published Source comparison bridge exposes only owned operations", () => {
  const bridge = createPublishedSourceComparisonBridge(client);

  assert.deepEqual(Object.keys(bridge).sort(), ["package", "source"]);
  assert.deepEqual(Object.keys(bridge.package), ["queryPackage"]);
  assert.deepEqual(Object.keys(bridge.source).sort(), [
    "cancelMemberSourceComparison",
    "queryMemberSourceComparison",
  ]);
  assert.equal(bridge.package.queryPackage, client.package.queryPackage);
  assert.equal(
    bridge.source.cancelMemberSourceComparison,
    client.source.cancelMemberSourceComparison,
  );
  assert.equal(
    bridge.source.queryMemberSourceComparison,
    client.source.queryMemberSourceComparison,
  );
});

test("published Source comparison bridge is opt-in", () => {
  const target: PublishedSourceComparisonTarget = {};
  const bridge = createPublishedSourceComparisonBridge(client);

  assert.equal(
    installPublishedSourceComparisonBridge(target, "", bridge),
    false,
  );
  assert.equal(target.__inspectWebSourceComparison, undefined);

  assert.equal(
    installPublishedSourceComparisonBridge(
      target,
      "?package=example&source-comparison-gate=1",
      bridge,
    ),
    true,
  );
  assert.equal(target.__inspectWebSourceComparison, bridge);
});
