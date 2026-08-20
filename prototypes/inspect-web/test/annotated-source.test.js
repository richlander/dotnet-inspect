import assert from "node:assert/strict";
import test from "node:test";
import { renderAnnotatedSource } from "../src/annotated-source.ts";
import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const result = { document: sampleDocument, provenance: "decompiled from IL" };

test("an invalid document is rejected with a message instead of throwing", () => {
  const html = renderAnnotatedSource({
    result: { ...result, document: { ...sampleDocument, targets: [{ fact_id: 0, node_id: 99 }] } },
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /Annotated source document rejected/);
  assert.match(html, /names a node that does not exist/);
});

test("both media render by default with provenance and a copy button", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /<strong>Annotated source<\/strong><span>decompiled from IL<\/span>/);
  assert.match(html, /<button id="copy-annotated" type="button">copy<\/button>/);
  assert.match(html, /medium-csharp/);
  assert.match(html, /medium-il/);
});

test("hiding a medium reports the hidden-line count", () => {
  const html = renderAnnotatedSource({
    result,
    media: { CSharp: true, Il: false },
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /annotated-hidden">2 lines hidden</);
  assert.doesNotMatch(html, /medium-il/);
});

test("a context limitation renders its narrowing message, escaped", () => {
  const html = renderAnnotatedSource({
    result: { ...result, contextLimitation: "<b>partial</b> assembly" },
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /annotated-limitation">The whole-assembly fact context was narrowed, so this fact list is incomplete: &lt;b&gt;partial&lt;\/b&gt; assembly</);
});

test("no context limitation renders no narrowing message", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.doesNotMatch(html, /annotated-limitation/);
});

test("an explicit null context limitation (as the backend serializes an absent narrowing) renders no narrowing message", () => {
  const html = renderAnnotatedSource({
    result: { ...result, contextLimitation: null },
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.doesNotMatch(html, /annotated-limitation/);
});

test("facts render their descriptor, category, detail, and anchored target count", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /annotated-fact-descriptor">alloc\.new</);
  assert.match(html, /annotated-fact-category">Allocation</);
  assert.match(html, /annotated-fact-detail">object</);
  assert.match(html, /annotated-fact-anchor">2 targets</);
});

test("an unanchored fact is marked unanchored rather than given a target count", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /annotated-fact unanchored" data-annotated-fact="2"/);
  assert.match(html, /annotated-fact-anchor">unanchored</);
});

test("selecting a fact marks it selected and shows a clear-selection control", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: 0,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /annotated-fact selected" data-annotated-fact="0"/);
  assert.match(html, /<button type="button" id="annotated-clear">clear selection<\/button>/);
});

test("with no selection, no clear-selection control is shown", () => {
  const html = renderAnnotatedSource({
    result,
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.doesNotMatch(html, /annotated-clear/);
});

test("source text is escaped as it is rendered into spans", () => {
  const html = renderAnnotatedSource({
    result: {
      document: {
        ...sampleDocument,
        text: "<script>alert(1)</script>",
        nodes: [],
        regions: [],
        facts: [],
        targets: [],
      },
      provenance: "decompiled from IL",
    },
    media: undefined,
    selectedFactId: null,
    selectedNodeIds: [],
    escapeHtml,
  });

  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>alert/);
});
