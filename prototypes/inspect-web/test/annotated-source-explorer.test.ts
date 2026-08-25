import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  AnnotatedSourceExplorerRenderCoordinator,
  bindAnnotatedSourceEntry,
  bindAnnotatedSourceExplorer,
  createAnnotatedSourceExplorerState,
  reduceAnnotatedSourceExplorerState,
  renderAnnotatedSourceEntry,
  renderAnnotatedSourceExplorer,
  sourceCodeLensAnnotations,
  type AnnotatedSourceResult,
} from "../src/annotated-source-explorer.ts";
import {
  validateAnnotatedSourceDocument,
  type AnnotatedSourceDocument,
} from "../src/annotated-source-view.ts";
import { sampleDocument as sampleDocumentFixture } from "../../annotated-source-viewer/src/sample-document.js";
import { fakeDom } from "./fake-dom.ts";
import {
  captureDocument,
  safetyCalleeDocument,
} from "./annotated-source-fixtures.ts";

validateAnnotatedSourceDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;
const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const explorerSource = readFileSync(
  new URL("../src/annotated-source-explorer.ts", import.meta.url),
  "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

const result: AnnotatedSourceResult = {
  document: sampleDocument,
  provenance: "test artifact",
  contextLimitation: null,
  findingEvidence: [],
};

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  readonly classes = new Set<string>();
  readonly sourceAffordances: FakeElement[] = [];
  tabIndex = -1;
  readonly classList = {
    contains: (token: string) => this.classes.has(token),
    toggle: (token: string, force?: boolean) => {
      const active = force ?? !this.classes.has(token);
      if (active) this.classes.add(token);
      else this.classes.delete(token);
      return active;
    },
  };
  readonly ownerDocument: {
    activeElement: unknown;
    getSelection(): {
      isCollapsed: boolean;
      containsNode(): boolean;
    } | null;
  };
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(
    dataset: Record<string, string | undefined> = {},
    ownerDocument: FakeElement["ownerDocument"] = {
      activeElement: null,
      getSelection: () => null,
    },
  ) {
    this.dataset = dataset;
    this.ownerDocument = ownerDocument;
  }

  addEventListener(type: string, listener: EventListener): void {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  setPointerCapture(_pointerId: number): void {}

  dispatch(type: string, event: Event = fakeDom.event()): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }

  contains(value: unknown): boolean {
    return value === this
      || this.sourceAffordances.some(element => element === value);
  }

  focus(): void {
    this.ownerDocument.activeElement = this;
  }

  querySelectorAll(selector: string): FakeElement[] {
    return selector === "[data-ase-source-affordance]"
      ? this.sourceAffordances
      : [];
  }

  scrollIntoView(): void {}
}

test("the member tab hands annotated source off to the full-screen explorer", () => {
  const html = renderAnnotatedSourceEntry({ result, escapeHtml });

  assert.match(html, /Open full-screen viewer/);
  assert.match(html, /<strong>4<\/strong>nodes/);
  assert.match(html, /<strong>3<\/strong>facts/);
  assert.match(html, /<strong>1<\/strong>unanchored/);
  assert.doesNotMatch(html, /IL_0001: newobj/);
});

test("the member tab binds copy and full-screen handoff controls", () => {
  const copy = new FakeElement();
  const open = new FakeElement();
  const elements = new Map<string, FakeElement>([
    ["#copy-annotated", copy],
    ["#open-annotated-explorer", open],
  ]);
  const calls: string[] = [];

  bindAnnotatedSourceEntry(fakeDom.parentNode({
    querySelector: (selector: string) => elements.get(selector) ?? null,
  }), {
    onCopy: () => calls.push("copy"),
    onOpen: () => calls.push("open"),
  });

  copy.dispatch("click");
  open.dispatch("click");
  assert.deepEqual(calls, ["copy", "open"]);
});

test("drag selection does not activate an addressable source segment", () => {
  let selecting = true;
  const ownerDocument = {
    activeElement: null,
    getSelection: () => ({
      isCollapsed: !selecting,
      containsNode: () => selecting,
    }),
  };
  const offset = new FakeElement({ aseOffset: "17" }, ownerDocument);
  const calls: number[] = [];
  const root = fakeDom.parentNode({
    querySelector: () => null,
    querySelectorAll: (selector: string) =>
      selector === "[data-ase-offset]" ? [offset] : [],
  });

  bindAnnotatedSourceExplorer(root, {
    onClearSelection: () => {},
    onCopy: () => {},
    onExit: () => {},
    onCaptureSelect: () => {},
    onCodeLensPreview: () => {},
    onCodeLensPreviewEnd: () => {},
    onCodeLensToggle: () => {},
    onFactSelect: () => {},
    onFindingMemberCopy: () => {},
    onFindingMemberNavigate: () => {},
    onMediumToggle: () => {},
    onNodeKindSelect: () => {},
    onRegionSelect: () => {},
    onNodeSelect: () => {},
    onOffsetSelect: value => calls.push(value),
  });

  offset.dispatch("click", fakeDom.event({ detail: 1 }));
  assert.deepEqual(calls, []);
  selecting = false;
  offset.dispatch("click", fakeDom.event({ detail: 1 }));
  offset.dispatch("click", fakeDom.event({ detail: 0 }));
  assert.deepEqual(calls, [17, 17]);
});

test("CodeLens preview state survives rerenders until the six-second animation ends", () => {
  const lens = new FakeElement({ aseCodelensNode: "7" });
  const target = new FakeElement({ aseCodelensPreviewNode: "7" });
  const toggle = new FakeElement();
  const previews: number[] = [];
  const ended: number[] = [];
  let toggles = 0;
  const root = fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === "[data-ase-codelens-toggle]" ? toggle : null,
    querySelectorAll: (selector: string) => {
      if (selector === "[data-ase-codelens-node]") return [lens];
      if (selector === "[data-ase-codelens-preview-node]") return [target];
      return [];
    },
  });

  bindAnnotatedSourceExplorer(root, {
    onClearSelection: () => {},
    onCopy: () => {},
    onExit: () => {},
    onCaptureSelect: () => {},
    onCodeLensPreview: nodeId => previews.push(nodeId),
    onCodeLensPreviewEnd: nodeId => ended.push(nodeId),
    onCodeLensToggle: () => toggles++,
    onFactSelect: () => {},
    onFindingMemberCopy: () => {},
    onFindingMemberNavigate: () => {},
    onMediumToggle: () => {},
    onNodeKindSelect: () => {},
    onRegionSelect: () => {},
    onNodeSelect: () => {},
    onOffsetSelect: () => {},
  });

  lens.dispatch("click");
  assert.deepEqual(previews, [7]);
  target.dispatch("animationend", fakeDom.event({
    animationName: "ase-codelens-preview",
  }));
  assert.deepEqual(ended, [7]);

  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  const candidate = initial.prepared.codeLensCandidates[0];
  assert.ok(candidate);
  const previewing = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "preview-codelens", nodeId: candidate.nodeId, startedAt: 1_000 },
  );
  const renderAt = (now: number) => renderAnnotatedSourceExplorer({
    result,
    state: previewing,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    now,
  });
  const firstRender = renderAt(2_000);
  const secondRender = renderAt(4_000);
  assert.match(
    firstRender,
    new RegExp(`class="previewing"[^>]*data-ase-codelens-node="${candidate.nodeId}"`),
  );
  assert.match(
    firstRender,
    new RegExp(`data-ase-codelens-preview-node="${candidate.nodeId}" style="animation-delay: -1000ms"`),
  );
  assert.match(
    secondRender,
    new RegExp(`data-ase-codelens-preview-node="${candidate.nodeId}" style="animation-delay: -3000ms"`),
  );
  assert.doesNotMatch(renderAt(7_600), /data-ase-codelens-preview-node/);
  assert.equal(
    reduceAnnotatedSourceExplorerState(
      sampleDocument,
      previewing,
      { type: "clear-codelens-preview", nodeId: candidate.nodeId },
    ).codeLensPreview,
    null,
  );
  assert.match(
    styles,
    /\.annotated-span\.codelens-preview\s*\{[^}]*box-shadow:\s*none;[^}]*animation:\s*ase-codelens-preview 6\.6s/,
  );
  assert.match(styles, /0%, 90\.909%/);
  const previewFrames = styles.slice(
    styles.indexOf("@keyframes ase-codelens-preview"),
    styles.indexOf("@media (prefers-reduced-motion: reduce)", styles.indexOf(
      "@keyframes ase-codelens-preview")),
  );
  assert.doesNotMatch(previewFrames, /box-shadow:/);

  toggle.dispatch("click");
  assert.equal(toggles, 1);
});

test("Finding evidence actions copy and navigate without changing selection", () => {
  const copy = new FakeElement({
    aseFindingMemberCopy: "System.Text.Json.JsonElement.CheckValidInstance()",
  });
  const navigate = new FakeElement({ aseFindingMemberNavigate: "2" });
  const calls: string[] = [];
  const root = fakeDom.parentNode({
    querySelector: () => null,
    querySelectorAll: (selector: string) => {
      if (selector === "[data-ase-finding-member-copy]") return [copy];
      if (selector === "[data-ase-finding-member-navigate]") return [navigate];
      return [];
    },
  });

  bindAnnotatedSourceExplorer(root, {
    onClearSelection: () => {},
    onCopy: () => {},
    onExit: () => {},
    onCaptureSelect: () => {},
    onCodeLensPreview: () => {},
    onCodeLensPreviewEnd: () => {},
    onCodeLensToggle: () => {},
    onFactSelect: () => {},
    onFindingMemberCopy: member => calls.push(`copy:${member}`),
    onFindingMemberNavigate: index => calls.push(`navigate:${index}`),
    onMediumToggle: () => {},
    onNodeKindSelect: () => {},
    onRegionSelect: () => {},
    onNodeSelect: () => {},
    onOffsetSelect: () => {},
  });

  copy.dispatch("click");
  navigate.dispatch("click");
  assert.deepEqual(calls, [
    "copy:System.Text.Json.JsonElement.CheckValidInstance()",
    "navigate:2",
  ]);
});

test("the app routes the member tab into the TypeScript explorer", () => {
  assert.match(appSource, /from "\.\/annotated-source-explorer\.ts"/);
  assert.match(appSource, /renderAnnotatedSourceEntry\(/);
  assert.match(appSource, /#open-annotated-explorer/);
  assert.match(
    appSource,
    /id: "annotated-source\.dismiss"[\s\S]*when: \(\) => Boolean\(state\.annotatedExplorer\)/,
  );
  assert.match(appSource, /renderAnnotatedSourceExplorer\(\)/);
  assert.match(appSource, /memberAnnotatedActiveFactIds/);
  assert.match(
    appSource,
    /document\.querySelector<HTMLElement>\("\.finding-peek:popover-open"\)/,
  );
  assert.match(appSource, /findingPeek\.hidePopover\(\)/);
});

test("the explorer presents canonical text beside anchored and unanchored facts", () => {
  const state = createAnnotatedSourceExplorerState(sampleDocument);
  const html = renderAnnotatedSourceExplorer({
    result,
    state,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });

  assert.match(html, /class="annotated-explorer"/);
  assert.match(html, /Example\.Run/);
  assert.deepEqual(state.media, { CSharp: true, Il: false });
  assert.doesNotMatch(html, /IL_0001: newobj instance void System\.Object::\.ctor\(\)/);
  assert.match(html, /Anchored facts/);
  assert.match(html, /alloc\.new/);
  assert.match(html, /Unanchored facts/);
  assert.match(html, /cost\.method/);
  assert.match(html, /<strong>CodeLens<\/strong>/);
  assert.match(html, /data-ase-codelens-toggle aria-pressed="true"/);
  assert.match(html, /finding available/);
  assert.deepEqual(state.activeFactIds, [0, 1]);
  assert.equal([...html.matchAll(/class="annotated-node-caret plane-finding"/g)].length, 2);
  assert.match(html, /class="annotated-caret-detail plane-finding"/);
  assert.match(html, /annotated-caret-label plane-finding"[^>]*>alloc\.new: object/);
  assert.match(html, /data-ase-finding-peek="ase-finding-peek-0-1"/);
  assert.match(html, /<dt>Finding<\/dt><dd>alloc\.new: object/);
  assert.match(html, /<dt>Location<\/dt><dd>C# · line 4 · \[\d+\.\.\d+\)/);
  assert.match(html, /<div class="finding-peek-code"><dt>Code<\/dt>/);
  assert.match(html, /class="finding-peek-evidence"/);
  assert.match(html, /data-ase-fact="2" aria-pressed="false"/);
});

test("fact, source node, node-kind, and clear actions preserve distinct selection semantics", () => {
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  const fact = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "select-fact", factId: 0 },
  );
  assert.equal(fact.selectedFactId, 0);
  assert.deepEqual(fact.activeFactIds, [0, 1]);
  assert.deepEqual(fact.selectedNodeIds, []);
  assert.equal(fact.prepared, initial.prepared);
  const factHtml = renderAnnotatedSourceExplorer({
    result,
    state: fact,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.match(factHtml, /annotated-span addressable has-fact selected semantic/);
  assert.doesNotMatch(factHtml, /selected structural/);
  assert.match(factHtml, /class="annotated-node-caret plane-finding"/);
  assert.match(factHtml, /class="annotated-caret-detail plane-finding"/);
  assert.match(factHtml, /alloc\.new:/);
  assert.match(factHtml, /<\/div><div class="annotated-node-caret/);

  const source = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    fact,
    { type: "select-offset", offset: sampleDocument.text.indexOf("new object()") },
  );
  assert.equal(source.selectedFactId, 0);
  assert.deepEqual(source.selectedNodeIds, [1]);
  const sourceHtml = renderAnnotatedSourceExplorer({
    result,
    state: source,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.match(sourceHtml, /class="annotated-node-caret plane-source plane-finding"/);
  assert.equal(
    [...sourceHtml.matchAll(/class="annotated-node-caret plane-source plane-finding"/g)].length,
    1,
  );
  assert.match(sourceHtml, /annotated-caret-label plane-source">ObjectCreationExpression · \[\d+\.\.\d+\)/);
  assert.match(sourceHtml, /annotated-caret-label plane-finding"[^>]*>alloc\.new: object/);
  assert.doesNotMatch(sourceHtml, /annotated-caret-label plane-source">#1/);
  assert.doesNotMatch(sourceHtml, /1 finding: alloc\.new/);
  assert.match(sourceHtml, /Findings at this node/);
  assert.match(sourceHtml, /data-ase-fact="0" aria-pressed="true"/);
  assert.match(sourceHtml, /selected semantic/);

  const secondSource = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    { ...source, media: { CSharp: true, Il: true } },
    { type: "select-offset", offset: sampleDocument.text.indexOf("IL_0001") },
  );
  assert.deepEqual(secondSource.selectedNodeIds, [1, 3]);
  const secondSourceHtml = renderAnnotatedSourceExplorer({
    result,
    state: secondSource,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.equal(
    [...secondSourceHtml.matchAll(/class="annotated-node-caret [^"]*plane-source/g)].length,
    2,
  );
  const secondSourceToggledOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    secondSource,
    { type: "select-offset", offset: sampleDocument.text.indexOf("IL_0001") },
  );
  assert.deepEqual(secondSourceToggledOff.selectedNodeIds, [1]);

  const sourceToggledOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    source,
    { type: "select-offset", offset: sampleDocument.text.indexOf("new object()") },
  );
  assert.deepEqual(sourceToggledOff.selectedNodeIds, []);
  assert.equal(sourceToggledOff.selectedFactId, 0);
  assert.match(
    styles,
    /\.annotated-span\.selected\.semantic\s*\{[^}]*linear-gradient\(180deg,[^}]*var\(--accent\) 16%[^}]*text-shadow:[^}]*var\(--accent\) 65%/,
  );
  assert.match(
    styles,
    /:root\[data-theme="light"\] \.annotated-span\.selected\.semantic\s*\{[^}]*var\(--accent\) 12%[^}]*text-shadow:[^}]*var\(--accent\) 68%/,
  );

  const kind = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    source,
    { type: "select-kind", kind: "Instruction" },
  );
  assert.equal(kind.selectedFactId, null);
  assert.equal(kind.selectedKind, "Instruction");
  assert.deepEqual(kind.selectedNodeIds, [2, 3]);
  const kindToggledOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    kind,
    { type: "select-kind", kind: "Instruction" },
  );
  assert.equal(kindToggledOff.selectedKind, "");
  assert.deepEqual(kindToggledOff.selectedNodeIds, []);

  const region = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    kind,
    { type: "select-region", role: "Body" },
  );
  assert.equal(region.selectedKind, "");
  assert.equal(region.selectedRegionRole, "Body");
  assert.deepEqual(region.selectedNodeIds, []);

  const cleared = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    region,
    { type: "clear-selection" },
  );
  assert.deepEqual(cleared.selectedNodeIds, []);
  assert.deepEqual(cleared.activeFactIds, []);
  assert.equal(cleared.selectedKind, "");
  assert.equal(cleared.selectedRegionRole, "");
  const clearedHtml = renderAnnotatedSourceExplorer({
    result,
    state: cleared,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.doesNotMatch(clearedHtml, /class="annotated-node-caret/);
  assert.doesNotMatch(clearedHtml, /id="ase-clear"/);
});

test("Finding provenance peeks show the discovered throw and member actions", () => {
  const throwText =
    "throw new InvalidOperationException(\"An element of type 'Undefined' cannot be converted.\");";
  const throwDocument: AnnotatedSourceDocument = {
    text: throwText,
    nodes: [{
      id: 0,
      kind: "ThrowStatement",
      medium: "CSharp",
      spans: [{ start: 0, length: throwText.length }],
    }],
    regions: [],
    facts: [],
    targets: [],
  };
  validateAnnotatedSourceDocument(throwDocument);
  const fact = sampleDocument.facts[0];
  const evidenceResult: AnnotatedSourceResult = {
    ...result,
    findingEvidence: [{
      descriptor: fact.descriptor,
      sourceOffset: fact.source_offset,
      member: "System.Text.Json.JsonElement.CheckValidInstance()",
      target: {
        id: "finding-0",
        assembly: "System.Text.Json",
        assemblyVersion: "10.0.0.0",
        assemblyCulture: null,
        assemblyPublicKeyToken: null,
        typeFullName: "System.Text.Json.JsonElement",
        typeMetadataId: "System.Text.Json.JsonElement",
        typeDefinitionId: "System.Text.Json.JsonElement",
        memberName: "CheckValidInstance",
        parameterTypes: [],
        returnType: "System.Void",
        genericArity: 0,
        metadataToken: 0x06000001,
        selectorKey: "method:CheckValidInstance",
        kind: "method",
        platformPack: null,
      },
      coordinates: [],
      document: throwDocument,
      nodeIds: [0],
      unavailableReason: null,
    }],
  };

  const html = renderAnnotatedSourceExplorer({
    result: evidenceResult,
    state: createAnnotatedSourceExplorerState(sampleDocument),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });

  assert.match(
    html,
    /<dt>Member<\/dt><dd><code>System\.Text\.Json\.JsonElement\.CheckValidInstance\(\)<\/code>/,
  );
  assert.match(
    html,
    /data-ase-finding-member-copy="System\.Text\.Json\.JsonElement\.CheckValidInstance\(\)">copy member/,
  );
  assert.match(
    html,
    /data-ase-finding-member-navigate="0">navigate/,
  );
  assert.match(html, /throw new InvalidOperationException/);
  assert.doesNotMatch(
    html,
    /finding-peek-line-text">[^<]*return new object/,
  );

  const unavailableHtml = renderAnnotatedSourceExplorer({
    result: {
      ...evidenceResult,
      findingEvidence: [{
        ...evidenceResult.findingEvidence[0],
        document: null,
        nodeIds: [],
        unavailableReason: "The callee source document was unavailable.",
      }],
    },
    state: createAnnotatedSourceExplorerState(sampleDocument),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.match(
    unavailableHtml,
    /The callee source document was unavailable\./,
  );
  assert.match(unavailableHtml, /<dt>Location<\/dt><dd>Callee evidence unavailable/);
  assert.doesNotMatch(
    unavailableHtml,
    /finding-peek-line-text">[^<]*return new object/,
  );
});

test("Safety Finding provenance peeks render the callee stackalloc evidence", () => {
  validateAnnotatedSourceDocument(safetyCalleeDocument);
  const safetyDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: sampleDocument.facts.map((fact, index) =>
      index === 0 ? { ...fact, descriptor: "safety.callee" } : fact),
  };
  const safetyResult: AnnotatedSourceResult = {
    ...result,
    document: safetyDocument,
    findingEvidence: [{
      descriptor: "safety.callee",
      sourceOffset: safetyDocument.facts[0].source_offset,
      member: "Example.UnsafeCallee()",
      target: {
        id: "finding-safety",
        assembly: "Example",
        assemblyVersion: "1.0.0.0",
        assemblyCulture: null,
        assemblyPublicKeyToken: null,
        typeFullName: "Example",
        typeMetadataId: "Example",
        typeDefinitionId: "Example",
        memberName: "UnsafeCallee",
        parameterTypes: [],
        returnType: "System.Void",
        genericArity: 0,
        metadataToken: 0x06000002,
        selectorKey: "method:UnsafeCallee",
        kind: "method",
        platformPack: null,
      },
      coordinates: [{
        member: {
          id: "finding-safety-evidence-0",
          assembly: "Example",
          assemblyVersion: "1.0.0.0",
          assemblyCulture: null,
          assemblyPublicKeyToken: null,
          typeFullName: "Example",
          typeMetadataId: "Example",
          typeDefinitionId: "Example",
          memberName: "UnsafeCallee",
          parameterTypes: [],
          returnType: "System.Void",
          genericArity: 0,
          metadataToken: 0x06000002,
          selectorKey: "method:UnsafeCallee",
          kind: "method",
          platformPack: null,
        },
        ilOffset: 4,
        kind: "localloc",
      }],
      document: safetyCalleeDocument,
      nodeIds: [0],
      unavailableReason: null,
    }],
  };

  const html = renderAnnotatedSourceExplorer({
    result: safetyResult,
    state: createAnnotatedSourceExplorerState(safetyDocument),
    title: "Example.InvokeUnsafeCallee",
    subtitle: "void InvokeUnsafeCallee()",
    escapeHtml,
  });

  assert.match(html, /Example\.UnsafeCallee\(\)/);
  assert.match(html, /data-ase-finding-member-copy="Example\.UnsafeCallee\(\)">copy member/);
  assert.match(html, /data-ase-finding-member-navigate="0">navigate/);
  assert.match(html, /stackalloc byte\[16\]/);
  assert.doesNotMatch(html, /finding-peek-line-text">[^<]*return new object/);
});

test("remote aggregate Findings never present caller code as callee evidence", () => {
  const aggregateDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: sampleDocument.facts.map(fact =>
      fact.id === 0
        ? {
            ...fact,
            descriptor: "cost.callee",
            detail: "callee has high leverage",
          }
        : fact),
  };
  const html = renderAnnotatedSourceExplorer({
    result: {
      ...result,
      document: aggregateDocument,
      findingEvidence: [],
    },
    state: createAnnotatedSourceExplorerState(
      aggregateDocument,
      { activeFactIds: [0] },
    ),
    title: "Example.InvokeExpensiveCallee",
    subtitle: "void InvokeExpensiveCallee()",
    escapeHtml,
  });

  assert.match(html, /annotated-caret-label plane-finding">cost\.callee:/);
  assert.doesNotMatch(html, /data-ase-finding-peek/);
  assert.doesNotMatch(html, /<strong>Finding evidence<\/strong>/);
  assert.doesNotMatch(html, /finding-peek-evidence/);
});

test("media actions refuse an empty-looking document", () => {
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  assert.deepEqual(initial.media, { CSharp: true, Il: false });
  const both = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "toggle-medium", medium: "Il" },
  );
  const csharpOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    both,
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

test("media state cannot hide every line in a single-medium document", () => {
  const csharpOnlyDocument: AnnotatedSourceDocument = {
    text: "return;",
    nodes: [{
      id: 0,
      kind: "ReturnStatement",
      medium: "CSharp",
      spans: [{ start: 0, length: 7 }],
    }],
    regions: [],
    facts: [],
    targets: [],
  };
  const normalized = createAnnotatedSourceExplorerState(
    csharpOnlyDocument,
    { media: { CSharp: false, Il: true } },
  );
  assert.deepEqual(normalized.media, { CSharp: true, Il: false });

  const both = reduceAnnotatedSourceExplorerState(
    csharpOnlyDocument,
    normalized,
    { type: "toggle-medium", medium: "Il" },
  );
  assert.deepEqual(both.media, { CSharp: true, Il: true });
  const refused = reduceAnnotatedSourceExplorerState(
    csharpOnlyDocument,
    both,
    { type: "toggle-medium", medium: "CSharp" },
  );
  assert.equal(refused, both);
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

test("C# syntax tokens stay inside addressable spans while findings remain ambient", () => {
  const html = renderAnnotatedSourceExplorer({
    result,
    state: createAnnotatedSourceExplorerState(sampleDocument),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    tokenizeCSharp: value => {
      const keyword = "return";
      const start = value.indexOf(keyword);
      return start < 0
        ? [{ text: value, classes: [] }]
        : [
            { text: value.slice(0, start), classes: [] },
            { text: keyword, classes: ["keyword"] },
            { text: value.slice(start + keyword.length), classes: [] },
          ];
    },
  });

  assert.match(html, /class="annotated-span addressable has-fact"/);
  assert.match(html, /finding available/);
  assert.match(html, /aria-label="[^"]*; 1 finding available:/);
  assert.doesNotMatch(html, /class="visually-hidden"/);
  assert.match(html, /<button[^>]*>\s*<span class="token keyword">return<\/span>/);
  assert.doesNotMatch(html, /<span class="token keyword">[^<]*<button/);
  assert.match(styles, /\.annotated-span\.has-fact\s*\{[^}]*box-shadow:/);
});

test("product labels render as toggleable structural CodeLens annotations", () => {
  const initial = createAnnotatedSourceExplorerState(sampleDocument);
  const html = renderAnnotatedSourceExplorer({
    result,
    state: initial,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    nodeKinds: [{ id: "ForStatement", label: "For loop" }],
  });
  assert.match(html, /class="annotated-codelens-row"/);
  assert.match(html, /data-ase-codelens-node="0"/);
  assert.match(
    html,
    /annotated-line-text"><button type="button"[^>]*data-ase-codelens-node="0"/,
  );
  assert.match(html, />For loop<\/button>/);
  assert.doesNotMatch(styles, /CodeLens ·/);

  const indentedDocument: AnnotatedSourceDocument = {
    text: "    if (true)\n    {\n    }",
    nodes: [{
      id: 0,
      kind: "IfStatement",
      medium: "CSharp",
      spans: [{ start: 0, length: 25 }],
    }],
    regions: [],
    facts: [],
    targets: [],
  };
  validateAnnotatedSourceDocument(indentedDocument);
  const indentedHtml = renderAnnotatedSourceExplorer({
    result: { ...result, document: indentedDocument },
    state: createAnnotatedSourceExplorerState(indentedDocument),
    title: "Example.Indented",
    subtitle: "void Indented()",
    escapeHtml,
    nodeKinds: [{ id: "IfStatement", label: "If statement" }],
  });
  assert.match(
    indentedHtml,
    /annotated-line-text">    <button type="button"[^>]*data-ase-codelens-node="0"/,
  );
  assert.doesNotMatch(html, /data-ase-kind=/);

  const selectedStructure = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "select-node", nodeId: 0 },
  );
  const selectedStructureHtml = renderAnnotatedSourceExplorer({
    result,
    state: selectedStructure,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    nodeKinds: [{ id: "ForStatement", label: "For loop" }],
  });
  assert.doesNotMatch(selectedStructureHtml, /class="annotated-node-caret plane-source"/);
  assert.doesNotMatch(selectedStructureHtml, /selected structural/);

  const disabled = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    initial,
    { type: "toggle-codelens" },
  );
  const disabledHtml = renderAnnotatedSourceExplorer({
    result,
    state: disabled,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    nodeKinds: [{ id: "ForStatement", label: "For loop" }],
  });
  assert.match(disabledHtml, /data-ase-codelens-toggle aria-pressed="false">off/);
  assert.doesNotMatch(disabledHtml, /class="annotated-codelens-row"/);
  assert.match(appSource, /memberAnnotatedCodeLens/);
  assert.match(appSource, /onCodeLensToggle:/);
});

test("CodeLens reuses prepared candidates and skips their construction while off", () => {
  const first = createAnnotatedSourceExplorerState(sampleDocument);
  const interacted = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    first,
    { type: "select-fact", factId: 0 },
  );
  const reopened = createAnnotatedSourceExplorerState(sampleDocument);
  const labels = new Map([["ForStatement", "For loop"]]);

  assert.equal(interacted.prepared, first.prepared);
  assert.equal(reopened.prepared, first.prepared);
  assert.deepEqual(
    [...sourceCodeLensAnnotations(first.prepared.codeLensCandidates, labels)],
    [[0, [{ label: "For loop", nodeId: 0, prefix: "" }]]],
  );
  assert.doesNotMatch(sourceCodeLensAnnotations.toString(), /\.filter\(/);
  assert.match(
    explorerSource,
    /state\.codeLens\s*\?\s*sourceCodeLensAnnotations\(state\.prepared\.codeLensCandidates, kindLabels\)/,
  );

  const offState = {
    ...first,
    codeLens: false,
    prepared: new Proxy(first.prepared, {
      get(target, property, receiver) {
        if (property === "codeLensCandidates") {
          throw new Error("CodeLens candidates must not be read while CodeLens is off.");
        }
        return Reflect.get(target, property, receiver) as unknown;
      },
    }),
  };
  const offHtml = renderAnnotatedSourceExplorer({
    result,
    state: offState,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    nodeKinds: [{ id: "ForStatement", label: "For loop" }],
  });

  assert.doesNotMatch(offHtml, /class="annotated-codelens-row"/);
});

test("captured variables remain discoverable and select their exact uses", () => {
  const captureResult = { ...result, document: captureDocument };
  const nodeKinds = [
    { id: "LambdaExpression", label: "Lambda expression" },
    { id: "NameExpression", label: "Name expression" },
    { id: "ReturnStatement", label: "Return statement" },
  ];
  const initial = createAnnotatedSourceExplorerState(captureDocument);
  const ambientHtml = renderAnnotatedSourceExplorer({
    result: captureResult,
    state: initial,
    title: "Example.Capture",
    subtitle: "Func<int, int> Capture(int first, int second)",
    escapeHtml,
    nodeKinds,
  });

  assert.match(ambientHtml, /Captured variables/);
  assert.match(ambientHtml, /data-ase-capture="0" aria-pressed="false"/);
  assert.match(ambientHtml, /annotated-span addressable has-capture/);
  assert.match(ambientHtml, /captured variable: first/);

  const selected = reduceAnnotatedSourceExplorerState(
    captureDocument,
    initial,
    { type: "select-capture", captureIndex: 0 },
  );
  assert.equal(selected.selectedCaptureIndex, 0);
  const selectedHtml = renderAnnotatedSourceExplorer({
    result: captureResult,
    state: selected,
    title: "Example.Capture",
    subtitle: "Func<int, int> Capture(int first, int second)",
    escapeHtml,
    nodeKinds,
  });

  assert.match(selectedHtml, /data-ase-capture="0" aria-pressed="true"/);
  assert.match(selectedHtml, /annotated-span addressable capture-scope/);
  assert.match(selectedHtml, /has-capture capture-scope selected capture/);
  assert.match(selectedHtml, /first · 1 captured use/);
  assert.match(selectedHtml, /Captured by/);
  assert.match(selectedHtml, /Lambda expression #0/);
});

test("shared capture names identify their distinct nested-function scopes", () => {
  const sharedDocument: AnnotatedSourceDocument = {
    text: "x => n\ny => n",
    nodes: [
      { id: 0, kind: "LambdaExpression", medium: "CSharp", spans: [{ start: 0, length: 6 }] },
      { id: 1, kind: "NameExpression", medium: "CSharp", spans: [{ start: 5, length: 1 }] },
      { id: 2, kind: "LambdaExpression", medium: "CSharp", spans: [{ start: 7, length: 6 }] },
      { id: 3, kind: "NameExpression", medium: "CSharp", spans: [{ start: 12, length: 1 }] },
    ],
    regions: [],
    facts: [],
    targets: [],
    captures: [
      { parent_node_id: 0, display_name: "n", use_node_ids: [1] },
      { parent_node_id: 2, display_name: "n", use_node_ids: [3] },
    ],
  };
  validateAnnotatedSourceDocument(sharedDocument);

  const html = renderAnnotatedSourceExplorer({
    result: { ...result, document: sharedDocument },
    state: createAnnotatedSourceExplorerState(sharedDocument),
    title: "Example.Shared",
    subtitle: "void Shared()",
    escapeHtml,
    nodeKinds: [
      { id: "LambdaExpression", label: "Lambda expression" },
      { id: "NameExpression", label: "Name expression" },
    ],
  });

  assert.match(html, /Lambda expression #0/);
  assert.match(html, /Lambda expression #2/);
});

test("empty source lines add no selectable characters", () => {
  const blankDocument: AnnotatedSourceDocument = {
    text: "a\n\nb",
    nodes: [],
    regions: [],
    facts: [],
    targets: [],
  };
  validateAnnotatedSourceDocument(blankDocument);

  const html = renderAnnotatedSourceExplorer({
    result: { ...result, document: blankDocument },
    state: createAnnotatedSourceExplorerState(blankDocument),
    title: "Example.Blank",
    subtitle: "void Blank()",
    escapeHtml,
  });

  assert.doesNotMatch(html, /&nbsp;/);
  assert.match(html, /annotated-line-text"><\/span>/);
});

test("merged source labels only C# and IL at medium boundaries", () => {
  const groupedDocument: AnnotatedSourceDocument = {
    text: "a\nb\nc\nx\ny",
    nodes: [
      { id: 0, kind: "Block", medium: "CSharp", spans: [{ start: 0, length: 5 }] },
      {
        id: 1,
        kind: "Instruction",
        medium: "Il",
        spans: [{ start: 6, length: 3 }],
        il_offset: 0,
      },
    ],
    regions: [],
    facts: [],
    targets: [],
  };
  validateAnnotatedSourceDocument(groupedDocument);

  const html = renderAnnotatedSourceExplorer({
    result: { ...result, document: groupedDocument },
    state: createAnnotatedSourceExplorerState(groupedDocument, {
      media: { CSharp: true, Il: true },
    }),
    title: "Example.Grouped",
    subtitle: "void Grouped()",
    escapeHtml,
  });

  assert.equal(html.match(/annotated-line-medium">C#</g)?.length, 1);
  assert.equal(html.match(/annotated-line-medium">IL</g)?.length, 1);
  assert.doesNotMatch(html, /C#\/IL/);
  assert.match(html, /annotated-line-medium"><\/span>/);

  for (const medium of ["CSharp", "Il"] as const) {
    const singleMediumState = createAnnotatedSourceExplorerState(groupedDocument, {
      media: {
        CSharp: medium === "CSharp",
        Il: medium === "Il",
      },
    });
    const singleMediumHtml = renderAnnotatedSourceExplorer({
      result: { ...result, document: groupedDocument },
      state: singleMediumState,
      title: "Example.Grouped",
      subtitle: "void Grouped()",
      escapeHtml,
    });

    assert.doesNotMatch(singleMediumHtml, /annotated-line-medium/);
    assert.match(singleMediumHtml, new RegExp(`medium-${medium.toLowerCase()}`));
  }
});

test("source carets inherit exact source glyph metrics", () => {
  assert.match(
    styles,
    /\.annotated-caret-detail\s*\{[^}]*min-height:\s*0;[^}]*font:\s*inherit;[^}]*line-height:\s*1;/,
  );
  assert.match(
    styles,
    /\.annotated-caret-label-stack\s*\{[^}]*gap:\s*2px;[^}]*margin-left:\s*-5px;/,
  );
  assert.match(
    styles,
    /\.annotated-caret-label\s*\{[^}]*max-width:\s*76ch;[^}]*font:\s*10px\/1\.25 var\(--mono\);[^}]*white-space:\s*normal;/,
  );
  assert.match(
    styles,
    /\.annotated-caret-label\.plane-finding\s*\{[^}]*margin:\s*1px 0 2px;[^}]*padding:\s*2px 6px;/,
  );
  assert.match(
    styles,
    /\.finding-peek\s*\{[^}]*position:\s*fixed;[^}]*width:\s*min\(620px,[^}]*transform:\s*translate\(-50%, -50%\);/,
  );
});

test("C# syntax tokenization is reused across interaction renders", () => {
  let calls = 0;
  const tokenizer = (value: string) => {
    calls++;
    return [{ text: value, classes: [] }];
  };
  const options = {
    result,
    state: createAnnotatedSourceExplorerState(sampleDocument),
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
    tokenizeCSharp: tokenizer,
  };

  renderAnnotatedSourceExplorer(options);
  const firstRenderCalls = calls;
  renderAnnotatedSourceExplorer(options);

  assert.ok(firstRenderCalls > 0);
  assert.equal(calls, firstRenderCalls);
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
  assert.match(
    html,
    /<button type="button" tabindex="-1" class="annotated-span addressable[^"]*" data-ase-source-affordance/,
  );
  assert.match(
    html,
    /<button type="button" tabindex="-1" data-ase-source-affordance data-ase-codelens-node=/,
  );
  assert.match(
    html,
    /<button type="button" tabindex="-1" class="annotated-caret-label plane-finding" data-ase-source-affordance data-ase-finding-peek=/,
  );
  assert.match(
    styles,
    /\.annotated-span\.addressable\s*\{[^}]*user-select:\s*text;/,
  );
  assert.match(explorerSource, /case "ArrowRight":/);
  assert.match(
    explorerSource,
    /querySelectorAll<HTMLElement>\("\[data-ase-source-affordance\]"\)/,
  );
  assert.match(
    explorerSource,
    /if \(event\.altKey \|\| event\.ctrlKey \|\| event\.metaKey \|\| event\.shiftKey\) return;/,
  );
  assert.match(explorerSource, /spans\[nextIndex\]\.focus\(\{ preventScroll: true \}\)/);
});

test("roving focus removes the source container from reverse tab order", () => {
  const ownerDocument = {
    activeElement: null as unknown,
    getSelection: () => null,
  };
  const code = new FakeElement({}, ownerDocument);
  const first = new FakeElement({}, ownerDocument);
  const second = new FakeElement({}, ownerDocument);
  const outside = new FakeElement({}, ownerDocument);
  code.tabIndex = 0;
  code.sourceAffordances.push(first, second);
  const root = fakeDom.parentNode({
    querySelector: (selector: string) =>
      selector === ".ase-code-scroll" ? code : null,
    querySelectorAll: () => [],
  });
  const htmlElementDescriptor =
    Object.getOwnPropertyDescriptor(globalThis, "HTMLElement");
  const nodeDescriptor =
    Object.getOwnPropertyDescriptor(globalThis, "Node");
  Object.defineProperty(globalThis, "HTMLElement", {
    configurable: true,
    value: FakeElement,
  });
  Object.defineProperty(globalThis, "Node", {
    configurable: true,
    value: FakeElement,
  });
  try {
    bindAnnotatedSourceExplorer(root, {
      onClearSelection: () => {},
      onCopy: () => {},
      onExit: () => {},
      onCaptureSelect: () => {},
      onCodeLensPreview: () => {},
      onCodeLensPreviewEnd: () => {},
      onCodeLensToggle: () => {},
      onFactSelect: () => {},
      onFindingMemberCopy: () => {},
      onFindingMemberNavigate: () => {},
      onMediumToggle: () => {},
      onNodeKindSelect: () => {},
      onRegionSelect: () => {},
      onNodeSelect: () => {},
      onOffsetSelect: () => {},
    });
    ownerDocument.activeElement = code;
    let prevented = false;
    code.dispatch("keydown", fakeDom.keyboardEvent({
      key: "ArrowDown",
      altKey: false,
      ctrlKey: false,
      metaKey: false,
      shiftKey: false,
      preventDefault: () => { prevented = true; },
    }));
    assert.equal(prevented, true);
    assert.equal(ownerDocument.activeElement, first);
    assert.equal(code.tabIndex, -1);

    code.dispatch("focusout", fakeDom.event({ relatedTarget: outside }));
    assert.equal(code.tabIndex, 0);
    code.dispatch("focusin", fakeDom.event({ target: second }));
    assert.equal(code.tabIndex, -1);
  } finally {
    if (htmlElementDescriptor) {
      Object.defineProperty(globalThis, "HTMLElement", htmlElementDescriptor);
    } else {
      Reflect.deleteProperty(globalThis, "HTMLElement");
    }
    if (nodeDescriptor) {
      Object.defineProperty(globalThis, "Node", nodeDescriptor);
    } else {
      Reflect.deleteProperty(globalThis, "Node");
    }
  }
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
  assert.match(appSource, /"aseKind"/);
  assert.match(appSource, /"aseRegion"/);
  assert.match(appSource, /"aseCapture"/);
  assert.doesNotMatch(appSource, /"ase-node-kind"/);
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
  assert.match(appSource, /\.annotated-span\.selected, \.annotated-region\.selected/);
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
  assert.match(html, /data-ase-fact="1" aria-pressed="true"/);

  const toggledOff = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    selected,
    { type: "select-fact", factId: 0 },
  );
  const toggledOffHtml = renderAnnotatedSourceExplorer({
    result,
    state: toggledOff,
    title: "Example.Run",
    subtitle: "public object Run()",
    escapeHtml,
  });
  assert.match(toggledOffHtml, /data-ase-fact="0" aria-pressed="false"/);
  assert.match(toggledOffHtml, /data-ase-fact="1" aria-pressed="true"/);
});

test("reopening an unchanged document reuses its prepared projection", () => {
  const first = createAnnotatedSourceExplorerState(sampleDocument);
  const selectedFinding = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    first,
    { type: "select-fact", factId: 0 },
  );
  const withOneFindingDeactivated = reduceAnnotatedSourceExplorerState(
    sampleDocument,
    selectedFinding,
    { type: "select-fact", factId: 0 },
  );
  const reopened = createAnnotatedSourceExplorerState(sampleDocument, {
    activeFactIds: withOneFindingDeactivated.activeFactIds,
  });

  assert.equal(reopened.prepared, first.prepared);
  assert.deepEqual(reopened.activeFactIds, [1]);
});

test("full-bleed feedback and narrow layouts stay reachable", () => {
  assert.match(styles, /\.toast\s*\{[^}]*z-index:\s*120/s);
  assert.match(
    styles,
    /\.ase-workspace\s*\{[^}]*grid-template-rows:\s*minmax\(0, 3fr\) minmax\(0, 2fr\)/s,
  );
  assert.doesNotMatch(styles, /grid-template-rows:[^;]*(?:45vh|240px)/);
});
