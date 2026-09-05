import {
  buildAnnotatedView,
  csharpHighlightingInput,
  MEDIUM_LABELS,
} from "./annotated-source-view.ts";
import {
  annotationState,
  capabilityReason,
  createAnnotatedSourceViewerModel,
  factForId,
  invocationDestinationForNode,
  nodesForPrimary,
  renderedFindingTargets,
  renderedStructuralTargets,
} from "./annotated-source-session.ts";
import type {
  AnnotatedFocusTarget,
  AnnotatedSourceResult,
  AnnotatedSourceSession,
  AnnotatedSourceViewerModel,
  AnnotationTargetIdentity,
  FindingDetailOpener,
  RenderedFindingTarget,
  RenderedStructuralTarget,
} from "./annotated-source-session.ts";
import type {
  AnnotatedSourceNode,
  SourceMedium,
} from "./document-model.ts";
import type {
  CSharpHighlightExclusion,
  CSharpRangeHighlighter,
} from "./csharp-highlighting.ts";

export type { AnnotatedSourceResult } from "./annotated-source-session.ts";

export interface AnnotatedSourceRenderOptions {
  result: AnnotatedSourceResult;
  session: AnnotatedSourceSession;
  escapeHtml: (value: unknown) => string;
  highlightCSharp?: (
    source: string,
    tokenizationSource: string,
    excludedRanges: readonly CSharpHighlightExclusion[],
  ) => CSharpRangeHighlighter;
}

export type AnnotatedSourceAction =
  | { kind: "copy" }
  | { kind: "explore" }
  | { kind: "close-modal" }
  | { kind: "close-detail" }
  | { kind: "annotation-open"; opener: FindingDetailOpener }
  | { kind: "inspector-open"; factId: number }
  | { kind: "annotation-set"; value: "Default" | "All" | "Clear" }
  | { kind: "finding-toggle"; factId: number }
  | { kind: "medium-toggle"; medium: SourceMedium }
  | { kind: "coordinate-toggle" }
  | { kind: "node-select"; nodeId: number }
  | {
      kind: "destination-open";
      destinationIndex: number;
      destination: "member" | "source";
    }
  | { kind: "source-select"; offset: number; medium: SourceMedium };

export interface AnnotatedSourceBindingActions {
  onAction(action: AnnotatedSourceAction): void;
}

export type AnnotatedSourceScrollSnapshot = ReadonlyMap<
  string,
  readonly [scrollLeft: number, scrollTop: number]
>;

interface SourceRenderContext {
  model: AnnotatedSourceViewerModel;
  session: AnnotatedSourceSession;
  escapeHtml: (value: unknown) => string;
  highlighting: CSharpRangeHighlighter;
}

type RenderedLineAnnotation =
  | {
      kind: "finding";
      start: number;
      target: RenderedFindingTarget;
    }
  | {
      kind: "structure";
      start: number;
      target: RenderedStructuralTarget;
    };

interface RenderedAnnotationRow {
  start: number;
  findings: RenderedFindingTarget[];
  structure: RenderedStructuralTarget[];
}

export function renderAnnotatedSource(
  options: AnnotatedSourceRenderOptions,
): string {
  const context = renderContext(options);
  const { result, escapeHtml } = options;
  return `
    <section class="annotated-reader" aria-label="Annotated source">
      ${renderSource(context)}
      ${renderDetail(context)}
      <footer class="annotated-reader-footer">
        ${result.contextLimitation
          ? `<span class="annotated-context">${escapeHtml(result.contextLimitation)}</span>`
          : ""}
        <span>${escapeHtml(result.provenance)}</span>
      </footer>
    </section>`;
}

export function renderAnnotatedSourcePageActions(enabled: boolean): string {
  const disabled = enabled ? "" : " disabled";
  return `
    <button id="copy-annotated" type="button" data-annotated-action="copy"
      title="Copy annotated source"${disabled}>Copy</button>
    <button id="explore-annotated" class="primary-action" type="button"
      data-annotated-action="explore"${disabled}>Explore</button>`;
}

export function renderAnnotatedSourceModal(
  options: AnnotatedSourceRenderOptions,
): string {
  const context = renderContext(options);
  const { model, session, escapeHtml } = context;
  const reported = annotationState(model, session);
  return `
    <div id="annotated-source-backdrop" class="annotated-modal-backdrop">
      <section id="annotated-source-modal" class="annotated-modal"
        role="dialog" aria-modal="true" aria-labelledby="annotated-modal-title">
        <header class="annotated-modal-head">
          <div>
            <p class="section-eyebrow">Explore Annotated Source</p>
            <h2 id="annotated-modal-title" tabindex="-1">Source evidence and structure</h2>
          </div>
          <div class="annotated-modal-head-actions">
            <button type="button" data-annotated-action="copy">copy source</button>
            <button id="annotated-modal-close" type="button"
              data-annotated-action="close-modal">Close</button>
          </div>
        </header>
        <div class="annotated-modal-controls" data-annotated-scroll="modal-controls">
          <fieldset class="annotated-control-group">
            <legend>Annotations <span>${reported}</span></legend>
            <div class="annotated-control-row">
              ${(["Default", "All", "Clear"] as const).map(value => `
                <button id="annotated-set-${value.toLowerCase()}" type="button"
                  class="annotated-set-control"
                  data-annotated-action="annotation-set"
                  data-annotated-set="${value}"
                  aria-pressed="${reported === value}">${value}</button>`).join("")}
            </div>
          </fieldset>
          <fieldset class="annotated-control-group">
            <legend>Finding annotations</legend>
            <div class="annotated-control-row annotated-finding-toggles">
              ${model.annotatableFindingIds.map(factId => {
                const fact = factForId(model, factId);
                return fact
                  ? `<button id="annotated-finding-toggle-${fact.id}" type="button"
                      class="annotated-finding-toggle category-${categoryClass(fact.category)}"
                      data-annotated-action="finding-toggle"
                      data-fact-id="${fact.id}"
                      aria-pressed="${session.activeFindingIds.includes(fact.id)}">
                      ${escapeHtml(fact.descriptor)}
                    </button>`
                  : "";
              }).join("")}
            </div>
          </fieldset>
          <fieldset class="annotated-control-group">
            <legend>Presentation</legend>
            <div class="annotated-control-row">
              ${model.supportedMedia.map(medium => `
                <button id="annotated-medium-${medium.toLowerCase()}" type="button"
                  class="annotated-medium-toggle"
                  data-annotated-action="medium-toggle"
                  data-medium="${medium}"
                  aria-pressed="${session.visibleMedia.includes(medium)}">
                  ${MEDIUM_LABELS[medium]}
                </button>`).join("")}
              <button id="annotated-coordinate-toggle" type="button"
                class="annotated-coordinate-toggle"
                data-annotated-action="coordinate-toggle"
                aria-pressed="${session.coordinatesVisible}">
                UTF-16 ranges
              </button>
            </div>
          </fieldset>
        </div>
        <div class="annotated-modal-workspace">
          <section class="annotated-modal-source" aria-label="Annotated source text"
            data-annotated-scroll="modal-source">
            ${renderSource(context)}
          </section>
          <aside class="annotated-modal-inspector" aria-label="Annotated source inspector"
            data-annotated-scroll="modal-inspector">
            ${renderPrimary(context)}
            ${renderFindingInspector(context)}
          </aside>
        </div>
        ${renderDetail(context)}
      </section>
    </div>`;
}

export function bindAnnotatedSource(
  root: ParentNode,
  actions: AnnotatedSourceBindingActions,
): void {
  root.querySelectorAll<HTMLElement>("[data-annotated-action]").forEach(element => {
    element.addEventListener("click", event => {
      const target = htmlEventTarget(event.currentTarget);
      if (!target) return;
      const action = actionForElement(target);
      if (action) actions.onAction(action);
    });
  });

  root.querySelectorAll<HTMLElement>("[data-annotated-source-start]").forEach(element => {
    bindSourceHit(element, actions);
  });

  const backdrop = root.querySelector<HTMLElement>("#annotated-source-backdrop");
  backdrop?.addEventListener("click", event => {
    if (event.target === backdrop) actions.onAction({ kind: "close-modal" });
  });

  const modal = root.querySelector<HTMLElement>("#annotated-source-modal");
  modal?.addEventListener("keydown", event => {
    if (event.key !== "Tab") return;
    trapModalTab(modal, event);
  });
}

export function captureAnnotatedSourceScroll(
  root: ParentNode,
): AnnotatedSourceScrollSnapshot {
  const snapshot = new Map<string, readonly [number, number]>();
  root.querySelectorAll<HTMLElement>("[data-annotated-scroll]").forEach(element => {
    const key = element.dataset.annotatedScroll;
    if (key) snapshot.set(key, [element.scrollLeft, element.scrollTop]);
  });
  return snapshot;
}

export function restoreAnnotatedSourceScroll(
  root: ParentNode,
  snapshot: AnnotatedSourceScrollSnapshot,
): void {
  root.querySelectorAll<HTMLElement>("[data-annotated-scroll]").forEach(element => {
    const key = element.dataset.annotatedScroll;
    const position = key ? snapshot.get(key) : undefined;
    if (!position) return;
    element.scrollLeft = position[0];
    element.scrollTop = position[1];
  });
}

function htmlEventTarget(value: EventTarget | null): HTMLElement | null {
  return isHtmlEventTarget(value) ? value : null;
}

function isHtmlEventTarget(value: EventTarget | null): value is HTMLElement {
  return value !== null && "dataset" in value && "addEventListener" in value;
}

export function annotatedFocusSelector(
  target: AnnotatedFocusTarget,
  surface: "embedded" | "modal" = "modal",
): string {
  switch (target.kind) {
    case "heading":
      return "#annotated-modal-title";
    case "explore":
      return "#explore-annotated";
    case "annotation-control":
      return `#annotated-set-${target.control.toLowerCase()}`;
    case "finding-toggle":
      return `#annotated-finding-toggle-${target.factId}`;
    case "medium-toggle":
      return `#annotated-medium-${target.medium.toLowerCase()}`;
    case "coordinate-toggle":
      return "#annotated-coordinate-toggle";
    case "inspector":
      return `#annotated-inspector-${target.factId}`;
    case "annotation":
      return annotationTargetSelector(surface, target);
    case "node":
      return `#annotated-node-${target.nodeId}`;
  }
  throw new Error("Unknown Annotated Source focus target");
}

function renderContext(
  options: AnnotatedSourceRenderOptions,
): SourceRenderContext {
  const model = createAnnotatedSourceViewerModel(options.result);
  const source = model.document.text;
  const highlightingInput = csharpHighlightingInput(model.document);
  return {
    model,
    session: options.session,
    escapeHtml: options.escapeHtml,
    highlighting: options.highlightCSharp?.(
      source,
      highlightingInput.text,
      highlightingInput.excludedRanges,
    ) ?? {
      render(start, length) {
        return options.escapeHtml(source.slice(start, start + length));
      },
    },
  };
}

function renderSource(context: SourceRenderContext): string {
  const { model, session, escapeHtml, highlighting } = context;
  const selectedFactId =
    session.primary?.kind === "finding" ? session.primary.id : null;
  const selectedNodeIds =
    session.primary?.kind === "node" ? [session.primary.id] : [];
  const visible = new Set(session.visibleMedia);
  const view = buildAnnotatedView(model.document, {
    media: {
      CSharp: visible.has("CSharp"),
      Il: visible.has("Il"),
    },
    selectedFactId,
    selectedNodeIds,
  });
  const targets = renderedFindingTargets(model, session);
  const structuralTargets = renderedStructuralTargets(model, session);
  const lineAnnotations = new Map<number, RenderedLineAnnotation[]>();
  const appendLineAnnotation = (
    lineNumber: number,
    annotation: RenderedLineAnnotation,
  ) => {
    const annotations = lineAnnotations.get(lineNumber) ?? [];
    annotations.push(annotation);
    lineAnnotations.set(lineNumber, annotations);
  };
  for (const target of targets) {
    const start = Math.min(...target.node.spans.map(span => span.start));
    const line = view.lines.find(candidate =>
      start >= candidate.start && start <= candidate.end);
    if (!line) continue;
    appendLineAnnotation(line.number, { kind: "finding", start, target });
  }
  for (const target of structuralTargets) {
    const line = view.lines.find(candidate =>
      target.start >= candidate.start && target.start <= candidate.end);
    if (!line) continue;
    appendLineAnnotation(line.number, {
      kind: "structure",
      start: target.start,
      target,
    });
  }

  return `
    <div class="annotated-source-code" data-annotated-surface="${session.surface}"
      data-annotated-scroll="${session.surface}-source-code">
      ${view.lines.map(line => {
        const annotationRows =
          groupLineAnnotations(lineAnnotations.get(line.number) ?? []);
        return `
          ${annotationRows.length
            ? `<div class="annotated-rows" aria-label="Annotations on line ${line.number}">
                ${annotationRows.map(row => renderAnnotationRow(
                  row,
                  line.start,
                  model.document.text,
                  session.surface,
                  escapeHtml,
                )).join("")}
              </div>`
            : ""}
          <div class="annotated-source-line medium-${line.medium.toLowerCase()}">
            <span class="annotated-line-number">${line.number}</span>
            ${session.surface === "modal"
              ? `<span class="annotated-medium-label">${line.medium === "Mixed" ? "C#/IL" : MEDIUM_LABELS[line.medium]}</span>`
              : ""}
            <code>${line.segments.length
              ? line.segments.map(segment => {
                  const actionable =
                    session.surface === "modal"
                    && segment.visible
                    && segment.nodeIds.length > 0;
                  const invocation = segment.nodeIds.some(id => {
                    const node = model.document.nodes[id];
                    return node && model.invocationLikeNodeKinds.has(node.kind);
                  });
                  const segmentMedium = line.medium === "Mixed"
                    ? segment.media.find(medium => visible.has(medium))
                      ?? segment.media[0]
                      ?? "CSharp"
                    : line.medium;
                  return `<span
                    ${actionable
                      ? `id="annotated-source-${session.surface}-segment-${segment.start}"`
                      : ""}
                    class="annotated-source-segment${segment.selected ? " selected" : ""}${segment.visible ? "" : " medium-hidden"}${actionable ? " addressable" : ""}${invocation ? " invocation" : ""}"
                    ${actionable
                      ? `role="button" tabindex="0"
                          aria-label="Select source node"
                          data-annotated-source-start="${segment.start}"
                          data-medium="${segmentMedium}"`
                      : ""}
                    >${highlighting.render(segment.start, segment.text.length)}</span>`;
                }).join("")
              : " "}</code>
            ${session.coordinatesVisible
              ? `<span class="annotated-line-coordinate">UTF-16 ${line.start}..${line.end}</span>`
              : ""}
          </div>
        `;
      }).join("")}
    </div>`;
}

function groupLineAnnotations(
  annotations: readonly RenderedLineAnnotation[],
): RenderedAnnotationRow[] {
  const rows = new Map<number, RenderedAnnotationRow>();
  for (const annotation of annotations) {
    const row = rows.get(annotation.start) ?? {
      start: annotation.start,
      findings: [],
      structure: [],
    };
    if (annotation.kind === "finding") row.findings.push(annotation.target);
    else row.structure.push(annotation.target);
    rows.set(annotation.start, row);
  }
  return [...rows.values()].sort((left, right) => left.start - right.start);
}

function renderAnnotationRow(
  row: RenderedAnnotationRow,
  lineStart: number,
  documentText: string,
  surface: "embedded" | "modal",
  escapeHtml: (value: unknown) => string,
): string {
  const prefix = documentText.slice(lineStart, row.start);
  return `
    <div class="annotated-row">
      <span class="annotated-row-gutter" aria-hidden="true"></span>
      ${surface === "modal"
        ? `<span class="annotated-row-gutter" aria-hidden="true"></span>`
        : ""}
      <div class="annotated-row-content">
        <span class="annotated-row-prefix" aria-hidden="true">${escapeHtml(prefix)}</span>
        <div class="annotated-row-items" data-annotated-anchor-start="${row.start}">
          ${row.findings.map(target =>
            renderAnnotationTarget(target, surface, escapeHtml)).join("")}
          ${row.structure.map(target => `
            <span class="annotated-structure-mark">
              structure · ${escapeHtml(target.region.role)} · ${MEDIUM_LABELS[target.medium]}
            </span>`).join("")}
        </div>
      </div>
    </div>`;
}

function renderAnnotationTarget(
  target: RenderedFindingTarget,
  surface: "embedded" | "modal",
  escapeHtml: (value: unknown) => string,
): string {
  const id = annotationTargetId(surface, target);
  return `<button id="${id}" type="button"
    class="annotated-finding-chip category-${categoryClass(target.fact.category)}"
    data-annotated-action="annotation-open"
    data-fact-id="${target.factId}"
    data-node-id="${target.nodeId}"
    data-medium="${target.medium}">
    <span>${escapeHtml(target.fact.descriptor)}</span>
    <small>${MEDIUM_LABELS[target.medium]} · ${escapeHtml(target.fact.category)}</small>
  </button>`;
}

function renderPrimary(context: SourceRenderContext): string {
  const { model, session, escapeHtml } = context;
  const nodes = nodesForPrimary(model, session.primary);
  const destination = session.primary?.kind === "node"
    ? invocationDestinationForNode(model, session.primary.id)
    : null;
  return `
    <section class="annotated-inspector-section">
      <p class="section-eyebrow">Selection</p>
      ${session.primary
        ? `<h3>${session.primary.kind === "finding"
            ? `Finding #${session.primary.id}`
            : `Node #${session.primary.id}`}</h3>
          ${nodes.length
            ? `<div class="annotated-node-list">
                ${nodes.map(node => renderNode(node, session, escapeHtml)).join("")}
              </div>`
            : `<p class="annotated-empty">This Finding has no product-issued source target.</p>`}
          ${destination
            ? renderInvocationDestinations(
                destination.index,
                destination.destination.target,
                escapeHtml)
            : ""}`
        : `<div class="annotated-selection-empty">
            <strong>Nothing selected</strong>
            <span>Select addressable source or inspect a Finding.</span>
          </div>`}
    </section>`;
}

function renderInvocationDestinations(
  destinationIndex: number,
  target: AnnotatedSourceViewerModel["invocationDestinations"][number]["target"],
  escapeHtml: (value: unknown) => string,
): string {
  const label = `${target.typeFullName}.${target.memberName}`;
  return `
    <div class="annotated-destinations">
      <span>Open selected invocation</span>
      <div>
        <button type="button"
          data-annotated-action="destination-open"
          data-destination-index="${destinationIndex}"
          data-destination="member"
          aria-label="Open member overview for ${escapeHtml(label)}"
          title="Open member overview for ${escapeHtml(label)}">Member</button>
        <button type="button"
          data-annotated-action="destination-open"
          data-destination-index="${destinationIndex}"
          data-destination="source"
          aria-label="Open source for ${escapeHtml(label)}"
          title="Open source for ${escapeHtml(label)}">Source</button>
      </div>
    </div>`;
}

function renderNode(
  node: AnnotatedSourceNode,
  session: AnnotatedSourceSession,
  escapeHtml: (value: unknown) => string,
): string {
  const selected =
    session.primary?.kind === "node" && session.primary.id === node.id;
  return `<button id="annotated-node-${node.id}" type="button"
    class="annotated-node-action"
    data-annotated-action="node-select"
    data-node-id="${node.id}"
    aria-pressed="${selected}">
    <strong>#${node.id} ${escapeHtml(node.kind)}</strong>
    <span>${MEDIUM_LABELS[node.medium]}</span>
    ${session.coordinatesVisible
      ? `<small>${node.spans.map(span =>
          `UTF-16 ${span.start}..${span.start + span.length}`).join(" · ")}
          ${node.il_offset == null
            ? ""
            : ` · IL offset ${node.il_offset}`}</small>`
      : ""}
  </button>`;
}

function renderFindingInspector(context: SourceRenderContext): string {
  const { model, session, escapeHtml } = context;
  const active = new Set(session.activeFindingIds);
  return `
    <section class="annotated-inspector-section">
      <p class="section-eyebrow">Findings</p>
      <div class="annotated-inspector-list">
        ${model.document.facts.map(fact => `
          <button id="annotated-inspector-${fact.id}" type="button"
            class="annotated-inspector-action category-${categoryClass(fact.category)}"
            data-annotated-action="inspector-open"
            data-fact-id="${fact.id}">
            <span>
              <strong>${escapeHtml(fact.descriptor)}</strong>
              <small>${escapeHtml(fact.category)} · ${escapeHtml(fact.origin)}</small>
            </span>
            <span class="annotated-finding-status">${active.has(fact.id)
              ? "active"
              : model.annotatableFindingIds.includes(fact.id)
                ? "off"
                : "unanchored"}</span>
          </button>`).join("")}
      </div>
    </section>`;
}

function renderDetail(context: SourceRenderContext): string {
  const { model, session, escapeHtml } = context;
  const detail = session.detail;
  if (!detail) return "";
  const fact = factForId(model, detail.factId);
  if (!fact) return "";
  const targets = model.document.targets
    .filter(target => target.fact_id === fact.id)
    .map(target => model.document.nodes[target.node_id])
    .filter((node): node is AnnotatedSourceNode => node !== undefined);
  return `
    <section class="annotated-detail" aria-labelledby="annotated-detail-title">
      <header>
        <div>
          <p class="section-eyebrow">Finding detail</p>
          <h3 id="annotated-detail-title" tabindex="-1">${escapeHtml(fact.descriptor)}</h3>
        </div>
        <button id="annotated-detail-close" type="button"
          data-annotated-action="close-detail">close detail</button>
      </header>
      <dl>
        ${detailRow("Descriptor", fact.descriptor, escapeHtml)}
        ${detailRow("Category", fact.category, escapeHtml)}
        ${detailRow("Conditionality", fact.conditionality, escapeHtml)}
        ${fact.detail ? detailRow("Detail", fact.detail, escapeHtml) : ""}
        ${detailRow("Origin", fact.origin, escapeHtml)}
        ${session.coordinatesVisible && fact.source_offset >= 0
          ? detailRow("Source offset", String(fact.source_offset), escapeHtml)
          : ""}
      </dl>
      <section>
        <h4>Targets</h4>
        ${targets.length
          ? `<ul>${targets.map(node => `<li>
              ${escapeHtml(node.kind)} · ${MEDIUM_LABELS[node.medium]}
              ${session.coordinatesVisible
                ? ` · ${node.spans.map(span =>
                    `UTF-16 ${span.start}..${span.start + span.length}`).join(", ")}`
                : ""}
            </li>`).join("")}</ul>`
          : `<p class="annotated-unavailable">No product-issued source target</p>`}
      </section>
      <section class="annotated-detail-capabilities">
        <div>
          <h4>Evidence</h4>
          <p>${escapeHtml(capabilityReason(model.catalog.findingEvidence))}</p>
        </div>
        <div>
          <h4>Destinations</h4>
          <p>${escapeHtml(capabilityReason(model.catalog.destinations))}</p>
        </div>
      </section>
    </section>`;
}

function detailRow(
  label: string,
  value: string,
  escapeHtml: (value: unknown) => string,
): string {
  return `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd></div>`;
}

function actionForElement(element: HTMLElement): AnnotatedSourceAction | null {
  switch (element.dataset.annotatedAction) {
    case "copy":
      return { kind: "copy" };
    case "explore":
      return { kind: "explore" };
    case "close-modal":
      return { kind: "close-modal" };
    case "close-detail":
      return { kind: "close-detail" };
    case "annotation-open": {
      const factId = dataInteger(element, "factId");
      const nodeId = dataInteger(element, "nodeId");
      const medium = dataMedium(element);
      return factId === null || nodeId === null || medium === null
        ? null
        : {
            kind: "annotation-open",
            opener: {
              kind: "annotation",
              factId,
              nodeId,
              medium,
            },
          };
    }
    case "inspector-open": {
      const factId = dataInteger(element, "factId");
      return factId === null ? null : { kind: "inspector-open", factId };
    }
    case "annotation-set": {
      const value = element.dataset.annotatedSet;
      return value === "Default" || value === "All" || value === "Clear"
        ? { kind: "annotation-set", value }
        : null;
    }
    case "finding-toggle": {
      const factId = dataInteger(element, "factId");
      return factId === null ? null : { kind: "finding-toggle", factId };
    }
    case "medium-toggle": {
      const medium = dataMedium(element);
      return medium === null ? null : { kind: "medium-toggle", medium };
    }
    case "coordinate-toggle":
      return { kind: "coordinate-toggle" };
    case "node-select": {
      const nodeId = dataInteger(element, "nodeId");
      return nodeId === null ? null : { kind: "node-select", nodeId };
    }
    case "destination-open": {
      const destinationIndex = dataInteger(element, "destinationIndex");
      const destination = element.dataset.destination;
      return destinationIndex === null
        || (destination !== "member" && destination !== "source")
        ? null
        : {
            kind: "destination-open",
            destinationIndex,
            destination,
          };
    }
    default:
      return null;
  }
}

function bindSourceHit(
  element: HTMLElement,
  actions: AnnotatedSourceBindingActions,
): void {
  let pointerStart: { id: number; x: number; y: number } | null = null;
  element.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    pointerStart = {
      id: event.pointerId,
      x: event.clientX,
      y: event.clientY,
    };
  });
  element.addEventListener("pointerup", event => {
    if (!pointerStart || pointerStart.id !== event.pointerId) return;
    const distance = Math.hypot(
      event.clientX - pointerStart.x,
      event.clientY - pointerStart.y,
    );
    pointerStart = null;
    const selection = element.ownerDocument.getSelection();
    const selectedThisSource =
      selection !== null
      && !selection.isCollapsed
      && selection.containsNode(element, true);
    if (distance > 5 || selectedThisSource) return;
    const medium = dataMedium(element);
    const offset = sourceOffsetAtPoint(
      element.ownerDocument,
      element,
      event.clientX,
      event.clientY,
    );
    if (medium && offset !== null) {
      actions.onAction({ kind: "source-select", offset, medium });
    }
  });
  element.addEventListener("pointercancel", () => {
    pointerStart = null;
  });
  element.addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    const medium = dataMedium(element);
    const offset = dataInteger(element, "annotatedSourceStart");
    if (!medium || offset === null) return;
    event.preventDefault();
    actions.onAction({ kind: "source-select", offset, medium });
  });
}

function sourceOffsetAtPoint(
  document: Document,
  element: HTMLElement,
  x: number,
  y: number,
): number | null {
  const start = dataInteger(element, "annotatedSourceStart");
  if (start === null) return null;
  const caret = document.caretPositionFromPoint?.(x, y);
  if (caret && element.contains(caret.offsetNode)) {
    return start + textOffsetWithin(element, caret.offsetNode, caret.offset);
  }
  const range = document.caretRangeFromPoint?.(x, y);
  if (range && element.contains(range.startContainer)) {
    return start + textOffsetWithin(
      element,
      range.startContainer,
      range.startOffset,
    );
  }
  return start;
}

function textOffsetWithin(
  element: HTMLElement,
  node: Node,
  nodeOffset: number,
): number {
  const range = element.ownerDocument.createRange();
  range.selectNodeContents(element);
  range.setEnd(node, nodeOffset);
  return range.toString().length;
}

function trapModalTab(modal: HTMLElement, event: KeyboardEvent): void {
  const focusable = [...modal.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), '
      + 'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )].filter(element => !element.hidden && element.getClientRects().length > 0);
  const first = focusable[0];
  const last = focusable.at(-1);
  if (!first || !last) {
    event.preventDefault();
    modal.focus();
    return;
  }
  if (event.shiftKey && modal.ownerDocument.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && modal.ownerDocument.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

function dataInteger(
  element: HTMLElement,
  key: string,
): number | null {
  const value = element.dataset[key];
  if (value === undefined || !/^\d+$/.test(value)) return null;
  return Number(value);
}

function dataMedium(element: HTMLElement): SourceMedium | null {
  const value = element.dataset.medium;
  return value === "CSharp" || value === "Il" ? value : null;
}

function annotationTargetId(
  surface: "embedded" | "modal",
  target: AnnotationTargetIdentity,
): string {
  return `annotated-chip-${surface}-${target.factId}-${target.nodeId}-${target.medium}`;
}

function annotationTargetSelector(
  surface: "embedded" | "modal",
  target: AnnotationTargetIdentity,
): string {
  return `#${annotationTargetId(surface, target)}`;
}

function categoryClass(category: string): string {
  return category.toLowerCase().replaceAll(/[^a-z0-9]+/g, "-");
}
