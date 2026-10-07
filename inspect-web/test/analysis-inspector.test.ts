import assert from "node:assert/strict";
import { test } from "node:test";
import { isLibraryLens, libraryLenses } from "../src/data.ts";
import {
  defaultAnalysisMode,
  isAnalysisMode,
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
  assert.equal(isAnalysisMode("metrics"), false);
});

test("Relationships is the first and default Analysis mode", () => {
  assert.equal(defaultAnalysisMode, "relationships");
  const html = renderAnalysisInspector({
    assemblyIdentity: "Microsoft.Extensions.AI, Version=10.0.0.0",
    assetPath: "lib/net10.0/Microsoft.Extensions.AI.dll",
    coordinate: "Microsoft.Extensions.AI@10.0.0",
    pickerHtml: "",
    escapeHtml: value => String(value),
  }, defaultAnalysisMode, "Ready", "<p>Relationships</p>");
  const modes = [...html.matchAll(/data-analysis-mode="([^"]+)"/g)]
    .map(match => match[1]);

  assert.deepEqual(
    modes,
    [
      "relationships",
      "dependencies",
      "complexity",
      "performance",
      "resource-triage",
      "integrations",
    ],
  );
});

for (const mode of [
  "complexity",
  "dependencies",
  "relationships",
  "performance",
      "resource-triage",
  "integrations",
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
    assert.equal(header.match(/role="tab"/g)?.length, 6);
    assert.match(header, /<p title="Loading">Loading<\/p>[\s\S]*role="tablist"/);
    assert.equal(html.match(/role="tab"/g)?.length, 6);
    assert.equal(html.match(/aria-selected="true"/g)?.length, 1);
    assert.match(html, new RegExp(`data-analysis-mode="${mode}" aria-selected="true"`));
    assert.match(html, new RegExp(`role="tabpanel" aria-labelledby="analysis-mode-${mode}"`));
    assert.match(html, /Pending scan/);
    assert.doesNotMatch(html, /metadata-surface-footer/);
  });
}

test("Performance and Resource Triage are direct Analysis tabs", () => {
  for (const mode of ["performance", "resource-triage"] as const) {
    const html = renderAnalysisInspector({ assemblyIdentity: "Fixture", assetPath: "Fixture.dll",
      coordinate: "Fixture", pickerHtml: "", escapeHtml: String }, mode, "Ready", "");
    assert.match(html, />Performance Triage<\/button>/);
    assert.match(html, />Resource Triage<\/button>/);
    assert.equal((html.match(/role="tablist"/g) ?? []).length, 1);
    assert.doesNotMatch(html, /data-triage-mode/);
  }
});
