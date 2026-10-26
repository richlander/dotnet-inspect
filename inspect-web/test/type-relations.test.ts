import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserAnnotatedTypeRelationsInspection,
} from "../src/facades/inspect-web-metadata.d.ts";
import {
  createTypeRelationsCoordinator,
  renderTypeRelations,
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
): BrowserAnnotatedTypeRelationsInspection {
  return {
    outcome: "Available",
    reason: null,
    inspection: {
      content: {
        terminal: {
          kind: "Rows",
          value: 1,
          is_complete: relationLimited === 0,
        },
        subjects: [
          {
            id: 1,
            subject: {
              member: {
                member: {
                  declaring_type: { namespace: "Example", name: "Target" },
                  name: "Run",
                },
              },
            },
          },
        ],
        relations: [{ id: 4, target_subject_id: 1 }],
        occurrences: [{ relation_id: 4 }, { relation_id: 4 }],
        annotations: [{
          descriptor: { id: "member.requires-dynamic-code" },
          target: { kind: "Subject", id: 1 },
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
    escapeHtml: String,
  });

  assert.match(html, /Inspect outgoing calls/);
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

test("available rendering exposes physical evidence, colors, and limits", () => {
  const current = state();
  current.signature = "package/type";
  current.result = available(1, 1);

  const html = renderTypeRelations({
    signature: "package/type",
    state: current,
    escapeHtml: String,
  });

  assert.match(html, /1 logical · 2 physical/);
  assert.match(html, /Example\.Target\.Run/);
  assert.match(html, /2 occurrences/);
  assert.match(html, /member\.requires-dynamic-code/);
  assert.match(html, /Call evidence is partial/);
  assert.match(html, /2 of 3 candidates examined; 0 unavailable; 1 limited/);
  assert.match(html, /Compatibility color evidence is partial/);
  assert.match(
    html,
    /member\.requires-dynamic-code<\/code>: 2 of 3 examined; 1 limited/);
});
