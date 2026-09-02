import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  bindAnnotatedSource,
  renderAnnotatedSource,
  renderAnnotatedSourceModal,
  renderAnnotatedSourcePageActions,
  type AnnotatedSourceAction,
  type AnnotatedSourceBindingActions,
} from "../src/annotated-source.ts";
import {
  createCSharpRangeHighlighter,
} from "../src/csharp-highlighting.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  openModalSession,
  selectAllAnnotations,
  selectFinding,
  selectNode,
  toggleCoordinates,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
} from "../src/annotated-source-session.ts";
import {
  csharpHighlightingInput,
  csharpHighlightingText,
  validateAnnotatedSourceDocument,
} from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";
import {
  csharpOnlyEmptyViewerCatalog,
  sampleInvocationTarget,
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
    new FakeElement({
      annotatedAction: "destination-open",
      destinationIndex: "2",
      destination: "source",
    }),
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
    {
      kind: "destination-open",
      destinationIndex: 2,
      destination: "source",
    },
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
    new FakeElement({
      annotatedAction: "destination-open",
      destinationIndex: "x",
      destination: "other",
    }),
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

function invocationResult(): AnnotatedSourceResult {
  return {
    ...result,
    document: {
      ...sampleDocument,
      nodes: sampleDocument.nodes.map(node =>
        node.id === 1
          ? { ...node, kind: "InvocationExpression" }
          : node),
    },
    viewerCatalog: {
      ...sampleViewerCatalog,
      invocationLikeNodeKinds: ["InvocationExpression"],
      invocationDestinations: [{
        nodeId: 1,
        target: sampleInvocationTarget,
      }],
      destinations: {
        available: true,
        unavailableReason: null,
      },
    },
  };
}

test("the result preserves the validated portable document contract", () => {
  const document: AnnotatedSourceDocument = result.document;
  assert.equal(document, sampleDocument);
});

test("the pure renderer rejects an invalid document for the shell to surface", () => {
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

test("inline working surface starts with complete C# and ends with provenance", () => {
  const html = embeddedHtml();
  const source = html.indexOf("medium-csharp");
  const provenance = html.indexOf("decompiled from IL");

  assert.doesNotMatch(html, /Annotated Source|C# with default findings/);
  assert.doesNotMatch(html, /data-annotated-action="(?:copy|explore)"/);
  assert.match(html, /class="annotated-reader-footer"/);
  assert.ok(source >= 0);
  assert.ok(provenance > source);
  assert.match(html, /medium-csharp/);
  assert.doesNotMatch(html, /medium-il/);
  assert.match(html, /annotated-chip-embedded-0-1-CSharp/);
  assert.match(html, /annotated-chip-embedded-1-0-CSharp/);
  assert.doesNotMatch(html, /annotated-chip-embedded-0-3-Il/);
  assert.doesNotMatch(html, /data-annotated-source-start/);
});

test("page-owned actions expose Copy and Explore only when the document is ready", () => {
  const enabled = renderAnnotatedSourcePageActions(true);
  const disabled = renderAnnotatedSourcePageActions(false);

  assert.match(
    enabled,
    /id="copy-annotated"[^>]*data-annotated-action="copy"[^>]*>Copy<\/button>/,
  );
  assert.match(
    enabled,
    /id="explore-annotated"[^>]*data-annotated-action="explore"[^>]*>Explore<\/button>/,
  );
  assert.doesNotMatch(enabled, / disabled/);
  assert.match(disabled, /id="copy-annotated"[^>]* disabled/);
  assert.match(disabled, /id="explore-annotated"[^>]* disabled/);
});

test("a selected invocation exposes separate Member and Source destinations", () => {
  const source = invocationResult();
  const model = createAnnotatedSourceViewerModel(source);
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;
  const unselected = renderAnnotatedSourceModal({
    result: source,
    session: modal,
    escapeHtml,
  });
  const selected = renderAnnotatedSourceModal({
    result: source,
    session: selectNode(modal, 1),
    escapeHtml,
  });

  assert.doesNotMatch(unselected, /data-annotated-action="destination-open"/);
  assert.match(
    selected,
    /data-destination-index="0"\s+data-destination="member"[\s\S]*?>Member<\/button>/,
  );
  assert.match(
    selected,
    /data-destination-index="0"\s+data-destination="source"[\s\S]*?>Source<\/button>/,
  );
  assert.match(selected, /Open member overview for Example\.Targets\.Target/);
  assert.match(selected, /Open source for Example\.Targets\.Target/);
  assert.doesNotMatch(selected, />Navigate</);
});

test("C# highlighting crosses product segments without changing source text", () => {
  const source = 'return Widget.Create("x");';
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: () => [
        { type: "keyword", content: "return" },
        " ",
        { type: "class-name", content: "Widget" },
        { type: "punctuation", content: "." },
        { type: "function", content: "Create" },
        { type: "punctuation", content: "(" },
        { type: "string", content: '"x"' },
        { type: "punctuation", content: ");" },
      ],
    },
    escapeHtml,
  );
  const start = 2;
  const length = source.length - 4;
  const html = highlighter.render(start, length);

  assert.match(html, /class="token keyword">turn<\/span>/);
  assert.match(html, /class="token class-name">Widget<\/span>/);
  assert.match(html, /class="token function">Create<\/span>/);
  assert.equal(
    html
      .replaceAll(/<[^>]+>/g, "")
      .replaceAll("&quot;", '"'),
    source.slice(start, start + length),
  );
});

test("C# highlighting leaves excluded IL ranges unstyled inside one Prism token", () => {
  const source = 'var s = @"start\nIL_0000: nop\nend";';
  const ilStart = source.indexOf("IL_0000");
  const ilLength = "IL_0000: nop".length;
  const tokenizationSource =
    source.slice(0, ilStart)
    + " ".repeat(ilLength)
    + source.slice(ilStart + ilLength);
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: value => [{ type: "string", content: value }],
    },
    escapeHtml,
    tokenizationSource,
    [{ start: ilStart, length: ilLength }],
  );
  const html = highlighter.render(0, source.length);

  assert.match(html, /class="token string">var s = @&quot;start\n<\/span>IL_0000: nop/);
  assert.doesNotMatch(html, /class="token string">IL_0000: nop/);
  assert.equal(
    html
      .replaceAll(/<[^>]+>/g, "")
      .replaceAll("&quot;", '"'),
    source,
  );
});

test("annotated source renderer keeps syntax tokens inside structural spans", () => {
  const html = renderAnnotatedSource({
    result,
    session: createEmbeddedSession(createAnnotatedSourceViewerModel(result)),
    escapeHtml,
    highlightCSharp: source => createCSharpRangeHighlighter(
      source,
      {
        languages: { csharp: {} },
        tokenize: () => [{ type: "keyword", content: source }],
      },
      escapeHtml,
    ),
  });

  assert.match(
    html,
    /class="annotated-source-segment"[^>]*><span class="token keyword">/,
  );
});

test("C# tokenization masks IL while preserving document UTF-16 offsets", () => {
  const document: AnnotatedSourceDocument = {
    text: "cs\nil\nab",
    nodes: [
      {
        id: 0,
        kind: "CSharp",
        medium: "CSharp",
        spans: [{ start: 0, length: 2 }],
      },
      {
        id: 1,
        kind: "Instruction",
        medium: "Il",
        spans: [{ start: 3, length: 2 }],
        il_offset: 0,
      },
      {
        id: 2,
        kind: "CSharp",
        medium: "CSharp",
        spans: [{ start: 6, length: 1 }],
      },
      {
        id: 3,
        kind: "Instruction",
        medium: "Il",
        spans: [{ start: 7, length: 1 }],
        il_offset: 1,
      },
    ],
    regions: [],
    facts: [],
    targets: [],
  };
  const highlightingInput = csharpHighlightingInput(document);
  const tokenizationSource = csharpHighlightingText(document);

  assert.equal(tokenizationSource, "cs\n  \na ");
  assert.equal(tokenizationSource.length, document.text.length);
  assert.deepEqual(highlightingInput, {
    text: tokenizationSource,
    excludedRanges: [
      { start: 3, length: 2 },
      { start: 7, length: 1 },
    ],
  });
});

test("C# highlighting falls back to escaped source when token text diverges", () => {
  const source = '<T value="x">';
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: () => ["x".repeat(source.length)],
    },
    escapeHtml,
  );

  assert.equal(highlighter.render(0, source.length), "&lt;T value=&quot;x&quot;&gt;");
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
  assert.match(html, /id="annotated-source-modal-segment-\d+"/);
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
  assert.doesNotMatch(annotation, /<dt>Source offset<\/dt>/);
  assert.match(annotation, /ObjectCreationExpression · C#/);
  assert.match(annotation, /Instruction · IL/);
  assert.match(annotation, /Not projected by the current product query/);

  const coordinates = renderAnnotatedSourceModal({
    result,
    session: selectFinding(
      toggleCoordinates(modal).state,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });
  assert.match(coordinates, /<dt>Source offset<\/dt><dd>1<\/dd>/);
});

test("mixed-line hidden media keeps its layout text but removes its action", () => {
  const source: AnnotatedSourceResult = {
    document: {
      text: "ab",
      nodes: [
        { id: 0, kind: "Name", medium: "CSharp", spans: [{ start: 0, length: 1 }] },
        {
          id: 1,
          kind: "Instruction",
          medium: "Il",
          spans: [{ start: 1, length: 1 }],
          il_offset: 0,
        },
      ],
      regions: [],
      facts: [],
      targets: [],
    },
    viewerCatalog: {
      defaultFindingIds: [],
      supportedMedia: ["CSharp", "Il"],
      invocationLikeNodeKinds: [],
      invocationDestinations: [],
      findingEvidence: {
        available: false,
        unavailableReason: "NotProjected",
      },
      destinations: {
        available: false,
        unavailableReason: "NotProjected",
      },
    },
    provenance: "mixed media",
    contextLimitation: null,
  };
  const html = modalHtml(source);
  const hidden = html.match(
    /<span\s+class="annotated-source-segment medium-hidden"[^>]*>b<\/span>/,
  )?.[0] ?? "";

  assert.ok(hidden);
  assert.doesNotMatch(hidden, /role="button"|tabindex|data-annotated-source-start/);
  assert.match(html, /id="annotated-source-modal-segment-0"[\s\S]*>a<\/span>/);
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
  assert.doesNotMatch(html, /data-annotated-action="(?:copy|explore)"/);
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
  const html =
    `${renderAnnotatedSourcePageActions(true)}${embeddedHtml()}`
    + `${modalHtml()}${selectedModal}${detailedModal}`;
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

test("Annotated Source composition requires a concrete overload and validated session", () => {
  const appSource = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    appSource,
    /const annotatedPageContext =\s*activeScope === "member"\s*&& state\.memberSection === "annotated"\s*&& memberSourceHasConcreteOverload\(\);/);
  assert.match(
    appSource,
    /const annotatedWorkingSurface =\s*annotatedPageContext && state\.memberAnnotatedEmbedded !== null;/);
  assert.match(
    appSource,
    /shell-actions\$\{annotatedPageContext \? " annotated-page-actions" : ""\}/);
  assert.match(
    appSource,
    /class="working-surface-actions" role="group" aria-label="\$\{annotatedPageContext \? "Annotated Source actions" : "Source actions"\}"/);
  assert.match(
    appSource,
    /detail-scroll\$\{annotatedWorkingSurface \? " annotated-working-surface" : ""\}/);
});

test("Annotated Source destination actions use typed graph routes and exact sections", () => {
  const appSource = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    appSource,
    /case "destination-open":[\s\S]*model\.invocationDestinations\[action\.destinationIndex\][\s\S]*callGraphTargetBinding\([\s\S]*destination\.target,[\s\S]*action\.destination,[\s\S]*"annotated"\)[\s\S]*dismissAnnotatedSourceModal\(false\)[\s\S]*binding\.onSelect\(\)/,
  );
  assert.match(
    appSource,
    /const loadedSection = destination === "source" \? "source" : "overview"/,
  );
  assert.match(
    appSource,
    /const runtimeSection = destination === "member" \? "overview" : "call-graph"/,
  );
  assert.match(
    appSource,
    /candidate\.status === "resident" && destination !== "default"[\s\S]*navigateToUnprojectedGraphMember\([\s\S]*section/,
  );
  assert.match(
    appSource,
    /singleProjectedGraphMember\(projection\.type\)[\s\S]*createAppTypeSurface\(projection\.type\)[\s\S]*callGraphTargetMatchesType\(target, projectedType\)[\s\S]*graphOnly: true/,
  );
  assert.match(
    appSource,
    /if \(section === "source"\) \{\s*observeAsync\(loadSelectedMemberSource\(\), "Loading member source"\)/,
  );
  assert.match(
    appSource,
    /failureSurface === "annotated"[\s\S]*state\.annotatedDestinationError[\s\S]*renderAndFocusAnnotated\(\{ kind: "explore" \}, "embedded"\)/,
  );
  assert.match(
    appSource,
    /id="annotated-destination-error"[\s\S]*role="alert"/,
  );
  assert.match(
    appSource,
    /function openAnnotatedSourceModal\(\) \{[\s\S]*invalidateMemberDestinationWork\(state\);/,
  );
  assert.match(
    appSource,
    /case "destination-open":[\s\S]*invalidateMemberDestinationWork\(state\);[\s\S]*callGraphTargetBinding/,
  );
  assert.match(
    appSource,
    /function invalidateSourceCaches\(\) \{\s*invalidateSourceDestinationWork\(state\);/,
  );
  assert.equal(
    appSource.match(
      /state\.memberAnnotated = null;\s*state\.memberAnnotated(?:Key = "";\s*state\.memberAnnotated)?Error = "";\s*state\.annotatedDestinationError = "";/g,
    )?.length,
    7,
  );
});
