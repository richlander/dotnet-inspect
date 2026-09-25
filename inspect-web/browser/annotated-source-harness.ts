import {
  annotatedFocusSelector,
  bindAnnotatedSource,
  captureAnnotatedSourceScroll,
  renderAnnotatedSource,
  renderAnnotatedSourcePageActions,
  renderAnnotatedSourceModal,
  restoreAnnotatedSourceScroll,
} from "../src/annotated-source.ts";
import type {
  AnnotatedSourceAction,
} from "../src/annotated-source.ts";
import {
  clearAnnotations,
  closeFindingDetail,
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  dismissModalSession,
  escapeAnnotatedSource,
  hitTestAnnotatedNode,
  openModalSession,
  selectAllAnnotations,
  selectDefaultAnnotations,
  selectFinding,
  selectNode,
  toggleCoordinates,
  toggleFindingAnnotation,
  toggleMedium,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedFocusTarget,
  AnnotatedSourceResult,
  AnnotatedSourceSession,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedSourceDocument,
} from "../src/document-model.ts";
import {
  createCSharpRangeHighlighter,
} from "../src/csharp-highlighting.ts";
import {
  prismCSharp,
} from "../src/prism-csharp.ts";
import type { InertString } from "../src/facades/inspect-web-source.d.ts";
import {
  validateDocument,
} from "../src/document-model.ts";
import { sampleDocument as sampleDocumentFixture } from "../../prototypes/annotated-source-viewer/src/sample-document.js";

const fixture: unknown = sampleDocumentFixture;
validateDocument(fixture);
const sampleDocument: AnnotatedSourceDocument = fixture;

function inertString(value: string): InertString {
  // Browser fixtures model values after the generated JSON boundary.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as InertString;
}

const objectStart = sampleDocument.text.indexOf("new object()");
const calleeText = [
  "public static bool DeepEquals(JsonElement left, JsonElement right)",
  "{",
  "    Span<byte> leftBuffer = stackalloc byte[64];",
  "    Span<byte> rightBuffer = stackalloc byte[128];",
  "    return left.ValueKind == right.ValueKind;",
  "}",
].join("\n");
const stackallocText = "stackalloc byte[64]";
const secondStackallocText = "stackalloc byte[128]";
const calleeDocument: AnnotatedSourceDocument = {
  text: calleeText,
  nodes: [
    {
      id: 0,
      kind: "StackAllocationExpression",
      medium: "CSharp",
      spans: [{
        start: calleeText.indexOf(stackallocText),
        length: stackallocText.length,
      }],
      provenance: {
        il_offsets: [18],
      },
    },
    {
      id: 1,
      kind: "StackAllocationExpression",
      medium: "CSharp",
      spans: [{
        start: calleeText.indexOf(secondStackallocText),
        length: secondStackallocText.length,
      }],
      provenance: {
        il_offsets: [24],
      },
    },
  ],
  regions: [],
  facts: [],
  targets: [],
};
const calleeTarget = {
  id: "method:System.Text.Json.JsonElement.DeepEquals",
  assembly: "System.Text.Json",
  assemblyVersion: "10.0.0.0",
  assemblyCulture: null,
  assemblyPublicKeyToken: "cc7b13ffcd2ddd51",
  typeFullName: "System.Text.Json.JsonElement",
  typeMetadataId: "System.Text.Json.JsonElement",
  typeDefinitionId: "System.Text.Json.JsonElement",
  memberName: "DeepEquals",
  parameterTypes: [
    "System.Text.Json.JsonElement",
    "System.Text.Json.JsonElement",
  ],
  returnType: "System.Boolean",
  genericArity: 0,
  metadataToken: 0x06000123,
  selectorKey: "method:DeepEquals",
  kind: "method",
  platformPack: null,
  surfaceAssemblyId: "compile:ref/net10.0/System.Text.Json.dll",
} as const;
const costCalleeTarget = {
  ...calleeTarget,
  id: "method:ResearchProjectionProbe.AllocationInLoop",
  assembly: "DotnetInspector.Queries.Tests",
  assemblyVersion: "1.0.0.0",
  assemblyPublicKeyToken: null,
  typeFullName: "DotnetInspector.Queries.Tests.ResearchProjectionProbe",
  typeMetadataId: "DotnetInspector.Queries.Tests.ResearchProjectionProbe",
  typeDefinitionId: "DotnetInspector.Queries.Tests.ResearchProjectionProbe",
  memberName: "AllocationInLoop",
  parameterTypes: ["System.Int32"],
  returnType: "System.Int32",
  metadataToken: 0x06000124,
  selectorKey: "method:AllocationInLoop",
  surfaceAssemblyId:
    "compile:lib/net11.0/DotnetInspector.Queries.Tests.dll",
} as const;
const documentWithTighterGeneric: AnnotatedSourceDocument = {
  ...sampleDocument,
  nodes: [
    ...sampleDocument.nodes.map(node =>
      node.id === 1
        ? { ...node, kind: "InvocationExpression" }
        : node),
    {
      id: 4,
      kind: "IdentifierName",
      medium: "CSharp",
      spans: [{
        start: objectStart + "new ".length,
        length: "object".length,
      }],
    },
  ],
  facts: [
    ...sampleDocument.facts,
    {
      id: 3,
      descriptor: "unsafe.cast",
      category: "Unsafety",
      conditionality: "Always",
      detail: "runtime cast",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 4,
      descriptor: "semantics.dispatch",
      category: "Semantics",
      conditionality: "Always",
      detail: "virtual dispatch",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 5,
      descriptor: "lifetime.escape",
      category: "Lifetime",
      conditionality: "Always",
      detail: "object escapes",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 6,
      descriptor: "safety.callee",
      category: "Unsafety",
      conditionality: "Always",
      detail: "callee uses stack allocation",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 7,
      descriptor: "safety.callee",
      category: "Unsafety",
      conditionality: "Always",
      detail: "callee uses a second stack allocation",
      source_offset: 1,
      origin: "Body",
    },
    {
      id: 8,
      descriptor: "cost.callee",
      category: "Cost",
      conditionality: "Always",
      detail: "callee AllocationInLoop: alloc-loop",
      source_offset: 1,
      origin: "Body",
    },
  ],
  targets: [
    ...sampleDocument.targets,
    { fact_id: 3, node_id: 1 },
    { fact_id: 4, node_id: 1 },
    { fact_id: 5, node_id: 1 },
    { fact_id: 6, node_id: 1 },
    { fact_id: 7, node_id: 1 },
    { fact_id: 8, node_id: 1 },
  ],
};
const result: AnnotatedSourceResult = {
  document: documentWithTighterGeneric,
  signature: inertString("public void Sample()"),
  viewerCatalog: {
    defaultFindingIds: [0, 1],
    supportedMedia: ["CSharp", "Il"],
    invocationLikeNodeKinds: ["InvocationExpression"],
    invocationDestinations: [{
      nodeId: 1,
      target: {
        id: "n1",
        assembly: "System.Private.CoreLib",
        assemblyVersion: "11.0.0.0",
        assemblyCulture: null,
        assemblyPublicKeyToken: "7cec85d7bea7798e",
        typeFullName: "System.Object",
        typeMetadataId: "System.Object",
        typeDefinitionId: "System.Object",
        memberName: ".ctor",
        parameterTypes: [],
        returnType: "System.Void",
        genericArity: 0,
        metadataToken: 0x06000001,
        selectorKey: "method:.ctor",
        kind: "definition",
        platformPack: "Microsoft.NETCore.App.Ref",
        surfaceAssemblyId: null,
      },
    }],
    findingEvidence: {
      available: true,
      unavailableReason: null,
    },
    destinations: {
      available: true,
      unavailableReason: null,
    },
    callRelationships: {
      available: false,
      unavailableReason: "NotProjected",
    },
    callCycles: {
      available: false,
      unavailableReason: "NotProjected",
      isComplete: false,
      limits: [],
      findings: [],
    },
    synchronousCompletions: {
      available: false,
      unavailableReason: "NotProjected",
      observations: [],
    },
    awaitCompletionPaths: {
      available: false,
      unavailableReason: "NotProjected",
      observations: [],
    },
    allocationExceptionPaths: {
      available: false,
      unavailableReason: "NotProjected",
      observations: [],
    },
    localThrowPaths: {
      available: false,
      unavailableReason: "NotProjected",
      isComplete: false,
      boundaries: [],
      limits: null,
      receipt: null,
      paths: [],
    },
  },
  findingEvidenceDocuments: [{
    id: 0,
    document: calleeDocument,
  }],
  findingEvidence: [{
    factId: 6,
    instanceKey: 61,
    member: "System.Text.Json.JsonElement.DeepEquals(JsonElement, JsonElement)",
    target: calleeTarget,
    state: "Instruction",
    aggregateInputs: [],
    coordinates: [{
      ilOffset: 18,
      kind: "Localloc",
    }],
    documentId: 0,
    nodeIds: [0],
    unavailableReason: null,
  }, {
    factId: 7,
    instanceKey: 62,
    member: "System.Text.Json.JsonElement.DeepEquals(JsonElement, JsonElement)",
    target: calleeTarget,
    state: "Instruction",
    aggregateInputs: [],
    coordinates: [{
      ilOffset: 24,
      kind: "Localloc",
    }],
    documentId: 0,
    nodeIds: [1],
    unavailableReason: null,
  }, {
    factId: 8,
    instanceKey: 63,
    member:
      "DotnetInspector.Queries.Tests.ResearchProjectionProbe.AllocationInLoop(int)",
    target: costCalleeTarget,
    state: "Method",
    aggregateInputs: [{
      kind: "AllocationInLoop",
      value: null,
    }],
    coordinates: [],
    documentId: null,
    nodeIds: [],
    unavailableReason: null,
  }],
  callRelationships: [],
  provenance: inertString("browser-gate product fixture"),
  contextLimitation: null,
};
const model = createAnnotatedSourceViewerModel(result);
const appCandidate = document.querySelector<HTMLElement>("#app");
if (!appCandidate) throw new Error("Annotated Source browser harness root is missing");
const app = appCandidate;

let embedded = createEmbeddedSession(model);
let modal: AnnotatedSourceSession | null = null;

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function renderAndFocus(
  target: AnnotatedFocusTarget | string | null = null,
  surface: "embedded" | "modal" = modal ? "modal" : "embedded",
  preventScroll = false,
): void {
  const scroll = captureAnnotatedSourceScroll(app);
  app.innerHTML = `
    <main id="harness-background" class="detail-pane"
      style="height: 100%"${modal ? " inert" : ""}>
      <header class="detail-head">
        <div class="breadcrumbs">
          <span>System.Text.Json</span><b>/</b>
          <span>System.Text.Json</span><b>/</b>
          <strong>JsonSerializer</strong><b>/</b>
          <strong>GetTypeInfo</strong>
        </div>
        <div class="detail-actions annotated-page-actions">
          ${renderAnnotatedSourcePageActions(true)}
        </div>
      </header>
      <article class="detail-scroll annotated-working-surface">
        ${renderAnnotatedSource({
          result,
          session: embedded,
          escapeHtml,
          highlightCSharp: (source, tokenizationSource, excludedRanges) =>
            createCSharpRangeHighlighter(
              source,
              prismCSharp,
              escapeHtml,
              tokenizationSource,
              excludedRanges,
            ),
        })}
      </article>
    </main>
    ${modal
      ? renderAnnotatedSourceModal({
          result,
          session: modal,
          escapeHtml,
          highlightCSharp: (source, tokenizationSource, excludedRanges) =>
            createCSharpRangeHighlighter(
              source,
              prismCSharp,
              escapeHtml,
              tokenizationSource,
              excludedRanges,
            ),
        })
      : ""}`;
  bindAnnotatedSource(app, { onAction });
  restoreAnnotatedSourceScroll(app, scroll);
  if (!target) return;
  const selector = typeof target === "string"
    ? target
    : annotatedFocusSelector(target, surface);
  const element = app.querySelector<HTMLElement>(selector);
  if (preventScroll) element?.focus({ preventScroll: true });
  else element?.focus();
}

function closeModal(): void {
  if (!modal) return;
  embedded = dismissModalSession(model, modal);
  modal = null;
  renderAndFocus({ kind: "explore" }, "embedded");
}

function updateSession(next: AnnotatedSourceSession): void {
  if (modal) modal = next;
  else embedded = next;
}

function currentSession(): AnnotatedSourceSession {
  return modal ?? embedded;
}

function onAction(action: AnnotatedSourceAction): void {
  const session = currentSession();
  switch (action.kind) {
    case "copy":
      document.body.dataset.copiedSource = result.document.text;
      return;
    case "explore": {
      const opened = openModalSession(model, embedded);
      embedded = opened.embedded;
      modal = opened.modal;
      renderAndFocus(opened.focus);
      return;
    }
    case "close-modal":
      closeModal();
      return;
    case "close-detail": {
      const closed = closeFindingDetail(model, session);
      updateSession(closed.state);
      renderAndFocus(closed.focus, session.surface);
      return;
    }
    case "annotation-open":
      updateSession(selectFinding(session, action.opener));
      renderAndFocus("#annotated-detail-title", session.surface, true);
      return;
    case "inspector-open":
      updateSession(selectFinding(session, {
        kind: "inspector",
        factId: action.factId,
      }));
      renderAndFocus("#annotated-detail-title", "modal", true);
      return;
    case "annotation-set": {
      const transition = action.value === "Default"
        ? selectDefaultAnnotations(model, session)
        : action.value === "All"
          ? selectAllAnnotations(model, session)
          : clearAnnotations(session);
      updateSession(transition.state);
      renderAndFocus(transition.focus);
      return;
    }
    case "finding-toggle": {
      const transition =
        toggleFindingAnnotation(model, session, action.factId);
      updateSession(transition.state);
      renderAndFocus(transition.focus);
      return;
    }
    case "medium-toggle": {
      const transition = toggleMedium(model, session, action.medium);
      updateSession(transition.state);
      renderAndFocus(transition.focus);
      return;
    }
    case "coordinate-toggle": {
      const transition = toggleCoordinates(session);
      updateSession(transition.state);
      renderAndFocus(transition.focus);
      return;
    }
    case "destination-open":
      document.body.dataset.destination =
        `${action.destination}:${action.destinationIndex}`;
      closeModal();
      return;
    case "finding-evidence-open":
      document.body.dataset.destination =
        `${action.destination}:evidence:${action.factId}`;
      closeModal();
      return;
    case "node-select":
      updateSession(selectNode(session, action.nodeId));
      renderAndFocus({ kind: "node", nodeId: action.nodeId });
      return;
    case "source-select": {
      const node =
        hitTestAnnotatedNode(model, action.offset, action.medium);
      if (!node) return;
      updateSession(selectNode(session, node.id));
      renderAndFocus({ kind: "node", nodeId: node.id });
      return;
    }
  }
}

document.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  const session = currentSession();
  const escaped = escapeAnnotatedSource(model, session);
  if (!escaped.handled) return;
  event.preventDefault();
  if (escaped.dismissModal) {
    closeModal();
    return;
  }
  updateSession(escaped.state);
  renderAndFocus(escaped.focus, session.surface);
});

renderAndFocus();
