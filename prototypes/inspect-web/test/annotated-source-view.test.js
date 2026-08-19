import assert from "node:assert/strict";
import test from "node:test";
import {
  buildAnnotatedView,
  factsForNode,
  nodeAtOffset,
} from "../src/annotated-source-view.ts";
import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";

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

  assert.deepEqual(view.lines.map(line => line.number), [1, 3, 4, 6]);
  assert.equal(view.hiddenLines, 2);
  assert.equal(view.lines[0].start, 0);
  assert.equal(
    view.lines[2].segments.map(segment => segment.text).join(""),
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

  assert.deepEqual(view.facts.map(fact => fact.id), [0, 1, 2]);
  assert.deepEqual(view.unanchoredFactIds, [2]);
  assert.equal(view.facts[2].anchored, false);
  assert.deepEqual(view.facts[2].nodeIds, []);
  assert.equal(view.facts[2].origin, "MemberHeader");
});

test("clicking text selects the tightest node and its facts", () => {
  const offset = sampleDocument.text.indexOf("new object()");

  assert.equal(nodeAtOffset(sampleDocument, offset).id, 1);
  assert.deepEqual(factsForNode(sampleDocument, 1).map(fact => fact.descriptor), ["alloc.new"]);
  assert.equal(nodeAtOffset(sampleDocument, sampleDocument.text.indexOf("for (")).id, 0);
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
