import assert from "node:assert/strict";
import test from "node:test";
import {
  renderLibraryDependencyStructureSurface,
  renderLibraryMetricsSurface,
  type LibraryAnalysisOptions,
  type LibraryMetricsMode,
} from "../src/library-metrics.ts";
import type {
  BrowserLibraryDependencyStructure,
  BrowserLibraryMetrics,
} from "../src/facades/inspect-web-analysis.d.ts";

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

const dependencyData: BrowserLibraryDependencyStructure = {
    outcome: "available",
    methodologyVersion: "library-dependency-structure.v1",
    completeness: "Complete",
    population: {
      examinedCallCount: 12,
      internalCallCount: 9,
      externalCallCount: 3,
      unresolvedCallCount: 0,
      incompleteBodyCount: 0,
      typeCount: 3,
      namespaceCount: 3,
    },
    namespaces: [{
      namespace: "Example.Api",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 0,
      cycleIndex: null,
      level: 1,
    }, {
      namespace: "Example.Core",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 1,
      cycleIndex: 0,
      level: 0,
    }, {
      namespace: "Example.Storage",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 0,
      cycleIndex: 0,
      level: 0,
    }],
    namespaceEdges: [{
      sourceNamespace: "Example.Api",
      targetNamespace: "Example.Core",
      counts: {
        invocations: 3,
        functionReferences: 1,
        total: 4,
      },
      contributingTypeEdgeCount: 2,
      explainingTypeEdges: [{
        sourceTypeKey: "Example.Api.Endpoint",
        sourceTypeDisplay: "Example.Api.Endpoint",
        targetTypeKey: "Example.Core.Engine",
        targetTypeDisplay: "Example.Core.Engine",
        counts: {
          invocations: 3,
          functionReferences: 0,
          total: 3,
        },
      }],
      remainingContributorCount: 1,
    }, {
      sourceNamespace: "Example.Core",
      targetNamespace: "Example.Storage",
      counts: {
        invocations: 2,
        functionReferences: 0,
        total: 2,
      },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [],
      remainingContributorCount: 1,
    }, {
      sourceNamespace: "Example.Storage",
      targetNamespace: "Example.Core",
      counts: {
        invocations: 1,
        functionReferences: 0,
        total: 1,
      },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [],
      remainingContributorCount: 1,
    }],
    totalNamespaceEdgeCount: 3,
    cycles: [{
      namespaces: ["Example.Core", "Example.Storage"],
    }],
    diagnostics: [],
    failure: null,
};

function render(
  overrides: Partial<LibraryAnalysisOptions> = {},
  mode: LibraryMetricsMode | "dependencies" = "complexity",
) {
  const options: LibraryAnalysisOptions = {
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
    dependencyFresh: false,
    dependencyLoading: false,
    dependencyError: "",
    dependencyData: null,
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
    ...overrides,
  };
  return mode === "dependencies"
    ? renderLibraryDependencyStructureSurface(options)
    : renderLibraryMetricsSurface(options, mode);
}

function renderRelationships(
  overrides: Partial<LibraryAnalysisOptions> = {},
) {
  return render(overrides, "relationships");
}

function renderDependencies(
  overrides: Partial<LibraryAnalysisOptions> = {},
) {
  return render(overrides, "dependencies");
}

test("partial physical coverage is visibly qualified and diagnostics are escaped", () => {
  const html = render();
  assert.match(html, /Analysis is qualified/);
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
  assert.doesNotMatch(html, /Analysis is qualified|metadata-warning/);
});

test("renders complexity and relationships as dedicated views", () => {
  const complexity = render();
  assert.match(complexity, /data-analysis-mode="complexity"/);
  assert.match(complexity, /Complexity Explorer/);
  assert.match(complexity, /Example\.Core\.Engine/);
  assert.doesNotMatch(complexity, /Relationship Crossing/);

  const relationships = renderRelationships();
  assert.match(relationships, /data-analysis-mode="relationships"/);
  assert.match(relationships, /Relationship Crossing/);
  assert.match(relationships, /Example\.Core\.Store/);
  assert.doesNotMatch(relationships, /Complexity Explorer/);
  assert.doesNotMatch(relationships, /Load dependency structure/);
  assert.doesNotMatch(complexity, /Load dependency structure/);

  const dependencies = renderDependencies();
  assert.match(dependencies, /data-analysis-mode="dependencies"/);
  assert.match(dependencies, /Building dependency structure/);
  assert.doesNotMatch(dependencies, /Relationship Crossing|Complexity Explorer/);
});

test("renders analysis-issued dependency levels, cycles, and explanations", () => {
  const html = renderDependencies({
    dependencyFresh: true,
    dependencyData,
  });

  assert.match(html, /Dependency Structure/);
  assert.match(html, /Level 0/);
  assert.match(html, /Level 1/);
  assert.match(html, /cycle 1/);
  assert.match(
    html,
    /Example\.Core depends on Example\.Storage through 2 relationships/,
  );
  assert.match(
    html,
    /Example\.Storage depends on Example\.Core through 1 relationship/,
  );
  assert.match(html, /Example\.Api\.Endpoint/);
  assert.match(
    html,
    /data-dependency-type-key="Example\.Core\.Engine"/,
  );
  assert.match(html, /1 additional contributing type edge not shown/);
  const reciprocalPaths = [...html.matchAll(
    /<path class="metrics-dependency-edge" d="([^"]+)"[^>]*><title>([^<]+)<\/title><\/path>/g,
  )].filter(match =>
    match[2]?.includes("Example.Core depends on Example.Storage")
    || match[2]?.includes("Example.Storage depends on Example.Core"));
  assert.equal(reciprocalPaths.length, 2);
  for (const path of reciprocalPaths) {
    assert.doesNotMatch(path[1]!, /(?:^|[ ,])-/);
  }
});

test("discloses bounded and qualified dependency evidence", () => {
  const html = renderDependencies({
    dependencyFresh: true,
    dependencyData: {
      ...dependencyData,
      completeness: "Qualified",
      totalNamespaceEdgeCount: 8,
      diagnostics: ["One call target could not be resolved."],
      population: {
        ...dependencyData.population!,
        unresolvedCallCount: 1,
        incompleteBodyCount: 1,
      },
    },
  });

  assert.match(html, /Dependency evidence is qualified/);
  assert.match(html, /1 unresolved call and 1 incomplete body/);
  assert.match(html, /Showing 3 of 8 issued edges/);
  assert.match(html, /One call target could not be resolved\./);
});

test("keeps dependency failure visible in the dedicated inspector", () => {
  const html = renderDependencies({
    dependencyFresh: true,
    dependencyData: {
      outcome: "failed",
      methodologyVersion: null,
      completeness: null,
      population: null,
      namespaces: [],
      namespaceEdges: [],
      totalNamespaceEdgeCount: 0,
      cycles: [],
      diagnostics: [],
      failure: "Dependency selection failed.",
    },
  });

  assert.doesNotMatch(html, /Relationship Crossing/);
  assert.match(html, /Dependency structure failed/);
  assert.match(html, /Dependency selection failed\./);
});

test("shows dependency progress without a load gesture until a result arrives", () => {
  for (const html of [
    renderDependencies(),
    renderDependencies({ dependencyFresh: true, dependencyLoading: true }),
  ]) {
    assert.match(html, /Building dependency structure/);
    assert.doesNotMatch(html, /data-load-dependency-structure/);
    assert.doesNotMatch(html, /metrics-dependency-structure/);
  }

  const failed = renderDependencies({
    dependencyFresh: true,
    dependencyError: "Analysis failed.",
  });
  assert.match(failed, /Dependency structure failed/);
  assert.match(failed, /data-load-dependency-structure/);
});

test("renders only the selected dependency detail", () => {
  const html = renderDependencies({
    dependencyFresh: true,
    dependencyData,
    dependencyState: {
      includeGlobalNamespace: false,
      selectedSourceNamespace: "Example.Api",
      selectedTargetNamespace: "Example.Core",
    },
  });

  assert.match(
    html,
    /data-source-namespace="Example\.Api"[^>]*aria-pressed="true"/,
  );
  assert.equal(
    html.match(/data-dependency-edge-detail="\d+"(?! hidden)/g)?.length,
    1,
  );
  assert.match(html, /Selected dependency/);
  assert.match(html, /Example\.Api[\s\S]*Example\.Core/);
});

test("hides global namespace evidence by default without renumbering levels", () => {
  const withGlobal: BrowserLibraryDependencyStructure = {
    ...dependencyData,
    namespaces: [{
      namespace: "",
      isGlobalNamespace: true,
      typeCount: 1,
      intraNamespaceRelationshipCount: 0,
      cycleIndex: null,
      level: 0,
    }, ...dependencyData.namespaces.map(node => ({
      ...node,
      level: node.level + 1,
    }))],
    namespaceEdges: [...dependencyData.namespaceEdges, {
      sourceNamespace: "Example.Core",
      targetNamespace: "",
      counts: { invocations: 1, functionReferences: 0, total: 1 },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [],
      remainingContributorCount: 1,
    }],
    totalNamespaceEdgeCount: 4,
  };

  const hidden = renderDependencies({
    dependencyFresh: true,
    dependencyData: withGlobal,
  });
  assert.doesNotMatch(hidden, /data-target-namespace=""/);
  assert.doesNotMatch(hidden, />Level 0</);
  assert.match(
    hidden,
    /1 retained relationship and 0 issued cycles[\s\S]*hidden/,
  );
  assert.match(hidden, /Include global namespace/);

  const included = renderDependencies({
    dependencyFresh: true,
    dependencyData: withGlobal,
    dependencyState: {
      includeGlobalNamespace: true,
      selectedSourceNamespace: null,
      selectedTargetNamespace: null,
    },
  });
  assert.match(included, /data-target-namespace=""/);
  assert.match(included, />Level 0</);
  assert.match(included, /data-dependency-include-global checked/);
});

test("keeps empty and unavailable dependency outcomes distinct", () => {
  const empty = renderDependencies({
    dependencyFresh: true,
    dependencyData: {
      ...dependencyData,
      namespaces: [],
      namespaceEdges: [],
      totalNamespaceEdgeCount: 0,
      cycles: [],
    },
  });
  assert.match(empty, /No namespace dependencies found/);

  const unavailable = renderDependencies({
    dependencyFresh: true,
    dependencyData: {
      ...dependencyData,
      outcome: "unavailable",
      failure: "Call evidence is unavailable.",
    },
  });
  assert.match(unavailable, /Dependency structure unavailable/);
  assert.match(unavailable, /Call evidence is unavailable/);
});

test("keeps structural salience out of the structural analysis presentation", () => {
  const html = render();
  assert.doesNotMatch(
    html,
    /Structural Salience|metrics-salience|data-type-leverage/,
  );
});

test("keeps detailed distributions and redundant summary copy out of structural analysis", () => {
  const html = render();

  assert.doesNotMatch(html, /Detailed distributions|metrics-table/);
  assert.doesNotMatch(
    html,
    /Compiled IL metrics for|not authored-source complexity/,
  );
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
    /class="metrics-treemap-cell"[^>]*>.*?<rect x="[^"]+" y="[^"]+" width="([^"]+)" height="([^"]+)"/g,
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
  }, "relationships");
  const edge = html.match(
    /<path class="metrics-relationship-edge" d="M ([\d.]+) \d+ C [^"]+, ([\d.]+) \d+"/,
  );

  assert.ok(edge);
  assert.notEqual(edge[1], edge[2]);
  assert.match(html, /2 most connected types/);
});

test("relationship arcs expose exact selection evidence and a detail surface", () => {
  const html = render({}, "relationships");

  assert.match(
    html,
    /data-metrics-relationship data-relationship-rank="1" data-source-type-key="Example\.Core\.Engine" data-source-type-display="Example\.Core\.Engine" data-target-type-key="Example\.Core\.Store" data-target-type-display="Example\.Core\.Store" data-call-site-count="4"[^>]*tabindex="0" role="button" aria-pressed="false"/,
  );
  assert.match(html, /data-metrics-relationship-detail aria-live="polite"/);
  assert.match(html, /data-metrics-relationship-source/);
  assert.match(html, /data-metrics-relationship-target/);
  assert.match(html, /data-metrics-relationship-rank/);
  assert.match(html, /Relationship depth/);
  assert.match(html, />Rank<\/span>/);
  assert.match(html, /Select an arc for details/);
});

test("relationship disclosure defaults to half capped at 24 and expands by quartiles", () => {
  const types = Array.from({ length: 9 }, (_, index) => `Type${index}`);
  const relationships = types.flatMap(source => types
    .filter(target => target !== source)
    .map(target => ({
      sourceTypeKey: `Example.${source}`,
      sourceTypeDisplay: `Example.${source}`,
      targetTypeKey: `Example.${target}`,
      targetTypeDisplay: `Example.${target}`,
      sourceDegree: 8,
      targetDegree: 8,
    }))).map((relationship, index, all) => ({
      ...relationship,
      callSiteCount: all.length - index,
    }));
  const html = render({
    data: {
      ...data,
      entangledRelationships: relationships,
    },
  }, "relationships");
  const edgeTags = [...html.matchAll(
    /<path class="metrics-relationship-edge"[^>]*>/g,
  )].map(match => match[0]);

  assert.equal(edgeTags.length, 72);
  assert.equal(edgeTags.filter(tag => !tag.includes(" hidden")).length, 24);
  assert.match(
    html,
    /data-metrics-relationship-limit data-limit-levels="18,24,36,54,72"/,
  );
  assert.match(html, /type="range" min="0" max="4" value="1"/);
  assert.match(html, /Showing top <span[^>]*>24<\/span> of 72/);
  assert.match(html, /data-relationship-rank="72"[^>]* hidden/);
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
  }, "relationships");
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
    callSiteCount: 16 - index,
    sourceDegree: 16,
    targetDegree: 1,
  }));
  const html = render({
    data: {
      ...data,
      entangledRelationships: relationships,
    },
  }, "relationships");

  assert.equal(
    html.match(/class="metrics-relationship-edge"/g)?.length,
    relationships.length,
  );
  assert.match(
    html,
    /17 most connected types · Showing top <span[^>]*>8<\/span> of 16 retained relationships/,
  );
});
