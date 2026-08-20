import {
  factsForNode,
  MEDIUM_LABELS,
  MEDIA,
  nodeAtOffset,
  prepareAnnotatedView,
  projectPreparedAnnotatedView,
  type AnnotatedSourceDocument,
  type AnnotatedSourceNode,
  type AnnotatedViewFact,
  type PreparedAnnotatedView,
  type SourceMedium,
  validateAnnotatedSourceDocument,
} from "./annotated-source-view.ts";

export interface AnnotatedSourceResult {
  document: AnnotatedSourceDocument;
  provenance: string;
  contextLimitation?: string | null;
}

export interface AnnotatedSourceExplorerState {
  prepared: PreparedAnnotatedView;
  media: Record<SourceMedium, boolean>;
  selectedFactId: number | null;
  selectedNodeIds: readonly number[];
  selectedKind: string;
}

export type AnnotatedSourceExplorerAction =
  | { type: "toggle-medium"; medium: SourceMedium }
  | { type: "select-fact"; factId: number }
  | { type: "select-node"; nodeId: number }
  | { type: "select-offset"; offset: number }
  | { type: "select-kind"; kind: string }
  | { type: "clear-selection" };

type EscapeHtml = (value: unknown) => string;

export interface AnnotatedSourceEntryOptions {
  result: AnnotatedSourceResult;
  escapeHtml: EscapeHtml;
}

export interface AnnotatedSourceExplorerOptions extends AnnotatedSourceEntryOptions {
  state: AnnotatedSourceExplorerState;
  title: string;
  subtitle: string;
}

const MAX_SELECTION_DETAILS = 50;
const preparedDocuments = new WeakMap<AnnotatedSourceDocument, PreparedAnnotatedView>();

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
    selectedFactId: initial.selectedFactId ?? null,
    selectedNodeIds: [...new Set(initial.selectedNodeIds ?? [])],
    selectedKind: initial.selectedKind ?? "",
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
    case "select-fact": {
      if (!document.facts.some(fact => fact.id === action.factId)) return state;
      const selectedFactId = state.selectedFactId === action.factId ? null : action.factId;
      return {
        ...state,
        selectedFactId,
        selectedNodeIds: [],
        selectedKind: "",
      };
    }
    case "select-node":
      if (!document.nodes.some(node => node.id === action.nodeId)) return state;
      return {
        ...state,
        selectedFactId: null,
        selectedNodeIds: [action.nodeId],
        selectedKind: "",
      };
    case "select-offset": {
      const node = nodeAtOffset(document, action.offset);
      const owningFacts = node ? factsForNode(document, node.id) : [];
      return {
        ...state,
        selectedFactId: owningFacts.length === 1 ? owningFacts[0].id : null,
        selectedNodeIds: node ? [node.id] : [],
        selectedKind: "",
      };
    }
    case "select-kind":
      if (action.kind && !document.nodes.some(node => node.kind === action.kind)) return state;
      return {
        ...state,
        selectedFactId: null,
        selectedNodeIds: action.kind
          ? document.nodes.filter(node => node.kind === action.kind).map(node => node.id)
          : [],
        selectedKind: action.kind,
      };
    case "clear-selection":
      return {
        ...state,
        selectedFactId: null,
        selectedNodeIds: [],
        selectedKind: "",
      };
  }
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
  const { result, state, title, subtitle, escapeHtml } = options;
  if (state.prepared.document !== result.document) {
    throw new Error("The annotated source explorer state belongs to a different document.");
  }
  const view = projectPreparedAnnotatedView(state.prepared, state);
  const nodeById = new Map(result.document.nodes.map(node => [node.id, node]));
  const selectedNodes = view.selectedNodeIds
    .map(id => nodeById.get(id))
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
  const unanchoredIds = new Set(view.unanchoredFactIds);
  const anchoredFacts = view.facts.filter(fact => !unanchoredIds.has(fact.id));
  const unanchoredFacts = view.facts.filter(fact => unanchoredIds.has(fact.id));
  const kindCounts = nodeKindCounts(result.document);

  const mediumButtons = MEDIA.map(medium =>
    `<button type="button" class="annotated-medium${view.media[medium] ? " on" : ""}" data-ase-medium="${medium}" aria-pressed="${view.media[medium]}">${escapeHtml(MEDIUM_LABELS[medium])}</button>`,
  ).join("");
  const kindOptions = [
    `<option value="">all node kinds</option>`,
    ...kindCounts.map(([kind, count]) =>
      `<option value="${escapeHtml(kind)}"${state.selectedKind === kind ? " selected" : ""}>${escapeHtml(kind)} · ${count}</option>`),
  ].join("");
  const lines = view.lines.map(line => {
    const segments = line.segments.map(segment => {
      const nodes = segment.nodeIds
        .map(id => nodeById.get(id))
        .filter((node): node is AnnotatedSourceNode => node !== undefined);
      const addressable = nodes.length > 0;
      const titleText = nodes.map(node => `#${node.id} ${node.kind}`).join(" · ");
      if (addressable) {
        return `<button type="button" tabindex="-1" class="annotated-span addressable${segment.selected ? " selected" : ""}" data-ase-offset="${segment.start}" title="${escapeHtml(titleText)}">${escapeHtml(segment.text)}</button>`;
      }
      return `<span class="annotated-span${segment.selected ? " selected" : ""}">${escapeHtml(segment.text)}</span>`;
    }).join("");
    const mediumLabel = line.medium === "Mixed" ? "C#/IL" : MEDIUM_LABELS[line.medium];
    return `<div class="annotated-line medium-${line.medium.toLowerCase()}">
      <span class="annotated-line-number">${line.number}</span>
      <span class="annotated-line-medium">${escapeHtml(mediumLabel)}</span>
      <span class="annotated-line-text">${segments || "&nbsp;"}</span>
    </div>`;
  }).join("");

  return `<div class="annotated-explorer" role="dialog" aria-modal="true" aria-label="Annotated source explorer">
      <header class="ase-bar">
        <button id="ase-exit" class="ase-exit" type="button" title="Exit the explorer">✕ Exit</button>
        <div class="ase-title">
          <strong>${escapeHtml(title)}</strong>
          <span>${escapeHtml(subtitle)}</span>
        </div>
        <label class="ase-kind-filter">node kind<select id="ase-node-kind">${kindOptions}</select></label>
        <div class="ase-media" role="group" aria-label="Visible source media">${mediumButtons}</div>
        <button id="ase-copy" class="ase-copy" type="button">copy source</button>
      </header>
      ${limitationHtml(result, escapeHtml)}
      <div class="ase-workspace">
        <section class="ase-code-panel" aria-label="Annotated source text">
          <div class="ase-panel-heading">
            <div><span>Canonical text</span><strong>Finding overlays</strong></div>
            <p>${result.document.nodes.length} nodes · ${result.document.targets.length} targets${view.hiddenLines ? ` · ${view.hiddenLines} hidden lines` : ""}</p>
          </div>
          <div class="ase-code-scroll" tabindex="0" aria-label="Annotated source text. Use arrow keys to move between structural spans.">
            <pre class="annotated-text"><code>${lines}</code></pre>
          </div>
        </section>
        <aside class="ase-inspector">
          <section class="ase-inspector-section">
            <div class="ase-section-heading">
              <div><span>Selection</span><strong>${escapeHtml(selectionTitle(view.selectedFactId, selectedNodes, state.selectedKind))}</strong></div>
              ${view.selectedFactId !== null || selectedNodes.length > 0 ? `<button id="ase-clear" type="button">clear</button>` : ""}
            </div>
            ${selectionHtml(selectedNodes, escapeHtml)}
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

function nodeKindCounts(document: AnnotatedSourceDocument): [string, number][] {
  const counts = new Map<string, number>();
  for (const node of document.nodes) counts.set(node.kind, (counts.get(node.kind) ?? 0) + 1);
  return [...counts];
}

function selectionTitle(
  selectedFactId: number | null,
  nodes: readonly AnnotatedSourceNode[],
  selectedKind: string,
): string {
  if (selectedFactId !== null) return `Fact #${selectedFactId} targets`;
  if (selectedKind) return `${nodes.length} ${selectedKind} nodes`;
  if (nodes.length === 1) return `Node #${nodes[0].id}`;
  if (nodes.length > 1) return `${nodes.length} nodes`;
  return "Nothing selected";
}

function selectionHtml(
  nodes: readonly AnnotatedSourceNode[],
  escapeHtml: EscapeHtml,
): string {
  if (nodes.length === 0) {
    return `<p class="ase-empty">Select a fact, node kind, or source substring.</p>`;
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
  return `<button type="button" class="annotated-fact${fact.selected ? " selected" : ""}${fact.anchored ? "" : " unanchored"}" data-ase-fact="${fact.id}">
      <span class="annotated-fact-descriptor">${escapeHtml(fact.descriptor)}</span>
      <span class="annotated-fact-category">${escapeHtml(fact.category)}</span>
      ${fact.detail ? `<span class="annotated-fact-detail">${escapeHtml(fact.detail)}</span>` : ""}
      <span class="annotated-fact-conditionality">${escapeHtml(fact.conditionality)}</span>
      <span class="annotated-fact-anchor">${fact.anchored ? `${fact.nodeIds.length} target${fact.nodeIds.length === 1 ? "" : "s"}` : "unanchored"}</span>
    </button>`;
}
