import {
  buildLines,
  nodesAtOffset,
  validateDocument,
} from "./document-model.ts";
import type {
  AnnotatedSourceDocument,
  AnnotatedSourceFact,
  AnnotatedSourceNode,
  LineMedium,
  SourceLine,
  SourceMedium,
  SourceSegment,
} from "./document-model.ts";
export type {
  AnnotatedSourceDocument,
  AnnotatedSourceFact,
  AnnotatedSourceNode,
  AnnotatedSourceRegion,
  SourceMedium,
} from "./document-model.ts";

// The portable model owns validation, coordinates, canonical line derivation, and point lookup.
// This module owns the indexed structural and fact projection needed for interactive rendering.

export function validateAnnotatedSourceDocument(
  document: unknown,
): asserts document is AnnotatedSourceDocument {
  validateDocument(document);
}

export interface AnnotatedViewState {
  media?: Partial<Record<SourceMedium, boolean | undefined>>;
  selectedFactId?: number | null;
  selectedCaptureIndex?: number | null;
  selectedNodeIds?: readonly number[];
}

interface LineIntersection {
  node: AnnotatedSourceNode;
  start: number;
  end: number;
}

interface IndexedLine {
  intersections: LineIntersection[];
  media: Set<SourceMedium>;
}

interface AnnotatedViewSegment extends SourceSegment {
  captureIds: number[];
  factIds: number[];
  selected: boolean;
}

interface AnnotatedViewLine {
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

export interface AnnotatedViewCapture {
  index: number;
  parentNodeId: number;
  displayName: string;
  useNodeIds: number[];
  selected: boolean;
}

export interface AnnotatedView {
  media: Record<SourceMedium, boolean | undefined>;
  selectedFactId: number | null;
  selectedCaptureIndex: number | null;
  captureScopeNodeId: number | null;
  selectedNodeIds: number[];
  lines: AnnotatedViewLine[];
  facts: AnnotatedViewFact[];
  captures: AnnotatedViewCapture[];
  unanchoredFactIds: number[];
  hiddenLines: number;
}

export interface PreparedAnnotatedView {
  codeLensCandidates: readonly PreparedSourceCodeLensCandidate[];
  document: AnnotatedSourceDocument;
  lines: readonly AnnotatedViewLine[];
  facts: readonly AnnotatedViewFact[];
  captures: readonly AnnotatedViewCapture[];
  unanchoredFactIds: readonly number[];
  totalLineCount: number;
}

/**
 * A structural annotation whose multi-line span was established while the immutable document
 * projection was prepared. Rendering only supplies the product label for its stable node kind.
 */
export interface PreparedSourceCodeLensCandidate {
  kind: string;
  lineStart: number;
  nodeId: number;
  prefix: string;
}

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

export function prepareAnnotatedView(
  document: AnnotatedSourceDocument,
): PreparedAnnotatedView {
  validateAnnotatedSourceDocument(document);
  const factIdsByNode = document.nodes.map((): number[] => []);
  const captureIdsByNode = document.nodes.map((): number[] => []);
  const nodeIdsByFact = document.facts.map((): number[] => []);
  for (const target of document.targets) {
    const nodeFacts = factIdsByNode[target.node_id];
    const factNodes = nodeIdsByFact[target.fact_id];
    if (!nodeFacts || !factNodes) {
      throw new Error("An annotated target referenced an unavailable node or fact.");
    }
    nodeFacts.push(target.fact_id);
    factNodes.push(target.node_id);
  }
  const captures = (document.captures ?? []).map((capture, index) => {
    for (const nodeId of capture.use_node_ids) {
      const nodeCaptures = captureIdsByNode[nodeId];
      if (!nodeCaptures) {
        throw new Error("An annotated capture referenced an unavailable node.");
      }
      nodeCaptures.push(index);
    }
    return {
      index,
      parentNodeId: capture.parent_node_id,
      displayName: capture.display_name,
      useNodeIds: [...capture.use_node_ids],
      selected: false,
    };
  });
  const sourceLines = buildLines(document.text);
  const indexed = indexLines(sourceLines, document.nodes);
  const indexedLines = indexed.lines;
  const lines = sourceLines.map((line, index) => {
    const indexedLine = indexedLines[index];
    if (!indexedLine) {
      throw new Error(`Annotated source line ${index} was not indexed.`);
    }
    return {
      number: line.number,
      medium: mediumForLine(indexedLine.media),
      start: line.start,
      end: line.end,
      segments: segmentsForIntersections(
        document.text,
        line,
        indexedLine.intersections,
      ).map(segment => ({
        ...segment,
        captureIds: [...new Set(segment.nodeIds.flatMap(nodeId => {
          const ids = captureIdsByNode[nodeId];
          if (!ids) throw new Error(`Annotated node ${nodeId} has no capture index.`);
          return ids;
        }))],
        factIds: [...new Set(segment.nodeIds.flatMap(nodeId => {
          const ids = factIdsByNode[nodeId];
          if (!ids) throw new Error(`Annotated node ${nodeId} has no fact index.`);
          return ids;
        }))],
      })),
    };
  });
  const codeLensCandidates = [...indexed.codeLensLineIndices]
    .flatMap(([node, lineIndices]) => {
      if (lineIndices.size < 2) return [];
      const lineIndex = Math.min(...lineIndices);
      const line = sourceLines[lineIndex];
      if (!line) {
        throw new Error(`CodeLens node ${node.id} has no source line.`);
      }
      return [{
        kind: node.kind,
        lineStart: line.start,
        nodeId: node.id,
        prefix: document.text.slice(line.start, line.end).match(/^[\t ]*/)?.[0] ?? "",
      }];
    });
  const anchored = new Set(document.targets.map(target => target.fact_id));
  const facts = document.facts.map(fact => {
    const nodeIds = nodeIdsByFact[fact.id];
    if (!nodeIds) {
      throw new Error(`Annotated fact ${fact.id} has no target index.`);
    }
    return {
      id: fact.id,
      descriptor: fact.descriptor,
      category: fact.category,
      conditionality: fact.conditionality,
      detail: fact.detail ?? null,
      origin: fact.origin,
      sourceOffset: fact.source_offset,
      anchored: anchored.has(fact.id),
      nodeIds,
      selected: false,
    };
  });
  return {
    codeLensCandidates,
    document,
    lines,
    facts,
    captures,
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
  const selectedCaptureIndex =
    selectedFactId === null
      && typeof state.selectedCaptureIndex === "number"
      && Number.isInteger(state.selectedCaptureIndex)
      && prepared.captures[state.selectedCaptureIndex]
        ? state.selectedCaptureIndex
        : null;
  const selectedCapture = selectedCaptureIndex == null
    ? null
    : prepared.captures[selectedCaptureIndex];
  const targetNodeIds: number[] = selectedFactId != null
    ? [...(prepared.facts[selectedFactId]?.nodeIds ?? [])]
    : selectedCapture
      ? [...selectedCapture.useNodeIds]
      : [...new Set(state.selectedNodeIds ?? [])];
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
  const captures = prepared.captures.map(capture => ({
    ...capture,
    selected: capture.index === selectedCaptureIndex,
  }));

  return {
    media,
    selectedFactId,
    selectedCaptureIndex,
    captureScopeNodeId: selectedCapture?.parentNodeId ?? null,
    selectedNodeIds: targetNodeIds,
    lines,
    facts,
    captures,
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

function isVisible(
  medium: LineMedium,
  media: Readonly<Record<SourceMedium, boolean | undefined>>,
): boolean | undefined {
  if (medium === "Mixed") return media.CSharp || media.Il;
  return media[medium] === true;
}

function indexLines(
  lines: readonly SourceLine[],
  nodes: readonly AnnotatedSourceNode[],
): {
  codeLensLineIndices: ReadonlyMap<AnnotatedSourceNode, ReadonlySet<number>>;
  lines: IndexedLine[];
} {
  const indexed = lines.map((): IndexedLine => ({
    intersections: [],
    media: new Set<SourceMedium>(),
  }));
  const codeLensLineIndices = new Map<AnnotatedSourceNode, Set<number>>();
  for (const node of nodes) {
    const isCodeLensCandidate =
      node.medium === "CSharp"
      && node.kind !== "Block"
      && node.kind !== "MemberBody";
    if (isCodeLensCandidate) codeLensLineIndices.set(node, new Set());
    for (const span of node.spans) {
      const spanEnd = span.start + span.length;
      for (
        let lineIndex = firstLineEndingAfter(lines, span.start);
        lineIndex < lines.length;
        lineIndex++
      ) {
        const line = lines[lineIndex];
        const indexedLine = indexed[lineIndex];
        if (!line || !indexedLine) {
          throw new Error(`Annotated node ${node.id} reached an unavailable source line.`);
        }
        if (line.start >= spanEnd) break;
        if (span.start < line.end && line.start < spanEnd) {
          indexedLine.media.add(node.medium);
        }
        const start = Math.max(line.start, span.start);
        const end = Math.min(line.end, spanEnd);
        if (start < end)
        {
          indexedLine.intersections.push({ node, start, end });
          if (isCodeLensCandidate) {
            const lineIndices = codeLensLineIndices.get(node);
            if (!lineIndices) {
              throw new Error(`CodeLens node ${node.id} has no line index.`);
            }
            lineIndices.add(lineIndex);
          }
        }
      }
    }
  }
  return { codeLensLineIndices, lines: indexed };
}

function firstLineEndingAfter(lines: readonly SourceLine[], offset: number): number {
  let low = 0;
  let high = lines.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    const line = lines[middle];
    if (!line) throw new Error(`Source line ${middle} was unavailable.`);
    if (line.end <= offset) low = middle + 1;
    else high = middle;
  }
  return low;
}

function mediumForLine(media: ReadonlySet<SourceMedium>): LineMedium {
  if (media.size > 1) return "Mixed";
  return media.has("Il") ? "Il" : "CSharp";
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
    if (start === undefined || end === undefined) {
      throw new Error("An annotated segment did not contain both boundaries.");
    }
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
