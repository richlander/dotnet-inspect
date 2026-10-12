import type {
  BrowserPackagePerformanceSummary,
  BrowserPerformanceMember,
} from "../src/facades/inspect-web-analysis.d.ts";

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
    compileLibrary: {
      status: "Selected",
      targetFramework: "net8.0",
      message: null,
    },
  };
}
