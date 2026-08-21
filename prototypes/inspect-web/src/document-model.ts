import * as model from "../../annotated-source-viewer/src/document-model.js";

export type SourceMedium = "CSharp" | "Il";
export type LineMedium = SourceMedium | "Mixed";

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

export interface SourceLine {
  number: number;
  start: number;
  end: number;
  text: string;
}

export interface SourceSegment {
  start: number;
  end: number;
  text: string;
  nodeIds: number[];
  media: SourceMedium[];
  selected: boolean;
}

type ValidateDocument = (
  document: unknown,
) => asserts document is AnnotatedSourceDocument;
type NodesAtOffset = (
  document: AnnotatedSourceDocument,
  offset: number,
  medium?: SourceMedium | null,
) => AnnotatedSourceNode[];
type SegmentsForLine = (
  document: AnnotatedSourceDocument,
  line: SourceLine,
  selectedNodeIds?: readonly number[],
) => SourceSegment[];

// The portable implementation remains owned by annotated-source-viewer. These typed aliases let
// inspect-web consume that one implementation without copying its validation or projection logic.
export const parseDocument: (json: string) => AnnotatedSourceDocument = model.parseDocument;
export const validateDocument: ValidateDocument = model.validateDocument;
export const buildLines: (text: string) => SourceLine[] = model.buildLines;
export const lineMedium: (
  document: AnnotatedSourceDocument,
  line: SourceLine,
) => LineMedium = model.lineMedium;
export const nodeIdsForFact: (
  document: AnnotatedSourceDocument,
  factId: number,
) => number[] = model.nodeIdsForFact;
export const nodeKinds: (document: AnnotatedSourceDocument) => string[] = model.nodeKinds;
export const nodeIdsForKind: (
  document: AnnotatedSourceDocument,
  kind: string,
) => number[] = model.nodeIdsForKind;
export const unanchoredFacts: (
  document: AnnotatedSourceDocument,
) => AnnotatedSourceFact[] = model.unanchoredFacts;
export const nodesAtOffset = model.nodesAtOffset as NodesAtOffset;
export const segmentsForLine = model.segmentsForLine as SegmentsForLine;
