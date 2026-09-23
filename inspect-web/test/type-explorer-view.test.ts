import assert from "node:assert/strict";
import test from "node:test";

import { defaultTypeExplorerIntent } from "../src/type-explorer-route.ts";
import {
  renderTypeExplorerView,
  type TypeExplorerInspection,
} from "../src/type-explorer-view.ts";

const identity = {
  stableSelector: "M:ConvertName(System.String)",
  canonicalSignature:
    "System.String System.Text.Json.JsonNamingPolicy::ConvertName(System.String)",
  fingerprint: "0123456789",
  typeFullName: "System.Text.Json.JsonNamingPolicy",
  memberName: "ConvertName",
};

const inspection: TypeExplorerInspection = {
  outcome: "Available",
  reason: null,
  bodyProjectionsAttempted: 1,
  failedBodyIds: [],
  document: {
    assemblyName: "System.Text.Json.dll",
    pdbSupplied: false,
    symbolSource: "decompiled",
    renderingPolicy: "whole-type",
    documentationCapability: "Available",
    contractRelationshipCapability: "Available",
    projectionFailure: null,
    projection: {
      revision: "a".repeat(64),
      text: "public abstract string ConvertName(string name);",
      diagnostics: [],
      declarations: [{
        declarationId: 7,
        identity,
        declarationToken: 100663297,
        kind: "Method",
        accessibility: "Public",
        placement: "Instance",
        origin: "NonGenerated",
        supportsSelectedBody: false,
        range: {
          start: 0,
          length: 48,
        },
      }],
    },
  },
  diagnostics: [],
};

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

test("Type Explorer renders owner-issued declarations and selection", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedMember: identity,
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer: JsonNamingPolicy/u);
  assert.match(html, /data-type-explorer-declaration="7"/u);
  assert.match(html, /aria-current="true"/u);
  assert.match(html, /type-explorer-source-declaration selected/u);
  assert.match(html, /ConvertName/u);
  assert.match(html, /id="type-explorer-outline-toggle"/u);
  assert.match(html, /aria-expanded="false"/u);
});

test("Type Explorer disables Selected body without owner-issued support", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedMember: identity,
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(
    html,
    /value="SelectedBody" disabled/u);
});

test("Type Explorer keeps failures visible and inert", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json",
    intent: defaultTypeExplorerIntent(),
    state: {
      status: "failed",
      error: "<projection failed>",
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer failed/u);
  assert.match(html, /&lt;projection failed&gt;/u);
  assert.doesNotMatch(html, /<projection failed>/u);
  assert.match(html, /id="type-explorer-retry"/u);
});
