import type { BrowserLibraryMetrics } from "../src/facades/inspect-web-analysis.d.ts";
import {
  bindLibraryMetricsInteractions,
  renderLibraryMetricsSurface,
} from "../src/library-metrics.ts";
import { projectTypeLeverage } from "../src/type-leverage.ts";

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
const salience = projectTypeLeverage({
  schemaVersion: 1,
  outcome: "available",
  methodologyVersion: "structural-salience.v2",
  evidenceMode: "signature",
  disposition: "complete",
  coverage: { considered: 5, examined: 5, unavailable: 0, limited: 0 },
  namespaces: [{
    namespace: "Example",
    typeCount: 5,
    externalIncomingSourceTypeCount: 7,
    topLeverage: true,
  }, {
    namespace: "Example.Tools",
    typeCount: 2,
    externalIncomingSourceTypeCount: 2,
    topLeverage: false,
  }],
  diagnostics: [],
  failure: null,
  compileLibrary: data.compileLibrary,
}, [{
  schemaVersion: 2,
  outcome: "available",
  methodologyVersion: "structural-salience.v2",
  evidenceMode: "signature",
  namespace: "Example",
  disposition: "complete",
  coverage: { considered: 5, examined: 5, unavailable: 0, limited: 0 },
  types: [{
    typeDefinitionId: "Example.A",
    typeDisplay: "Example.A",
    designationEligible: true,
    signatureIncomingDegree: 7,
    signatureOutgoingDegree: 1,
    role: "foundation",
    pole: "SeaLevel",
  }, {
    typeDefinitionId: "Example.B",
    typeDisplay: "Example.B",
    designationEligible: true,
    signatureIncomingDegree: 2,
    signatureOutgoingDegree: 6,
    role: "orchestrator",
    pole: "MountainPeak",
  }],
  seaLevelOrder: ["Example.A", "Example.B"],
  mountainPeakOrder: ["Example.B", "Example.A"],
  diagnostics: [],
  failure: null,
  compileLibrary: data.compileLibrary,
}]);
const zeroTopSalience = projectTypeLeverage({
  schemaVersion: 1,
  outcome: "available",
  methodologyVersion: "structural-salience.v2",
  evidenceMode: "signature",
  disposition: "complete",
  coverage: { considered: 4, examined: 4, unavailable: 0, limited: 0 },
  namespaces: [{
    namespace: "Only",
    typeCount: 4,
    externalIncomingSourceTypeCount: 0,
    topLeverage: false,
  }],
  diagnostics: [],
  failure: null,
  compileLibrary: data.compileLibrary,
}, []);
const renderedSalience = new URLSearchParams(location.search).has("zero-top")
  ? zeroTopSalience
  : salience;

const app = document.querySelector("#app");
if (!(app instanceof HTMLElement))
  throw new Error("Library metrics harness root is missing.");

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
  salienceLoading: false,
  salienceError: "",
  salience: renderedSalience,
  selectedSalienceNamespace: null,
  escapeHtml: value => String(value).replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
});

const activation = document.createElement("output");
activation.id = "metrics-activated-type";
app.append(activation);
const namespaceSelection = document.createElement("output");
namespaceSelection.id = "metrics-selected-namespace";
app.append(namespaceSelection);
bindLibraryMetricsInteractions(app, {
  activateType: typeKey => {
    activation.value = typeKey;
  },
  selectSalienceNamespace: exactNamespace => {
    namespaceSelection.value = exactNamespace;
  },
  retrySalience: () => undefined,
});
