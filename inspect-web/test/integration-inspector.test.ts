import assert from "node:assert/strict";
import { test } from "node:test";
import { isLibraryLens, libraryLenses } from "../src/data.ts";
import { renderIntegrationInspector } from "../src/integration-inspector.ts";

test("Integrations is the only inspector for detected and suggested integrations", () => {
  assert.equal(libraryLenses.filter(([id]) => id === "integrations").length, 1);
  assert.equal(isLibraryLens("opportunities"), false);
});

for (const mode of ["integrations", "opportunities"] as const) {
  test(`${mode} has one stable Integrations frame with an accessible mode panel`, () => {
    const html = renderIntegrationInspector({
      assemblyIdentity: "Microsoft.Extensions.AI, Version=10.0.0.0",
      assetPath: "lib/net10.0/Microsoft.Extensions.AI.dll",
      coordinate: "Microsoft.Extensions.AI@10.0.0",
      pickerHtml: "",
      escapeHtml: value => String(value),
    }, mode, "Loading", "<p>Pending scan</p>");
    assert.equal(html.match(/<h1\b/g)?.length, 1);
    assert.match(html, /<h1 id="library-integrations-title">Integrations<\/h1>/);
    assert.equal(html.match(/role="tab"/g)?.length, 2);
    assert.equal(html.match(/aria-selected="true"/g)?.length, 1);
    assert.match(html, new RegExp(`data-integration-mode="${mode}" aria-selected="true"`));
    assert.match(html, new RegExp(`role="tabpanel" aria-labelledby="integration-mode-${mode}"`));
    assert.match(html, /Pending scan/);
    assert.match(html, /Microsoft.Extensions.AI@10.0.0/);
  });
}
