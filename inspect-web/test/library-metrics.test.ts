import assert from "node:assert/strict";
import test from "node:test";
import { renderLibraryMetricsSurface, type LibraryMetricsOptions } from "../src/library-metrics.ts";
import type { BrowserLibraryMetrics } from "../src/facades/inspect-web-analysis.d.ts";
import { projectTypeLeverage } from "../src/type-leverage.ts";

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
  typeSummaries: [{
    typeKey: "Example.Core.Engine",
    typeDisplay: "Example.Core.Engine",
    namespace: "Example.Core",
    name: "Engine",
    bodyCount: 1,
    instructionCount: 12,
    complexityTotal: 3,
    loopCount: 1,
    directCallCount: 2,
    allocationCount: 1,
  }],
  entangledRelationships: [{
    sourceTypeKey: "Example.Core.Engine",
    sourceTypeDisplay: "Example.Core.Engine",
    targetTypeKey: "Example.Core.Store",
    targetTypeDisplay: "Example.Core.Store",
    callSiteCount: 4,
    sourceDegree: 2,
    targetDegree: 1,
  }],
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
    salienceLoading: true,
    salienceError: "",
    salience: null,
    selectedSalienceNamespace: null,
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

test("renders the complexity and relationship visual evidence", () => {
  const html = render();
  assert.match(html, /Complexity Explorer/);
  assert.match(html, /Example\.Core\.Engine/);
  assert.match(html, /Relationship Crossing/);
  assert.match(html, /Example\.Core\.Store/);
});

test("renders owner-issued structural salience orders and qualification", () => {
  const salience = projectTypeLeverage({
    schemaVersion: 1,
    outcome: "available",
    methodologyVersion: "structural-salience.v2",
    evidenceMode: "signature",
    namespaceIndex: {
      disposition: "Qualified",
      coverage: { considered: 5, examined: 4, unavailable: 1, limited: 0 },
      namespaces: [{
        namespace: "Example.Core",
        typeCount: 2,
        externalIncomingSourceTypeCount: 6,
        topLeverage: true,
      }],
      diagnostics: ["One signature was unavailable."],
    },
    typeLeverageShards: [{
      namespace: "Example.Core",
      disposition: "complete",
      coverage: { considered: 2, examined: 2, unavailable: 0, limited: 0 },
      types: [{
        typeDefinitionId: "Example.Core.Engine",
        typeDisplay: "Example.Core.Engine",
        designationEligible: true,
        signatureIncomingDegree: 6,
        signatureOutgoingDegree: 2,
        role: "foundation",
        pole: "SeaLevel",
      }, {
        typeDefinitionId: "Example.Core.Store",
        typeDisplay: "Example.Core.Store",
        designationEligible: true,
        signatureIncomingDegree: 1,
        signatureOutgoingDegree: 5,
        role: "orchestrator",
        pole: "MountainPeak",
      }],
      seaLevelOrder: ["Example.Core.Engine", "Example.Core.Store"],
      mountainPeakOrder: ["Example.Core.Store", "Example.Core.Engine"],
      diagnostics: [],
    }],
    failure: null,
    compileLibrary: data.compileLibrary,
  });
  const html = render({
    salienceLoading: false,
    salience,
  });

  assert.match(html, /Structural Salience/);
  assert.match(html, /Example\.Core · 6 external source Types · top leverage/);
  assert.match(html, /Incoming peers/);
  assert.match(html, /Engine/);
  assert.match(html, /6 incoming peers · foundation/);
  assert.match(html, /6 incoming peers · foundation · sea level/);
  assert.match(html, /Outgoing peers/);
  assert.match(html, /5 outgoing peers · orchestrator/);
  assert.match(html, /5 outgoing peers · orchestrator · mountain peak/);
  assert.match(html, /metrics-salience-type sea-level/);
  assert.match(html, /metrics-salience-type mountain-peak/);
  assert.match(html, /item-achievement-glyph sea-level/);
  assert.match(html, /item-achievement-glyph mountain-peak/);
  assert.match(html, /Structural salience is qualified/);
  assert.match(html, /One signature was unavailable\./);
  assert.match(
    html,
    /data-metrics-salience-type-key="Example\.Core\.Engine"/,
  );
});

test("structural salience remains available when body metrics fail", () => {
  const html = render({
    error: "Body metrics failed.",
    data: null,
    salienceLoading: true,
  });

  assert.match(html, /Structural Salience/);
  assert.match(html, /Body metrics failed\./);
});

test("a sole zero-leverage namespace remains explicitly selectable", () => {
  const zeroLeverageIndex = {
    schemaVersion: 1,
    outcome: "available",
    methodologyVersion: "structural-salience.v2",
    evidenceMode: "signature",
    namespaceIndex: {
      disposition: "complete",
      coverage: { considered: 4, examined: 4, unavailable: 0, limited: 0 },
      namespaces: [{
        namespace: "Only",
        typeCount: 4,
        externalIncomingSourceTypeCount: 0,
        topLeverage: false,
      }],
      diagnostics: [],
    },
    typeLeverageShards: [{
      namespace: "Only",
      disposition: "complete",
      coverage: { considered: 4, examined: 4, unavailable: 0, limited: 0 },
      types: [],
      seaLevelOrder: [],
      mountainPeakOrder: [],
      diagnostics: [],
    }],
    failure: null,
    compileLibrary: data.compileLibrary,
  } as const;
  const salience = projectTypeLeverage(zeroLeverageIndex);
  const html = render({
    salienceLoading: false,
    salience,
  });

  assert.match(
    html,
    /<option value="Only" selected>/,
  );
  assert.equal(
    [...html.matchAll(/0 owner-issued rows/g)].length,
    2,
  );
  assert.match(html, /1 exact namespace analyzed/);

  const globalHtml = render({
    salienceLoading: false,
    salience: projectTypeLeverage({
      ...zeroLeverageIndex,
      namespaceIndex: {
        ...zeroLeverageIndex.namespaceIndex,
        namespaces: [{
          ...zeroLeverageIndex.namespaceIndex.namespaces[0],
          namespace: "",
        }],
      },
      typeLeverageShards: [{
        ...zeroLeverageIndex.typeLeverageShards[0],
        namespace: "",
      }],
    }),
  });
  assert.match(
    globalHtml,
    /<option value="" selected>\(global namespace\)/,
  );
});

test("keeps detailed distributions out of the website presentation", () => {
  const html = render();

  assert.doesNotMatch(html, /Detailed distributions|metrics-table/);
  assert.match(html, /Compiled IL metrics for <strong>Example\.Core<\/strong>/);
});

test("treemap cells expose exact type activation and evidence semantics", () => {
  const html = render();

  assert.match(
    html,
    /data-metrics-type-key="Example\.Core\.Engine" tabindex="0" role="button"/,
  );
  assert.match(
    html,
    /data-metrics-evidence="Example\.Core\.Engine · 1 body · 12 instructions · average complexity 3\.0"/,
  );
  assert.match(html, /data-metrics-treemap-evidence/);
});

test("treemap omits relationship-only zero-body summaries", () => {
  const html = render({
    data: {
      ...data,
      typeSummaries: [
        ...data.typeSummaries,
        {
          ...data.typeSummaries[0]!,
          typeKey: "Example.Core.Contract",
          typeDisplay: "Example.Core.Contract",
          name: "Contract",
          bodyCount: 0,
          instructionCount: 0,
          complexityTotal: 0,
          loopCount: 0,
          directCallCount: 0,
          allocationCount: 0,
        },
      ],
    },
  });

  assert.doesNotMatch(
    html,
    /data-metrics-type-key="Example\.Core\.Contract"/,
  );
  assert.match(html, />1 types · Scroll the page/);
});

test("the grouped Other types cell is evidence-only", () => {
  const typeSummaries = Array.from({ length: 74 }, (_, index) => ({
    ...data.typeSummaries[0]!,
    typeKey: `Example.Core.Type${index}`,
    typeDisplay: `Example.Core.Type${index}`,
    name: `Type${index}`,
    instructionCount: 74 - index,
  }));
  const html = render({ data: { ...data, typeSummaries } });
  const aggregate = html.match(
    /<g class="metrics-treemap-cell"[^>]*data-metrics-evidence="Other types[^"]*"[^>]*>/,
  );

  assert.ok(aggregate);
  assert.doesNotMatch(aggregate[0], /data-metrics-type-key|tabindex="0"/);
  assert.match(aggregate[0], /role="img"/);
});

test("treemap rectangle area remains proportional to instruction volume", () => {
  const html = render({
    data: {
      ...data,
      typeSummaries: [
        {
          ...data.typeSummaries[0]!,
          typeKey: "Example.Core.Large",
          typeDisplay: "Example.Core.Large",
          name: "Large",
          instructionCount: 200_000,
        },
        {
          ...data.typeSummaries[0]!,
          typeKey: "Example.Core.Small",
          typeDisplay: "Example.Core.Small",
          name: "Small",
          instructionCount: 1,
        },
      ],
    },
  });
  const rectangles = [...html.matchAll(
    /<rect x="[^"]+" y="[^"]+" width="([^"]+)" height="([^"]+)"/g,
  )].map(match => Number(match[1]) * Number(match[2]));

  assert.equal(rectangles.length, 2);
  assert.ok(rectangles[1]! > 0);
  const totalArea = rectangles.reduce((sum, area) => sum + area, 0);
  assert.ok(
    Math.abs(rectangles[0]! / totalArea - 200_000 / 200_001) < .000001,
  );
  assert.ok(
    Math.abs(rectangles[1]! / totalArea - 1 / 200_001) < .000001,
  );
});

test("relationship topology keeps same-display generic arities distinct", () => {
  const html = render({
    data: {
      ...data,
      entangledRelationships: [{
        sourceTypeKey: "Target.Box`1",
        sourceTypeDisplay: "Target.Box",
        targetTypeKey: "Target.Box`2",
        targetTypeDisplay: "Target.Box",
        callSiteCount: 3,
        sourceDegree: 1,
        targetDegree: 1,
      }],
    },
  });
  const edge = html.match(
    /<path class="metrics-relationship-edge" d="M ([\d.]+) \d+ C [^"]+, ([\d.]+) \d+"/,
  );

  assert.ok(edge);
  assert.notEqual(edge[1], edge[2]);
  assert.match(html, /2 most connected types/);
});

test("reciprocal relationships use distinct geometry independent of insertion lanes", () => {
  const relationships = [
    ["A", "B"],
    ["A", "C"],
    ["A", "D"],
    ["A", "E"],
    ["B", "A"],
  ].map(([source, target]) => ({
    sourceTypeKey: `Example.${source}`,
    sourceTypeDisplay: `Example.${source}`,
    targetTypeKey: `Example.${target}`,
    targetTypeDisplay: `Example.${target}`,
    callSiteCount: 1,
    sourceDegree: 4,
    targetDegree: 4,
  }));
  const html = render({
    data: {
      ...data,
      entangledRelationships: relationships,
    },
  });
  const paths = new Map(
    [...html.matchAll(
      /<path class="metrics-relationship-edge" d="([^"]+)"[^>]*><title>([^<]+)<\/title><\/path>/g,
    )].map(match => [match[2]!, match[1]!]),
  );

  assert.notEqual(
    paths.get("Example.A calls Example.B at 1 retained site"),
    paths.get("Example.B calls Example.A at 1 retained site"),
  );
});

test("relationship topology renders the complete Research projection", () => {
  const relationships = Array.from({ length: 16 }, (_, index) => ({
    sourceTypeKey: "Example.Hub",
    sourceTypeDisplay: "Example.Hub",
    targetTypeKey: `Example.Leaf${index}`,
    targetTypeDisplay: `Example.Leaf${index}`,
    callSiteCount: index + 1,
    sourceDegree: 16,
    targetDegree: 1,
  }));
  const html = render({
    data: {
      ...data,
      entangledRelationships: relationships,
    },
  });

  assert.equal(
    html.match(/class="metrics-relationship-edge"/g)?.length,
    relationships.length,
  );
  assert.match(html, /17 most connected types · 16 retained relationships/);
});
