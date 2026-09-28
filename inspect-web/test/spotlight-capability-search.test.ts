import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlightCapabilitySearch,
  normalizeSpotlightCapabilitySearchSnapshot,
  SPOTLIGHT_CAPABILITY_RESULT_LIMIT,
  spotlightCapabilitySearchMessage,
  visibleSpotlightCapabilityResults,
  type SpotlightCapabilitySearchState,
} from "../src/spotlight-capability-search.ts";
import type {
  BrowserCapabilityCatalogSearchInspection,
} from "../src/facades/inspect-web-package.d.ts";

function inspection(
  query: string,
): BrowserCapabilityCatalogSearchInspection {
  return {
    content: {
      query,
      similarityThreshold: 0.6,
      candidateResourceCount: 1,
      matchCount: 1,
      returnedCount: 1,
      isTruncated: false,
      results: [{
        similarity: 1,
        matchedTerm: "literal",
        matchSource: "CanonicalKey",
        isSegment: true,
        resourceIdentity: {
          kind: "QueryFacet",
          identity: "package-query.term.library-literal",
          parentIdentity: "package-query/query-space/v1",
        },
        resourceKind: "QueryFacet",
        resourceName: "library literal",
        canonicalKeys: ["library-literal"],
        resourcePath: "package-query/query/facets/library-literal",
        owningRoutes: [{
          identity: "package-query/route/default",
          name: "Package Query",
          resourcePath: "package-query/routes/default",
        }],
        productionBindings: [{
          identity: "dotnet-inspect.web/package-query",
          name: "dotnet-inspect Browser",
          consumerKind: "Browser",
          gesture: "Package Query workspace search",
          resourcePath: "package-query/bindings/browser",
        }],
      }],
    },
    share: {
      kind: "nonProjectable",
      fullUrl: null,
      packet: null,
      path: "capability-catalog-search/share",
      reason: "No canonical Browser projection is available.",
    },
    diagnostics: [],
  };
}

test("Capability search publishes only the current All-scope query", async () => {
  const state: SpotlightCapabilitySearchState = {
    spotlightQuery: "literal",
    spotlightScope: "all",
    spotlightCapabilitySearch: { status: "idle" },
  };
  const calls: Array<readonly [string, number]> = [];
  const callbacks: Array<() => Promise<void>> = [];
  let updates = 0;
  const search = createSpotlightCapabilitySearch({
    state,
    searchCapabilities: async (query, maximumResults) => {
      calls.push([query, maximumResults]);
      return inspection(query);
    },
    schedule: callback => {
      callbacks.push(callback);
      return callback;
    },
    cancelScheduled: () => {},
    updateResults: () => { updates++; },
  });

  search.schedule();
  assert.equal(state.spotlightCapabilitySearch.status, "loading");
  await callbacks[0]!();

  assert.deepEqual(calls, [["literal", SPOTLIGHT_CAPABILITY_RESULT_LIMIT]]);
  assert.equal(updates, 1);
  assert.equal(
    visibleSpotlightCapabilityResults(
      state.spotlightCapabilitySearch,
      "literal",
    )[0]?.resourcePath,
    "package-query/query/facets/library-literal",
  );
  assert.deepEqual(
    visibleSpotlightCapabilityResults(
      state.spotlightCapabilitySearch,
      "litteral",
    ),
    [],
  );

  state.spotlightScope = "packages";
  search.schedule();
  assert.equal(state.spotlightCapabilitySearch.status, "idle");
});

test("Capability search ignores superseded completion and exposes failures", async () => {
  const state: SpotlightCapabilitySearchState = {
    spotlightQuery: "literal",
    spotlightScope: "all",
    spotlightCapabilitySearch: { status: "idle" },
  };
  const callbacks: Array<() => Promise<void>> = [];
  const pending = new Map<string, {
    resolve: (value: BrowserCapabilityCatalogSearchInspection) => void;
    reject: (error: Error) => void;
  }>();
  const search = createSpotlightCapabilitySearch({
    state,
    searchCapabilities: query =>
      new Promise((resolve, reject) => {
        pending.set(query, { resolve, reject });
      }),
    schedule: callback => {
      callbacks.push(callback);
      return callback;
    },
    cancelScheduled: () => {},
    updateResults: () => {},
  });

  search.schedule();
  const literal = callbacks.shift()!();
  state.spotlightQuery = "litteral";
  search.schedule();
  const litteral = callbacks.shift()!();
  pending.get("literal")!.resolve(inspection("literal"));
  await literal;
  assert.equal(state.spotlightCapabilitySearch.status, "loading");

  pending.get("litteral")!.reject(new Error("catalog unavailable"));
  await litteral;
  assert.match(
    spotlightCapabilitySearchMessage(
      state.spotlightCapabilitySearch,
      "litteral",
    ),
    /Capability search failed: catalog unavailable/,
  );
});

test("Capability search snapshots never preserve in-flight work", () => {
  assert.deepEqual(
    normalizeSpotlightCapabilitySearchSnapshot({
      status: "loading",
      query: "literal",
    }),
    { status: "idle" },
  );
});
