import {
  buildLines,
  lineMedium,
  nodeIdsForFact,
  nodesAtOffset,
  segmentsForLine,
  unanchoredFacts,
  validateDocument,
} from "./document-model.ts";
import type {
  AnnotatedSourceDocument,
  AnnotatedSourceFact,
  AnnotatedSourceNode,
  LineMedium,
  SourceMedium,
  SourceSegment,
} from "./document-model.ts";
export type {
  AnnotatedSourceDocument,
  AnnotatedSourceFact,
  AnnotatedSourceNode,
  SourceMedium,
  TextSpan,
} from "./document-model.ts";

// The document model owns validation, coordinates, line derivation, segmentation, and
// fact/target/node resolution. This module owns only the selection state a browser section
// carries on top of it, and returns a plain model the renderer walks.

export function validateAnnotatedSourceDocument(
  document: unknown,
): asserts document is AnnotatedSourceDocument {
  validateDocument(document);
}

export interface AnnotatedViewState {
  media?: Partial<Record<SourceMedium, boolean | undefined>>;
  selectedFactId?: number | null;
  selectedNodeIds?: readonly number[];
}

export interface AnnotatedViewSegment extends SourceSegment {
  selected: boolean;
}

export interface AnnotatedViewLine {
  number: number;
  medium: LineMedium;
  start: number;
  end: number;
  segments: AnnotatedViewSegment[];
}

export interface AnnotatedViewFact {
  id: number;
  descriptor: string;
  category: string;
  conditionality: string;
  detail: string | null;
  origin: string;
  sourceOffset: number;
  anchored: boolean;
  nodeIds: number[];
  selected: boolean;
}

export interface AnnotatedView {
  media: Record<SourceMedium, boolean | undefined>;
  selectedFactId: number | null;
  selectedNodeIds: number[];
  lines: AnnotatedViewLine[];
  facts: AnnotatedViewFact[];
  unanchoredFactIds: number[];
  hiddenLines: number;
}

export const MEDIA = ["CSharp", "Il"] as const satisfies readonly SourceMedium[];

export const MEDIUM_LABELS: Readonly<Record<SourceMedium, string>> = {
  CSharp: "C#",
  Il: "IL",
};

export function buildAnnotatedView(
  document: unknown,
  state: AnnotatedViewState = {},
): AnnotatedView {
  validateDocument(document);

  const media: Record<SourceMedium, boolean | undefined> = {
    CSharp: true,
    Il: true,
    ...state.media,
  };
  const selectedFactId =
    typeof state.selectedFactId === "number" && Number.isInteger(state.selectedFactId)
      ? state.selectedFactId
      : null;
  const targetNodeIds: number[] = selectedFactId == null
    ? [...new Set(state.selectedNodeIds ?? [])]
    : nodeIdsForFact(document, selectedFactId);
  const targeted = new Set(targetNodeIds);

  const sourceLines = buildLines(document.text);
  const lines = sourceLines
    .map(line => ({
      ...line,
      medium: lineMedium(document, line),
    }))
    .filter(line => isVisible(line.medium, media))
    .map(line => ({
      number: line.number,
      medium: line.medium,
      start: line.start,
      end: line.end,
      segments: segmentsForLine(document, line, targetNodeIds).map(segment => ({
        ...segment,
        // A segment is highlighted only when a targeted node actually covers it, so one node's
        // several separated spans light up without selecting the text between them.
        selected: segment.nodeIds.some(id => targeted.has(id)),
      })),
    }));

  const anchored = new Set(document.targets.map(target => target.fact_id));
  const facts = document.facts.map(fact => ({
    id: fact.id,
    descriptor: fact.descriptor,
    category: fact.category,
    conditionality: fact.conditionality,
    detail: fact.detail ?? null,
    origin: fact.origin,
    sourceOffset: fact.source_offset,
    anchored: anchored.has(fact.id),
    nodeIds: nodeIdsForFact(document, fact.id),
    selected: fact.id === selectedFactId,
  }));

  const unanchored = unanchoredFacts(document);
  return {
    media,
    selectedFactId,
    selectedNodeIds: targetNodeIds,
    lines,
    facts,
    unanchoredFactIds: unanchored.map(fact => fact.id),
    hiddenLines: sourceLines.length - lines.length,
  };
}

/// The tightest structural node covering one text offset, or null when nothing covers it.
export function nodeAtOffset(
  document: AnnotatedSourceDocument,
  offset: number,
  medium: SourceMedium | null = null,
): AnnotatedSourceNode | null {
  const [node] = nodesAtOffset(document, offset, medium);
  return node ?? null;
}

/// The facts anchored to one node, in document fact order.
export function factsForNode(
  document: AnnotatedSourceDocument,
  nodeId: number,
): AnnotatedSourceFact[] {
  const factIds = new Set(
    document.targets.filter(target => target.node_id === nodeId).map(target => target.fact_id),
  );
  return document.facts.filter(fact => factIds.has(fact.id));
}

export function factLabel(fact: AnnotatedSourceFact): string {
  const detail = fact.detail ? ` ${fact.detail}` : "";
  return `${fact.descriptor}${detail}`;
}

function isVisible(
  medium: LineMedium,
  media: Readonly<Record<SourceMedium, boolean | undefined>>,
): boolean | undefined {
  if (medium === "Mixed") return media.CSharp || media.Il;
  return media[medium] === true;
}
