import {
  annotatedFocusSelector,
  bindAnnotatedSource,
  renderAnnotatedSource,
  renderAnnotatedSourcePageActions,
  renderAnnotatedSourceModal,
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
import {
  validateDocument,
} from "../src/document-model.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";

const fixture: unknown = sampleDocumentFixture;
validateDocument(fixture);
const sampleDocument: AnnotatedSourceDocument = fixture;
const objectStart = sampleDocument.text.indexOf("new object()");
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
};
const result: AnnotatedSourceResult = {
  document: documentWithTighterGeneric,
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
      available: false,
      unavailableReason: "NotProjected",
    },
    destinations: {
      available: true,
      unavailableReason: null,
    },
  },
  provenance: "browser-gate product fixture",
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
): void {
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
  if (!target) return;
  const selector = typeof target === "string"
    ? target
    : annotatedFocusSelector(target, surface);
  app.querySelector<HTMLElement>(selector)?.focus();
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
      renderAndFocus("#annotated-detail-title", session.surface);
      return;
    case "inspector-open":
      updateSession(selectFinding(session, {
        kind: "inspector",
        factId: action.factId,
      }));
      renderAndFocus("#annotated-detail-title");
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
