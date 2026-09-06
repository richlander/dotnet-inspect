import {
  bindAnnotatedSource,
  renderAnnotatedSource,
  renderAnnotatedSourceModal,
  type AnnotatedSourceAction,
} from "../src/annotated-source.ts";
import {
  clearAnnotations,
  closeFindingDetail,
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  dismissModalSession,
  hitTestAnnotatedNode,
  openModalSession,
  selectAllAnnotations,
  selectDefaultAnnotations,
  selectFinding,
  selectNode,
  toggleCoordinates,
  toggleFindingAnnotation,
  toggleMedium,
  type AnnotatedSourceSession,
} from "../src/annotated-source-session.ts";
import {
  clearFindingSelection,
  createMemberFindingInteraction,
  selectAnnotatedSourceFact,
  selectFindingInstance,
} from "../src/finding-interaction.ts";
import {
  bindMemberFacts,
  renderMemberFacts,
} from "../src/member-facts.ts";
import { memberFactsFixture } from "../test/member-facts-fixture.ts";
import {
  memberFindingCensusFixture,
} from "../test/member-finding-census-fixture.ts";

const app = requireApp();

let interaction =
  createMemberFindingInteraction(memberFindingCensusFixture());
const result = interaction.census.annotatedSource;
const model = createAnnotatedSourceViewerModel(result);
let surface: "facts" | "annotated" = "facts";
let embedded = createEmbeddedSession(model);
let modal: AnnotatedSourceSession | null = null;
let selectionError = "";

function requireApp(): HTMLElement {
  const candidate = document.querySelector<HTMLElement>("#app");
  if (!candidate) {
    throw new Error("Finding interaction browser harness root is missing");
  }
  return candidate;
}

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function syncFindingSelection(session: AnnotatedSourceSession): void {
  const factId =
    session.primary?.kind === "finding" ? session.primary.id : null;
  if (factId === null) {
    interaction = clearFindingSelection(interaction);
    selectionError = "";
    return;
  }
  const transition = selectAnnotatedSourceFact(interaction, factId);
  interaction = transition.accepted
    ? transition.interaction
    : clearFindingSelection(interaction);
  selectionError = transition.error ?? "";
}

function updateSession(next: AnnotatedSourceSession): void {
  if (modal) modal = next;
  else embedded = next;
}

function currentSession(): AnnotatedSourceSession {
  return modal ?? embedded;
}

function openFinding(receipt: string, instanceKey: number): void {
  const transition = selectFindingInstance(interaction, receipt, instanceKey);
  if (!transition.accepted) {
    selectionError = transition.error;
    render();
    return;
  }
  interaction = transition.interaction;
  selectionError = "";
  const opened = openModalSession(model, embedded);
  embedded = opened.embedded;
  modal = selectFinding(opened.modal, {
    kind: "inspector",
    factId: transition.factId,
  });
  surface = "annotated";
  render("#annotated-detail-title");
}

function onAnnotatedAction(action: AnnotatedSourceAction): void {
  const session = currentSession();
  switch (action.kind) {
    case "copy":
    case "destination-open":
      return;
    case "explore": {
      const opened = openModalSession(model, embedded);
      embedded = opened.embedded;
      modal = opened.modal;
      syncFindingSelection(modal);
      render();
      return;
    }
    case "close-modal":
      if (!modal) return;
      embedded = dismissModalSession(model, modal);
      modal = null;
      syncFindingSelection(embedded);
      render();
      return;
    case "close-detail": {
      const transition = closeFindingDetail(model, session);
      updateSession(transition.state);
      render();
      return;
    }
    case "annotation-open": {
      const next = selectFinding(session, action.opener);
      updateSession(next);
      syncFindingSelection(next);
      render("#annotated-detail-title");
      return;
    }
    case "inspector-open": {
      const next = selectFinding(session, {
        kind: "inspector",
        factId: action.factId,
      });
      updateSession(next);
      syncFindingSelection(next);
      render("#annotated-detail-title");
      return;
    }
    case "annotation-set": {
      const transition = action.value === "Default"
        ? selectDefaultAnnotations(model, session)
        : action.value === "All"
          ? selectAllAnnotations(model, session)
          : clearAnnotations(session);
      updateSession(transition.state);
      syncFindingSelection(transition.state);
      render();
      return;
    }
    case "finding-toggle": {
      const transition =
        toggleFindingAnnotation(model, session, action.factId);
      updateSession(transition.state);
      syncFindingSelection(transition.state);
      render();
      return;
    }
    case "medium-toggle": {
      const transition = toggleMedium(model, session, action.medium);
      updateSession(transition.state);
      render();
      return;
    }
    case "coordinate-toggle": {
      const transition = toggleCoordinates(session);
      updateSession(transition.state);
      render();
      return;
    }
    case "node-select": {
      const next = selectNode(session, action.nodeId);
      updateSession(next);
      syncFindingSelection(next);
      render();
      return;
    }
    case "source-select": {
      const node =
        hitTestAnnotatedNode(model, action.offset, action.medium);
      if (!node) return;
      const next = selectNode(session, node.id);
      updateSession(next);
      syncFindingSelection(next);
      render();
      return;
    }
  }
}

function render(focusSelector: string | null = null): void {
  app.innerHTML = `
    <nav aria-label="Member sections">
      <button id="show-facts" type="button" aria-pressed="${surface === "facts"}">Facts</button>
      <button id="show-annotated" type="button" aria-pressed="${surface === "annotated"}">Annotated source</button>
    </nav>
    <main id="harness-background" class="member-surface"${modal ? " inert" : ""}>
      <div class="member-surface-scroll">
        ${surface === "facts"
          ? renderMemberFacts({
              memberFacts: memberFactsFixture(),
              memberFactsLoading: false,
              memberFactsError: "",
              memberAnnotatedLoading: false,
              memberAnnotatedError: "",
              memberFindingInteraction: interaction,
              memberFindingSelectionError: selectionError,
            })
          : renderAnnotatedSource({
              result,
              session: embedded,
              escapeHtml,
            })}
      </div>
    </main>
    ${modal
      ? renderAnnotatedSourceModal({
          result,
          session: modal,
          escapeHtml,
        })
      : ""}`;

  app.querySelector("#show-facts")?.addEventListener("click", () => {
    surface = "facts";
    render();
  });
  app.querySelector("#show-annotated")?.addEventListener("click", () => {
    surface = "annotated";
    render();
  });
  bindMemberFacts(app, { onSelectFinding: openFinding });
  bindAnnotatedSource(app, { onAction: onAnnotatedAction });
  if (focusSelector) {
    requestAnimationFrame(() =>
      app.querySelector<HTMLElement>(focusSelector)?.focus());
  }
}

render();
