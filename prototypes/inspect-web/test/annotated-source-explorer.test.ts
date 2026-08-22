import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  AnnotatedSourceExplorerRenderCoordinator,
  createAnnotatedSourceExplorerState,
  reduceAnnotatedSourceExplorerState,
  renderAnnotatedSourceEntry,
  renderAnnotatedSourceExplorer,
  type AnnotatedSourceResult,
} from "../src/annotated-source-explorer.ts";
import {
  validateAnnotatedSourceDocument,
  type AnnotatedSourceDocument,
} from "../src/annotated-source-view.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";

validateAnnotatedSourceDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;
const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

const result: AnnotatedSourceResult = {
  document: sampleDocument,
  provenance: "test artifact",
  contextLimitation: null,
};

function escapeHtml(value: unknown) {
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
  assert.match(appSource, /from "\.\/annotated-source-explorer\.ts"/);
  assert.match(appSource, /renderAnnotatedSourceEntry\(/);
  assert.match(appSource, /#open-annotated-explorer/);
  assert.match(appSource, /if \(state\.annotatedExplorer\)/);
  assert.match(appSource, /renderAnnotatedSourceExplorer\(\)/);
});

test("the explorer presents canonical text beside anchored and unanchored facts", () => {
  const html = renderAnnotatedSourceExplorer({
    result,
    state: createAnnotatedSourceExplorerState(sampleDocument),
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
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  const fact = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "select-fact", factId: 0 },
  );
  assert.equal(fact.selectedFactId, 0);
  assert.equal(fact.prepared, initial.prepared);

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
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
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
  const hostile: AnnotatedSourceResult = {
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
    state: createAnnotatedSourceExplorerState(hostile.document),
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
  const invalidResult: AnnotatedSourceResult = {
    ...result,
    document: { ...sampleDocument, targets: [{ fact_id: 0, node_id: 99 }] },
  };
  assert.throws(
    () => renderAnnotatedSourceEntry({
      result: invalidResult,
      escapeHtml,
    }),
    /names a node that does not exist/,
  );
});

test("entry and explorer disclose narrowed fact context", () => {
  const limited: AnnotatedSourceResult = {
    ...result,
    contextLimitation: "<partial> assembly",
  };

  for (const html of [
    renderAnnotatedSourceEntry({ result: limited, escapeHtml }),
    renderAnnotatedSourceExplorer({
      result: limited,
      state: createAnnotatedSourceExplorerState(sampleDocument),
      title: "Example.Run",
      subtitle: "public object Run()",
      escapeHtml,
    }),
  ]) {
    assert.match(html, /annotated-limitation/);
    assert.match(html, /&lt;partial&gt; assembly/);
  }
});

test("addressable source uses one tab stop and roving keyboard navigation", () => {
  const html = renderAnnotatedSourceExplorer({
    result,
    state: createAnnotatedSourceExplorerState(sampleDocument),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });

  assert.match(html, /class="ase-code-scroll" tabindex="0"/);
  assert.match(html, /<button type="button" tabindex="-1" class="annotated-span addressable"/);
  assert.match(
    styles,
    /\.annotated-span\.addressable\s*\{[^}]*user-select:\s*text;/,
  );
  assert.match(appSource, /case "ArrowRight":/);
  assert.match(
    appSource,
    /if \(event\.altKey \|\| event\.ctrlKey \|\| event\.metaKey \|\| event\.shiftKey\) return;/,
  );
  assert.match(appSource, /spans\[nextIndex\]\.focus\(\{ preventScroll: true \}\)/);
});

test("all explorer renders preserve focus and scroll while home invalidates the context", () => {
  assert.match(appSource, /captureAnnotatedSourceExplorerRenderState\(\)/);
  assert.match(appSource, /annotatedSourceExplorerRenderCoordinator\.begin/);
  assert.match(appSource, /restoreAnnotatedSourceExplorerRenderState\(renderGeneration\)/);
  assert.match(appSource, /code\.scrollTop = renderState\.codeScroll/);
  assert.match(appSource, /code\.scrollLeft = renderState\.codeScrollLeft/);
  assert.match(
    appSource,
    /document\.querySelector<HTMLElement>\(renderState\.focusSelector\)\?\.focus/,
  );
  assert.match(
    appSource,
    /annotatedSourceExplorerRenderCoordinator\.isCurrent\(renderGeneration\)/,
  );
  assert.match(appSource, /!state\.annotatedExplorer \|\| state\.home/);
});

test("back-to-back renders retain the pre-replacement scroll snapshot", () => {
  const coordinator = new AnnotatedSourceExplorerRenderCoordinator();
  const preserved = {
    codeScroll: 123,
    codeScrollLeft: 45,
    inspectorScroll: 87,
    focusSelector: "#ase-exit",
  };
  const reset = {
    codeScroll: 0,
    codeScrollLeft: 0,
    inspectorScroll: 0,
    focusSelector: "",
  };

  const firstGeneration = coordinator.begin(preserved);
  const secondGeneration = coordinator.begin(reset);

  assert.equal(coordinator.consume(firstGeneration), null);
  assert.deepEqual(coordinator.consume(secondGeneration), preserved);
  assert.equal(coordinator.isCurrent(secondGeneration), true);
  coordinator.invalidate();
  assert.equal(coordinator.isCurrent(secondGeneration), false);
});

test("action focus remains visible when inspector content changes height", () => {
  assert.match(appSource, /focused\?\.focus\(\{ preventScroll: true \}\)/);
  assert.match(appSource, /focused\?\.closest\("\.ase-inspector"\)/);
  assert.match(
    appSource,
    /focused\.scrollIntoView\(\{ block: "nearest", inline: "nearest" \}\)/,
  );
});

test("fact buttons expose their toggle state", () => {
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  const selected = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "select-fact", factId: 0 },
  );
  const html = renderAnnotatedSourceExplorer({
    result,
    state: selected,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });

  assert.match(html, /data-ase-fact="0" aria-pressed="true"/);
  assert.match(html, /data-ase-fact="1" aria-pressed="false"/);
});

test("reopening an unchanged document reuses its prepared projection", () => {
  const first = createAnnotatedSourceExplorerState(sampleDocument);
  const reopened = createAnnotatedSourceExplorerState(sampleDocument);

  assert.equal(reopened.prepared, first.prepared);
});

test("full-bleed feedback and narrow layouts stay reachable", () => {
  assert.match(styles, /\.toast\s*\{[^}]*z-index:\s*120/s);
  assert.match(
    styles,
    /\.ase-workspace\s*\{[^}]*grid-template-rows:\s*minmax\(0, 3fr\) minmax\(0, 2fr\)/s,
  );
  assert.doesNotMatch(styles, /grid-template-rows:[^;]*(?:45vh|240px)/);
});
