import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  createAnnotatedSourceExplorerState,
  reduceAnnotatedSourceExplorerState,
  renderAnnotatedSourceEntry,
  renderAnnotatedSourceExplorer,
} from "../src/annotated-source-explorer.ts";
import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";

const appSource = readFileSync(new URL("../src/app.js", import.meta.url), "utf8");

const result = {
  document: sampleDocument,
  provenance: "test artifact",
  contextLimitation: null,
};

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

test("the member tab hands annotated source off to the full-screen explorer", () => {
  const html = renderAnnotatedSourceEntry({ result, escapeHtml });

  assert.match(html, /Open full-screen viewer/);
  assert.match(html, /<strong>4<\/strong>nodes/);
  assert.match(html, /<strong>3<\/strong>facts/);
  assert.match(html, /<strong>1<\/strong>unanchored/);
  assert.doesNotMatch(html, /IL_0001: newobj/);
});

test("the app routes the member tab into the TypeScript explorer", () => {
  assert.match(appSource, /from "\/src\/annotated-source-explorer\.ts"/);
  assert.match(appSource, /renderAnnotatedSourceEntry\(/);
  assert.match(appSource, /#open-annotated-explorer/);
  assert.match(appSource, /if \(state\.annotatedExplorer\)/);
  assert.match(appSource, /renderAnnotatedSourceExplorer\(\)/);
});

test("the explorer presents canonical text beside anchored and unanchored facts", () => {
  const html = renderAnnotatedSourceExplorer({
    result,
    state: createAnnotatedSourceExplorerState(),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });

  assert.match(html, /class="annotated-explorer"/);
  assert.match(html, /Example\.Run/);
  assert.match(html, /IL_0001: newobj instance void System\.Object::\.ctor\(\)/);
  assert.match(html, /Anchored facts/);
  assert.match(html, /alloc\.new/);
  assert.match(html, /Unanchored facts/);
  assert.match(html, /cost\.method/);
  assert.match(html, /ForStatement · 1/);
});

test("fact, source, node-kind, and clear actions preserve typed selection semantics", () => {
  const initial = createAnnotatedSourceExplorerState();
  const fact = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "select-fact", factId: 0 },
  );
  assert.equal(fact.selectedFactId, 0);

  const source = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    fact,
    { type: "select-offset", offset: sampleDocument.text.indexOf("new object()") },
  );
  assert.equal(source.selectedFactId, 0);
  assert.deepEqual(source.selectedNodeIds, [1]);

  const kind = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    source,
    { type: "select-kind", kind: "Instruction" },
  );
  assert.equal(kind.selectedFactId, null);
  assert.equal(kind.selectedKind, "Instruction");
  assert.deepEqual(kind.selectedNodeIds, [2, 3]);

  const cleared = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    kind,
    { type: "clear-selection" },
  );
  assert.deepEqual(cleared.selectedNodeIds, []);
  assert.equal(cleared.selectedKind, "");
});

test("media actions refuse an empty-looking document", () => {
  const initial = createAnnotatedSourceExplorerState();
  const csharpOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "toggle-medium", medium: "CSharp" },
  );
  assert.deepEqual(csharpOff.media, { CSharp: false, Il: true });

  const refused = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    csharpOff,
    { type: "toggle-medium", medium: "Il" },
  );
  assert.equal(refused, csharpOff);
});

test("explorer presentation escapes document and member text", () => {
  const hostile = {
    ...result,
    provenance: "<img src=x>",
    document: {
      ...sampleDocument,
      facts: sampleDocument.facts.map((fact, index) =>
        index === 0 ? { ...fact, detail: "<script>alert(1)</script>" } : fact),
    },
  };
  const html = renderAnnotatedSourceExplorer({
    result: hostile,
    state: createAnnotatedSourceExplorerState(),
    title: "<member>",
    subtitle: '"signature"',
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>/);
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /&lt;member&gt;/);
  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
});

test("invalid portable documents remain visible failures instead of empty explorers", () => {
  assert.throws(
    () => renderAnnotatedSourceExplorer({
      result: {
        ...result,
        document: { ...sampleDocument, targets: [{ fact_id: 0, node_id: 99 }] },
      },
      state: createAnnotatedSourceExplorerState(),
      title: "Example.Run",
      subtitle: "public object Run()",
      escapeHtml,
    }),
    /names a node that does not exist/,
  );
});
