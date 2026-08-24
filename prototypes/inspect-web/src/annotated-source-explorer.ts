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
  type PreparedAnnotatedView,
  type SourceMedium,
  validateAnnotatedSourceDocument,
} from "./annotated-source-view.ts";
import type { BrowserAnnotatedSource } from "./inspect-web-engine.d.ts";

export type AnnotatedSourceResult = Omit<BrowserAnnotatedSource, "document"> & {
  document: AnnotatedSourceDocument;
};

export interface AnnotatedSourceExplorerState {
  prepared: PreparedAnnotatedView;
  media: Record<SourceMedium, boolean>;
  codeLens: boolean;
  selectedFactId: number | null;
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
}

const MAX_SELECTION_DETAILS = 50;
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
    const targets = [...root.querySelectorAll<HTMLElement>(
      `[data-ase-node-ids~="${nodeId}"]`,
    )];
    targets.forEach(target => {
      target.addEventListener("animationend", event => {
        if (event.animationName !== "ase-codelens-preview") return;
        target.classList.toggle("codelens-preview", false);
        if (!targets.some(span => span.classList.contains("codelens-preview"))) {
          button.classList.toggle("previewing", false);
        }
      });
    });
    button.addEventListener("click", () => {
      for (const target of targets) {
        target.classList.toggle("codelens-preview", false);
        void target.offsetWidth;
        target.classList.toggle("codelens-preview", true);
      }
      button.classList.toggle("previewing", targets.length > 0);
    });
  });
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
  code?.addEventListener("keydown", event => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const spans = [...code.querySelectorAll<HTMLElement>("[data-ase-offset]")];
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
  const media = {
    CSharp: initial.media?.CSharp !== false,
    Il: initial.media?.Il !== false,
  };
  if (!MEDIA.some(medium => media[medium])) media.CSharp = true;

  return {
    prepared: preparedDocument(document),
    media,
    codeLens: initial.codeLens !== false,
    selectedFactId: initial.selectedFactId ?? null,
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
      return MEDIA.some(medium => media[medium]) ? { ...state, media } : state;
    }
    case "toggle-codelens":
      return { ...state, codeLens: !state.codeLens };
    case "select-fact": {
      if (!document.facts.some(fact => fact.id === action.factId)) return state;
      const selectedFactId = state.selectedFactId === action.factId ? null : action.factId;
      return {
        ...state,
        selectedFactId,
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
        selectedNodeIds: [],
        selectedKind: "",
        selectedRegionRole: "",
      };
    }
    case "select-node":
      if (!document.nodes.some(node => node.id === action.nodeId)) return state;
      return {
        ...state,
        selectedCaptureIndex: null,
        selectedNodeIds:
          state.selectedNodeIds.length === 1 && state.selectedNodeIds[0] === action.nodeId
            ? []
            : [action.nodeId],
        selectedKind: "",
        selectedRegionRole: "",
      };
    case "select-offset": {
      const node = nodeAtOffset(document, action.offset);
      return {
        ...state,
        selectedCaptureIndex: null,
        selectedNodeIds: node
          ? state.selectedNodeIds.length === 1 && state.selectedNodeIds[0] === node.id
            ? []
            : [node.id]
          : [],
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
        selectedCaptureIndex: null,
        selectedNodeIds: [],
        selectedKind: "",
        selectedRegionRole: "",
      };
  }
  const unhandledAction: never = action;
  throw new Error(`Unsupported annotated source explorer action: ${String(unhandledAction)}`);
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
  const { result, state, title, subtitle, escapeHtml, tokenizeCSharp } = options;
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
  const codeLensAnnotations = sourceCodeLensAnnotations(
    result.document.nodes,
    view.lines,
    kindLabels,
    result.document.text);
  const codeLensNodeIds = new Set(
    [...codeLensAnnotations.values()].flat().map(annotation => annotation.nodeId));
  const nodeCaretAnnotations = directlySelectedNode
    && !codeLensNodeIds.has(directlySelectedNode.id)
    ? sourceNodeCaretAnnotations(
        directlySelectedNode,
        view.lines,
        result.document.text,
        kindLabels.get(directlySelectedNode.kind) ?? directlySelectedNode.kind,
        directlySelectedNodeFacts)
    : new Map<number, readonly SourceNodeCaretAnnotation[]>();
  const selectedFact = view.selectedFactId === null ? null : view.facts[view.selectedFactId];
  const findingCaretAnnotations = selectedFact
    ? sourceFindingCaretAnnotations(
        selectedFact,
        selectedFact.nodeIds
          .map(id => nodeById.get(id))
          .filter((node): node is AnnotatedSourceNode => node !== undefined),
        view.lines,
        result.document.text)
    : new Map<number, readonly SourceNodeCaretAnnotation[]>();

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
        return `<button type="button" tabindex="-1" class="annotated-span addressable${factCount > 0 ? " has-fact" : ""}${captureCount > 0 ? " has-capture" : ""}${captureScope ? " capture-scope" : ""}${suppressPersistentStructure ? "" : selectionClass}" data-ase-offset="${segment.start}" data-ase-node-ids="${segment.nodeIds.join(" ")}" title="${escapeHtml(titleText)}"${accessibleLabel}>${content}</button>`;
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
            <span class="annotated-line-text">${escapeHtml(annotation.prefix)}<button type="button" data-ase-codelens-node="${annotation.nodeId}" title="Preview ${escapeHtml(annotation.label)} for six seconds">${escapeHtml(annotation.label)}</button></span>
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
    const carets = [
      ...(nodeCaretAnnotations.get(line.start) ?? []),
      ...(findingCaretAnnotations.get(line.start) ?? []),
    ].map(annotation =>
      `<div class="annotated-node-caret ${annotation.plane}" aria-label="${escapeHtml(annotation.accessibleLabel)}">
        <span class="annotated-line-number"></span>
        ${showMediumGutter ? `<span class="annotated-line-medium"></span>` : ""}
        <span class="annotated-line-text" aria-hidden="true">${escapeHtml(annotation.prefix)}<span class="annotated-caret-run">${"^".repeat(annotation.length)}</span>${annotation.label ? `<span class="annotated-caret-label"> ${escapeHtml(annotation.label)}</span>` : ""}</span>
      </div>`,
    ).join("") ?? "";
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
          <div class="ase-code-scroll" tabindex="0" aria-label="Annotated source text. Use arrow keys to move between structural spans.">
            <pre class="annotated-text"><code>${lines}</code></pre>
          </div>
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
              ${view.selectedFactId !== null || selectedCapture !== null || selectedNodes.length > 0 || selectedRegions.length > 0 ? `<button id="ase-clear" type="button">clear</button>` : ""}
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
                      escapeHtml)
                  : selectionHtml(selectedNodes, escapeHtml)}
          </section>
          <section class="ase-inspector-section">
            <div class="ase-section-heading"><div><span>Semantic plane</span><strong>Anchored facts</strong></div><em>${anchoredFacts.length}</em></div>
            <div class="ase-fact-list">${factListHtml(anchoredFacts, escapeHtml, "No anchored facts were observed.")}</div>
          </section>
          <section class="ase-inspector-section">
            <div class="ase-section-heading"><div><span>No invented coordinate</span><strong>Unanchored facts</strong></div><em>${unanchoredFacts.length}</em></div>
            <div class="ase-fact-list">${factListHtml(unanchoredFacts, escapeHtml, "None")}</div>
          </section>
        </aside>
      </div>
    </div>`;
}

interface SourceNodeCaretAnnotation {
  accessibleLabel: string;
  label: string;
  length: number;
  plane: "source" | "finding";
  prefix: string;
}

interface SourceCodeLensAnnotation {
  label: string;
  nodeId: number;
  prefix: string;
}

function sourceCodeLensAnnotations(
  nodes: readonly AnnotatedSourceNode[],
  lines: readonly { start: number; end: number }[],
  labels: ReadonlyMap<string, string>,
  text: string,
): ReadonlyMap<number, readonly SourceCodeLensAnnotation[]> {
  const annotations = new Map<number, SourceCodeLensAnnotation[]>();
  for (const node of nodes) {
    if (node.medium !== "CSharp"
      || node.kind === "Block"
      || node.kind === "MemberBody"
      || !labels.has(node.kind)) {
      continue;
    }
    const intersectingLines = lines.filter(line => node.spans.some(span =>
      span.start < line.end && line.start < span.start + span.length));
    if (intersectingLines.length < 2) continue;
    const lineStart = intersectingLines[0].start;
    const lineText = text.slice(lineStart, intersectingLines[0].end);
    const annotation = {
      label: labels.get(node.kind) ?? node.kind,
      nodeId: node.id,
      prefix: lineText.match(/^[\t ]*/)?.[0] ?? "",
    };
    const existing = annotations.get(lineStart);
    if (existing) existing.push(annotation);
    else annotations.set(lineStart, [annotation]);
  }
  return annotations;
}

function sourceNodeCaretAnnotations(
  node: AnnotatedSourceNode,
  lines: readonly { start: number; end: number }[],
  text: string,
  kindLabel: string,
  facts: readonly AnnotatedViewFact[],
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
      const factSummary = facts.length === 0
        ? ""
        : ` · ${facts.length} finding${facts.length === 1 ? "" : "s"}: ${facts
            .map(factDescription)
            .join(", ")}`;
      const label = labelPending
        ? `#${node.id} ${kindLabel} · ${coordinates}${factSummary}`
        : "";
      labelPending = false;
      const annotation = {
        accessibleLabel:
          `Selected source node #${node.id} ${kindLabel}, range ${coordinates}${factSummary}`,
        label,
        length: end - start,
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
  lines: readonly { start: number; end: number }[],
  text: string,
): ReadonlyMap<number, readonly SourceNodeCaretAnnotation[]> {
  const annotations = new Map<number, SourceNodeCaretAnnotation[]>();
  for (const node of nodes) {
    let added = false;
    for (const line of lines) {
      for (const span of node.spans) {
        const start = Math.max(line.start, span.start);
        const end = Math.min(line.end, span.start + span.length);
        if (start >= end) continue;
        const coordinates = `[${start}..${end})`;
        const annotation = {
          accessibleLabel:
            `Selected Finding ${factDescription(fact)}, target range ${coordinates}`,
          label: `${factDescription(fact)} · ${coordinates}`,
          length: end - start,
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

function factDescription(fact: AnnotatedViewFact): string {
  return fact.detail ? `${fact.descriptor}: ${fact.detail}` : fact.descriptor;
}

function preparedDocument(document: AnnotatedSourceDocument): PreparedAnnotatedView {
  const existing = preparedDocuments.get(document);
  if (existing) return existing;
  const prepared = prepareAnnotatedView(document);
  preparedDocuments.set(document, prepared);
  return prepared;
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
            ${facts.map(fact => factHtml(fact, escapeHtml)).join("")}
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
      ...(inRegion ? ["annotated-region", "selected"] : []),
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
  escapeHtml: EscapeHtml,
  emptyText: string,
): string {
  if (facts.length === 0) return `<p class="ase-empty">${escapeHtml(emptyText)}</p>`;
  return facts.map(fact => factHtml(fact, escapeHtml)).join("");
}

function factHtml(fact: AnnotatedViewFact, escapeHtml: EscapeHtml): string {
  return `<button type="button" class="annotated-fact${fact.selected ? " selected" : ""}${fact.anchored ? "" : " unanchored"}" data-ase-fact="${fact.id}" aria-pressed="${fact.selected}">
      <span class="annotated-fact-descriptor">${escapeHtml(fact.descriptor)}</span>
      <span class="annotated-fact-category">${escapeHtml(fact.category)}</span>
      ${fact.detail ? `<span class="annotated-fact-detail">${escapeHtml(fact.detail)}</span>` : ""}
      <span class="annotated-fact-conditionality">${escapeHtml(fact.conditionality)}</span>
      <span class="annotated-fact-anchor">${fact.anchored ? `${fact.nodeIds.length} target${fact.nodeIds.length === 1 ? "" : "s"}` : "unanchored"}</span>
    </button>`;
}
