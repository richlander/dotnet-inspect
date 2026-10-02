import assert from "node:assert/strict";
import { test } from "node:test";
import { isLibraryLens, libraryLenses } from "../src/data.ts";
import {
  renderAnalysisInspector,
  type AnalysisMode,
} from "../src/analysis-inspector.ts";

test("Library exposes one ordered Analysis inspector", () => {
  assert.deepEqual(
    libraryLenses.map(([id]) => id),
    ["overview", "references", "compare", "analysis", "metadata"],
  );
  assert.equal(isLibraryLens("integrations"), false);
  assert.equal(isLibraryLens("opportunities"), false);
  assert.equal(isLibraryLens("metrics"), false);
});

for (const mode of [
  "performance",
  "integrations",
  "opportunities",
  "metrics",
] as const satisfies readonly AnalysisMode[]) {
  test(`${mode} has one stable Analysis frame with an accessible mode panel`, () => {
    const html = renderAnalysisInspector({
      assemblyIdentity: "Microsoft.Extensions.AI, Version=10.0.0.0",
      assetPath: "lib/net10.0/Microsoft.Extensions.AI.dll",
      coordinate: "Microsoft.Extensions.AI@10.0.0",
      pickerHtml: "",
      escapeHtml: value => String(value),
    }, mode, "Loading", "<p>Pending scan</p>");
    assert.equal(html.match(/<h1\b/g)?.length, 1);
    assert.match(html, /<h1 id="library-analysis-title">Analysis<\/h1>/);
    const header = html.match(/<header\b[^>]*>[\s\S]*?<\/header>/)?.[0] ?? "";
    assert.equal(header.match(/role="tab"/g)?.length, 4);
    assert.match(header, /<p title="Loading">Loading<\/p>[\s\S]*role="tablist"/);
    assert.equal(html.match(/role="tab"/g)?.length, 4);
    assert.equal(html.match(/aria-selected="true"/g)?.length, 1);
    assert.match(html, new RegExp(`data-analysis-mode="${mode}" aria-selected="true"`));
    assert.match(html, new RegExp(`role="tabpanel" aria-labelledby="analysis-mode-${mode}"`));
    assert.match(html, /Pending scan/);
    assert.match(html, /Microsoft.Extensions.AI@10.0.0/);
  });
}
