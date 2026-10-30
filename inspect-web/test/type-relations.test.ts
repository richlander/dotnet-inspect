import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createTypeRelationsCoordinator,
  renderTypeAnalysisPage,
  renderTypeRelations,
  renderTypeRelationsSummary,
  type TypeRelationsRequest,
  type TypeRelationsState,
} from "../src/type-relations.ts";
import { metadataInertStringFixture } from "./inert-string-fixture.ts";

function state(): TypeRelationsState {
  return {
    signature: "",
    loading: false,
    result: null,
    error: "",
    generation: 0,
    selectedTargetTypeKey: null,
  };
}

function available(
  relationLimited = 0,
  colorLimited = 0,
  hasRelations = true,
): BrowserAnnotatedTypeRelationsInspection {
  return {
    outcome: "Available",
    reason: null,
    inspection: {
      content: {
        terminal: {
          kind: "Rows",
          value: hasRelations ? 2 : 0,
          is_complete: relationLimited === 0,
        },
        subjects: [
          {
            id: 1,
            subject: {
              member: {
                member: {
                  declaring_type: {
                    assembly: "Example",
                    kind: "Class",
                    namespace: "Example",
                    name: "Entry",
                    resolution: {
                      origin: {
                        kind: "CurrentAssembly",
                        assembly: {
                          name: "Example",
                        },
                      },
                      type: {
                        namespace: "Example",
                        segments: ["Entry"],
                      },
                    },
                  },
                  name: "Start",
                  parameter_types: [],
                },
              },
            },
          },
          {
            id: 2,
            subject: {
              member: {
                member: {
                  declaring_type: {
                    assembly: "Example.Dependency",
                    kind: "Class",
                    namespace: "Example",
                    name: "Target",
                    resolution: {
                      origin: {
                        kind: "AssemblyReference",
                        assembly: {
                          name: "Example.Dependency",
                        },
                      },
                      type: {
                        namespace: "Example",
                        segments: ["Target"],
                      },
                    },
                  },
                  name: "Run",
                  parameter_types: [],
                },
              },
            },
          },
          {
            id: 3,
            subject: {
              member: {
                member: {
                  declaring_type: {
                    assembly: "Example.Dependency",
                    kind: "Class",
                    namespace: "Example",
                    name: "Target",
                    resolution: {
                      origin: {
                        kind: "AssemblyReference",
                        assembly: {
                          name: "Example.Dependency",
                        },
                      },
                      type: {
                        namespace: "Example",
                        segments: ["Target"],
                      },
                    },
                  },
                  name: "Stop",
                  parameter_types: [],
                },
              },
            },
          },
        ],
        relations: hasRelations
          ? [
              {
                id: 4,
                source_subject_id: 1,
                target_subject_id: 2,
              },
              {
                id: 5,
                source_subject_id: 1,
                target_subject_id: 3,
              },
            ]
          : [],
        occurrences: hasRelations
          ? [
              {
                relation_id: 4,
                evidence: {
                  kind: "FocusedCallSite",
                  caller_method_token: 0x06000001,
                  il_offset: 10,
                  operand_token: 0x0A000001,
                  call_kind: "Call",
                },
              },
              {
                relation_id: 4,
                evidence: {
                  kind: "FocusedCallSite",
                  caller_method_token: 0x06000001,
                  il_offset: 24,
                  operand_token: 0x0A000001,
                  call_kind: "Call",
                },
              },
              {
                relation_id: 5,
                evidence: {
                  kind: "FocusedCallSite",
                  caller_method_token: 0x06000001,
                  il_offset: 36,
                  operand_token: 0x0A000002,
                  call_kind: "Callvirt",
                },
              },
            ]
          : [],
        annotations: [{
          descriptor: { id: "member.requires-dynamic-code" },
          target: { kind: "Subject", id: 2 },
        }],
        relationship_outcomes: [{
          family: "call",
          disposition: "Produced",
          coverage: {
            considered: 3,
            examined: 3 - relationLimited,
            excluded: 0,
            unavailable: 0,
            limited: relationLimited,
          },
        }],
        color_outcomes: [{
          family: "member.requires-dynamic-code",
          disposition: "Produced",
          coverage: {
            considered: 3,
            examined: 3 - colorLimited,
            excluded: 0,
            unavailable: 0,
            limited: colorLimited,
          },
        }],
      },
      share: {
        kind: "nonProjectable",
        fullUrl: null,
        packet: null,
        path: "annotated-type-relations",
        reason: metadataInertStringFixture(
          "Fixture relations are not shareable."),
      },
      diagnostics: [],
    },
  };
}

function request(isVisible = () => true): TypeRelationsRequest {
  return {
    signature: "package/type",
    packageId: "Example",
    version: "1.0.0",
    framework: "net11.0",
    runtimeIdentifier: null,
    assembly: "Example.dll",
    typeDefinitionId: "Example.Entry",
    workspaceJson: "[]",
    isCurrent: () => true,
    isVisible,
  };
}

test("ordinary rendering offers explicit inspection without querying", () => {
  let queries = 0;
  const current = state();
  createTypeRelationsCoordinator({
    state: current,
    query: async () => {
      queries++;
      return available();
    },
    describeError: String,
    render() {},
  });

  const html = renderTypeRelations({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });
  const summary = renderTypeRelationsSummary({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });

  assert.equal(html, "");
  assert.match(summary, /data-type-analysis>Analysis/);
  assert.equal(queries, 0);
});

test("the dedicated Analysis page owns acquisition and relationship presentation", () => {
  const html = renderTypeAnalysisPage({
    signature: "package/type",
    state: state(),
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });

  assert.match(html, /class="metadata-surface type-analysis-surface"/);
  assert.match(html, /<h1 id="type-analysis-title">Analysis<\/h1>/);
  assert.match(html, /Preparing focused binary relationship evidence/);
  assert.doesNotMatch(html, /<h2[^>]*>Relationships/);
});

test("duplicate activation shares one in-flight request", async () => {
  let queries = 0;
  let complete:
    ((value: BrowserAnnotatedTypeRelationsInspection) => void) | undefined;
  const pending = new Promise<BrowserAnnotatedTypeRelationsInspection>(
    resolve => {
      complete = resolve;
    });
  const current = state();
  const coordinator = createTypeRelationsCoordinator({
    state: current,
    query: async () => {
      queries++;
      return pending;
    },
    describeError: String,
    render() {},
  });

  const first = coordinator.inspect(request());
  await coordinator.inspect(request());
  assert.equal(queries, 1);
  complete?.(available());
  await first;
  assert.equal(current.result?.outcome, "Available");
});

test("current results rerender a visible Overview", async () => {
  let lens: "analysis" | "overview" = "analysis";
  let queries = 0;
  let renders = 0;
  let complete:
    ((value: BrowserAnnotatedTypeRelationsInspection) => void) | undefined;
  const pending = new Promise<BrowserAnnotatedTypeRelationsInspection>(
    resolve => {
      complete = resolve;
    });
  const current = state();
  const coordinator = createTypeRelationsCoordinator({
    state: current,
    query: async () => {
      queries++;
      return pending;
    },
    describeError: String,
    render() {
      renders++;
    },
  });

  const inspection = coordinator.inspect(
    request(() => lens === "analysis" || lens === "overview"));
  lens = "overview";
  complete?.(available());
  await inspection;

  assert.equal(current.result?.outcome, "Available");
  assert.equal(queries, 1);
  assert.equal(renders, 2);
});

test("current results do not rerender an unrelated surface", async () => {
  let renders = 0;
  const current = state();
  const coordinator = createTypeRelationsCoordinator({
    state: current,
    query: async () => available(),
    describeError: String,
    render() {
      renders++;
    },
  });

  await coordinator.inspect(request(() => false));

  assert.equal(current.result?.outcome, "Available");
  assert.equal(renders, 1);
});

test("stale results are discarded and retry is explicit", async () => {
  let currentRequest = true;
  let queries = 0;
  const current = state();
  const coordinator = createTypeRelationsCoordinator({
    state: current,
    query: async () => {
      queries++;
      return available();
    },
    describeError: String,
    render() {},
  });
  const staleRequest = request();
  staleRequest.isCurrent = () => currentRequest;
  const stale = coordinator.inspect(staleRequest);
  currentRequest = false;
  await stale;
  assert.equal(current.result, null);

  currentRequest = true;
  await coordinator.inspect(request());
  await coordinator.inspect(request());
  assert.equal(queries, 3);
});

test("available rendering accepts omitted assembly fields and aggregates exact member pairs", () => {
  const current = state();
  current.signature = "package/type";
  current.result = available(1, 1);

  const html = renderTypeRelations({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });
  const summary = renderTypeRelationsSummary({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });

  assert.match(summary, /1\+ called type/);
  assert.match(html, /1 called type · 2 member relationships/);
  assert.match(html, /aria-label="Directed Type relationship graph"/);
  assert.match(html, /data-type-analysis-relationship/);
  assert.match(html, /Select a relationship in the graph/);
  assert.match(
    html,
    /data-type-query-id="Example\.Entry" data-type-assembly="Example">Example\.Entry<\/button>/);
  assert.match(
    html,
    /data-type-query-id="Example\.Target" data-type-assembly="Example\.Dependency">Example\.Target<\/button>/);
  assert.match(html, /2 member relationships · 3 physical sites/);
  assert.match(html, /Run\(\)/);
  assert.match(html, /2 sites/);
  assert.match(html, /IL_000A/);
  assert.match(html, /IL_0018/);
  assert.match(html, /Call · operand 0x0A000001 · method 0x06000001/);
  assert.match(html, /Stop\(\)/);
  assert.match(html, /1 site/);
  assert.match(html, /IL_0024/);
  assert.match(html, /Callvirt · operand 0x0A000002/);
  assert.match(html, /title="member\.requires-dynamic-code">RDC/);
  assert.match(html, /Partial call evidence/);
  assert.match(html, /Partial compatibility evidence/);
  assert.doesNotMatch(html, /type-relations-signals/);
});

test("complete topology keeps partial compatibility evidence visible", () => {
  const current = state();
  current.signature = "package/type";
  current.result = available(0, 1);

  const html = renderTypeRelations({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });
  const summary = renderTypeRelationsSummary({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });

  assert.match(summary, />1 called type</);
  assert.doesNotMatch(summary, /1\+ called type/);
  assert.match(html, /Partial compatibility evidence/);
  assert.match(html, /an unmarked member may still carry/);
  assert.doesNotMatch(html, /Partial call evidence/);
  assert.doesNotMatch(html, /Counts are observed lower bounds/);
});

test("partial empty rendering does not claim that no outgoing calls exist", () => {
  const current = state();
  current.signature = "package/type";
  current.result = available(1, 0, false);

  const html = renderTypeRelations({
    signature: "package/type",
    state: current,
    focusTypeName: "Example.Entry",
    escapeHtml: String,
  });

  assert.match(html, /Partial call evidence/);
  assert.match(
    html,
    /No called Types were resolved from the available evidence/);
  assert.doesNotMatch(html, /No outgoing Type edges found/);
});
