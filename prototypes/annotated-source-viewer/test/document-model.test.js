import assert from "node:assert/strict";
import test from "node:test";
import {
  buildLines,
  lineMedium,
  nodeIdsForFact,
  nodeIdsForKind,
  nodeKinds,
  nodesAtOffset,
  parseDocument,
  segmentsForLine,
  unanchoredFacts,
  validateDocument,
} from "../src/document-model.js";
import { sampleDocument } from "../src/sample-document.js";

test("line coordinates use decoded UTF-16 code units", () => {
  const lines = buildLines("a😀\r\nb\rc\n");

  assert.deepEqual(lines, [
    { number: 1, start: 0, end: 3, text: "a😀" },
    { number: 2, start: 5, end: 6, text: "b" },
    { number: 3, start: 7, end: 8, text: "c" },
    { number: 4, start: 9, end: 9, text: "" },
  ]);
});

test("one fact resolves through targets to C# and IL nodes", () => {
  validateDocument(sampleDocument);

  assert.deepEqual(nodeIdsForFact(sampleDocument, 0), [1, 3]);
  assert.deepEqual(unanchoredFacts(sampleDocument).map(fact => fact.id), [2]);
});

test("node kinds form a sorted structural selector", () => {
  const document = structuredClone(sampleDocument);
  document.nodes.push({
    id: 4,
    kind: "FutureSyntax",
    medium: "CSharp",
    spans: structuredClone(sampleDocument.nodes[0].spans),
  });
  validateDocument(document);

  assert.deepEqual(nodeKinds(document), [
    "ForStatement",
    "FutureSyntax",
    "Instruction",
    "ObjectCreationExpression",
  ]);
  assert.deepEqual(nodeIdsForKind(document, "Instruction"), [2, 3]);
  assert.deepEqual(nodeIdsForKind(document, "FutureSyntax"), [4]);

  const lines = buildLines(document.text);
  const header = segmentsForLine(document, lines[0], [4]);
  const instruction = segmentsForLine(document, lines[1], [4]);
  const body = segmentsForLine(document, lines[3], [4]);
  assert.ok(header.some(segment => segment.selected));
  assert.ok(instruction.every(segment => !segment.selected));
  assert.ok(body.some(segment => segment.selected));
});

test("multi-span nodes highlight each separated run without filling gaps", () => {
  const lines = buildLines(sampleDocument.text);
  const selected = [0];
  const header = segmentsForLine(sampleDocument, lines[0], selected);
  const instruction = segmentsForLine(sampleDocument, lines[1], selected);
  const body = segmentsForLine(sampleDocument, lines[3], selected);

  assert.ok(header.some(segment => segment.selected));
  assert.ok(instruction.every(segment => !segment.selected));
  assert.ok(body.some(segment => segment.selected));
});

test("offset lookup selects the tightest nested node first", () => {
  const objectNode = sampleDocument.nodes[1];
  const offset = objectNode.spans[0].start;

  assert.deepEqual(nodesAtOffset(sampleDocument, offset, "CSharp").map(node => node.id), [1, 0]);
});

test("offset lookup ranks the containing run, not unrelated spans", () => {
  const document = {
    text: "0123456789----x--",
    nodes: [
      {
        id: 0,
        kind: "Discontinuous",
        medium: "CSharp",
        spans: [{ start: 0, length: 10 }, { start: 14, length: 1 }],
      },
      {
        id: 1,
        kind: "Enclosing",
        medium: "CSharp",
        spans: [{ start: 13, length: 3 }],
      },
    ],
    regions: [],
    facts: [],
    targets: [],
  };
  validateDocument(document);

  assert.deepEqual(nodesAtOffset(document, 14).map(node => node.id), [0, 1]);
});

test("loaded JSON rejects dangling targets and adjacent spans", () => {
  const dangling = structuredClone(sampleDocument);
  dangling.targets[0].node_id = 99;
  assert.throws(() => parseDocument(JSON.stringify(dangling)), /node that does not exist/);

  const adjacent = structuredClone(sampleDocument);
  adjacent.nodes[0].spans = [
    { start: 0, length: 2 },
    { start: 2, length: 2 },
  ];
  assert.throws(() => validateDocument(adjacent), /ordered, separated, and non-overlapping/);
});

test("semantic target invariants reject impossible joins", () => {
  const headerTarget = structuredClone(sampleDocument);
  headerTarget.targets.push({ fact_id: 2, node_id: 1 });
  assert.throws(() => validateDocument(headerTarget), /not about the member body/);

  const wrongIlOffset = structuredClone(sampleDocument);
  wrongIlOffset.facts[0].source_offset = 0;
  assert.throws(() => validateDocument(wrongIlOffset), /wrong source offset/);

  const unknownConditionality = structuredClone(sampleDocument);
  unknownConditionality.facts[0].conditionality = "Sometimes";
  assert.throws(() => validateDocument(unknownConditionality), /known conditionality and origin/);
});

test("document fact ids keep display-identical Findings distinct", () => {
  const document = structuredClone(sampleDocument);
  document.facts[1] = {
    ...document.facts[0],
    id: 1,
  };
  document.targets = [
    ...document.targets.filter(target => target.fact_id !== 1),
    ...document.targets
      .filter(target => target.fact_id === 0)
      .map(target => ({ ...target, fact_id: 1 })),
  ];

  assert.equal(validateDocument(document), document);
  assert.deepEqual(nodeIdsForFact(document, 0), [1, 3]);
  assert.deepEqual(nodeIdsForFact(document, 1), [1, 3]);
});

test("instruction and fact offsets stay within the signed 32-bit contract", () => {
  const oversizedInstruction = structuredClone(sampleDocument);
  oversizedInstruction.nodes[3].il_offset = 2_147_483_648;
  assert.throws(() => validateDocument(oversizedInstruction), /non-negative 32-bit integer/);

  const oversizedFact = structuredClone(sampleDocument);
  oversizedFact.facts[0].source_offset = 2_147_483_648;
  assert.throws(() => validateDocument(oversizedFact), /non-negative 32-bit integer/);
});

test("only IL instruction nodes may carry an IL offset", () => {
  const csharpWithOffset = structuredClone(sampleDocument);
  csharpWithOffset.nodes[1].il_offset = 1;
  assert.throws(() => validateDocument(csharpWithOffset), /do not identify one instruction/);
});

test("mixed-medium lines classify and segment each substring independently", () => {
  const mixed = {
    text: "ab",
    nodes: [
      { id: 0, kind: "Name", medium: "CSharp", spans: [{ start: 0, length: 1 }] },
      { id: 1, kind: "Instruction", medium: "Il", spans: [{ start: 1, length: 1 }], il_offset: 0 },
    ],
    regions: [],
    facts: [],
    targets: [],
  };
  validateDocument(mixed);
  const line = buildLines(mixed.text)[0];
  const segments = segmentsForLine(mixed, line);

  assert.equal(lineMedium(mixed, line), "Mixed");
  assert.deepEqual(segments.map(segment => segment.media), [["CSharp"], ["Il"]]);
  assert.deepEqual(nodesAtOffset(mixed, 0).map(node => node.id), [0]);
  assert.deepEqual(nodesAtOffset(mixed, 1).map(node => node.id), [1]);
});
