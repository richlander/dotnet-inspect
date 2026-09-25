import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserCallGraph,
  BrowserCallGraphBoundary,
  BrowserDirectUseClusterInspection,
} from "../src/facades/inspect-web-call-graph.d.ts";
import {
  createDirectUseClusterInspectionController,
  renderDirectUseClusterInspection,
  type DirectUseClusterInspectionState,
} from "../src/direct-use-cluster-inspection.ts";

const boundary: BrowserCallGraphBoundary = {
  id: "e4",
  sourcePackageId: "Consumer",
  sourcePackageVersion: "1.0.0",
  sourcePackageFramework: "netstandard2.0",
  sourceAssembly: "Consumer.dll",
  targetPackageId: "Provider",
  targetPackageVersion: "8.8.0",
  targetPackageFramework: "net8.0",
  targetAssembly: "Provider.dll",
};

function state(): DirectUseClusterInspectionState {
  return {
    directUseClusterGraphKey: "",
    directUseClusterBoundary: null,
    directUseClusterResult: null,
    directUseClusterLoading: false,
    directUseClusterError: "",
    directUseClusterSeq: 0,
  };
}

function result(selectedCluster: number | null): BrowserDirectUseClusterInspection {
  const library = {
    name: "Consumer",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
    moduleVersionId: "11111111-1111-1111-1111-111111111111",
  };
  return {
    outcome: "available",
    isComplete: true,
    clusters: [{
      ordinal: 1,
      source: library,
      target: {
        ...library,
        name: "Provider",
        moduleVersionId: "22222222-2222-2222-2222-222222222222",
      },
      anchorSourceToken: 0x06000001,
      anchorTargetToken: 0x06000002,
      sourceMembers: 2,
      providerTypes: 1,
      targetMembers: 3,
      extensionMethods: 1,
      callSites: 4,
    }],
    selectedCluster,
    callSites: selectedCluster === null ? [] : [{
      source: library,
      sourceMember: "Consumer.Worker.Run()",
      sourceToken: 0x06000001,
      target: {
        ...library,
        name: "Provider",
        moduleVersionId: "22222222-2222-2222-2222-222222222222",
      },
      targetMember: "Provider.Policy.Execute()",
      targetToken: 0x06000002,
      callKind: "callvirt",
      evidenceMethod: "Consumer.Worker.Run()",
      evidenceModuleVersionId: library.moduleVersionId,
      evidenceToken: 0x06000001,
      ilOffset: 0x0012,
    }],
    diagnostics: [],
    failure: null,
  };
}

function graph(): BrowserCallGraph {
  const node = {
    label: "Consumer.Worker.Run",
    status: "Analyzed",
    inLoop: false,
    source: null,
    children: [],
    assembly: "Consumer",
    typeFullName: "Consumer.Worker",
    memberName: "Run",
  };
  return {
    mermaid: "graph-a",
    callers: node,
    callees: node,
    scope: {
      packages: 2,
      assemblies: 2,
      callerAssemblies: 1,
      calleeScope: "Supply Chain",
    },
    targets: [],
    boundaries: [boundary],
    diagnostics: {
      incompleteNodes: 0,
      incompleteEdges: 0,
      bindingIdentityConflicts: 0,
      hasUnexploredTraversalBoundary: false,
      hasAnalysisFailureBoundary: false,
      unavailableDependencyRoutes: 0,
      isIncomplete: false,
    },
    noBody: false,
  };
}

test("boundary and cluster selections remain managed query requests", async () => {
  const current = state();
  const requests: unknown[] = [];
  const controller = createDirectUseClusterInspectionController({
    state: current,
    query: async request => {
      requests.push(request);
      return result(request.selectedCluster || null);
    },
    describeError: String,
    render: () => {},
  });

  await controller.selectBoundary("graph-a", boundary);
  assert.deepEqual(requests[0], {
    sourcePackageId: "Consumer",
    sourceVersion: "1.0.0",
    sourceFramework: "netstandard2.0",
    sourceAssembly: "Consumer.dll",
    targetPackageId: "Provider",
    targetVersion: "8.8.0",
    targetFramework: "net8.0",
    targetAssembly: "Provider.dll",
    selectedCluster: 0,
  });
  assert.equal(current.directUseClusterResult?.selectedCluster, null);

  await controller.selectCluster(1);
  assert.equal(current.directUseClusterResult?.selectedCluster, 1);
  assert.equal(current.directUseClusterResult?.callSites.length, 1);
});

test("cluster rendering exposes separate footprint dimensions and exact receipts", () => {
  const current = state();
  current.directUseClusterGraphKey = "graph-a";
  current.directUseClusterBoundary = boundary;
  current.directUseClusterResult = result(1);
  const html = renderDirectUseClusterInspection(
    graph(),
    "graph-a",
    current,
    value => value);

  assert.match(html, /2 source/);
  assert.match(html, /1 provider types/);
  assert.match(html, /3 targets/);
  assert.match(html, /1 extensions/);
  assert.match(html, /4 call sites/);
  assert.match(html, /Consumer\.Worker\.Run\(\)/);
  assert.match(html, /Provider\.Policy\.Execute\(\)/);
  assert.match(html, /callvirt · IL 0x0012/);
  assert.match(html, /0x06000001/);
  assert.match(html, /MVID 11111111-1111-1111-1111-111111111111/);
  assert.match(html, /MVID 22222222-2222-2222-2222-222222222222/);
  assert.match(html, /data-direct-use-cluster="1"/);
});

test("focused loading preserves pair-wide cluster choices", async () => {
  const current = state();
  const renders: string[] = [];
  let completeFocused:
    ((value: BrowserDirectUseClusterInspection) => void) | undefined;
  const controller = createDirectUseClusterInspectionController({
    state: current,
    query: request => request.selectedCluster === 0
      ? Promise.resolve(result(null))
      : new Promise(resolve => {
          completeFocused = resolve;
        }),
    describeError: String,
    render: () => {
      renders.push(renderDirectUseClusterInspection(
        graph(),
        "graph-a",
        current,
        value => value));
    },
  });

  await controller.selectBoundary("graph-a", boundary);
  const focused = controller.selectCluster(1);
  assert.match(renders.at(-1) ?? "", /Inspecting exact Library use/);
  assert.match(renders.at(-1) ?? "", /Cluster 1/);
  assert.doesNotMatch(renders.at(-1) ?? "", /Exact Call Sites/);

  assert.ok(completeFocused);
  completeFocused(result(1));
  await focused;
});

test("rejected inspection remains an explicit failure", () => {
  const current = state();
  current.directUseClusterGraphKey = "graph-a";
  current.directUseClusterBoundary = boundary;
  current.directUseClusterResult = {
    ...result(null),
    outcome: "rejected",
    isComplete: false,
    clusters: [],
    diagnostics: [{
      code: "pair.rejected",
      severity: "error",
      summary: "The pair could not be inspected.",
      correspondence: null,
    }],
    failure: "The selected Libraries are the same participant.",
  };

  const html = renderDirectUseClusterInspection(
    graph(),
    "graph-a",
    current,
    value => value);

  assert.match(html, /The selected Libraries are the same participant/);
  assert.doesNotMatch(html, /No direct use was observed/);
});

test("request identity hides selection from a replacement graph", () => {
  const current = state();
  current.directUseClusterGraphKey = "request-a";
  current.directUseClusterBoundary = boundary;
  current.directUseClusterResult = result(1);
  const replacement = {
    ...graph(),
    boundaries: [{
      ...boundary,
      sourcePackageVersion: "2.0.0",
      targetPackageVersion: "9.0.0",
    }],
  };

  const html = renderDirectUseClusterInspection(
    replacement,
    "request-b",
    current,
    value => value);

  assert.doesNotMatch(html, /Exact Call Sites/);
  assert.doesNotMatch(html, /11111111-1111-1111-1111-111111111111/);
  assert.match(html, /Consumer@2\.0\.0/);
  assert.match(html, /Provider@9\.0\.0/);
});

test("failed pair-wide retry does not retain focused call sites", async () => {
  const current = state();
  let rejectRetry: ((reason?: unknown) => void) | undefined;
  const controller = createDirectUseClusterInspectionController({
    state: current,
    query: request => {
      if (request.selectedCluster === 1) return Promise.resolve(result(1));
      if (current.directUseClusterResult === null) {
        return Promise.resolve(result(null));
      }
      return new Promise((_, reject) => {
        rejectRetry = reject;
      });
    },
    describeError: error => String(error),
    render: () => {},
  });

  await controller.selectBoundary("request-a", boundary);
  await controller.selectCluster(1);
  const retry = controller.selectBoundary("request-a", boundary);
  assert.equal(current.directUseClusterResult?.selectedCluster, null);
  assert.deepEqual(current.directUseClusterResult?.callSites, []);

  assert.ok(rejectRetry);
  rejectRetry(new Error("retry failed"));
  await retry;

  const html = renderDirectUseClusterInspection(
    graph(),
    "request-a",
    current,
    value => value);
  assert.match(html, /retry failed/);
  assert.doesNotMatch(html, /Exact Call Sites/);
  assert.doesNotMatch(html, /11111111-1111-1111-1111-111111111111/);
});
