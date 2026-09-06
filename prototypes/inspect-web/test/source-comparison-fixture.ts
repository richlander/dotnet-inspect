import type {
  BrowserSourceComparison,
  BrowserSourceComparisonEndpoint,
  BrowserSourceComparisonRequest,
  BrowserSourceComparisonResult,
} from "../src/facades/inspect-web-source.d.ts";
import type { SourceComparisonContext } from "../src/source-comparison.ts";

export const sourceContext: SourceComparisonContext = {
  packageId: "Example.Package", version: "1.2.3", framework: "net10.0",
  assembly: "Example.Package", typeIdentity: "Example.Widget", memberName: "Build",
  selectorKey: "method:Build()", metadataToken: 0x06000001, label: "int Build()",
};

export const sourceRequest: BrowserSourceComparisonRequest = {
  packageId: sourceContext.packageId,
  beforeVersion: sourceContext.version,
  afterVersion: "2.0.0",
  framework: sourceContext.framework,
  assembly: sourceContext.assembly,
  typeIdentity: sourceContext.typeIdentity,
  memberName: sourceContext.memberName,
  selectorKey: sourceContext.selectorKey,
  metadataToken: sourceContext.metadataToken,
};

export function sourceEndpoint(
  overrides: Partial<BrowserSourceComparisonEndpoint> = {},
): BrowserSourceComparisonEndpoint {
  return {
    packageId: sourceRequest.packageId, version: sourceRequest.beforeVersion,
    framework: sourceRequest.framework, assembly: sourceRequest.assembly,
    assetPath: "lib/net10.0/Example.Package.dll", moduleVersionId: "before-mvid",
    assemblyIdentity: "Example.Package, Version=1.2.3.0",
    memberIdentity: "M:Example.Widget.Build", metadataToken: 0x06000001,
    state: "Available", detail: null, text: "int Build() => 1 + 2;",
    sourceUrl: "https://example.org/Widget.cs",
    repositoryUrl: "https://example.org/repo", revision: "before-revision",
    ...overrides,
  };
}

export function sourceComparison(
  overrides: Partial<BrowserSourceComparison> = {},
): BrowserSourceComparison {
  return {
    request: sourceRequest, status: "Compared", isExact: false,
    before: sourceEndpoint(),
    after: sourceEndpoint({
      version: sourceRequest.afterVersion, metadataToken: 0x06000019,
      moduleVersionId: "after-mvid", text: "int Build() => 3;",
      revision: "after-revision",
    }),
    lines: [{
      kind: "Changed", difference: "None",
      beforeLine: 1, beforeText: "int Build() => 1 + 2;",
      afterLine: 1, afterText: "int Build() => 3;",
    }],
    failure: null,
    ...overrides,
  };
}

export function sourceResult(
  value: BrowserSourceComparison | null = sourceComparison(),
  overrides: Partial<BrowserSourceComparisonResult> = {},
): BrowserSourceComparisonResult {
  return {
    version: 1, kind: "Succeeded", value,
    failureKind: null, error: null, diagnostic: null, reason: null,
    ...overrides,
  };
}
