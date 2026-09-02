import type {
  BrowserCallGraphTarget,
  BrowserAnnotatedSourceViewerCatalog,
} from "../src/inspect-web-engine.d.ts";

export const sampleInvocationTarget = {
  id: "n1",
  assembly: "Example",
  assemblyVersion: "1.0.0.0",
  assemblyCulture: null,
  assemblyPublicKeyToken: null,
  typeFullName: "Example.Targets",
  typeMetadataId: "Example.Targets",
  typeDefinitionId: "Example.Targets",
  memberName: "Target",
  parameterTypes: ["System.Int32"],
  returnType: "System.Int32",
  genericArity: 0,
  metadataToken: 0x06000001,
  selectorKey: "method:Target",
  kind: "definition",
  platformPack: null,
  surfaceAssemblyId: "compile:ref/net11.0/Example.dll",
} as const satisfies BrowserCallGraphTarget;

export const sampleViewerCatalog = {
  defaultFindingIds: [0, 1],
  supportedMedia: ["CSharp", "Il"],
  invocationLikeNodeKinds: ["ObjectCreationExpression"],
  invocationDestinations: [],
  findingEvidence: {
    available: false,
    unavailableReason: "NotProjected",
  },
  destinations: {
    available: false,
    unavailableReason: "NotProjected",
  },
} as const satisfies BrowserAnnotatedSourceViewerCatalog;

export const csharpOnlyEmptyViewerCatalog = {
  defaultFindingIds: [],
  supportedMedia: ["CSharp"],
  invocationLikeNodeKinds: [],
  invocationDestinations: [],
  findingEvidence: {
    available: false,
    unavailableReason: "NotProjected",
  },
  destinations: {
    available: false,
    unavailableReason: "NotProjected",
  },
} as const satisfies BrowserAnnotatedSourceViewerCatalog;
