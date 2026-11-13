import assert from "node:assert/strict";
import { test } from "node:test";
import {
  isLibraryLens,
  libraryLensDefinitions,
  setLibraryViewFacetCatalog,
} from "../src/data.ts";
import {
  analysisModeForScope,
  analysisModeIsListed,
  analysisModesFor,
  defaultAnalysisMode,
  defaultAnalysisModeFor,
  isAnalysisMode,
  renderAnalysisInspector,
  type AnalysisMode,
} from "../src/analysis-inspector.ts";

setLibraryViewFacetCatalog([
  { id: "library.types", kind: "Library", title: "Types", order: 10 },
  { id: "library.info", kind: "Library", title: "Info", order: 20 },
  { id: "library.references", kind: "Library", title: "References", order: 30 },
  { id: "library.ecosystem-dependencies", kind: "Library", title: "Ecosystem dependencies", order: 40 },
  { id: "library.reference-hierarchy", kind: "Library", title: "Reference hierarchy", order: 50 },
  { id: "library.integrations", kind: "Library", title: "Integrations", order: 60 },
  { id: "library.analysis", kind: "Library", title: "Analysis", order: 70 },
  { id: "library.metadata", kind: "Library", title: "Metadata", order: 80 },
  { id: "library.compare", kind: "Library", title: "Compare", order: 90 },
]);

test("Library exposes one ordered Analysis inspector", () => {
  assert.deepEqual(
    libraryLensDefinitions().map(([id]) => id),
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
      "integrations",
      "performance",
      "resource-triage",
      "unsafe",
    ],
  );
});

for (const mode of [
  "complexity",
  "dependencies",
  "relationships",
  "performance",
      "resource-triage",
  "unsafe",
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
    assert.equal(header.match(/role="tab"/g)?.length, 7);
    assert.match(header, /<p title="Loading">Loading<\/p>[\s\S]*role="tablist"/);
    assert.equal(html.match(/role="tab"/g)?.length, 7);
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

test("each Analysis scope lists its own modes and default", () => {
  const packageType = { relationshipsOffered: true };
  const platformType = { relationshipsOffered: false };
  assert.deepEqual(analysisModesFor("library").map(([mode]) => mode), [
    "relationships", "dependencies", "complexity", "integrations",
    "performance", "resource-triage", "unsafe",
  ]);
  assert.deepEqual(analysisModesFor("type", packageType), [["relationships", "Relationships"], ["unsafe", "Unsafe"]]);
  assert.deepEqual(analysisModesFor("type", platformType), [["unsafe", "Unsafe"]]);
  assert.deepEqual(analysisModesFor("member"), [["unsafe", "Unsafe"]]);
  assert.equal(defaultAnalysisModeFor("library"), "relationships");
  assert.equal(defaultAnalysisModeFor("type", packageType), "relationships");
  assert.equal(defaultAnalysisModeFor("type", platformType), "unsafe");
  assert.equal(defaultAnalysisModeFor("member"), "unsafe");
  assert.equal(analysisModeForScope("library", "performance"), "performance");
  assert.equal(analysisModeForScope("type", "unsafe", packageType), "unsafe");
  assert.equal(analysisModeForScope("type", "performance", packageType), "relationships");
  assert.equal(analysisModeForScope("type", null, platformType), "unsafe");
  // A Platform Type doesn't list Relationships, so it selects its default.
  assert.equal(analysisModeForScope("type", "relationships", platformType), "unsafe");
  assert.equal(analysisModeForScope("member", null), "unsafe");
  assert.equal(analysisModeForScope("library", "unknown"), "relationships");
  assert.equal(analysisModeIsListed("type", "relationships", platformType), false);
  assert.equal(analysisModeIsListed("member", "relationships"), false);
});

test("a package Type's Analysis has a tab per mode; a one-mode scope has no tab strip", () => {
  const packageType = renderAnalysisInspector({ escapeHtml: String }, "unsafe", "Ready", "<p>Rows</p>", "type", { relationshipsOffered: true });
  assert.match(packageType, /<h1 id="type-analysis-title">Analysis<\/h1>/);
  assert.match(packageType, /class="analysis-inspector library-analysis-surface type-analysis-surface type-unsafe-surface"/);
  assert.deepEqual([...packageType.matchAll(/data-analysis-mode="([^"]+)"/g)].map(match => match[1]), ["relationships", "unsafe"]);
  assert.match(packageType, /data-analysis-scope="type" data-analysis-mode="unsafe" aria-selected="true"/);
  assert.match(packageType, /role="tabpanel" aria-labelledby="analysis-mode-unsafe"/);
  for (const [scope, context] of [["type", { relationshipsOffered: false }], ["member", undefined]] as const) {
    const html = renderAnalysisInspector({ escapeHtml: String }, "unsafe", "Ready", "<p>Rows</p>", scope, context);
    assert.match(html, new RegExp(`<h1 id="${scope}-analysis-title">Analysis</h1>`));
    assert.match(html, new RegExp(`data-analysis-inspector="${scope}"`));
    assert.doesNotMatch(html, /role="tab"|role="tablist"/);
    assert.match(html, new RegExp(`role="region" aria-labelledby="${scope}-analysis-title"`));
    assert.match(html, /<p>Rows<\/p>/);
    assert.doesNotMatch(html, /library-analysis-controls/);
  }
});
