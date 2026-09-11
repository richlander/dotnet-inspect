import assert from "node:assert/strict";
import test from "node:test";
import {
  createPublishedRuntimeBenchmarkBridge,
  installPublishedRuntimeBenchmarkBridge,
  type PublishedRuntimeBenchmarkTarget,
} from "../src/published-runtime-benchmark-bridge.ts";

function unused(): never {
  throw new Error("The operation is not invoked by this contract test.");
}

const client = {
  host: { buildIdentity: unused },
  package: { queryPackage: unused },
  analysis: {
    queryMemberFacts: unused,
    queryPackagePerformance: unused,
  },
  source: {
    queryMethodBodyComparison: unused,
    queryMethodBodyComparisonTargets: unused,
  },
};

test("published runtime benchmark bridge exposes only owned operations", () => {
  const bridge = createPublishedRuntimeBenchmarkBridge(client);

  assert.deepEqual(Object.keys(bridge).sort(), [
    "analysis",
    "host",
    "package",
    "source",
  ]);
  assert.deepEqual(Object.keys(bridge.host), ["buildIdentity"]);
  assert.deepEqual(Object.keys(bridge.package), ["queryPackage"]);
  assert.deepEqual(Object.keys(bridge.analysis).sort(), [
    "queryMemberFacts",
    "queryPackagePerformance",
  ]);
  assert.deepEqual(Object.keys(bridge.source).sort(), [
    "queryMethodBodyComparison",
    "queryMethodBodyComparisonTargets",
  ]);
  assert.equal(bridge.host.buildIdentity, client.host.buildIdentity);
  assert.equal(bridge.package.queryPackage, client.package.queryPackage);
  assert.equal(
    bridge.analysis.queryMemberFacts,
    client.analysis.queryMemberFacts,
  );
  assert.equal(
    bridge.analysis.queryPackagePerformance,
    client.analysis.queryPackagePerformance,
  );
  assert.equal(
    bridge.source.queryMethodBodyComparison,
    client.source.queryMethodBodyComparison,
  );
  assert.equal(
    bridge.source.queryMethodBodyComparisonTargets,
    client.source.queryMethodBodyComparisonTargets,
  );
});

test("published runtime benchmark bridge is opt-in", () => {
  const target: PublishedRuntimeBenchmarkTarget = {};
  const bridge = createPublishedRuntimeBenchmarkBridge(client);

  assert.equal(
    installPublishedRuntimeBenchmarkBridge(target, "", bridge),
    false,
  );
  assert.equal(target.__inspectWebRuntimeBenchmark, undefined);

  assert.equal(
    installPublishedRuntimeBenchmarkBridge(
      target,
      "?package=example&runtime-benchmark=1",
      bridge,
    ),
    true,
  );
  assert.equal(target.__inspectWebRuntimeBenchmark, bridge);
});
