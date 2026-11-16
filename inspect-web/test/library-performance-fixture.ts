import type {
  BrowserPackagePerformanceSummary,
  BrowserPerformanceMember,
  BrowserPerformanceTypeSurface,
  BrowserSyncCallInAsync,
} from "../src/facades/inspect-web-analysis.d.ts";

export const declaringType: BrowserPerformanceTypeSurface = {
  id: "Example.Library:Example.Library.Widget",
  definitionId: "Example.Library.Widget",
  queryId: "Example.Library.Widget",
  metadataId: "Example.Library.Widget",
  name: "Widget",
  displayName: "Widget",
  namespace: "Example.Library",
  kind: "class",
  kindFacetId: "class",
  traitFacetIds: [],
  accessibility: "public",
  accessibilityId: "public",
  assembly: "Example.Library",
  assemblyId: "Example.Library",
  assemblyName: "Example.Library",
  members: 1,
  signature: "class Example.Library.Widget",
  platformPack: null,
};

export const syncCallInAsync: BrowserSyncCallInAsync = {
  caller: "Example.Library.Widget.RunAsync()",
  callee: "Example.Library.Widget.Run()",
  alternative: "Example.Library.Widget.RunAsync()",
  pairKind: "Blocking",
};

export const member: BrowserPerformanceMember = {
  assembly: "Example.Library",
  typeId: "Example.Library.Widget",
  memberName: "Compute",
  stableSelector: "Compute()",
  bodyTokens: [100_663_297],
  opportunityCount: 2,
  inLoopCount: 1,
  shapes: ["Boxing"],
  confidence: "High",
  declaringType,
  bodyTargets: [
    {
      typeId: "Example.Library.Widget",
      memberName: "Compute",
      selectorKey: "Compute()",
      methodToken: 100_663_297,
      issueOffsets: [12],
    },
  ],
};

export function summary(): BrowserPackagePerformanceSummary {
  return {
    inspectionError: null,
    nonPublicOpportunities: 0,
    totalOpportunities: 2,
    syncCallsInAsync: [syncCallInAsync],
    syncCallsInAsyncCount: 1,
    compileLibrary: {
      status: "Selected",
      targetFramework: "net8.0",
      message: null,
    },
  };
}
