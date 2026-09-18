import type {
  BrowserCallGraphTarget,
  BrowserAnnotatedSourceViewerCatalog,
} from "../src/facades/inspect-web-source.d.ts";
import type {
  AnnotatedSourceDocument,
} from "../src/document-model.ts";

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

const calleeText = "Span<int> values = stackalloc int[1];";
const stackallocText = "stackalloc int[1]";

export const sampleCalleeDocument = {
  text: calleeText,
  nodes: [{
    id: 0,
    kind: "StackAllocationExpression",
    medium: "CSharp",
    spans: [{
      start: calleeText.indexOf(stackallocText),
      length: stackallocText.length,
    }],
    provenance: {
      il_offsets: [2],
    },
  }],
  regions: [],
  facts: [],
  targets: [],
} as const satisfies AnnotatedSourceDocument;

export const sampleCalleeEvidence = {
  factId: 0,
  instanceKey: 41,
  member: "Example.Targets.Target(int)",
  target: {
    ...sampleInvocationTarget,
    kind: "method",
  },
  state: "Instruction",
  aggregateInputs: [],
  coordinates: [{
    ilOffset: 2,
    kind: "Localloc",
  }],
  documentId: 0,
  nodeIds: [0],
  unavailableReason: null,
} as const;

export const sampleCalleeEvidenceDocuments = [{
  id: 0,
  document: sampleCalleeDocument,
}] as const;

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
