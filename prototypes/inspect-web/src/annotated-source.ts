import type { BrowserAnnotatedSource } from "./inspect-web-engine.d.ts";
import type {
  AnnotatedSourceDocument,
  AnnotatedViewState,
} from "./annotated-source-view.ts";
import { buildAnnotatedView, MEDIA, MEDIUM_LABELS } from "./annotated-source-view.ts";

// BrowserAnnotatedSource's "document" field is generated as `unknown` because tsbindgen doesn't
// model the wire shape of the annotated-source document graph, only which fields are DTO
// boundaries. AnnotatedSourceDocument (annotated-source-view.ts) is the product-owned structural
// model of that same JSON payload, coupled to document-model.ts's runtime validation — so this
// narrows the generated field rather than re-declaring the outer DTO shape independently.
export type AnnotatedSourceResult = Omit<BrowserAnnotatedSource, "document"> & {
  document: AnnotatedSourceDocument;
};

export interface RenderAnnotatedSourceOptions {
  result: BrowserAnnotatedSource;
  media: AnnotatedViewState["media"];
  selectedFactId: AnnotatedViewState["selectedFactId"];
  selectedNodeIds: AnnotatedViewState["selectedNodeIds"];
  escapeHtml: (value: unknown) => string;
}

export function renderAnnotatedSource(options: RenderAnnotatedSourceOptions): string {
  const { result, media, selectedFactId, selectedNodeIds, escapeHtml } = options;
  let view;
  try {
    view = buildAnnotatedView(result.document, {
      media,
      selectedFactId,
      selectedNodeIds,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return `<section class="document-section empty-member-section"><h2>Annotated source document rejected</h2><p>${escapeHtml(message)}</p></section>`;
  }

  const toggles = MEDIA.map(medium =>
    `<button type="button" class="annotated-medium${view.media[medium] ? " on" : ""}" data-annotated-medium="${medium}" aria-pressed="${view.media[medium]}">${escapeHtml(MEDIUM_LABELS[medium])}</button>`).join("");

  const lines = view.lines.map(line => {
    const segments = line.segments.map(segment =>
      `<span class="annotated-span${segment.selected ? " selected" : ""}" data-annotated-offset="${segment.start}">${escapeHtml(segment.text)}</span>`).join("");
    return `<div class="annotated-line medium-${line.medium.toLowerCase()}"><span class="annotated-line-number">${line.number}</span><span class="annotated-line-text">${segments || "&nbsp;"}</span></div>`;
  }).join("");

  const facts = view.facts.length === 0
    ? `<li class="annotated-fact empty">No facts were observed about this member.</li>`
    : view.facts.map(fact =>
      `<li><button type="button" class="annotated-fact${fact.selected ? " selected" : ""}${fact.anchored ? "" : " unanchored"}" data-annotated-fact="${fact.id}">
          <span class="annotated-fact-descriptor">${escapeHtml(fact.descriptor)}</span>
          <span class="annotated-fact-category">${escapeHtml(fact.category)}</span>
          ${fact.detail ? `<span class="annotated-fact-detail">${escapeHtml(fact.detail)}</span>` : ""}
          <span class="annotated-fact-conditionality">${escapeHtml(fact.conditionality)}</span>
          <span class="annotated-fact-anchor">${fact.anchored ? `${fact.nodeIds.length} target${fact.nodeIds.length === 1 ? "" : "s"}` : "unanchored"}</span>
        </button></li>`).join("");

  return `<section class="document-section source-result annotated-result">
      <div class="source-provenance"><strong>Annotated source</strong><span>${escapeHtml(result.provenance)}</span><button id="copy-annotated" type="button">copy</button></div>
      ${result.contextLimitation ? `<p class="annotated-limitation">The whole-assembly fact context was narrowed, so this fact list is incomplete: ${escapeHtml(result.contextLimitation)}</p>` : ""}
      <div class="annotated-controls">
        <span class="annotated-controls-label">show</span>${toggles}
        ${view.hiddenLines > 0 ? `<span class="annotated-hidden">${view.hiddenLines} line${view.hiddenLines === 1 ? "" : "s"} hidden</span>` : ""}
        ${view.selectedFactId !== null || view.selectedNodeIds.length > 0 ? `<button type="button" id="annotated-clear">clear selection</button>` : ""}
      </div>
      <div class="annotated-body">
        <pre class="annotated-text"><code>${lines}</code></pre>
        <ol class="annotated-facts">${facts}</ol>
      </div>
    </section>`;
}
