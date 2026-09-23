import assert from "node:assert/strict";
import test from "node:test";

import { defaultTypeExplorerIntent } from "../src/type-explorer-route.ts";
import {
  bindTypeExplorerView,
  canceledTypeExplorerView,
  renderTypeExplorerView,
  restoreTypeExplorerMemberSelection,
  type TypeExplorerInspection,
  type TypeExplorerViewActions,
} from "../src/type-explorer-view.ts";
import { fakeDom } from "./fake-dom.ts";

const identity = {
  stableSelector: "M:ConvertName(System.String)",
  canonicalSignature:
    "System.String System.Text.Json.JsonNamingPolicy::ConvertName(System.String)",
  fingerprint: "0123456789",
  typeFullName: "System.Text.Json.JsonNamingPolicy",
  memberName: "ConvertName",
};

const inspection: TypeExplorerInspection = {
  outcome: "Available",
  reason: null,
  bodyProjectionsAttempted: 1,
  failedBodyIds: [],
  document: {
    assemblyName: "System.Text.Json.dll",
    pdbSupplied: false,
    symbolSource: "decompiled",
    renderingPolicy: "whole-type",
    documentationCapability: "Available",
    contractRelationshipCapability: "Available",
    projectionFailure: null,
    projection: {
      revision: "a".repeat(64),
      text: "public abstract string ConvertName(string name);",
      diagnostics: [],
      declarations: [{
        declarationId: 7,
        identity,
        declarationToken: 100663297,
        kind: "Method",
        accessibility: "Public",
        placement: "Instance",
        origin: "NonGenerated",
        supportsSelectedBody: false,
        range: {
          start: 0,
          length: 48,
        },
      }],
    },
  },
  diagnostics: [],
};

function escapeHtml(value: unknown): string {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

test("Type Explorer renders owner-issued declarations and selection", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedMember: identity,
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer: JsonNamingPolicy/u);
  assert.match(html, /data-type-explorer-declaration="7"/u);
  assert.match(html, /aria-current="true"/u);
  assert.match(html, /type-explorer-source-declaration selected/u);
  assert.match(html, /ConvertName/u);
  assert.match(html, /id="type-explorer-outline-toggle"/u);
  assert.match(html, /aria-expanded="false"/u);
});

test("Type Explorer disables Selected body without owner-issued support", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json 11.0.0 · net11.0",
    intent: {
      ...defaultTypeExplorerIntent(),
      selectedMember: identity,
    },
    state: {
      status: "ready",
      inspection,
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(
    html,
    /value="SelectedBody" disabled/u);
});

test("Type Explorer keeps failures visible and inert", () => {
  const html = renderTypeExplorerView({
    typeDisplay: "JsonNamingPolicy",
    packageDisplay: "System.Text.Json",
    intent: defaultTypeExplorerIntent(),
    state: {
      status: "failed",
      error: "<projection failed>",
    },
    escapeHtml,
    highlightCSharp: escapeHtml,
  });

  assert.match(html, /Type Explorer failed/u);
  assert.match(html, /&lt;projection failed&gt;/u);
  assert.doesNotMatch(html, /<projection failed>/u);
  assert.match(html, /id="type-explorer-retry"/u);
});

test("Type Explorer presents current Worker cancellation as retryable failure", () => {
  assert.deepEqual(
    canceledTypeExplorerView("worker-restarted"),
    {
      status: "failed",
      error:
        "The inspection engine restarted while building Type Explorer. Retry to rebuild the Type document.",
    });
});

test("Type Explorer reveals both panes and restores the initiating member focus", () => {
  class FakeTarget {
    scrollCount = 0;
    focusCount = 0;

    scrollIntoView(options?: ScrollIntoViewOptions) {
      assert.equal(options?.block, "nearest");
      this.scrollCount++;
    }

    focus(options?: FocusOptions) {
      assert.equal(options?.preventScroll, true);
      this.focusCount++;
    }
  }

  const outline = new FakeTarget();
  const source = new FakeTarget();
  const root = fakeDom.parentNode({
    querySelector(selector: string) {
      if (selector.startsWith(".type-explorer-outline")) return outline;
      if (selector.startsWith(".type-explorer-source")) return source;
      return null;
    },
  });
  const projection = inspection.document?.projection;
  assert.ok(projection);

  assert.equal(
    restoreTypeExplorerMemberSelection(
      root,
      projection,
      identity,
      "outline"),
    true);
  assert.equal(outline.scrollCount, 1);
  assert.equal(source.scrollCount, 1);
  assert.equal(outline.focusCount, 1);
  assert.equal(source.focusCount, 0);
});

test("Type Explorer reports the pane that initiated member selection", () => {
  class FakeTarget {
    readonly dataset = { typeExplorerDeclaration: "7" };
    private readonly pane: "outline" | "source";
    private click: (() => void) | null = null;

    constructor(pane: "outline" | "source") {
      this.pane = pane;
    }

    addEventListener(name: string, listener: () => void) {
      if (name === "click") this.click = listener;
    }

    closest() {
      return this.pane === "outline" ? {} : null;
    }

    activate() {
      this.click?.();
    }
  }

  const outline = new FakeTarget("outline");
  const source = new FakeTarget("source");
  const root = fakeDom.parentNode({
    querySelector() {
      return null;
    },
    querySelectorAll(selector: string) {
      return selector === "[data-type-explorer-declaration]"
        ? [outline, source]
        : [];
    },
  });
  const selected: string[] = [];
  const actions: TypeExplorerViewActions = {
    close() {},
    retry() {},
    toggleOutline() {},
    selectBodyMode() {},
    selectPlacement() {},
    selectAccessibility() {},
    setIncludeGenerated() {},
    setIncludeDocumentation() {},
    setIncludeAttributes() {},
    selectMember(_declaration, pane) {
      selected.push(pane);
    },
  };

  bindTypeExplorerView(root, { status: "ready", inspection }, actions);
  outline.activate();
  source.activate();

  assert.deepEqual(selected, ["outline", "source"]);
});
