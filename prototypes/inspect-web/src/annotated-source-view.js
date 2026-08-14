import {
  buildLines,
  lineMedium,
  nodeIdsForFact,
  nodesAtOffset,
  segmentsForLine,
  unanchoredFacts,
  validateDocument,
} from "./document-model.js";

// The document model owns validation, coordinates, line derivation, segmentation, and
// fact/target/node resolution. This module owns only the selection state a browser section
// carries on top of it, and returns a plain model the renderer walks.

export const MEDIA = ["CSharp", "Il"];

export const MEDIUM_LABELS = { CSharp: "C#", Il: "IL" };

export function buildAnnotatedView(document, state = {}) {
  validateDocument(document);

  const media = { CSharp: true, Il: true, ...(state.media ?? {}) };
  const selectedFactId = Number.isInteger(state.selectedFactId) ? state.selectedFactId : null;
  const targetNodeIds = selectedFactId == null
    ? [...new Set(state.selectedNodeIds ?? [])]
    : nodeIdsForFact(document, selectedFactId);
  const targeted = new Set(targetNodeIds);

  const lines = buildLines(document.text)
    .map(line => ({ ...line, medium: lineMedium(document, line) }))
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

  return {
    media,
    selectedFactId,
    selectedNodeIds: targetNodeIds,
    lines,
    facts,
    unanchoredFactIds: unanchoredFacts(document).map(fact => fact.id),
    hiddenLines: buildLines(document.text).length - lines.length,
  };
}

/// The tightest structural node covering one text offset, or null when nothing covers it.
export function nodeAtOffset(document, offset, medium = null) {
  const [node] = nodesAtOffset(document, offset, medium);
  return node ?? null;
}

/// The facts anchored to one node, in document fact order.
export function factsForNode(document, nodeId) {
  const factIds = new Set(
    document.targets.filter(target => target.node_id === nodeId).map(target => target.fact_id),
  );
  return document.facts.filter(fact => factIds.has(fact.id));
}

export function factLabel(fact) {
  const detail = fact.detail ? ` ${fact.detail}` : "";
  return `${fact.descriptor}${detail}`;
}

function isVisible(medium, media) {
  if (medium === "Mixed") return media.CSharp || media.Il;
  return media[medium] === true;
}
