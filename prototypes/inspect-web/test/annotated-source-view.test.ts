import assert from "node:assert/strict";
import test from "node:test";
import {
  buildAnnotatedView,
  factsForNode,
  nodeAtOffset,
  prepareAnnotatedView,
  projectPreparedAnnotatedView,
  validateAnnotatedSourceDocument,
} from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import { buildLines, lineMedium, segmentsForLine } from "../src/document-model.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";
import { captureDocument } from "./annotated-source-fixtures.ts";

validateAnnotatedSourceDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;

test("an invalid document is refused rather than rendered", () => {
  assert.throws(
    () => buildAnnotatedView({ ...sampleDocument, targets: [{ fact_id: 0, node_id: 99 }] }),
    /names a node that does not exist/,
  );
});

test("canonical lines carry their medium and the whole text buffer", () => {
  const view = buildAnnotatedView(sampleDocument);

  assert.deepEqual(view.lines.map(line => line.medium), [
    "CSharp",
    "Il",
    "CSharp",
    "CSharp",
    "Il",
    "CSharp",
  ]);
  assert.equal(
    view.lines.map(line => line.segments.map(segment => segment.text).join("")).join("\n"),
    sampleDocument.text,
  );
  assert.equal(view.hiddenLines, 0);
});

test("hiding a medium drops only that medium's lines and rebases no coordinate", () => {
  const view = buildAnnotatedView(sampleDocument, { media: { CSharp: true, Il: false } });
  const firstLine = view.lines[0];
  const thirdLine = view.lines[2];
  assert.ok(firstLine);
  assert.ok(thirdLine);

  assert.deepEqual(view.lines.map(line => line.number), [1, 3, 4, 6]);
  assert.equal(view.hiddenLines, 2);
  assert.equal(firstLine.start, 0);
  assert.equal(
    thirdLine.segments.map(segment => segment.text).join(""),
    "    return new object();",
  );
});

test("an explicitly undefined medium preserves the JavaScript visibility semantics", () => {
  const view = buildAnnotatedView(sampleDocument, {
    media: { CSharp: undefined, Il: true },
  });

  assert.equal(view.media.CSharp, undefined);
  assert.deepEqual(view.lines.map(line => line.number), [2, 5]);
});

test("selecting a fact highlights every target node across both media", () => {
  const view = buildAnnotatedView(sampleDocument, { selectedFactId: 0 });

  assert.deepEqual(view.selectedNodeIds, [1, 3]);
  const highlighted = view.lines
    .flatMap(line => line.segments)
    .filter(segment => segment.selected)
    .map(segment => segment.text);
  assert.deepEqual(highlighted, [
    "new object()",
    "IL_0001: newobj instance void System.Object::.ctor()",
  ]);
});

test("anchored fact ids are projected onto every target segment before selection", () => {
  const view = buildAnnotatedView(sampleDocument);
  const ambient = view.lines
    .flatMap(line => line.segments)
    .filter(segment => segment.factIds.length > 0)
    .map(segment => [segment.text, segment.factIds]);

  assert.deepEqual(ambient, [
    ["for (var i = 0; i < 2; i++)", [1]],
    ["IL_0000: ldc.i4.0", [1]],
    ["{", [1]],
    ["    return ", [1]],
    ["new object()", [0, 1]],
    [";", [1]],
    ["IL_0001: newobj instance void System.Object::.ctor()", [0]],
    ["}", [1]],
  ]);
});

test("captured variables project ambient uses and exact capture selection", () => {
  const ambient = buildAnnotatedView(captureDocument);
  assert.deepEqual(
    ambient.lines.flatMap(line => line.segments)
      .filter(segment => segment.captureIds.length > 0)
      .map(segment => [segment.text, segment.captureIds]),
    [["first", [0]], ["second", [1]]],
  );

  const selected = buildAnnotatedView(captureDocument, { selectedCaptureIndex: 0 });
  assert.equal(selected.selectedCaptureIndex, 0);
  assert.equal(selected.captureScopeNodeId, 0);
  assert.deepEqual(selected.selectedNodeIds, [1]);
  assert.deepEqual(
    selected.lines.flatMap(line => line.segments)
      .filter(segment => segment.selected)
      .map(segment => segment.text),
    ["first"],
  );
  const firstCapture = selected.captures[0];
  const secondCapture = selected.captures[1];
  assert.ok(firstCapture);
  assert.ok(secondCapture);
  assert.equal(firstCapture.selected, true);
  assert.equal(secondCapture.selected, false);
});

test("a multi-span node highlights its spans without selecting interleaved IL", () => {
  const view = buildAnnotatedView(sampleDocument, { selectedFactId: 1 });

  assert.deepEqual(
    view.lines.filter(line => line.segments.some(segment => segment.selected))
      .map(line => line.number),
    [1, 2, 3, 4, 6],
  );

  const csharpOnly = buildAnnotatedView(sampleDocument, {
    selectedFactId: 1,
    media: { CSharp: true, Il: false },
  });
  assert.deepEqual(
    csharpOnly.lines.filter(line => line.segments.some(segment => segment.selected))
      .map(line => line.number),
    [1, 3, 4, 6],
  );
});

test("facts with no targets stay visible as explicitly unanchored", () => {
  const view = buildAnnotatedView(sampleDocument);
  const unanchored = view.facts[2];
  assert.ok(unanchored);

  assert.deepEqual(view.facts.map(fact => fact.id), [0, 1, 2]);
  assert.deepEqual(view.unanchoredFactIds, [2]);
  assert.equal(unanchored.anchored, false);
  assert.deepEqual(unanchored.nodeIds, []);
  assert.equal(unanchored.origin, "MemberHeader");
});

test("clicking text selects the tightest node and its facts", () => {
  const offset = sampleDocument.text.indexOf("new object()");

  assert.equal(nodeAtOffset(sampleDocument, offset)!.id, 1);
  assert.deepEqual(factsForNode(sampleDocument, 1).map(fact => fact.descriptor), ["alloc.new"]);
  assert.equal(nodeAtOffset(sampleDocument, sampleDocument.text.indexOf("for ("))!.id, 0);
  assert.equal(nodeAtOffset(sampleDocument, sampleDocument.text.length), null);
});

test("clicking a node highlights it without selecting a fact", () => {
  const view = buildAnnotatedView(sampleDocument, { selectedNodeIds: [3] });

  assert.equal(view.selectedFactId, null);
  assert.deepEqual(
    view.lines.flatMap(line => line.segments).filter(segment => segment.selected)
      .map(segment => segment.text),
    ["IL_0001: newobj instance void System.Object::.ctor()"],
  );
  assert.deepEqual(view.facts.filter(fact => fact.selected), []);
});

test("prepared documents preserve projection semantics across interactions", () => {
  const prepared = prepareAnnotatedView(sampleDocument);
  const state = {
    selectedFactId: 1,
    media: { CSharp: true, Il: false },
  };

  assert.deepEqual(
    projectPreparedAnnotatedView(prepared, state),
    buildAnnotatedView(sampleDocument, state),
  );
  assert.equal(prepared.document, sampleDocument);
});

test("indexed preparation preserves the portable model's segmentation", () => {
  const overlappingMixedDocument: AnnotatedSourceDocument = {
    text: "ab\r\ncd\nef",
    nodes: [
      {
        id: 0,
        kind: "Block",
        medium: "CSharp",
        spans: [{ start: 0, length: 2 }, { start: 4, length: 2 }],
        il_offset: null,
      },
      {
        id: 1,
        kind: "Block",
        medium: "Il",
        spans: [{ start: 1, length: 5 }],
        il_offset: null,
      },
      {
        id: 2,
        kind: "Identifier",
        medium: "CSharp",
        spans: [{ start: 7, length: 2 }],
        il_offset: null,
      },
    ],
    regions: [],
    facts: [],
    targets: [],
  };

  for (const document of [sampleDocument, overlappingMixedDocument]) {
    const prepared = prepareAnnotatedView(document);
    const sourceLines = buildLines(document.text);
    const expected = sourceLines.map(line => ({
      number: line.number,
      medium: lineMedium(document, line),
      start: line.start,
      end: line.end,
      segments: segmentsForLine(document, line),
    }));

    assert.deepEqual(
      prepared.lines.map(line => ({
        ...line,
        segments: line.segments.map(segment => ({
          start: segment.start,
          end: segment.end,
          text: segment.text,
          nodeIds: segment.nodeIds,
          media: segment.media,
          selected: segment.selected,
        })),
      })),
      expected,
    );
  }
});

test("indexed preparation preserves blank-line media for LF and CRLF text", () => {
  for (const text of ["a\n\nb", "a\r\n\r\nb"]) {
    const document: AnnotatedSourceDocument = {
      text,
      nodes: [{
        id: 0,
        kind: "Block",
        medium: "Il",
        spans: [{ start: 0, length: text.length }],
        il_offset: null,
      }],
      regions: [],
      facts: [],
      targets: [],
    };
    const prepared = prepareAnnotatedView(document);
    const sourceLines = buildLines(text);

    assert.deepEqual(
      prepared.lines.map(line => line.medium),
      sourceLines.map(line => lineMedium(document, line)),
    );
    assert.deepEqual(
      projectPreparedAnnotatedView(prepared, { media: { CSharp: false, Il: true } })
        .lines.map(line => line.number),
      sourceLines.map(line => line.number),
    );
  }
});

test("large line-oriented documents prepare within the interaction budget", () => {
  const lineCount = 8_000;
  const text = "x\n".repeat(lineCount);
  const document: AnnotatedSourceDocument = {
    text,
    nodes: Array.from({ length: lineCount }, (_, id) => ({
      id,
      kind: "Identifier",
      medium: "CSharp",
      spans: [{ start: id * 2, length: 1 }],
      il_offset: null,
    })),
    regions: [],
    facts: [],
    targets: [],
  };

  const wallStarted = performance.now();
  const cpuStarted = process.cpuUsage();
  const prepared = prepareAnnotatedView(document);
  const wallElapsed = performance.now() - wallStarted;
  const cpuUsage = process.cpuUsage(cpuStarted);
  const cpuElapsed = (cpuUsage.user + cpuUsage.system) / 1_000;

  assert.equal(prepared.lines.length, lineCount + 1);
  assert.equal(prepared.lines.slice(0, lineCount).every(line => line.segments.length === 1), true);
  assert.ok(
    cpuElapsed < 2_000,
    `preparing ${lineCount} lines took ${cpuElapsed.toFixed(1)}ms CPU (${wallElapsed.toFixed(1)}ms wall)`,
  );
});
