import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  bindAnnotatedSource,
  renderAnnotatedSource,
  renderAnnotatedSourceModal,
  type AnnotatedSourceAction,
  type AnnotatedSourceBindingActions,
} from "../src/annotated-source.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  openModalSession,
  selectAllAnnotations,
  selectFinding,
  selectNode,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
} from "../src/annotated-source-session.ts";
import { validateAnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";
import {
  csharpOnlyEmptyViewerCatalog,
  sampleViewerCatalog,
} from "./annotated-source-result-fixture.ts";
import { fakeDom } from "./fake-dom.ts";

validateAnnotatedSourceDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  hidden = false;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, values: object = {}) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({
        currentTarget: this,
        target: this,
        ...values,
      }));
    }
  }
}

class FakeRoot {
  private readonly actions: FakeElement[];

  constructor(actions: FakeElement[] = []) {
    this.actions = actions;
  }

  querySelector() {
    return null;
  }

  querySelectorAll(selector: string) {
    if (selector === "[data-annotated-action]") return this.actions;
    return [];
  }
}

function recordingActions(calls: AnnotatedSourceAction[]): AnnotatedSourceBindingActions {
  return {
    onAction: action => calls.push(action),
  };
}

test("annotated source bindings dispatch the documented fixed and chip actions", () => {
  const elements = [
    new FakeElement({ annotatedAction: "copy" }),
    new FakeElement({ annotatedAction: "explore" }),
    new FakeElement({ annotatedAction: "close-modal" }),
    new FakeElement({ annotatedAction: "close-detail" }),
    new FakeElement({
      annotatedAction: "annotation-open",
      factId: "4",
      nodeId: "7",
      medium: "Il",
    }),
    new FakeElement({ annotatedAction: "inspector-open", factId: "4" }),
    new FakeElement({ annotatedAction: "annotation-set", annotatedSet: "All" }),
    new FakeElement({ annotatedAction: "finding-toggle", factId: "4" }),
    new FakeElement({ annotatedAction: "medium-toggle", medium: "CSharp" }),
    new FakeElement({ annotatedAction: "coordinate-toggle" }),
    new FakeElement({ annotatedAction: "node-select", nodeId: "7" }),
  ];
  const calls: AnnotatedSourceAction[] = [];
  bindAnnotatedSource(
    fakeDom.parentNode(new FakeRoot(elements)),
    recordingActions(calls),
  );

  for (const element of elements) element.dispatch("click");

  assert.deepEqual(calls, [
    { kind: "copy" },
    { kind: "explore" },
    { kind: "close-modal" },
    { kind: "close-detail" },
    {
      kind: "annotation-open",
      opener: {
        kind: "annotation",
        factId: 4,
        nodeId: 7,
        medium: "Il",
      },
    },
    { kind: "inspector-open", factId: 4 },
    { kind: "annotation-set", value: "All" },
    { kind: "finding-toggle", factId: 4 },
    { kind: "medium-toggle", medium: "CSharp" },
    { kind: "coordinate-toggle" },
    { kind: "node-select", nodeId: 7 },
  ]);
});

test("malformed action identities are inert rather than dispatched as NaN", () => {
  const elements = [
    new FakeElement({ annotatedAction: "annotation-open", factId: "x" }),
    new FakeElement({ annotatedAction: "inspector-open" }),
    new FakeElement({ annotatedAction: "annotation-set", annotatedSet: "Maybe" }),
    new FakeElement({ annotatedAction: "finding-toggle", factId: "-1" }),
    new FakeElement({ annotatedAction: "medium-toggle", medium: "Other" }),
    new FakeElement({ annotatedAction: "node-select", nodeId: "" }),
  ];
  const calls: AnnotatedSourceAction[] = [];
  bindAnnotatedSource(
    fakeDom.parentNode(new FakeRoot(elements)),
    recordingActions(calls),
  );

  for (const element of elements) element.dispatch("click");

  assert.deepEqual(calls, []);
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const result: AnnotatedSourceResult = {
  document: sampleDocument,
  viewerCatalog: sampleViewerCatalog,
  provenance: "decompiled from IL",
  contextLimitation: null,
};

function embeddedHtml(source: AnnotatedSourceResult = result): string {
  const model = createAnnotatedSourceViewerModel(source);
  return renderAnnotatedSource({
    result: source,
    session: createEmbeddedSession(model),
    escapeHtml,
  });
}

function modalHtml(source: AnnotatedSourceResult = result): string {
  const model = createAnnotatedSourceViewerModel(source);
  return renderAnnotatedSourceModal({
    result: source,
    session: openModalSession(model, createEmbeddedSession(model)).modal,
    escapeHtml,
  });
}

test("the result preserves the validated portable document contract", () => {
  const document: AnnotatedSourceDocument = result.document;
  assert.equal(document, sampleDocument);
});

test("an invalid document remains a visible failure instead of empty output", () => {
  assert.throws(
    () => embeddedHtml({
      ...result,
      document: {
        ...sampleDocument,
        targets: [{ fact_id: 0, node_id: 99 }],
      },
    }),
    /names a node that does not exist/,
  );
});

test("embedded reader renders complete C# defaults, source copy, and Explore", () => {
  const html = embeddedHtml();

  assert.match(html, /id="annotated-reader-title">C# with default findings/);
  assert.match(html, /decompiled from IL/);
  assert.match(html, /id="copy-annotated"[^>]*data-annotated-action="copy"/);
  assert.match(html, /id="explore-annotated"[^>]*data-annotated-action="explore"/);
  assert.match(html, /medium-csharp/);
  assert.doesNotMatch(html, /medium-il/);
  assert.match(html, /annotated-chip-embedded-0-1-CSharp/);
  assert.match(html, /annotated-chip-embedded-1-0-CSharp/);
  assert.doesNotMatch(html, /annotated-chip-embedded-0-3-Il/);
  assert.doesNotMatch(html, /data-annotated-source-start/);
});

test("annotation rows begin at their product-issued source span", () => {
  const html = embeddedHtml();
  const objectStart = sampleDocument.text.indexOf("new object()");
  const annotation = html.indexOf("annotated-chip-embedded-0-1-CSharp");
  const source = html.indexOf("new object()");

  assert.match(
    html,
    new RegExp(`data-annotated-anchor-start="${objectStart}"`),
  );
  assert.match(
    html,
    /class="annotated-row-prefix" aria-hidden="true">    return <\/span>\s*<div class="annotated-row-items"/,
  );
  assert.ok(annotation >= 0);
  assert.ok(source >= 0);
  assert.ok(annotation < source);
});

test("embedded reader renders a product context limitation without rewriting it", () => {
  const html = embeddedHtml({
    ...result,
    contextLimitation: "<b>partial</b> assembly",
  });

  assert.match(html, /annotated-context/);
  assert.match(html, /&lt;b&gt;partial&lt;\/b&gt; assembly/);
});

test("modal controls are exactly catalog-supported media and annotatable Findings", () => {
  const html = modalHtml();

  assert.match(html, /role="dialog" aria-modal="true"/);
  assert.match(html, /data-annotated-set="Default"/);
  assert.match(html, /data-annotated-set="All"/);
  assert.match(html, /data-annotated-set="Clear"/);
  assert.match(html, /annotated-finding-toggle-0/);
  assert.match(html, /annotated-finding-toggle-1/);
  assert.doesNotMatch(html, /annotated-finding-toggle-2/);
  assert.match(html, /annotated-medium-csharp/);
  assert.match(html, /annotated-medium-il/);
  assert.match(html, /annotated-coordinate-toggle/);
  assert.match(html, /data-annotated-source-start/);
});

test("Selection and Findings are peer inspector sections with a tiled empty state", () => {
  const html = modalHtml();
  const selection = html.indexOf('class="section-eyebrow">Selection');
  const findings = html.indexOf('class="section-eyebrow">Findings');

  assert.ok(selection >= 0);
  assert.ok(findings > selection);
  assert.match(
    html,
    /class="annotated-selection-empty">\s*<strong>Nothing selected<\/strong>\s*<span>Select addressable source or inspect a Finding\.<\/span>/,
  );
  assert.doesNotMatch(html, /Persistent inspector/);
});

test("modal inspector has one persistent action for every Finding including unanchored", () => {
  const html = modalHtml();

  assert.match(html, /id="annotated-inspector-0"/);
  assert.match(html, /id="annotated-inspector-1"/);
  assert.match(html, /id="annotated-inspector-2"/);
  assert.match(html, /annotated-finding-status">unanchored/);
});

test("All renders product-issued structure without turning it into a chip", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const all = selectAllAnnotations(
    model,
    openModalSession(model, createEmbeddedSession(model)).modal,
  ).state;
  const html = renderAnnotatedSourceModal({
    result,
    session: all,
    escapeHtml,
  });

  assert.match(html, /class="annotated-structure-mark">/);
  assert.match(html, /structure · Body · C#/);
  assert.doesNotMatch(
    html,
    /<button[^>]*class="annotated-structure-mark"/,
  );
});

test("chip and persistent inspector paths render identical non-empty detail", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;
  const annotation = renderAnnotatedSourceModal({
    result,
    session: selectFinding(modal, {
      kind: "annotation",
      factId: 0,
      nodeId: 1,
      medium: "CSharp",
    }),
    escapeHtml,
  });
  const inspector = renderAnnotatedSourceModal({
    result,
    session: selectFinding(modal, {
      kind: "inspector",
      factId: 0,
    }),
    escapeHtml,
  });
  const detail = (html: string) =>
    html.match(/<section class="annotated-detail"[\s\S]*?<\/section>\s*<\/section>/)?.[0];

  assert.ok(detail(annotation));
  assert.equal(detail(annotation), detail(inspector));
  assert.match(annotation, /<dt>Descriptor<\/dt><dd>alloc\.new<\/dd>/);
  assert.match(annotation, /<dt>Category<\/dt><dd>Allocation<\/dd>/);
  assert.match(annotation, /<dt>Conditionality<\/dt><dd>Always<\/dd>/);
  assert.match(annotation, /<dt>Detail<\/dt><dd>object<\/dd>/);
  assert.match(annotation, /<dt>Origin<\/dt><dd>Body<\/dd>/);
  assert.match(annotation, /ObjectCreationExpression · C#/);
  assert.match(annotation, /Instruction · IL/);
  assert.match(annotation, /Not projected by the current product query/);
});

test("source text is escaped while source actions and chrome remain separate", () => {
  const source: AnnotatedSourceResult = {
    document: {
      ...sampleDocument,
      text: "<script>alert(1)</script>",
      nodes: [],
      regions: [],
      facts: [],
      targets: [],
    },
    viewerCatalog: csharpOnlyEmptyViewerCatalog,
    provenance: "decompiled from IL",
    contextLimitation: null,
  };
  const html = embeddedHtml(source);

  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>alert/);
  assert.match(html, />copy source<\/button>/);
});

test("every chip-shaped rendered action is a button with one documented verb", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const selectedModal = renderAnnotatedSourceModal({
    result,
    session: selectNode(
      openModalSession(model, createEmbeddedSession(model)).modal,
      1,
    ),
    escapeHtml,
  });
  const detailedModal = renderAnnotatedSourceModal({
    result,
    session: selectFinding(
      openModalSession(model, createEmbeddedSession(model)).modal,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });
  const html = `${embeddedHtml()}${modalHtml()}${selectedModal}${detailedModal}`;
  const actionTags = [...html.matchAll(
    /<([a-z]+)[^>]*data-annotated-action="([^"]+)"[^>]*>/g,
  )];

  assert.ok(actionTags.length > 0);
  assert.deepEqual(
    new Set(actionTags.map(match => match[2])),
    new Set([
      "copy",
      "explore",
      "annotation-open",
      "annotation-set",
      "finding-toggle",
      "medium-toggle",
      "coordinate-toggle",
      "node-select",
      "inspector-open",
      "close-modal",
      "close-detail",
    ]),
  );
  assert.ok(actionTags.every(match => match[1] === "button"));
});

test("persistent source affordances use no underline treatment", () => {
  const styles = readFileSync(
    new URL("../src/styles.css", import.meta.url),
    "utf8",
  );
  const sourceRules = [...styles.matchAll(
    /\.annotated-source-segment[^{]*\{([^}]*)\}/g,
  )].map(match => match[1]).join("\n");

  assert.ok(sourceRules.length > 0);
  assert.doesNotMatch(sourceRules, /text-decoration|border-bottom|inset 0 -/);

  const annotationRows =
    /\.annotated-row-items\s*\{([^}]*)\}/.exec(styles)?.[1] ?? "";
  assert.ok(annotationRows.length > 0);
  assert.doesNotMatch(annotationRows, /border-left|padding-left/);
});
