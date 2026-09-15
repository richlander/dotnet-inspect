const media = new Set(["CSharp", "Il"]);
const conditionalities = new Set(["Always", "CachedOnce", "PerIteration"]);
const origins = new Set(["Body", "MemberHeader"]);
const regionRoles = new Set(["Construct", "Header", "Body", "Else", "Catch", "Finally", "Case"]);
const maxInt32 = 2_147_483_647;

export function parseDocument(json) {
  const document = JSON.parse(json);
  validateDocument(document);
  return document;
}

export function validateDocument(document) {
  if (!document || typeof document !== "object" || Array.isArray(document)) {
    throw new TypeError("The payload must be an AnnotatedSourceDocument object.");
  }
  if (typeof document.text !== "string") {
    throw new TypeError("The document text must be a string.");
  }
  validateUtf16(document.text, "text");

  for (const property of ["nodes", "regions", "facts", "targets"]) {
    if (!Array.isArray(document[property])) {
      throw new TypeError(`The document ${property} must be an array.`);
    }
  }

  let previousIlOffset = -1;
  document.nodes.forEach((node, index) => {
    if (node?.id !== index) {
      throw new TypeError(`Node ids must be contiguous; slot ${index} has id ${node?.id}.`);
    }
    if (typeof node.kind !== "string" || !media.has(node.medium)) {
      throw new TypeError(`Node ${index} must have a string kind and a known medium.`);
    }
    validateUtf16(node.kind, `nodes[${index}].kind`);
    validateSpans(node.spans, document.text.length, `nodes[${index}].spans`);

    const hasIlOffset = node.il_offset != null;
    if (
      hasIlOffset
      && (!Number.isInteger(node.il_offset) || node.il_offset < 0 || node.il_offset > maxInt32)
    ) {
      throw new TypeError(`Node ${index} IL offset must be a non-negative 32-bit integer or null.`);
    }
    const instruction = node.kind === "Instruction";
    if (instruction && node.medium !== "Il") {
      throw new TypeError(`Node ${index} kind identifies an instruction outside IL text.`);
    }
    if (instruction !== hasIlOffset) {
      throw new TypeError(`Node ${index} kind, medium, and IL offset do not identify one instruction.`);
    }
    if (hasIlOffset && node.il_offset <= previousIlOffset) {
      throw new TypeError("IL instruction offsets must be unique and strictly increasing in node order.");
    }
    if (hasIlOffset) previousIlOffset = node.il_offset;
  });

  document.regions.forEach((region, index) => {
    if (!regionRoles.has(region?.role)) {
      throw new TypeError(`Region ${index} must have a known role.`);
    }
    validateSpans(region.spans, document.text.length, `regions[${index}].spans`);
  });

  document.facts.forEach((fact, index) => {
    if (fact?.id !== index) {
      throw new TypeError(`Fact ids must be contiguous; slot ${index} has id ${fact?.id}.`);
    }
    for (const property of ["descriptor", "category", "conditionality", "origin"]) {
      if (typeof fact[property] !== "string") {
        throw new TypeError(`Fact ${index} must have a string ${property}.`);
      }
      validateUtf16(fact[property], `facts[${index}].${property}`);
    }
    if (!conditionalities.has(fact.conditionality) || !origins.has(fact.origin)) {
      throw new TypeError(`Fact ${index} must have a known conditionality and origin.`);
    }
    if (fact.detail != null) {
      if (typeof fact.detail !== "string") {
        throw new TypeError(`Fact ${index} detail must be a string or null.`);
      }
      validateUtf16(fact.detail, `facts[${index}].detail`);
    }
    if (
      !Number.isInteger(fact.source_offset)
      || fact.source_offset < -1
      || fact.source_offset > maxInt32
    ) {
      throw new TypeError(
        `Fact ${index} source offset must be -1 or a non-negative 32-bit integer.`,
      );
    }
    if (fact.origin === "MemberHeader" && fact.source_offset !== -1) {
      throw new TypeError(`Member-header fact ${index} must have source offset -1.`);
    }

  });

  const seenTargets = new Set();
  document.targets.forEach((target, index) => {
    if (!Number.isInteger(target?.fact_id) || !document.facts[target.fact_id]) {
      throw new TypeError(`Target ${index} names a fact that does not exist.`);
    }
    if (!Number.isInteger(target.node_id) || !document.nodes[target.node_id]) {
      throw new TypeError(`Target ${index} names a node that does not exist.`);
    }
    const key = `${target.fact_id}:${target.node_id}`;
    if (seenTargets.has(key)) {
      throw new TypeError(`Target ${index} repeats ${key}.`);
    }
    seenTargets.add(key);

    const fact = document.facts[target.fact_id];
    const node = document.nodes[target.node_id];
    if (fact.origin !== "Body") {
      throw new TypeError(`Target ${index} anchors a fact that is not about the member body.`);
    }
    if (node.medium === "Il" && node.il_offset !== fact.source_offset) {
      throw new TypeError(`Target ${index} anchors an IL instruction at the wrong source offset.`);
    }
  });

  return document;
}

export function buildLines(text) {
  const lines = [];
  let start = 0;

  for (let index = 0; index < text.length; index++) {
    if (text[index] !== "\r" && text[index] !== "\n") continue;
    lines.push({
      number: lines.length + 1,
      start,
      end: index,
      text: text.slice(start, index),
    });
    if (text[index] === "\r" && text[index + 1] === "\n") index++;
    start = index + 1;
  }

  lines.push({
    number: lines.length + 1,
    start,
    end: text.length,
    text: text.slice(start),
  });
  return lines;
}

export function lineMedium(document, line) {
  const lineMedia = new Set(
    document.nodes
      .filter(node => node.spans.some(span => intersectsLine(span, line)))
      .map(node => node.medium),
  );
  if (lineMedia.size > 1) return "Mixed";
  return lineMedia.has("Il") ? "Il" : "CSharp";
}

export function nodeIdsForFact(document, factId) {
  return document.targets
    .filter(target => target.fact_id === factId)
    .map(target => target.node_id);
}

export function nodeKinds(document) {
  return [...new Set(document.nodes.map(node => node.kind))].sort();
}

export function nodeIdsForKind(document, kind) {
  return document.nodes.filter(node => node.kind === kind).map(node => node.id);
}

export function unanchoredFacts(document) {
  const targeted = new Set(document.targets.map(target => target.fact_id));
  return document.facts.filter(fact => !targeted.has(fact.id));
}

export function nodesAtOffset(document, offset, medium = null) {
  return document.nodes
    .filter(node => medium == null || node.medium === medium)
    .map(node => ({
      node,
      containingLength: Math.min(
        ...node.spans
          .filter(span => span.start <= offset && offset < span.start + span.length)
          .map(span => span.length),
      ),
    }))
    .filter(candidate => Number.isFinite(candidate.containingLength))
    .sort((left, right) =>
      left.containingLength - right.containingLength
      || nodeLength(left.node) - nodeLength(right.node)
      || left.node.id - right.node.id)
    .map(candidate => candidate.node);
}

export function segmentsForLine(document, line, selectedNodeIds = []) {
  const boundaries = new Set([line.start, line.end]);
  const intersections = new Map();

  for (const node of document.nodes) {
    for (const span of node.spans) {
      const start = Math.max(line.start, span.start);
      const end = Math.min(line.end, span.start + span.length);
      if (start >= end) continue;
      boundaries.add(start);
      boundaries.add(end);
      const spans = intersections.get(node.id) ?? [];
      spans.push({ start, end });
      intersections.set(node.id, spans);
    }
  }

  const selected = new Set(selectedNodeIds);
  const ordered = [...boundaries].sort((left, right) => left - right);
  const segments = [];

  for (let index = 0; index < ordered.length - 1; index++) {
    const start = ordered[index];
    const end = ordered[index + 1];
    if (start === end) continue;
    const nodes = document.nodes
      .filter(node => intersections.get(node.id)?.some(span => span.start <= start && end <= span.end))
      .sort((left, right) => nodeLength(left) - nodeLength(right) || left.id - right.id);
    const nodeIds = nodes.map(node => node.id);
    segments.push({
      start,
      end,
      text: document.text.slice(start, end),
      nodeIds,
      media: [...new Set(nodes.map(node => node.medium))],
      selected: nodeIds.some(id => selected.has(id)),
    });
  }

  return segments;
}

function validateSpans(spans, textLength, name) {
  if (!Array.isArray(spans) || spans.length === 0) {
    throw new TypeError(`${name} must contain at least one span.`);
  }
  let previousEnd = -1;
  spans.forEach((span, index) => {
    if (!Number.isInteger(span?.start) || !Number.isInteger(span.length) || span.length <= 0) {
      throw new TypeError(`${name}[${index}] must contain integer start and positive length.`);
    }
    const end = span.start + span.length;
    if (span.start < 0 || end > textLength) {
      throw new TypeError(`${name}[${index}] is outside the document text.`);
    }
    if (index > 0 && span.start <= previousEnd) {
      throw new TypeError(`${name} must be ordered, separated, and non-overlapping.`);
    }
    previousEnd = end;
  });
}

function validateUtf16(value, name) {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code < 0xd800 || code > 0xdfff) continue;
    if (code <= 0xdbff && index + 1 < value.length) {
      const next = value.charCodeAt(index + 1);
      if (next >= 0xdc00 && next <= 0xdfff) {
        index++;
        continue;
      }
    }
    throw new TypeError(`${name} contains malformed UTF-16 at offset ${index}.`);
  }
}

function intersectsLine(span, line) {
  return span.start < line.end && line.start < span.start + span.length;
}

function nodeLength(node) {
  return node.spans.reduce((sum, span) => sum + span.length, 0);
}
