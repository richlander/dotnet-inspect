import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserTypeMethodLeverage,
  BrowserTypeMethodLeverageWinner,
} from "../src/facades/inspect-web-analysis.d.ts";
import {
  createTypeMethodLeverageCoordinator,
  methodLeverageFor,
  methodLeverageEmptyStateMessage,
  projectTypeMethodLeverage,
  typeMethodLeverageCacheKey,
  type PackageTypeMethodLeverageRequest,
  type TypeMethodLeverageStateHost,
} from "../src/method-leverage.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

const request: PackageTypeMethodLeverageRequest = {
  kind: "package",
  workspaceGeneration: "generation-1",
  packageId: "Example",
  version: "1.0.0",
  targetFramework: "net10.0",
  assemblyName: "Example.dll",
  typeDefinitionId: "Example.Widget",
};

function available(
  anchoredWinners: ReadonlyArray<BrowserTypeMethodLeverageWinner> = [{
      typeDefinitionId: request.typeDefinitionId,
      stableSelector: "HiddenWinner~1234567890",
      methodTokens: [0x06000003],
    }],
): BrowserTypeMethodLeverage {
  return {
    schemaVersion: 1,
    outcome: "available",
    subject: {
      identity: {
        name: "Example",
        version: "1.0.0.0",
        culture: null,
        publicKeyToken: null,
      },
      moduleVersionId: "00000000-0000-0000-0000-000000000001",
      provenance: {
        kind: "package",
        packageId: "Example",
        packageVersion: "1.0.0",
        framework: "net10.0",
        frameworkVersion: null,
        runtimeIdentifier: null,
        assetPath: "lib/net10.0/Example.dll",
        resolverSource: null,
        project: null,
        contentRef: null,
        digest: null,
        declaredName: null,
      },
    },
    content: {
      typeDefinitionId: request.typeDefinitionId,
      methodCount: 8,
      winnerCount: 2,
      winningRank: {
        directCallerCount: 3,
        rootReach: 4,
        fanout: 1,
        loopCallCount: 0,
        maxDepth: 2,
      },
      anchoredWinners,
      analysisDiagnostics: [],
      apiSurfaceInspectionFailures: [],
    },
    failure: null,
    share: {
      kind: "non-projectable",
      fullUrl: null,
      packet: null,
      path: "type-method-leverage/share",
      reason: "not shareable",
    },
    diagnostics: [],
    compileLibrary: {
      status: "Selected",
      targetFramework: "net10.0",
      message: null,
    },
  };
}

test("method leverage projects exact anchors without visible fallback", () => {
  const presentation = projectTypeMethodLeverage(available(), request);

  assert.equal(presentation.methodCount, 8);
  assert.equal(presentation.winnerCount, 2);
  assert.equal(presentation.anchoredWinnerCount, 1);
  assert.equal(presentation.anchoredMethodCount, 1);
  assert.match(
    presentation.byStableSelector.get(
      "HiddenWinner~1234567890",
    )?.description ?? "",
    /Top Leverage; 3 direct callers; 4 roots/,
  );
  assert.equal(
    presentation.byStableSelector.has("PublicRunnerUp~0987654321"),
    false,
  );
});

test("method leverage counts tied accessors by winning method token", () => {
  const presentation = projectTypeMethodLeverage(
    available([{
      typeDefinitionId: request.typeDefinitionId,
      stableSelector: "Value~1234567890",
      methodTokens: [0x06000003, 0x06000004],
    }]),
    request,
  );

  assert.equal(presentation.anchoredWinnerCount, 1);
  assert.equal(presentation.anchoredMethodCount, 2);
  assert.equal(
    methodLeverageEmptyStateMessage({
      status: "ready",
      request,
      isCurrent: () => true,
      presentation,
    }),
    "No Top Leverage member matches the current filters.",
  );
});

test("method leverage explains a valid zero-winner result", () => {
  const result = available([]);
  if (!result.content) throw new Error("Expected available content.");
  const presentation = projectTypeMethodLeverage({
    ...result,
    content: {
      ...result.content,
      winnerCount: 0,
      winningRank: null,
    },
  }, request);

  assert.equal(
    methodLeverageEmptyStateMessage({
      status: "ready",
      request,
      isCurrent: () => true,
      presentation,
    }),
    "This Type has no inbound-call Top Leverage designation.",
  );
});

test("method leverage rejects duplicate winner correspondence", () => {
  assert.throws(
    () => projectTypeMethodLeverage(
      available([
        {
          typeDefinitionId: request.typeDefinitionId,
          stableSelector: "Winner~1234567890",
          methodTokens: [0x06000001],
        },
        {
          typeDefinitionId: request.typeDefinitionId,
          stableSelector: "Winner~1234567890",
          methodTokens: [0x06000002],
        },
      ]),
      request,
    ),
    /winner identity is inconsistent/,
  );
});

test("method leverage coordinator caches one exact Type result", async () => {
  const state: TypeMethodLeverageStateHost = {
    typeMethodLeverage: { status: "idle" },
  };
  let queries = 0;
  const coordinator = createTypeMethodLeverageCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage(),
    query: async () => {
      queries++;
      return available();
    },
    whenWorkerIdle: () => Promise.resolve(),
    describeError: String,
    reportOperationDiagnostic: () => undefined,
    render: () => undefined,
  });

  coordinator.request(request, () => true);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(state.typeMethodLeverage.status, "ready");
  assert.ok(methodLeverageFor(
    state.typeMethodLeverage,
    "HiddenWinner~1234567890",
  ));

  coordinator.request(request, () => true);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(queries, 1);
  assert.equal(
    typeMethodLeverageCacheKey(request),
    typeMethodLeverageCacheKey({ ...request }),
  );
});
