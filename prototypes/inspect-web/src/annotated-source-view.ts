import {
  buildLines,
  nodesAtOffset,
  validateDocument,
} from "./document-model.js";

// The document model owns validation, coordinates, line derivation, segmentation, and
// fact/target/node resolution. This module owns only the selection state a browser section
// carries on top of it, and returns a plain model the renderer walks.

export type SourceMedium = "CSharp" | "Il";

type LineMedium = SourceMedium | "Mixed";

export interface TextSpan {
  start: number;
  length: number;
}

export interface AnnotatedSourceNode {
  id: number;
  kind: string;
  medium: SourceMedium;
  spans: readonly TextSpan[];
  il_offset?: number | null;
}

export interface AnnotatedSourceRegion {
  role: string;
  spans: readonly TextSpan[];
}

export interface AnnotatedSourceFact {
  id: number;
  descriptor: string;
  category: string;
  conditionality: string;
  detail?: string | null;
  origin: string;
  source_offset: number;
}

export interface AnnotatedSourceTarget {
  fact_id: number;
  node_id: number;
}

export interface AnnotatedSourceDocument {
  text: string;
  nodes: readonly AnnotatedSourceNode[];
  regions: readonly AnnotatedSourceRegion[];
  facts: readonly AnnotatedSourceFact[];
  targets: readonly AnnotatedSourceTarget[];
}

export interface AnnotatedViewState {
  media?: Partial<Record<SourceMedium, boolean | undefined>>;
  selectedFactId?: number | null;
  selectedNodeIds?: readonly number[];
}

interface SourceLine {
  number: number;
  start: number;
  end: number;
  text: string;
}

interface SourceSegment {
  start: number;
  end: number;
  text: string;
  nodeIds: number[];
  media: SourceMedium[];
  selected: boolean;
}

interface LineIntersection {
  node: AnnotatedSourceNode;
  start: number;
  end: number;
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

export interface PreparedAnnotatedView {
  document: AnnotatedSourceDocument;
  lines: readonly AnnotatedViewLine[];
  facts: readonly AnnotatedViewFact[];
  unanchoredFactIds: readonly number[];
  totalLineCount: number;
}

const getLines = buildLines as (text: string) => SourceLine[];
const getNodesAtOffset = nodesAtOffset as (
  document: AnnotatedSourceDocument,
  offset: number,
  medium?: SourceMedium | null,
) => AnnotatedSourceNode[];

export const MEDIA = ["CSharp", "Il"] as const satisfies readonly SourceMedium[];

export const MEDIUM_LABELS: Readonly<Record<SourceMedium, string>> = {
  CSharp: "C#",
  Il: "IL",
};

export function buildAnnotatedView(
  document: AnnotatedSourceDocument,
  state: AnnotatedViewState = {},
): AnnotatedView {
  return projectPreparedAnnotatedView(prepareAnnotatedView(document), state);
}

export function validateAnnotatedSourceDocument(document: AnnotatedSourceDocument): void {
  validateDocument(document);
}

export function prepareAnnotatedView(
  document: AnnotatedSourceDocument,
): PreparedAnnotatedView {
  validateAnnotatedSourceDocument(document);
  const sourceLines = getLines(document.text);
  const intersectionsByLine = indexLineIntersections(sourceLines, document.nodes);
  const lines = sourceLines.map((line, index) => ({
    number: line.number,
    medium: mediumForIntersections(intersectionsByLine[index]),
    start: line.start,
    end: line.end,
    segments: segmentsForIntersections(document.text, line, intersectionsByLine[index]),
  }));
  const nodeIdsByFact = document.facts.map((): number[] => []);
  for (const target of document.targets) nodeIdsByFact[target.fact_id].push(target.node_id);
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
    nodeIds: nodeIdsByFact[fact.id],
    selected: false,
  }));
  return {
    document,
    lines,
    facts,
    unanchoredFactIds: document.facts
      .filter(fact => !anchored.has(fact.id))
      .map(fact => fact.id),
    totalLineCount: sourceLines.length,
  };
}

export function projectPreparedAnnotatedView(
  prepared: PreparedAnnotatedView,
  state: AnnotatedViewState = {},
): AnnotatedView {
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
    : [...(prepared.facts[selectedFactId]?.nodeIds ?? [])];
  const targeted = new Set(targetNodeIds);

  const lines = prepared.lines
    .filter(line => isVisible(line.medium, media))
    .map(line => ({
      number: line.number,
      medium: line.medium,
      start: line.start,
      end: line.end,
      segments: line.segments.map(segment => ({
        ...segment,
        selected: segment.nodeIds.some(id => targeted.has(id)),
      })),
    }));

  const facts = prepared.facts.map(fact => ({
    ...fact,
    selected: fact.id === selectedFactId,
  }));

  return {
    media,
    selectedFactId,
    selectedNodeIds: targetNodeIds,
    lines,
    facts,
    unanchoredFactIds: [...prepared.unanchoredFactIds],
    hiddenLines: prepared.totalLineCount - lines.length,
  };
}

/// The tightest structural node covering one text offset, or null when nothing covers it.
export function nodeAtOffset(
  document: AnnotatedSourceDocument,
  offset: number,
  medium: SourceMedium | null = null,
): AnnotatedSourceNode | null {
  const [node] = getNodesAtOffset(document, offset, medium);
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

function indexLineIntersections(
  lines: readonly SourceLine[],
  nodes: readonly AnnotatedSourceNode[],
): LineIntersection[][] {
  const indexed = lines.map((): LineIntersection[] => []);
  for (const node of nodes) {
    for (const span of node.spans) {
      const spanEnd = span.start + span.length;
      for (
        let lineIndex = firstLineEndingAfter(lines, span.start);
        lineIndex < lines.length && lines[lineIndex].start < spanEnd;
        lineIndex++
      ) {
        const line = lines[lineIndex];
        const start = Math.max(line.start, span.start);
        const end = Math.min(line.end, spanEnd);
        if (start < end) indexed[lineIndex].push({ node, start, end });
      }
    }
  }
  return indexed;
}

function firstLineEndingAfter(lines: readonly SourceLine[], offset: number): number {
  let low = 0;
  let high = lines.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (lines[middle].end <= offset) low = middle + 1;
    else high = middle;
  }
  return low;
}

function mediumForIntersections(intersections: readonly LineIntersection[]): LineMedium {
  let medium: SourceMedium | null = null;
  for (const intersection of intersections) {
    if (medium && medium !== intersection.node.medium) return "Mixed";
    medium = intersection.node.medium;
  }
  return medium === "Il" ? "Il" : "CSharp";
}

function segmentsForIntersections(
  text: string,
  line: SourceLine,
  intersections: readonly LineIntersection[],
): SourceSegment[] {
  const boundaries = new Set([line.start, line.end]);
  const additions = new Map<number, AnnotatedSourceNode[]>();
  const removals = new Map<number, AnnotatedSourceNode[]>();
  for (const intersection of intersections) {
    boundaries.add(intersection.start);
    boundaries.add(intersection.end);
    appendEvent(additions, intersection.start, intersection.node);
    appendEvent(removals, intersection.end, intersection.node);
  }

  const ordered = [...boundaries].sort((left, right) => left - right);
  const active = new Map<number, AnnotatedSourceNode>();
  const segments: SourceSegment[] = [];
  for (let index = 0; index < ordered.length - 1; index++) {
    const start = ordered[index];
    const end = ordered[index + 1];
    for (const node of removals.get(start) ?? []) active.delete(node.id);
    for (const node of additions.get(start) ?? []) active.set(node.id, node);
    if (start === end) continue;
    const nodes = [...active.values()]
      .sort((left, right) => nodeLength(left) - nodeLength(right) || left.id - right.id);
    segments.push({
      start,
      end,
      text: text.slice(start, end),
      nodeIds: nodes.map(node => node.id),
      media: [...new Set(nodes.map(node => node.medium))],
      selected: false,
    });
  }
  return segments;
}

function appendEvent(
  events: Map<number, AnnotatedSourceNode[]>,
  offset: number,
  node: AnnotatedSourceNode,
): void {
  const nodes = events.get(offset);
  if (nodes) nodes.push(node);
  else events.set(offset, [node]);
}

function nodeLength(node: AnnotatedSourceNode): number {
  return node.spans.reduce((sum, span) => sum + span.length, 0);
}
