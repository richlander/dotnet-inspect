import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createTypeRelationsCoordinator,
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
                          version: "1.0.0.0",
                          public_key_token: "",
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
                          version: "1.0.0.0",
                          public_key_token: "",
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
                          version: "1.0.0.0",
                          public_key_token: "",
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
              { relation_id: 4 },
              { relation_id: 4 },
              { relation_id: 5 },
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
  assert.match(summary, /Inspect call graph/);
  assert.equal(queries, 0);
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

test("stale results are discarded and retry is explicit", async () => {
  let visible = true;
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
  const stale = coordinator.inspect(request(() => visible));
  visible = false;
  await stale;
  assert.equal(current.result, null);

  visible = true;
  await coordinator.inspect(request(() => visible));
  await coordinator.inspect(request(() => visible));
  assert.equal(queries, 3);
});

test("available rendering aggregates exact member pairs by target Type", () => {
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
  assert.match(html, /1 called type · 2 member edges/);
  assert.match(html, /<strong>2<\/strong> member edges/);
  assert.match(html, /Example\.Entry → Example\.Target/);
  assert.match(html, /Run\(\)/);
  assert.match(html, /2 sites/);
  assert.match(html, /Stop\(\)/);
  assert.match(html, /1 site/);
  assert.match(html, /title="member\.requires-dynamic-code">RDC/);
  assert.match(html, /Partial call evidence/);
  assert.doesNotMatch(html, /type-relations-signals/);
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
