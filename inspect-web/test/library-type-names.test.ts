import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryTypeNames } from "../src/library-type-names.ts";

const escape = (value: string) => value
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");

test("Library Names retain owner grouping, kinds, and forwarder declarations", () => {
  const html = renderLibraryTypeNames({
    outcome: "Available",
    detail: null,
    namespaces: [{
      name: "System.Text.Json",
      declarationCount: 2,
      types: [{
        definitionId: "System.Text.Json.JsonElement",
        name: "JsonElement",
        declarationKind: "Definition",
        definitionKind: "Struct",
      }, {
        definitionId: "System.Text.Json.Exported`1",
        name: "Exported<T>",
        declarationKind: "Forwarder",
        definitionKind: null,
      }],
    }],
  }, escape);
  assert.match(html, /System.Text.Json/);
  assert.match(html, /2 types/);
  assert.match(html, /struct JsonElement/);
  assert.match(html, /Exported&lt;T&gt; \(forwarded\)/);
  assert.match(html, /data-library-type-name="System.Text.Json.JsonElement"/);
  assert.doesNotMatch(html, /data-library-type-name="System.Text.Json.Exported/);
  assert.doesNotMatch(html, /members|signature/i);
});

test("Library Names distinguish empty, absent, and incomplete results", () => {
  assert.match(renderLibraryTypeNames({
    outcome: "Available", detail: null, namespaces: [],
  }, escape), /No public types/);
  assert.match(renderLibraryTypeNames(null, escape), /Type names unavailable/);
  const incomplete = renderLibraryTypeNames({
    outcome: "Unavailable",
    detail: "Type Names reached the RetainedDeclarations bound (6/5).",
    namespaces: [],
  }, escape);
  assert.match(incomplete, /role="alert"/);
  assert.match(incomplete, /RetainedDeclarations/);
  assert.doesNotMatch(incomplete, /No public types/);
});
