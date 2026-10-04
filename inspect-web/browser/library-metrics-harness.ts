import type {
  BrowserLibraryDependencyStructure,
  BrowserLibraryMetrics,
} from "../src/facades/inspect-web-analysis.d.ts";
import {
  bindLibraryMetricsInteractions,
  renderLibraryMetricsSurface,
} from "../src/library-metrics.ts";

const types = [
  { key: "Example.A", display: "Example.A" },
  { key: "Example.B", display: "Example.B" },
  {
    key: "Example.C",
    display: "Example.LongRunningRequestCoordinator",
  },
  {
    key: "Example.D",
    display: "Example.DistributedOperationDispatcher",
  },
  {
    key: "Example.E",
    display: "Example.PersistentWorkspaceRelationshipIndex",
  },
];
const entangledRelationships = types.flatMap(source =>
  types
    .filter(target => target.key !== source.key)
    .map(target => ({
      sourceTypeKey: source.key,
      sourceTypeDisplay: source.display,
      targetTypeKey: target.key,
      targetTypeDisplay: target.display,
      callSiteCount: 1,
      sourceDegree: 4,
      targetDegree: 4,
    })));

const data: BrowserLibraryMetrics = {
  outcome: "available",
  methodologyVersion: "1",
  population: {
    physicalEvidenceBodyCount: 1,
    profiledPhysicalEvidenceBodyCount: 1,
    logicalOwnerCount: 1,
    completeProfileCount: 1,
    incompleteProfileCount: 0,
  },
  distributions: [],
  asyncStateMachinePresence: null,
  typeSummaries: types.slice(0, 2).map((type, index) => ({
    typeKey: type.key,
    typeDisplay: type.display,
    namespace: "Example",
    name: type.display.slice("Example.".length),
    bodyCount: index + 1,
    instructionCount: (index + 1) * 12,
    complexityTotal: (index + 1) * 3,
    loopCount: index,
    directCallCount: 4,
    allocationCount: index,
  })),
  entangledRelationships,
  diagnostics: [],
  failure: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net11.0",
    message: null,
  },
};
const dependencyData: BrowserLibraryDependencyStructure = {
  outcome: "available",
  methodologyVersion: "library-dependency-structure.v1",
  completeness: "Complete",
  population: {
      examinedCallCount: 48,
      internalCallCount: 38,
      externalCallCount: 10,
      unresolvedCallCount: 0,
      incompleteBodyCount: 0,
      typeCount: 5,
      namespaceCount: 4,
  },
  namespaces: [{
      namespace: "Example.Api",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 0,
      cycleIndex: null,
      level: 0,
    }, {
      namespace: "Example.Core",
      isGlobalNamespace: false,
      typeCount: 2,
      intraNamespaceRelationshipCount: 2,
      cycleIndex: 0,
      level: 1,
    }, {
      namespace: "Example.Workflows",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 1,
      cycleIndex: 0,
      level: 1,
    }, {
      namespace: "Example.Storage",
      isGlobalNamespace: false,
      typeCount: 1,
      intraNamespaceRelationshipCount: 0,
      cycleIndex: null,
      level: 2,
  }],
  namespaceEdges: [{
      sourceNamespace: "Example.Api",
      targetNamespace: "Example.Core",
      counts: { invocations: 6, functionReferences: 1, total: 7 },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [{
        sourceTypeKey: "Example.A",
        sourceTypeDisplay: "Example.A",
        targetTypeKey: "Example.B",
        targetTypeDisplay: "Example.B",
        counts: { invocations: 6, functionReferences: 1, total: 7 },
      }],
      remainingContributorCount: 0,
    }, {
      sourceNamespace: "Example.Core",
      targetNamespace: "Example.Storage",
      counts: { invocations: 4, functionReferences: 0, total: 4 },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [{
        sourceTypeKey: "Example.B",
        sourceTypeDisplay: "Example.B",
        targetTypeKey: "Example.E",
        targetTypeDisplay: "Example.PersistentWorkspaceRelationshipIndex",
        counts: { invocations: 4, functionReferences: 0, total: 4 },
      }],
      remainingContributorCount: 0,
    }, {
      sourceNamespace: "Example.Core",
      targetNamespace: "Example.Workflows",
      counts: { invocations: 2, functionReferences: 0, total: 2 },
      contributingTypeEdgeCount: 1,
      explainingTypeEdges: [{
        sourceTypeKey: "Example.B",
        sourceTypeDisplay: "Example.B",
        targetTypeKey: "Example.C",
        targetTypeDisplay: "Example.LongRunningRequestCoordinator",
        counts: { invocations: 2, functionReferences: 0, total: 2 },
      }],
      remainingContributorCount: 0,
  }],
  totalNamespaceEdgeCount: 3,
  cycles: [{
    namespaces: ["Example.Core", "Example.Workflows"],
  }],
  diagnostics: [],
  failure: null,
};
const appElement = document.querySelector("#app");
if (!(appElement instanceof HTMLElement))
  throw new Error("Library metrics harness root is missing.");
const app = appElement;

function render(
  dependency: BrowserLibraryDependencyStructure | null,
): void {
  app.innerHTML = renderLibraryMetricsSurface({
    libraryName: "Example",
    assemblyIdentity: "Example, Version=1.0.0.0",
    assetPath: "Example.dll",
    coordinate: "net11.0 / Example@1.0.0",
    requireLibrary: false,
    pickerHtml: "",
    fresh: true,
    loading: false,
    error: "",
    data,
    dependencyFresh: dependency !== null,
    dependencyLoading: false,
    dependencyError: "",
    dependencyData: dependency,
    escapeHtml: value => String(value).replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
  });

  const activation = document.createElement("output");
  activation.id = "metrics-activated-type";
  app.append(activation);
  bindLibraryMetricsInteractions(app, {
    activateType: typeKey => {
      activation.value = typeKey;
    },
    loadDependencyStructure: () => render(dependencyData),
  });
}

render(null);
