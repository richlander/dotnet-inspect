import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryMetricsSurface, type LibraryMetricsOptions } from "../src/library-metrics.ts";
import type { BrowserLibraryMetrics } from "../src/facades/inspect-web-analysis.d.ts";

const data: BrowserLibraryMetrics = {
  outcome: "available",
  methodologyVersion: "1",
  population: {
    physicalEvidenceBodyCount: 2,
    profiledPhysicalEvidenceBodyCount: 1,
    logicalOwnerCount: 1,
    completeProfileCount: 1,
    incompleteProfileCount: 0,
  },
  distributions: [{
    metric: "cyclomaticComplexity",
    completeBodyCount: 1,
    minimum: 1,
    p50: 1,
    p90: 1,
    p95: 1,
    p99: 1,
    maximum: 1,
  }],
  asyncStateMachinePresence: null,
  diagnostics: ["One method body could not be analyzed."],
  failure: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net10.0",
    message: null,
  },
};

function render(overrides: Partial<LibraryMetricsOptions> = {}) {
  return renderLibraryMetricsSurface({
    libraryName: "Example.Core",
    assemblyIdentity: "Example.Core, Version=1.0.0.0",
    assetPath: "lib/net10.0/Example.Core.dll",
    coordinate: "net10.0 / Example.Package@1.0.0",
    requireLibrary: false,
    pickerHtml: "",
    fresh: true,
    loading: false,
    error: "",
    data,
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  });
}

test("partial physical coverage is visibly qualified and diagnostics are escaped", () => {
  const html = render();
  assert.match(html, /Metrics are qualified/);
  assert.match(html, /1 of 2 physical bodies were profiled/);
  assert.match(html, /One method body could not be analyzed\./);
  assert.match(html, /1 complete bodies/);
});

test("fully profiled complete results do not render a qualification warning", () => {
  const html = render({
    data: {
      ...data,
      population: {
        ...data.population!,
        physicalEvidenceBodyCount: 1,
      },
      diagnostics: [],
    },
  });
  assert.doesNotMatch(html, /Metrics are qualified|metadata-warning/);
});
