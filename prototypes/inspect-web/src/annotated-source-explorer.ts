import {
  MEDIUM_LABELS,
  MEDIA,
  nodeAtOffset,
  prepareAnnotatedView,
  projectPreparedAnnotatedView,
  type AnnotatedSourceDocument,
  type AnnotatedSourceNode,
  type AnnotatedSourceRegion,
  type AnnotatedViewCapture,
  type AnnotatedViewFact,
  type PreparedSourceCodeLensCandidate,
  type PreparedAnnotatedView,
  type SourceMedium,
  validateAnnotatedSourceDocument,
} from "./annotated-source-view.ts";
import type {
  BrowserAnnotatedSource,
  BrowserAnnotatedSourceFindingEvidence,
  BrowserCallGraphTarget,
} from "./inspect-web-engine.d.ts";

export interface AnnotatedSourceFindingEvidence
  extends Omit<BrowserAnnotatedSourceFindingEvidence, "document"> {
  document: AnnotatedSourceDocument | null;
}

export type AnnotatedSourceResult =
  Omit<BrowserAnnotatedSource, "document" | "findingEvidence"> & {
  document: AnnotatedSourceDocument;
  findingEvidence: AnnotatedSourceFindingEvidence[];
};

export interface AnnotatedSourceExplorerState {
  prepared: PreparedAnnotatedView;
  media: Record<SourceMedium, boolean>;
  codeLens: boolean;
  codeLensPreview: { nodeId: number; startedAt: number } | null;
  selectedFactId: number | null;
  activeFactIds: readonly number[];
  selectedCaptureIndex: number | null;
  selectedNodeIds: readonly number[];
  selectedKind: string;
  selectedRegionRole: string;
}

export interface AnnotatedSourceExplorerRenderState {
  codeScroll: number;
  codeScrollLeft: number;
  inspectorScroll: number;
  focusSelector: string;
}

export type AnnotatedSourceExplorerAction =
  | { type: "toggle-medium"; medium: SourceMedium }
  | { type: "toggle-codelens" }
  | { type: "preview-codelens"; nodeId: number; startedAt: number }
  | { type: "clear-codelens-preview"; nodeId: number }
  | { type: "select-fact"; factId: number }
  | { type: "select-capture"; captureIndex: number }
  | { type: "select-node"; nodeId: number }
  | { type: "select-offset"; offset: number }
  | { type: "select-kind"; kind: string }
  | { type: "select-region"; role: string }
  | { type: "clear-selection" };

type EscapeHtml = (value: unknown) => string;

export interface AnnotatedSourceEntryOptions {
  result: AnnotatedSourceResult;
  escapeHtml: EscapeHtml;
}

export interface AnnotatedSourceEntryBindingActions {
  onCopy: () => void;
  onOpen: () => void;
}

export interface AnnotatedSourceExplorerBindingActions {
  onClearSelection: () => void;
  onCopy: () => void;
  onExit: () => void;
  onCaptureSelect: (captureIndex: number) => void;
  onFactSelect: (factId: number) => void;
  onFindingMemberCopy: (member: string) => void;
  onFindingMemberNavigate: (evidenceIndex: number) => void;
  onCodeLensPreview: (nodeId: number) => void;
  onCodeLensPreviewEnd: (nodeId: number) => void;
  onCodeLensToggle: () => void;
  onMediumToggle: (medium: SourceMedium) => void;
  onNodeKindSelect: (kind: string) => void;
  onRegionSelect: (role: string) => void;
  onNodeSelect: (nodeId: number) => void;
  onOffsetSelect: (offset: number) => void;
}

export interface AnnotatedSourceKindOption {
  id: string;
  label: string;
}

export interface CSharpSyntaxToken {
  text: string;
  classes: readonly string[];
}

type CSharpTokenizer = (value: string) => readonly CSharpSyntaxToken[];

export interface AnnotatedSourceExplorerOptions extends AnnotatedSourceEntryOptions {
  state: AnnotatedSourceExplorerState;
  title: string;
  subtitle: string;
  nodeKinds?: readonly AnnotatedSourceKindOption[];
  tokenizeCSharp?: CSharpTokenizer;
  now?: number;
}

const MAX_SELECTION_DETAILS = 50;
const MAX_FINDING_PEEK_LINES = 8;
const CODELENS_PREVIEW_DURATION_MS = 6_600;
const REMOTE_FINDING_DESCRIPTORS = new Set([
  "cost.callee",
  "safety.callee",
  "semantics.callee",
]);
const preparedDocuments = new WeakMap<AnnotatedSourceDocument, PreparedAnnotatedView>();
const preparedSyntax = new WeakMap<
  AnnotatedSourceDocument,
  WeakMap<CSharpTokenizer, Map<number, readonly SyntaxRange[]>>
>();

export function bindAnnotatedSourceEntry(
  root: ParentNode,
  actions: AnnotatedSourceEntryBindingActions,
): void {
  root.querySelector("#copy-annotated")?.addEventListener("click", actions.onCopy);
  root.querySelector("#open-annotated-explorer")?.addEventListener("click", actions.onOpen);
}

export function bindAnnotatedSourceExplorer(
  root: ParentNode,
  actions: AnnotatedSourceExplorerBindingActions,
): void {
  root.querySelector("#ase-exit")?.addEventListener("click", actions.onExit);
  root.querySelector("#ase-copy")?.addEventListener("click", actions.onCopy);
  root.querySelector("[data-ase-codelens-toggle]")?.addEventListener(
    "click",
    actions.onCodeLensToggle);
  root.querySelectorAll<HTMLElement>("[data-ase-medium]").forEach(button =>
    button.addEventListener("click", () => {
      const medium = button.dataset.aseMedium;
      if (medium === "CSharp" || medium === "Il") actions.onMediumToggle(medium);
    }));
  root.querySelectorAll<HTMLElement>("[data-ase-kind]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onNodeKindSelect(button.dataset.aseKind ?? "")));
  root.querySelectorAll<HTMLElement>("[data-ase-region]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onRegionSelect(button.dataset.aseRegion ?? "")));
  root.querySelectorAll<HTMLElement>("[data-ase-capture]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onCaptureSelect(Number(button.dataset.aseCapture))));
  root.querySelectorAll<HTMLElement>("[data-ase-fact]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onFactSelect(Number(button.dataset.aseFact))));
  root.querySelectorAll<HTMLElement>("[data-ase-node]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onNodeSelect(Number(button.dataset.aseNode))));
  root.querySelectorAll<HTMLElement>("[data-ase-codelens-node]").forEach(button => {
    const nodeId = Number(button.dataset.aseCodelensNode);
    button.addEventListener("click", () => actions.onCodeLensPreview(nodeId));
  });
  root.querySelectorAll<HTMLElement>("[data-ase-codelens-preview-node]").forEach(target =>
    target.addEventListener("animationend", event => {
      if (event.animationName !== "ase-codelens-preview") return;
      actions.onCodeLensPreviewEnd(
        Number(target.dataset.aseCodelensPreviewNode));
    }));
  root.querySelectorAll<HTMLElement>("[data-ase-finding-peek]").forEach(button => {
    const peekId = button.dataset.aseFindingPeek;
    const peek = peekId ? button.ownerDocument.getElementById(peekId) : null;
    const view = button.ownerDocument.defaultView;
    if (!peek || !view) return;
    button.addEventListener("click", () => {
      view.requestAnimationFrame(() => {
        if (!peek.matches(":popover-open")) return;
        const anchor = button.getBoundingClientRect();
        const bounds = peek.getBoundingClientRect();
        const edge = 12;
        const gap = 8;
        const left = Math.min(
          Math.max(edge, anchor.left),
          Math.max(edge, view.innerWidth - bounds.width - edge));
        const below = anchor.bottom + gap;
        const top = below + bounds.height <= view.innerHeight - edge
          ? below
          : Math.max(edge, anchor.top - bounds.height - gap);
        peek.style.left = `${left}px`;
        peek.style.top = `${top}px`;
        peek.classList.add("positioned");
      });
    });
  });
  root.querySelectorAll<HTMLElement>("[data-ase-finding-member-copy]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onFindingMemberCopy(
        button.dataset.aseFindingMemberCopy ?? "")));
  root.querySelectorAll<HTMLElement>("[data-ase-finding-member-navigate]").forEach(button =>
    button.addEventListener(
      "click",
      () => actions.onFindingMemberNavigate(
        Number(button.dataset.aseFindingMemberNavigate))));
  root.querySelectorAll<HTMLElement>("[data-ase-offset]").forEach(button =>
    button.addEventListener(
      "click",
      event => {
        const selection = button.ownerDocument.getSelection();
        if (event.detail !== 0
          && selection
          && !selection.isCollapsed
          && selection.containsNode(button, true)) {
          return;
        }
        actions.onOffsetSelect(Number(button.dataset.aseOffset));
      }));
  root.querySelector("#ase-clear")?.addEventListener("click", actions.onClearSelection);

  const code = root.querySelector<HTMLElement>(".ase-code-scroll");
  code?.addEventListener("focusin", event => {
    if (event.target !== code) code.tabIndex = -1;
  });
  code?.addEventListener("focusout", event => {
    if (!(event.relatedTarget instanceof Node) || !code.contains(event.relatedTarget)) {
      code.tabIndex = 0;
    }
  });
  code?.addEventListener("keydown", event => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const spans = [
      ...code.querySelectorAll<HTMLElement>("[data-ase-source-affordance]"),
    ];
    if (spans.length === 0) return;
    const currentIndex = code.ownerDocument.activeElement instanceof HTMLElement
      ? spans.indexOf(code.ownerDocument.activeElement)
      : -1;
    let nextIndex: number;
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        nextIndex = currentIndex < 0 ? 0 : Math.min(currentIndex + 1, spans.length - 1);
        break;
      case "ArrowLeft":
      case "ArrowUp":
        nextIndex = currentIndex < 0 ? spans.length - 1 : Math.max(currentIndex - 1, 0);
        break;
      case "Home":
        nextIndex = 0;
        break;
      case "End":
        nextIndex = spans.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    code.tabIndex = -1;
    spans[nextIndex].focus({ preventScroll: true });
    spans[nextIndex].scrollIntoView({ block: "nearest", inline: "nearest" });
  });
}

export class AnnotatedSourceExplorerRenderCoordinator {
  #pendingState: AnnotatedSourceExplorerRenderState | null = null;
  #generation = 0;

  get generation(): number {
    return this.#generation;
  }

  begin(renderState: AnnotatedSourceExplorerRenderState | null): number {
    if (this.#pendingState === null && renderState !== null) {
      this.#pendingState = renderState;
    }
    return ++this.#generation;
  }

  invalidate(): void {
    this.#generation++;
    this.#pendingState = null;
  }

  isCurrent(generation: number): boolean {
    return generation === this.#generation;
  }

  consume(generation: number): AnnotatedSourceExplorerRenderState | null {
    if (!this.isCurrent(generation)) return null;
    const renderState = this.#pendingState;
    this.#pendingState = null;
    return renderState;
  }
}

export function createAnnotatedSourceExplorerState(
  document: AnnotatedSourceDocument,
  initial: Partial<Omit<AnnotatedSourceExplorerState, "prepared">> = {},
): AnnotatedSourceExplorerState {
  const prepared = preparedDocument(document);
  const anchoredFactIds = new Set(document.targets.map(target => target.fact_id));
  const media = {
    CSharp: initial.media?.CSharp !== false,
    Il: initial.media?.Il === true,
  };
  if (!hasVisibleSource(prepared, media)) {
    media.CSharp = prepared.lines.some(line =>
      line.medium === "CSharp" || line.medium === "Mixed");
    media.Il = !media.CSharp
      && prepared.lines.some(line => line.medium === "Il");
  }

  return {
    prepared,
    media,
    codeLens: initial.codeLens !== false,
    codeLensPreview: initial.codeLensPreview ?? null,
    selectedFactId: initial.selectedFactId ?? null,
    activeFactIds: [...new Set(
      initial.activeFactIds
        ?? anchoredFactIds,
    )].filter(id => anchoredFactIds.has(id)),
    selectedCaptureIndex: initial.selectedCaptureIndex ?? null,
    selectedNodeIds: [...new Set(initial.selectedNodeIds ?? [])],
    selectedKind: initial.selectedKind ?? "",
    selectedRegionRole: initial.selectedRegionRole ?? "",
  };
}

export function reduceAnnotatedSourceExplorerState(
  document: AnnotatedSourceDocument,
  state: AnnotatedSourceExplorerState,
  action: AnnotatedSourceExplorerAction,
): AnnotatedSourceExplorerState {
  switch (action.type) {
    case "toggle-medium": {
      const media = { ...state.media, [action.medium]: !state.media[action.medium] };
      return hasVisibleSource(state.prepared, media) ? { ...state, media } : state;
    }
    case "toggle-codelens":
      return {
        ...state,
        codeLens: !state.codeLens,
        codeLensPreview: state.codeLens ? null : state.codeLensPreview,
      };
    case "preview-codelens":
      if (!state.prepared.codeLensCandidates.some(
        candidate => candidate.nodeId === action.nodeId)) return state;
      return {
        ...state,
        codeLensPreview: {
          nodeId: action.nodeId,
          startedAt: action.startedAt,
        },
      };
    case "clear-codelens-preview":
      return state.codeLensPreview?.nodeId === action.nodeId
        ? { ...state, codeLensPreview: null }
        : state;
    case "select-fact": {
      if (!document.facts.some(fact => fact.id === action.factId)) return state;
      const selected = state.selectedFactId === action.factId;
      const anchored = document.targets.some(target => target.fact_id === action.factId);
      return {
        ...state,
        selectedFactId: selected ? null : action.factId,
        activeFactIds: !anchored
          ? state.activeFactIds
          : selected
            ? state.activeFactIds.filter(id => id !== action.factId)
            : [...new Set([...state.activeFactIds, action.factId])],
        selectedCaptureIndex: null,
        selectedKind: "",
        selectedRegionRole: "",
      };
    }
    case "select-capture": {
      if (!(document.captures ?? [])[action.captureIndex]) return state;
      const selectedCaptureIndex = state.selectedCaptureIndex === action.captureIndex
        ? null
        : action.captureIndex;
      return {
        ...state,
        selectedFactId: null,
        selectedCaptureIndex,
        selectedKind: "",
        selectedRegionRole: "",
      };
    }
    case "select-node":
      if (!document.nodes.some(node => node.id === action.nodeId)) return state;
      return {
        ...state,
        selectedCaptureIndex: null,
        selectedNodeIds: toggleId(state.selectedNodeIds, action.nodeId),
        selectedKind: "",
        selectedRegionRole: "",
      };
    case "select-offset": {
      const node = nodeAtOffset(document, action.offset);
      return {
        ...state,
        selectedCaptureIndex: null,
        selectedNodeIds: node
          ? toggleId(state.selectedNodeIds, node.id)
          : state.selectedNodeIds,
        selectedKind: "",
        selectedRegionRole: "",
      };
    }
    case "select-kind": {
      if (action.kind && !document.nodes.some(node => node.kind === action.kind)) return state;
      const selectedKind = state.selectedKind === action.kind ? "" : action.kind;
      return {
        ...state,
        selectedFactId: null,
        selectedCaptureIndex: null,
        selectedNodeIds: selectedKind
          ? document.nodes.filter(node => node.kind === selectedKind).map(node => node.id)
          : [],
        selectedKind,
        selectedRegionRole: "",
      };
    }
    case "select-region":
      if (action.role && !document.regions.some(region => region.role === action.role)) return state;
      return {
        ...state,
        selectedFactId: null,
        selectedCaptureIndex: null,
        selectedNodeIds: [],
        selectedKind: "",
        selectedRegionRole: state.selectedRegionRole === action.role ? "" : action.role,
      };
    case "clear-selection":
      return {
        ...state,
        selectedFactId: null,
        activeFactIds: [],
        selectedCaptureIndex: null,
        selectedNodeIds: [],
        selectedKind: "",
        selectedRegionRole: "",
      };
  }
  const unhandledAction: never = action;
  throw new Error(`Unsupported annotated source explorer action: ${String(unhandledAction)}`);
}

function toggleId(ids: readonly number[], id: number): number[] {
  return ids.includes(id) ? ids.filter(candidate => candidate !== id) : [...ids, id];
}

export function renderAnnotatedSourceEntry(options: AnnotatedSourceEntryOptions): string {
  const { result, escapeHtml } = options;
  validateAnnotatedSourceDocument(result.document);
  const anchoredFacts = new Set(result.document.targets.map(target => target.fact_id));
  const targetCount = result.document.targets.length;

  return `<section class="document-section source-result annotated-entry">
      <div class="source-provenance"><strong>Annotated source</strong><span>${escapeHtml(result.provenance)}</span><button id="copy-annotated" type="button">copy</button></div>
      ${limitationHtml(result, escapeHtml)}
      <div class="annotated-entry-body">
        <div class="annotated-entry-glyph" aria-hidden="true">⌁</div>
        <div class="annotated-entry-copy">
          <h2>Explore source and findings together</h2>
          <p>Open the full-screen viewer to follow facts through their structural targets into exact C# and IL spans.</p>
          <div class="annotated-entry-counts">
            ${countHtml(result.document.nodes.length, "node", escapeHtml)}
            ${countHtml(result.document.facts.length, "fact", escapeHtml)}
            ${countHtml(targetCount, "target", escapeHtml)}
            ${countHtml(result.document.captures?.length ?? 0, "capture", escapeHtml)}
            ${countHtml(result.document.facts.length - anchoredFacts.size, "unanchored", escapeHtml)}
          </div>
        </div>
        <button id="open-annotated-explorer" class="annotated-entry-open" type="button">Open full-screen viewer</button>
      </div>
    </section>`;
}

export function renderAnnotatedSourceExplorer(
  options: AnnotatedSourceExplorerOptions,
): string {
  const {
    result,
    state,
    title,
    subtitle,
    escapeHtml,
    tokenizeCSharp,
    now = Date.now(),
  } = options;
  if (state.prepared.document !== result.document) {
    throw new Error("The annotated source explorer state belongs to a different document.");
  }
  const view = projectPreparedAnnotatedView(state.prepared, state);
  const nodeById = new Map(result.document.nodes.map(node => [node.id, node]));
  const selectedNodes = view.selectedNodeIds
    .map(id => nodeById.get(id))
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
  const persistentSelectedNodes = state.selectedNodeIds
    .map(id => nodeById.get(id))
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
  const unanchoredIds = new Set(view.unanchoredFactIds);
  const anchoredFacts = view.facts.filter(fact => !unanchoredIds.has(fact.id));
  const unanchoredFacts = view.facts.filter(fact => unanchoredIds.has(fact.id));
  const activeFactIds = new Set(state.activeFactIds);
  const findingEvidence = new Map(
    result.findingEvidence.map((evidence, index) => [
      `${evidence.descriptor}\0${evidence.sourceOffset}`,
      { evidence, index },
    ]));
  const selectedCapture = view.selectedCaptureIndex === null
    ? null
    : view.captures[view.selectedCaptureIndex];
  const kindCounts = nodeKindCounts(result.document, options.nodeKinds ?? []);
  const kindLabels = new Map(kindCounts.map(kind => [kind.id, kind.label]));
  const selectedRegions = result.document.regions
    .filter(region => region.role === state.selectedRegionRole);
  const selectedRegionSpans = selectedRegions.flatMap(region => region.spans);
  const selectedKindLabel = kindCounts.find(kind => kind.id === state.selectedKind)?.label
    ?? (state.selectedKind === "Instruction" ? "Instruction" : state.selectedKind);
  const directlySelectedNode =
    state.selectedCaptureIndex === null
      && state.selectedKind === ""
      && state.selectedRegionRole === ""
      && persistentSelectedNodes.length === 1
      ? persistentSelectedNodes[0]
      : null;
  const directlySelectedNodeFacts = directlySelectedNode
    ? view.facts.filter(fact => fact.nodeIds.includes(directlySelectedNode.id))
    : [];
  const codeLensAnnotations = state.codeLens
    ? sourceCodeLensAnnotations(state.prepared.codeLensCandidates, kindLabels)
    : new Map<number, readonly SourceCodeLensAnnotation[]>();
  const codeLensNodeIds = new Set(
    [...codeLensAnnotations.values()].flat().map(annotation => annotation.nodeId));
  const codeLensPreviewElapsed = state.codeLensPreview
    ? Math.max(0, now - state.codeLensPreview.startedAt)
    : 0;
  const activeCodeLensPreview = state.codeLens
    && state.codeLensPreview
    && codeLensPreviewElapsed < CODELENS_PREVIEW_DURATION_MS
    ? state.codeLensPreview
    : null;
  const nodeCaretAnnotations = combineCaretAnnotations(
    persistentSelectedNodes
      .filter(node => !codeLensNodeIds.has(node.id))
      .map(node => sourceNodeCaretAnnotations(
        node,
        view.lines,
        result.document.text,
        kindLabels.get(node.kind) ?? node.kind)));
  const findingCaretAnnotations = combineCaretAnnotations(
    state.activeFactIds.flatMap(factId => {
      const fact = view.facts[factId];
      if (!fact || unanchoredIds.has(fact.id)) return [];
      return [sourceFindingCaretAnnotations(
        fact,
        fact.nodeIds
          .map(id => nodeById.get(id))
          .filter((node): node is AnnotatedSourceNode => node !== undefined),
        view.lines,
        result.document,
        findingEvidence.get(`${fact.descriptor}\0${fact.sourceOffset}`))];
    }));
  const findingPeeks = [...findingCaretAnnotations.values()]
    .flat()
    .flatMap(annotation => annotation.peek ? [annotation.peek] : []);

  const mediumButtons = MEDIA.map(medium =>
    `<button type="button" class="annotated-medium${view.media[medium] ? " on" : ""}" data-ase-medium="${medium}" aria-pressed="${view.media[medium]}">${escapeHtml(MEDIUM_LABELS[medium])}</button>`,
  ).join("");
  const showMediumGutter = MEDIA.every(medium => view.media[medium]);
  let previousMedium = "";
  const lines = view.lines.map(line => {
    const lineText = line.segments.map(segment => segment.text).join("");
    const syntaxRanges = line.medium === "CSharp" && tokenizeCSharp
      ? syntaxRangesForDocumentLine(
          result.document,
          tokenizeCSharp,
          lineText,
          line.start)
      : [];
    const lineRegionSpans = selectedRegionSpans.filter(
      span => span.start < line.end && line.start < span.start + span.length);
    const segments = line.segments.map(segment => {
      const nodes = segment.nodeIds
        .map(id => nodeById.get(id))
        .filter((node): node is AnnotatedSourceNode => node !== undefined);
      const addressable = nodes.length > 0;
      const factCount = segment.factIds.length;
      const captureCount = segment.captureIds.length;
      const factDescriptors = segment.factIds
        .map(factId => view.facts[factId]?.descriptor)
        .filter((descriptor): descriptor is string => descriptor !== undefined);
      const captureNames = segment.captureIds
        .map(captureId => view.captures[captureId]?.displayName)
        .filter((name): name is string => name !== undefined);
      const titleText = [
        nodes.map(node => `#${node.id} ${node.kind}`).join(" · "),
        factCount > 0
          ? `${factCount} finding${factCount === 1 ? "" : "s"}: ${factDescriptors.join(", ")}`
          : "",
        captureCount > 0
          ? `${captureCount} captured variable${captureCount === 1 ? "" : "s"}: ${captureNames.join(", ")}`
          : "",
      ].filter(Boolean).join(" · ");
      const selectionClass = segment.selected
        ? state.selectedFactId !== null
          ? " selected semantic"
          : state.selectedCaptureIndex !== null
            ? " selected capture"
            : " selected structural"
        : "";
      const suppressPersistentStructure = directlySelectedNode !== null
        && codeLensNodeIds.has(directlySelectedNode.id)
        && state.selectedFactId === null
        && segment.nodeIds.includes(directlySelectedNode.id);
      const captureScope = view.captureScopeNodeId !== null
        && segment.nodeIds.includes(view.captureScopeNodeId);
      const codeLensPreview = activeCodeLensPreview !== null
        && segment.nodeIds.includes(activeCodeLensPreview.nodeId);
      const descriptions = [
        factCount > 0
          ? `${factCount} finding${factCount === 1 ? "" : "s"} available: ${factDescriptors.join(", ")}`
          : "",
        captureCount > 0
          ? `captured variable${captureCount === 1 ? "" : "s"}: ${captureNames.join(", ")}`
          : "",
      ].filter(Boolean).join("; ");
      const content = renderSegmentText(
        segment.start,
        segment.end,
        result.document.text,
        syntaxRanges,
        lineRegionSpans,
        escapeHtml);
      if (addressable) {
        const accessibleLabel = descriptions
          ? ` aria-label="${escapeHtml(`${segment.text}; ${descriptions}`)}"`
          : "";
        const previewAttributes = codeLensPreview
          ? ` data-ase-codelens-preview-node="${activeCodeLensPreview.nodeId}" style="animation-delay: -${codeLensPreviewElapsed}ms"`
          : "";
        return `<button type="button" tabindex="-1" class="annotated-span addressable${factCount > 0 ? " has-fact" : ""}${captureCount > 0 ? " has-capture" : ""}${captureScope ? " capture-scope" : ""}${suppressPersistentStructure ? "" : selectionClass}${codeLensPreview ? " codelens-preview" : ""}" data-ase-source-affordance data-ase-offset="${segment.start}" data-ase-node-ids="${segment.nodeIds.join(" ")}"${previewAttributes} title="${escapeHtml(titleText)}"${accessibleLabel}>${content}</button>`;
      }
      return `<span class="annotated-span${selectionClass}">${content}</span>`;
    }).join("");
    const mediumLabel = line.medium !== previousMedium && line.medium !== "Mixed"
      ? MEDIUM_LABELS[line.medium]
      : "";
    previousMedium = line.medium;
    const lenses = state.codeLens
      ? codeLensAnnotations.get(line.start)?.map(annotation =>
          `<div class="annotated-codelens-row">
            <span class="annotated-line-number"></span>
            ${showMediumGutter ? `<span class="annotated-line-medium"></span>` : ""}
            <span class="annotated-line-text">${escapeHtml(annotation.prefix)}<button type="button" tabindex="-1"${activeCodeLensPreview?.nodeId === annotation.nodeId ? ' class="previewing"' : ""} data-ase-source-affordance data-ase-codelens-node="${annotation.nodeId}" title="Preview ${escapeHtml(annotation.label)} for six seconds">${escapeHtml(annotation.label)}</button></span>
          </div>`,
        ).join("") ?? ""
      : "";
    const sourceLine = `<div class="annotated-line medium-${line.medium.toLowerCase()}">
      <span class="annotated-line-number">${line.number}</span>
      ${showMediumGutter
        ? `<span class="annotated-line-medium">${escapeHtml(mediumLabel)}</span>`
        : ""}
      <span class="annotated-line-text">${segments}</span>
    </div>`;
    const carets = groupCaretAnnotations([
      ...(nodeCaretAnnotations.get(line.start) ?? []),
      ...(findingCaretAnnotations.get(line.start) ?? []),
    ]).map(annotation => {
      const planes = [...annotation.planes].map(plane => `plane-${plane}`).join(" ");
      const caret = `<div class="annotated-node-caret ${planes}" aria-label="${escapeHtml(annotation.accessibleLabel)}">
        <span class="annotated-line-number"></span>
        ${showMediumGutter ? `<span class="annotated-line-medium"></span>` : ""}
        <span class="annotated-line-text" aria-hidden="true">${escapeHtml(annotation.prefix)}<span class="annotated-caret-run">${"^".repeat(annotation.length)}</span></span>
      </div>`;
      const detail = annotation.labels.length === 0
        ? ""
        : `<div class="annotated-caret-detail ${planes}">
          <span class="annotated-line-number"></span>
          ${showMediumGutter ? `<span class="annotated-line-medium"></span>` : ""}
          <span class="annotated-line-text">${escapeHtml(annotation.prefix)}<span class="annotated-caret-label-stack">${annotation.labels
            .map(label => caretLabelHtml(label, escapeHtml))
            .join("")}</span></span>
        </div>`;
      return caret + detail;
    }).join("");
    return lenses + sourceLine + carets;
  }).join("");

  return `<div class="annotated-explorer" role="dialog" aria-modal="true" aria-label="Annotated source explorer">
      <header class="ase-bar">
        <button id="ase-exit" class="ase-exit" type="button" title="Exit the explorer">✕ Exit</button>
        <div class="ase-title">
          <strong>${escapeHtml(title)}</strong>
          <span>${escapeHtml(subtitle)}</span>
        </div>
        <div class="ase-media" role="group" aria-label="Visible source media">${mediumButtons}</div>
        <button id="ase-copy" class="ase-copy" type="button">copy source</button>
      </header>
      ${limitationHtml(result, escapeHtml)}
      <div class="ase-workspace">
        <section class="ase-code-panel" aria-label="Annotated source text">
          <div class="ase-panel-heading">
            <div><span>Canonical text</span><strong>Finding overlays</strong></div>
            <div class="ase-overlay-legend" role="group" aria-label="Overlay legend">
              <span><i class="finding"></i>finding available</span>
              <span><i class="semantic"></i>active finding</span>
              <span><i class="capture"></i>captured variable</span>
              <span><i class="structure"></i>active structure</span>
            </div>
            <p>${result.document.nodes.length} nodes · ${result.document.targets.length} targets${view.hiddenLines ? ` · ${view.hiddenLines} hidden lines` : ""}</p>
          </div>
          <div class="ase-code-scroll" tabindex="0" aria-label="Annotated source text. Use arrow keys to move between source affordances.">
            <pre class="annotated-text"><code>${lines}</code></pre>
          </div>
          ${findingPeeks.map(peek => findingPeekHtml(
            peek,
            tokenizeCSharp,
            escapeHtml)).join("")}
        </section>
        <aside class="ase-inspector">
          <section class="ase-inspector-section">
            <div class="ase-section-heading">
              <div><span>Structural plane</span><strong>CodeLens</strong></div>
              <button type="button" data-ase-codelens-toggle aria-pressed="${state.codeLens}">${state.codeLens ? "on" : "off"}</button>
            </div>
            <p class="ase-structure-help">Unnumbered annotations identify multi-line source constructs. Activate a chip to preview its exact span for six seconds.</p>
          </section>
          ${captureSectionHtml(view.captures, nodeById, kindLabels, escapeHtml)}
          <section class="ase-inspector-section">
            <div class="ase-section-heading">
              <div><span>Selection</span><strong>${escapeHtml(directlySelectedNode ? `Node #${directlySelectedNode.id}` : selectionTitle(view.selectedFactId, selectedCapture, selectedNodes, selectedKindLabel, state.selectedRegionRole, selectedRegions.length))}</strong></div>
              ${view.selectedFactId !== null || state.activeFactIds.length > 0 || selectedCapture !== null || state.selectedNodeIds.length > 0 || selectedRegions.length > 0 ? `<button id="ase-clear" type="button">clear</button>` : ""}
            </div>
            ${selectedRegions.length > 0
              ? regionSelectionHtml(selectedRegions, state.prepared, escapeHtml)
              : selectedCapture
                ? captureSelectionHtml(selectedCapture, nodeById, kindLabels, escapeHtml)
                : directlySelectedNode
                  ? sourceNodeSelectionHtml(
                      directlySelectedNode,
                      directlySelectedNodeFacts,
                      kindLabels,
                      activeFactIds,
                      escapeHtml)
                  : selectionHtml(selectedNodes, escapeHtml)}
          </section>
          <section class="ase-inspector-section">
            <div class="ase-section-heading"><div><span>Semantic plane</span><strong>Anchored facts</strong></div><em>${anchoredFacts.length}</em></div>
            <div class="ase-fact-list">${factListHtml(anchoredFacts, activeFactIds, escapeHtml, "No anchored facts were observed.")}</div>
          </section>
          <section class="ase-inspector-section">
            <div class="ase-section-heading"><div><span>No invented coordinate</span><strong>Unanchored facts</strong></div><em>${unanchoredFacts.length}</em></div>
            <div class="ase-fact-list">${factListHtml(unanchoredFacts, activeFactIds, escapeHtml, "None")}</div>
          </section>
        </aside>
      </div>
    </div>`;
}

interface SourceNodeCaretAnnotation {
  accessibleLabel: string;
  label: string;
  length: number;
  nodeId: number;
  peek?: SourceFindingPeek;
  plane: "source" | "finding";
  prefix: string;
}

interface SourceCaretGroup {
  accessibleLabel: string;
  labels: readonly SourceCaretLabel[];
  length: number;
  planes: ReadonlySet<"source" | "finding">;
  prefix: string;
}

interface SourceCaretLabel {
  peek?: SourceFindingPeek;
  plane: "source" | "finding";
  text: string;
}

interface SourceFindingPeekLine {
  evidenceSpans: readonly { start: number; length: number }[];
  end: number;
  medium: SourceMedium | "Mixed";
  number: number;
  start: number;
}

interface SourceFindingPeek {
  document: AnnotatedSourceDocument;
  evidenceIndex?: number;
  finding: string;
  hiddenLineCount: number;
  id: string;
  lines: readonly SourceFindingPeekLine[];
  location: string;
  member?: string;
  target?: BrowserCallGraphTarget;
  unavailableReason?: string;
}

interface SourceCodeLensAnnotation {
  label: string;
  nodeId: number;
  prefix: string;
}

export function sourceCodeLensAnnotations(
  candidates: readonly PreparedSourceCodeLensCandidate[],
  labels: ReadonlyMap<string, string>,
): ReadonlyMap<number, readonly SourceCodeLensAnnotation[]> {
  const annotations = new Map<number, SourceCodeLensAnnotation[]>();
  for (const candidate of candidates) {
    if (!labels.has(candidate.kind)) continue;
    const annotation = {
      label: labels.get(candidate.kind) ?? candidate.kind,
      nodeId: candidate.nodeId,
      prefix: candidate.prefix,
    };
    const existing = annotations.get(candidate.lineStart);
    if (existing) existing.push(annotation);
    else annotations.set(candidate.lineStart, [annotation]);
  }
  return annotations;
}

function sourceNodeCaretAnnotations(
  node: AnnotatedSourceNode,
  lines: readonly { start: number; end: number }[],
  text: string,
  kindLabel: string,
): ReadonlyMap<number, readonly SourceNodeCaretAnnotation[]> {
  const annotations = new Map<number, SourceNodeCaretAnnotation[]>();
  let labelPending = true;
  for (const line of lines) {
    for (const span of node.spans) {
      const spanEnd = span.start + span.length;
      const start = Math.max(line.start, span.start);
      const end = Math.min(line.end, spanEnd);
      if (start >= end) continue;
      const coordinates = `[${start}..${end})`;
      const label = labelPending
        ? `${kindLabel} · ${coordinates}`
        : "";
      labelPending = false;
      const annotation = {
        accessibleLabel:
          `Selected source node #${node.id} ${kindLabel}, range ${coordinates}`,
        label,
        length: end - start,
        nodeId: node.id,
        plane: "source" as const,
        prefix: text.slice(line.start, start).replace(/[^\t]/g, " "),
      };
      const existing = annotations.get(line.start);
      if (existing) existing.push(annotation);
      else annotations.set(line.start, [annotation]);
    }
  }
  return annotations;
}

function sourceFindingCaretAnnotations(
  fact: AnnotatedViewFact,
  nodes: readonly AnnotatedSourceNode[],
  lines: readonly {
    end: number;
    medium: SourceMedium | "Mixed";
    number: number;
    start: number;
  }[],
  document: AnnotatedSourceDocument,
  findingEvidence?: {
    evidence: AnnotatedSourceFindingEvidence;
    index: number;
  },
): ReadonlyMap<number, readonly SourceNodeCaretAnnotation[]> {
  const text = document.text;
  const annotations = new Map<number, SourceNodeCaretAnnotation[]>();
  for (const node of nodes) {
    const evidenceNodes = findingEvidence?.evidence.document?.nodes
      .filter(candidate => findingEvidence.evidence.nodeIds.includes(candidate.id))
      ?? [];
    const peekDocument = evidenceNodes.length > 0
      ? findingEvidence!.evidence.document!
      : null;
    const peekNodes = evidenceNodes.length > 0
      ? evidenceNodes
      : findingEvidence
        ? []
        : [node];
    const peekLines = peekDocument
      ? preparedDocument(peekDocument).lines
      : lines;
    const relevantLines = peekLines.filter(line => peekNodes.some(candidate =>
      candidate.spans.some(span =>
        span.start < line.end && line.start < span.start + span.length)));
    const visibleLines = relevantLines.slice(0, MAX_FINDING_PEEK_LINES);
    const lineSummary = relevantLines.length === 0
      ? "unavailable"
      : relevantLines.length === 1
      ? `line ${relevantLines[0]?.number ?? "unknown"}`
      : `lines ${relevantLines[0].number}–${relevantLines.at(-1)?.number}`;
    const peek: SourceFindingPeek = {
      finding: factDescription(fact),
      hiddenLineCount: relevantLines.length - visibleLines.length,
      id: `ase-finding-peek-${fact.id}-${node.id}`,
      lines: visibleLines.map(line => ({
        evidenceSpans: peekNodes.flatMap(candidate => candidate.spans).flatMap(span => {
          const start = Math.max(line.start, span.start);
          const end = Math.min(line.end, span.start + span.length);
          return start < end ? [{ start, length: end - start }] : [];
        }),
        end: line.end,
        medium: line.medium,
        number: line.number,
        start: line.start,
      })),
      location: peekNodes.length > 0
        ? `${MEDIUM_LABELS[peekNodes[0].medium]} · ${lineSummary} · ${peekNodes
          .flatMap(candidate => candidate.spans)
          .map(span => `[${span.start}..${span.start + span.length})`)
          .join(" · ")}`
        : "Callee evidence unavailable",
      ...(findingEvidence
        ? {
            evidenceIndex: findingEvidence.index,
            member: findingEvidence.evidence.member,
            target: findingEvidence.evidence.target,
          }
        : {}),
      ...(findingEvidence?.evidence.unavailableReason
        ? { unavailableReason: findingEvidence.evidence.unavailableReason }
        : {}),
      document: peekDocument ?? document,
    };
    let added = false;
    for (const line of lines) {
      for (const span of node.spans) {
        const start = Math.max(line.start, span.start);
        const end = Math.min(line.end, span.start + span.length);
        if (start >= end) continue;
        const coordinates = `[${start}..${end})`;
        const annotation = {
          accessibleLabel:
            `Finding ${factDescription(fact)}, target range ${coordinates}`,
          label: `${factDescription(fact)} · ${coordinates}`,
          length: end - start,
          nodeId: node.id,
          ...(!findingEvidence && REMOTE_FINDING_DESCRIPTORS.has(fact.descriptor)
            ? {}
            : { peek }),
          plane: "finding" as const,
          prefix: text.slice(line.start, start).replace(/[^\t]/g, " "),
        };
        const existing = annotations.get(line.start);
        if (existing) existing.push(annotation);
        else annotations.set(line.start, [annotation]);
        added = true;
        break;
      }
      if (added) break;
    }
  }
  return annotations;
}

function combineCaretAnnotations(
  sources: readonly ReadonlyMap<number, readonly SourceNodeCaretAnnotation[]>[],
): ReadonlyMap<number, readonly SourceNodeCaretAnnotation[]> {
  const combined = new Map<number, SourceNodeCaretAnnotation[]>();
  for (const source of sources) {
    for (const [lineStart, annotations] of source) {
      const existing = combined.get(lineStart);
      if (existing) existing.push(...annotations);
      else combined.set(lineStart, [...annotations]);
    }
  }
  return combined;
}

function groupCaretAnnotations(
  annotations: readonly SourceNodeCaretAnnotation[],
): SourceCaretGroup[] {
  const groups = new Map<string, {
    accessibleLabels: string[];
    labels: SourceCaretLabel[];
    length: number;
    planes: Set<"source" | "finding">;
    prefix: string;
  }>();
  for (const annotation of annotations) {
    const key = `${annotation.nodeId}\0${annotation.prefix}\0${annotation.length}`;
    let group = groups.get(key);
    if (!group) {
      group = {
        accessibleLabels: [],
        labels: [],
        length: annotation.length,
        planes: new Set(),
        prefix: annotation.prefix,
      };
      groups.set(key, group);
    }
    group.accessibleLabels.push(annotation.accessibleLabel);
    group.planes.add(annotation.plane);
    if (annotation.label
      && !group.labels.some(label =>
        label.plane === annotation.plane
          && label.text === annotation.label
          && label.peek?.id === annotation.peek?.id)) {
      group.labels.push(annotation.peek
        ? {
            peek: annotation.peek,
            plane: annotation.plane,
            text: annotation.label,
          }
        : {
            plane: annotation.plane,
            text: annotation.label,
          });
    }
  }
  return [...groups.values()].map(group => ({
    accessibleLabel: group.accessibleLabels.join("; "),
    labels: group.labels,
    length: group.length,
    planes: group.planes,
    prefix: group.prefix,
  }));
}

function factDescription(fact: AnnotatedViewFact): string {
  return fact.detail ? `${fact.descriptor}: ${fact.detail}` : fact.descriptor;
}

function caretLabelHtml(
  label: SourceCaretLabel,
  escapeHtml: EscapeHtml,
): string {
  const cssClass = `annotated-caret-label plane-${label.plane}`;
  if (!label.peek) {
    return `<span class="${cssClass}">${escapeHtml(label.text)}</span>`;
  }
  return `<button type="button" tabindex="-1" class="${cssClass}" data-ase-source-affordance data-ase-finding-peek="${label.peek.id}" popovertarget="${label.peek.id}" aria-label="Show evidence for Finding ${escapeHtml(label.peek.finding)}">${escapeHtml(label.text)}</button>`;
}

function findingPeekHtml(
  peek: SourceFindingPeek,
  tokenizeCSharp: CSharpTokenizer | undefined,
  escapeHtml: EscapeHtml,
): string {
  const code = peek.lines.map(line => {
    const lineText = peek.document.text.slice(line.start, line.end);
    const syntaxRanges = line.medium === "CSharp" && tokenizeCSharp
      ? syntaxRangesForDocumentLine(peek.document, tokenizeCSharp, lineText, line.start)
      : [];
    return `<span class="finding-peek-code-line"><span class="finding-peek-line-number">${line.number}</span><span class="finding-peek-line-text">${renderSegmentText(
      line.start,
      line.end,
      peek.document.text,
      syntaxRanges,
      line.evidenceSpans,
      escapeHtml,
      ["finding-peek-evidence"])}</span></span>`;
  }).join("");
  return `<aside id="${peek.id}" class="finding-peek" popover="auto" aria-label="Finding evidence">
      <header><strong>Finding evidence</strong><button type="button" popovertarget="${peek.id}" popovertargetaction="hide" aria-label="Close Finding evidence">×</button></header>
      <dl>
        <div><dt>Finding</dt><dd>${escapeHtml(peek.finding)}</dd></div>
        ${peek.member && peek.evidenceIndex !== undefined
          ? `<div class="finding-peek-member"><dt>Member</dt><dd><code>${escapeHtml(peek.member)}</code><span><button type="button" data-ase-finding-member-copy="${escapeHtml(peek.member)}">copy member</button><button type="button" data-ase-finding-member-navigate="${peek.evidenceIndex}">navigate</button></span></dd></div>`
          : ""}
        <div><dt>Location</dt><dd>${escapeHtml(peek.location)}</dd></div>
        <div class="finding-peek-code"><dt>Code</dt><dd>${peek.unavailableReason ? `<p>${escapeHtml(peek.unavailableReason)}</p>` : `<pre><code>${code}</code></pre>${peek.hiddenLineCount > 0 ? `<small>${peek.hiddenLineCount} additional relevant line${peek.hiddenLineCount === 1 ? "" : "s"} omitted</small>` : ""}`}</dd></div>
      </dl>
    </aside>`;
}

function preparedDocument(document: AnnotatedSourceDocument): PreparedAnnotatedView {
  const existing = preparedDocuments.get(document);
  if (existing) return existing;
  const prepared = prepareAnnotatedView(document);
  preparedDocuments.set(document, prepared);
  return prepared;
}

function hasVisibleSource(
  prepared: PreparedAnnotatedView,
  media: Readonly<Record<SourceMedium, boolean>>,
): boolean {
  return prepared.lines.some(line =>
    line.medium === "Mixed"
      ? media.CSharp || media.Il
      : media[line.medium]);
}

function limitationHtml(result: AnnotatedSourceResult, escapeHtml: EscapeHtml): string {
  return result.contextLimitation
    ? `<p class="annotated-limitation">The whole-assembly fact context was narrowed, so this fact list is incomplete: ${escapeHtml(result.contextLimitation)}</p>`
    : "";
}

function countHtml(count: number, label: string, escapeHtml: EscapeHtml): string {
  const plural = count === 1 || label === "unanchored" ? label : `${label}s`;
  return `<span><strong>${count}</strong>${escapeHtml(plural)}</span>`;
}

function nodeKindCounts(
  document: AnnotatedSourceDocument,
  catalog: readonly AnnotatedSourceKindOption[],
): { id: string; label: string; count: number }[] {
  const counts = new Map<string, number>();
  for (const node of document.nodes) {
    if (node.medium === "CSharp") counts.set(node.kind, (counts.get(node.kind) ?? 0) + 1);
  }
  const seen = new Set<string>();
  const known = catalog.flatMap(option => {
    const count = counts.get(option.id);
    if (count === undefined) return [];
    seen.add(option.id);
    return [{ ...option, count }];
  });
  return [
    ...known,
    ...[...counts]
      .filter(([id]) => !seen.has(id) && id !== "MemberBody" && id !== "Block")
      .map(([id, count]) => ({ id, label: id, count })),
  ];
}

function selectionTitle(
  selectedFactId: number | null,
  selectedCapture: AnnotatedViewCapture | null,
  nodes: readonly AnnotatedSourceNode[],
  selectedKindLabel: string,
  selectedRegionRole: string,
  selectedRegionCount: number,
): string {
  if (selectedFactId !== null) return `Fact #${selectedFactId} targets`;
  if (selectedCapture) {
    return `${selectedCapture.displayName} · ${selectedCapture.useNodeIds.length} captured use${selectedCapture.useNodeIds.length === 1 ? "" : "s"}`;
  }
  if (selectedKindLabel) return `${nodes.length} ${selectedKindLabel} nodes`;
  if (selectedRegionRole) {
    return `${selectedRegionCount} ${selectedRegionRole} region${selectedRegionCount === 1 ? "" : "s"}`;
  }
  if (nodes.length === 1) return `Node #${nodes[0].id}`;
  if (nodes.length > 1) return `${nodes.length} nodes`;
  return "Nothing selected";
}

function captureSectionHtml(
  captures: readonly AnnotatedViewCapture[],
  nodeById: ReadonlyMap<number, AnnotatedSourceNode>,
  labels: ReadonlyMap<string, string>,
  escapeHtml: EscapeHtml,
): string {
  if (captures.length === 0) return "";
  return `<section class="ase-inspector-section">
      <div class="ase-section-heading"><div><span>Closure plane</span><strong>Captured variables</strong></div><em>${captures.length}</em></div>
      <p class="ase-structure-help">Select a variable to reveal its recovered nested-function scope and exact addressable uses.</p>
      <div class="ase-capture-list">${captures.map(capture => {
        const parent = nodeById.get(capture.parentNodeId);
        const parentLabel = parent ? labels.get(parent.kind) ?? parent.kind : "Nested function";
        return `<button type="button" class="${capture.selected ? "selected" : ""}" data-ase-capture="${capture.index}" aria-pressed="${capture.selected}">
            <span><strong>${escapeHtml(capture.displayName)}</strong><em>${escapeHtml(parentLabel)} #${capture.parentNodeId}</em></span>
            <small>${capture.useNodeIds.length} addressable use${capture.useNodeIds.length === 1 ? "" : "s"}</small>
          </button>`;
      }).join("")}</div>
    </section>`;
}

function captureSelectionHtml(
  capture: AnnotatedViewCapture,
  nodeById: ReadonlyMap<number, AnnotatedSourceNode>,
  labels: ReadonlyMap<string, string>,
  escapeHtml: EscapeHtml,
): string {
  const parent = nodeById.get(capture.parentNodeId);
  const parentLabel = parent ? labels.get(parent.kind) ?? parent.kind : "Nested function";
  const uses = capture.useNodeIds
    .map(id => nodeById.get(id))
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
  return `<div class="ase-capture-selection">
      <p><span>Captured by</span><strong>${escapeHtml(parentLabel)} #${capture.parentNodeId}</strong></p>
      ${selectionHtml(uses, escapeHtml)}
    </div>`;
}

function sourceNodeSelectionHtml(
  node: AnnotatedSourceNode,
  facts: readonly AnnotatedViewFact[],
  labels: ReadonlyMap<string, string>,
  activeFactIds: ReadonlySet<number>,
  escapeHtml: EscapeHtml,
): string {
  const kindLabel = labels.get(node.kind) ?? node.kind;
  return `<div class="ase-source-node-selection">
      <p><span>Exact source node</span><strong>#${node.id} ${escapeHtml(kindLabel)}</strong></p>
      ${selectionHtml([node], escapeHtml)}
      ${facts.length === 0
        ? `<p class="ase-node-facts-empty">No Findings target this source node.</p>`
        : `<div class="ase-node-facts">
            <span>Findings at this node</span>
            ${facts.map(fact => factHtml(fact, activeFactIds, escapeHtml)).join("")}
          </div>`}
    </div>`;
}

function regionSelectionHtml(
  regions: readonly AnnotatedSourceRegion[],
  prepared: PreparedAnnotatedView,
  escapeHtml: EscapeHtml,
): string {
  const visible = regions.slice(0, MAX_SELECTION_DETAILS);
  const overflow = regions.length - visible.length;
  return `<div class="ase-selection-list">${visible.map((region, index) => `
      <button type="button" class="ase-region-selection" data-ase-offset="${region.spans[0].start}">
        <span><strong>${escapeHtml(region.role)} region ${index + 1}</strong></span>
        <small>${escapeHtml(regionLineSummary(region, prepared))}</small>
      </button>`).join("")}
      ${overflow > 0 ? `<p class="ase-overflow">${overflow} more regions; choose a source span to inspect its enclosing structure.</p>` : ""}
    </div>`;
}

function regionLineSummary(
  region: AnnotatedSourceRegion,
  prepared: PreparedAnnotatedView,
): string {
  const lineNumbers = prepared.lines
    .filter(line => region.spans.some(
      span => span.start < line.end && line.start < span.start + span.length))
    .map(line => line.number);
  if (lineNumbers.length === 0) return "No rendered lines";
  if (lineNumbers.length === 1) return `Line ${lineNumbers[0]}`;
  return `Lines ${lineNumbers[0]}–${lineNumbers.at(-1)}`;
}

interface SyntaxRange {
  start: number;
  end: number;
  classes: readonly string[];
}

function syntaxRangesForLine(
  tokens: readonly CSharpSyntaxToken[],
  lineText: string,
  lineStart: number,
): SyntaxRange[] {
  if (tokens.map(token => token.text).join("") !== lineText) return [];
  let start = lineStart;
  return tokens.map(token => {
    const range = {
      start,
      end: start + token.text.length,
      classes: token.classes.filter(cssClass => /^[A-Za-z0-9_-]+$/.test(cssClass)),
    };
    start = range.end;
    return range;
  });
}

function syntaxRangesForDocumentLine(
  document: AnnotatedSourceDocument,
  tokenizer: CSharpTokenizer,
  lineText: string,
  lineStart: number,
): readonly SyntaxRange[] {
  let tokenizers = preparedSyntax.get(document);
  if (!tokenizers) {
    tokenizers = new WeakMap();
    preparedSyntax.set(document, tokenizers);
  }
  let lines = tokenizers.get(tokenizer);
  if (!lines) {
    lines = new Map();
    tokenizers.set(tokenizer, lines);
  }
  const cached = lines.get(lineStart);
  if (cached) return cached;
  const ranges = syntaxRangesForLine(tokenizer(lineText), lineText, lineStart);
  lines.set(lineStart, ranges);
  return ranges;
}

function renderSegmentText(
  segmentStart: number,
  segmentEnd: number,
  text: string,
  syntaxRanges: readonly SyntaxRange[],
  selectedRegionSpans: readonly { start: number; length: number }[],
  escapeHtml: EscapeHtml,
  selectedClasses: readonly string[] = ["annotated-region", "selected"],
): string {
  const boundaries = new Set([segmentStart, segmentEnd]);
  for (const range of syntaxRanges) {
    if (range.start < segmentEnd && segmentStart < range.end) {
      boundaries.add(Math.max(segmentStart, range.start));
      boundaries.add(Math.min(segmentEnd, range.end));
    }
  }
  for (const span of selectedRegionSpans) {
    const end = span.start + span.length;
    if (span.start < segmentEnd && segmentStart < end) {
      boundaries.add(Math.max(segmentStart, span.start));
      boundaries.add(Math.min(segmentEnd, end));
    }
  }
  const ordered = [...boundaries].sort((left, right) => left - right);
  return ordered.slice(0, -1).map((start, index) => {
    const end = ordered[index + 1];
    const syntax = syntaxRanges.find(range => range.start <= start && end <= range.end);
    const inRegion = selectedRegionSpans.some(
      span => span.start <= start && end <= span.start + span.length);
    const classes = [
      ...(syntax?.classes.length ? ["token", ...syntax.classes] : []),
      ...(inRegion ? selectedClasses : []),
    ];
    const content = escapeHtml(text.slice(start, end));
    return classes.length > 0
      ? `<span class="${classes.map(escapeHtml).join(" ")}">${content}</span>`
      : content;
  }).join("");
}

function selectionHtml(
  nodes: readonly AnnotatedSourceNode[],
  escapeHtml: EscapeHtml,
): string {
  if (nodes.length === 0) {
    return `<p class="ase-empty">Select a Finding or source substring.</p>`;
  }
  const visible = nodes.slice(0, MAX_SELECTION_DETAILS);
  const overflow = nodes.length - visible.length;
  return `<div class="ase-selection-list">${visible.map(node => `
      <button type="button" data-ase-node="${node.id}">
        <span><strong>#${node.id} ${escapeHtml(node.kind)}</strong><em>${escapeHtml(node.medium)}</em></span>
        <small>${escapeHtml(node.spans.map(span => `[${span.start}..${span.start + span.length})`).join(" · "))}${node.il_offset == null ? "" : ` · IL_${node.il_offset.toString(16).padStart(4, "0").toUpperCase()}`}</small>
      </button>`).join("")}
      ${overflow > 0 ? `<p class="ase-overflow">${overflow} more selected nodes; narrow the node kind or click source text to inspect one.</p>` : ""}
    </div>`;
}

function factListHtml(
  facts: readonly AnnotatedViewFact[],
  activeFactIds: ReadonlySet<number>,
  escapeHtml: EscapeHtml,
  emptyText: string,
): string {
  if (facts.length === 0) return `<p class="ase-empty">${escapeHtml(emptyText)}</p>`;
  return facts.map(fact => factHtml(fact, activeFactIds, escapeHtml)).join("");
}

function factHtml(
  fact: AnnotatedViewFact,
  activeFactIds: ReadonlySet<number>,
  escapeHtml: EscapeHtml,
): string {
  return `<button type="button" class="annotated-fact${fact.selected ? " selected" : ""}${fact.anchored ? "" : " unanchored"}" data-ase-fact="${fact.id}" aria-pressed="${activeFactIds.has(fact.id)}">
      <span class="annotated-fact-descriptor">${escapeHtml(fact.descriptor)}</span>
      <span class="annotated-fact-category">${escapeHtml(fact.category)}</span>
      ${fact.detail ? `<span class="annotated-fact-detail">${escapeHtml(fact.detail)}</span>` : ""}
      <span class="annotated-fact-conditionality">${escapeHtml(fact.conditionality)}</span>
      <span class="annotated-fact-anchor">${fact.anchored ? `${fact.nodeIds.length} target${fact.nodeIds.length === 1 ? "" : "s"}` : "unanchored"}</span>
    </button>`;
}
