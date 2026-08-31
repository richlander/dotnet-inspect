import type {
  BrowserAnnotatedSourceViewerCatalog,
} from "../src/inspect-web-engine.d.ts";

export const sampleViewerCatalog = {
  defaultFindingIds: [0, 1],
  supportedMedia: ["CSharp", "Il"],
  invocationLikeNodeKinds: ["ObjectCreationExpression"],
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
  findingEvidence: {
    available: false,
    unavailableReason: "NotProjected",
  },
  destinations: {
    available: false,
    unavailableReason: "NotProjected",
  },
} as const satisfies BrowserAnnotatedSourceViewerCatalog;
