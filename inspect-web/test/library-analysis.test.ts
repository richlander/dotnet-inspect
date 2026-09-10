import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryAnalysisSurface } from "../src/library-analysis.ts";
import type {
  BrowserPackagePerformance,
  BrowserPerformanceMember,
} from "../src/facades/inspect-web-analysis.d.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const baseOptions = {
  libraryName: "Test.Assembly",
  assemblyIdentity: "Test.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
  assetPath: "lib/net10.0/Test.Assembly.dll",
  coordinate: "net10.0 · Test.Package@1.0.0",
  requireLibrary: false,
  pickerHtml: "",
  fresh: true,
  loading: false,
  error: "",
  data: null,
  escapeHtml,
};

function member(overrides: Partial<BrowserPerformanceMember> = {}): BrowserPerformanceMember {
  return {
    assembly: "Test.Assembly.dll",
    typeId: "Test.Namespace.Widget",
    memberName: "Run",
    stableSelector: "Run",
    bodyTokens: [0x06000001],
    opportunityCount: 3,
    inLoopCount: 1,
    shapes: ["box-value-type", "string-concat"],
    confidence: "high",
    ...overrides,
  };
}

function result(
  overrides: Partial<BrowserPackagePerformance> = {},
): BrowserPackagePerformance {
  return {
    members: [member()],
    inspectionError: null,
    nonPublicOpportunities: 2,
    totalOpportunities: 5,
    compileLibrary: {
      status: "Selected",
      targetFramework: "net10.0",
      message: null,
    },
    ...overrides,
  };
}

test("a platform package with no scoped library prompts before analysis", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    requireLibrary: true,
    pickerHtml: "<select><option>PICKER</option></select>",
    fresh: false,
  });

  assert.match(html, /library-analysis-controls/);
  assert.match(html, /Pick a library to analyze/);
  assert.match(html, /Select a library/);
});

test("fresh and stale loading states stay distinct", () => {
  const fresh = renderLibraryAnalysisSurface({ ...baseOptions, loading: true });
  const stale = renderLibraryAnalysisSurface({
    ...baseOptions,
    loading: true,
    fresh: false,
  });

  assert.match(fresh, /Analyzing allocations/);
  assert.doesNotMatch(stale, /Analyzing allocations…/);
  assert.match(stale, /Loading…/);
});

test("a fresh query error is visible and escaped", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    error: "<boom> failed",
  });

  assert.match(html, /Analysis failed/);
  assert.match(html, /&lt;boom&gt; failed/);
});

test("ranked member rows retain navigation identity and triage evidence", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result(),
  });

  assert.match(html, /1 public member/);
  assert.match(html, /5 opportunities/);
  assert.match(html, /2 non-public/);
  assert.match(html, /data-perf-selector="Run"/);
  assert.match(html, /data-perf-assembly="Test\.Assembly\.dll"/);
  assert.match(html, /data-perf-type="Test\.Namespace\.Widget"/);
  assert.match(html, /Widget\.Run/);
  assert.match(html, /box-value-type/);
  assert.match(html, /string-concat/);
  assert.match(html, /perf-loop[^>]*>&#x21BB; 1/);
  assert.match(html, /perf-high[^>]*>high/);
});

test("the full-area frame retains Library identity and package coordinates", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result(),
  });

  assert.match(html, /lib\/net10\.0\/Test\.Assembly\.dll/);
  assert.match(html, /Test\.Assembly, Version=1\.0\.0\.0/);
  assert.match(html, /net10\.0 · Test\.Package@1\.0\.0/);
});

test("a complete empty result may state there are no public hot spots", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result({ members: [], totalOpportunities: 2 }),
  });

  assert.match(html, /No public allocation hot spots/);
  assert.match(html, /2 allocation\/performance opportunities were classified/);
  assert.match(html, /2 opportunities are in non-public members/);
});

test("a partial empty result does not claim established absence", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result({
      members: [],
      inspectionError: "A method body could not be analyzed.",
    }),
  });

  assert.match(html, /Analysis incomplete/);
  assert.match(html, /partial/);
  assert.match(html, /A method body could not be analyzed/);
  assert.doesNotMatch(html, /No public allocation hot spots/);
});

test("partial rows remain available with a visible diagnostic", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result({ inspectionError: "<bad> body" }),
  });

  assert.match(html, /This library could not be analyzed completely/);
  assert.match(html, /&lt;bad&gt; body/);
  assert.match(html, /data-perf-selector="Run"/);
});

test("member names, shapes, and confidence are escaped", () => {
  const html = renderLibraryAnalysisSurface({
    ...baseOptions,
    data: result({
      members: [member({
        memberName: "<Run>",
        shapes: ["<shape>"],
        confidence: "<high>",
      })],
    }),
  });

  assert.match(html, /Widget\.&lt;Run&gt;/);
  assert.match(html, /&lt;shape&gt;/);
  assert.match(html, /&lt;high&gt;/);
  assert.doesNotMatch(html, /<Run>|<shape>|<high>/);
});
