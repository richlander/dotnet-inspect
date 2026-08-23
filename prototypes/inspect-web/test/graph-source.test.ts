import assert from "node:assert/strict";
import test from "node:test";
import {
  bindGraphSource,
  renderGraphSource,
} from "../src/graph-source.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  private readonly listeners = new Map<string, EventListener[]>();

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, target: EventTarget = fakeDom.eventTarget(this)) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({ target }));
    }
  }
}

class FakeRoot {
  private readonly elements = new Map<string, FakeElement>();

  add(selector: string, element: FakeElement) {
    this.elements.set(selector, element);
    return element;
  }

  querySelector(selector: string) {
    return this.elements.get(selector) ?? null;
  }
}

test("graph source bindings close from the button or bare backdrop only", () => {
  const root = new FakeRoot();
  const backdrop = root.add("#graph-source-backdrop", new FakeElement());
  const close = root.add("#graph-source-close", new FakeElement());
  const inner = new FakeElement();
  const calls: string[] = [];
  bindGraphSource(
    fakeDom.parentNode(root),
    { onClose: () => calls.push("close") });

  assert.deepEqual(calls, []);
  backdrop.dispatch("mousedown", fakeDom.eventTarget(inner));
  assert.deepEqual(calls, []);
  backdrop.dispatch("mousedown");
  assert.deepEqual(calls, ["close"]);
  close.dispatch("click");
  assert.deepEqual(calls, ["close", "close"]);
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function highlightCSharp(value: unknown) {
  return `<mark>${escapeHtml(value)}</mark>`;
}

const request = {
  packageId: "Widget",
  version: "1.0.0",
  framework: "net10.0",
  assembly: "Widget.dll",
  type: "Widget.Renderer",
  member: "Render",
  selectorKey: "Widget.Renderer.Render()",
  metadataToken: 100663297,
} as const;

// The predecessor of this test passed a source and an error alongside `loading: true` to prove
// the renderer ignored them. The union removes those fields from the loading variant, so that
// input no longer type-checks and the stale-content case cannot be constructed. What remains to
// assert is that the status is scoped to the request's own title.
test("loading state shows a status scoped to the title", () => {
  const html = renderGraphSource({
    state: { status: "loading", request, title: "Widget.Render()" },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status">Resolving source for Widget\.Render\(\)…/);
});

test("loaded PDB source renders provenance, an open-source link, and highlighted text", () => {
  const html = renderGraphSource({
    state: {
      status: "ready",
      request,
      title: "Widget.Render()",
      source: {
        provider: "pdb",
        provenance: "github.com/example/widget",
        url: "https://github.com/example/widget/blob/main/Widget.cs",
        pdbSourceLimitation: null,
        text: "void Render() {}",
      },
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<strong>PDB Source<\/strong>/);
  assert.match(html, /<span>github\.com\/example\/widget<\/span>/);
  assert.match(html, /<a href="https:\/\/github\.com\/example\/widget\/blob\/main\/Widget\.cs" target="_blank" rel="noreferrer">open source ↗<\/a>/);
  assert.match(html, /<mark>void Render\(\) \{\}<\/mark>/);
});

test("loaded decompiled source labels the provenance as decompiled and omits the link when url is null", () => {
  const html = renderGraphSource({
    state: {
      status: "ready",
      request,
      title: "Widget.Render()",
      source: {
        provider: "decompiled",
        provenance: "decompiled from IL",
        url: null,
        pdbSourceLimitation: "<checksum mismatch>",
        text: "void Render() {}",
      },
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<strong>Decompiled source<\/strong>/);
  assert.doesNotMatch(html, /open source/);
  assert.match(html, /PDB source unavailable: &lt;checksum mismatch&gt;/);
});

test("a failure with no message falls back to the default text", () => {
  const html = renderGraphSource({
    state: { status: "failed", request, title: "Widget.Render()", error: "" },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status error">No source was returned\.</);
});

// A competing member- or type-source request retires an in-flight graph load while its modal
// stays open. The previous field layout expressed that as the absence of loading, result and
// error all at once, which rendered the same default text; this pins that the named variant
// still renders it, so naming the state did not change what a user sees.
test("a cancelled load renders the same default text as an unexplained failure", () => {
  const html = renderGraphSource({
    state: { status: "cancelled", request, title: "Widget.Render()" },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status error">No source was returned\.</);
  assert.match(html, /graph-source-title">Widget\.Render\(\)</);
});

test("error state with an explicit message renders that message escaped", () => {
  const html = renderGraphSource({
    state: {
      status: "failed",
      request,
      title: "Widget.Render()",
      error: "<script>alert(1)</script>",
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status error">&lt;script&gt;alert\(1\)&lt;\/script&gt;</);
});

test("provenance and url are escaped", () => {
  const html = renderGraphSource({
    state: {
      status: "ready",
      request,
      title: "Widget.Render()",
      source: {
        provider: "pdb",
        provenance: '<b>"evil"</b>',
        url: 'https://example.com/"><script>alert(1)</script>',
        pdbSourceLimitation: null,
        text: "void Render() {}",
      },
    },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<span>&lt;b&gt;&quot;evil&quot;&lt;\/b&gt;<\/span>/);
  assert.match(html, /href="https:\/\/example\.com\/&quot;&gt;&lt;script&gt;alert\(1\)&lt;\/script&gt;"/);
});

test("the title is escaped in both the header and the loading status", () => {
  const html = renderGraphSource({
    state: { status: "loading", request, title: "<b>Evil</b>" },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-title">&lt;b&gt;Evil&lt;\/b&gt;</);
  assert.match(html, /Resolving source for &lt;b&gt;Evil&lt;\/b&gt;…/);
});

test("markup carries the modal dialog scaffolding and close button", () => {
  const html = renderGraphSource({
    state: { status: "failed", request, title: "Widget.Render()", error: "boom" },
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<div class="graph-source-backdrop" id="graph-source-backdrop">/);
  assert.match(html, /role="dialog" aria-modal="true" aria-label="Member source"/);
  assert.match(html, /<button id="graph-source-close" type="button" aria-label="Close">esc<\/button>/);
});

