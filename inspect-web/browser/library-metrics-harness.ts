import type { BrowserLibraryMetrics } from "../src/facades/inspect-web-analysis.d.ts";
import { renderLibraryMetricsSurface } from "../src/library-metrics.ts";

const typeNames = ["A", "B", "C", "D", "E"];
const entangledRelationships = typeNames.flatMap(source =>
  typeNames
    .filter(target => target !== source)
    .map(target => ({
      sourceTypeKey: `Example.${source}`,
      sourceTypeDisplay: `Example.${source}`,
      targetTypeKey: `Example.${target}`,
      targetTypeDisplay: `Example.${target}`,
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
  typeSummaries: [{
    typeKey: "Example.A",
    typeDisplay: "Example.A",
    namespace: "Example",
    name: "A",
    bodyCount: 1,
    instructionCount: 1,
    complexityTotal: 1,
    loopCount: 0,
    directCallCount: 4,
    allocationCount: 0,
  }],
  entangledRelationships,
  diagnostics: [],
  failure: null,
  compileLibrary: {
    status: "Selected",
    targetFramework: "net11.0",
    message: null,
  },
};

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
  escapeHtml: value => String(value).replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#39;"),
});
