import assert from "node:assert/strict";
import test from "node:test";
import {
  bindDocViewer,
  renderDocViewer,
  renderPackageDocuments,
} from "../src/doc-viewer.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined> = {};
  private readonly listeners = new Map<string, EventListener[]>();

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, target: EventTarget = this as unknown as EventTarget) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ target } as unknown as Event);
    }
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

test("document viewer bindings open documents and close from its modal controls", () => {
  const root = new FakeRoot();
  const backdrop = root.add("#doc-viewer-backdrop", new FakeElement());
  const close = root.add("#doc-viewer-close", new FakeElement());
  const document = new FakeElement();
  document.dataset.docPath = "docs/CHANGELOG.md";
  const secondDocument = new FakeElement();
  secondDocument.dataset.docPath = "docs/README.md";
  root.addAll("[data-doc-path]", document, secondDocument);
  const inner = new FakeElement();
  const calls: string[] = [];
  bindDocViewer(
    root as unknown as ParentNode,
    {
      onClose: () => calls.push("close"),
      onOpenDocument: path => calls.push(`open:${path}`),
    });

  assert.deepEqual(calls, []);
  document.dispatch("click");
  assert.deepEqual(calls, ["open:docs/CHANGELOG.md"]);
  secondDocument.dispatch("click");
  assert.deepEqual(calls, [
    "open:docs/CHANGELOG.md",
    "open:docs/README.md",
  ]);
  backdrop.dispatch("mousedown", inner as unknown as EventTarget);
  assert.deepEqual(calls, [
    "open:docs/CHANGELOG.md",
    "open:docs/README.md",
  ]);
  backdrop.dispatch("mousedown");
  assert.deepEqual(calls, [
    "open:docs/CHANGELOG.md",
    "open:docs/README.md",
    "close",
  ]);
  close.dispatch("click");
  assert.deepEqual(calls, [
    "open:docs/CHANGELOG.md",
    "open:docs/README.md",
    "close",
    "close",
  ]);
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const doc = { name: "CHANGELOG.md", path: "docs/CHANGELOG.md" };

test("package document list renders live escaped chips and file counts", () => {
  const localizedSize = (12_345_678).toLocaleString();
  assert.notEqual(localizedSize, "12345678");
  const html = renderPackageDocuments([
    {
      kind: "readme",
      name: "<README>.md",
      path: "<docs>/README.md",
      size: 12_345_678,
    },
    {
      kind: "<skill>",
      name: "SKILL.md",
      path: "skills/widget/SKILL.md",
      size: 42,
    },
    {
      kind: "skill",
      name: "SKILL.md",
      path: "skills/known/SKILL.md",
      size: 64,
    },
    {
      kind: "constructor",
      name: "CONSTRUCTOR.md",
      path: "docs/CONSTRUCTOR.md",
      size: 1,
    },
  ], escapeHtml);

  assert.match(html, /Documentation<\/h2><span>4 files — click to read/);
  assert.match(
    html,
    /class="doc-chip doc-readme" data-doc-path="&lt;docs&gt;\/README\.md"/);
  assert.ok(html.includes(
    `title="&lt;docs&gt;/README.md · ${localizedSize} bytes"`));
  assert.match(html, /<span class="doc-name">&lt;README&gt;\.md<\/span>/);
  assert.match(html, /<span class="doc-kind">Readme<\/span>/);
  assert.match(html, /class="doc-chip doc-&lt;skill&gt;"/);
  assert.match(html, /<span class="doc-kind">&lt;skill&gt;<\/span>/);
  assert.match(html, /<span class="doc-glyph">◆<\/span>/);
  assert.match(html, /<span class="doc-kind">Skill<\/span>/);
  assert.match(
    html,
    /doc-constructor[\s\S]*?<span class="doc-glyph">▤<\/span>[\s\S]*?<span class="doc-kind">constructor<\/span>/);
  assert.doesNotMatch(html, /<docs>/);
  assert.doesNotMatch(html, /<README>/);
  assert.doesNotMatch(html, /<skill>/);
  assert.doesNotMatch(html, /function Object/);
});

test("package document list stays absent when the package ships no documents", () => {
  assert.equal(renderPackageDocuments([], escapeHtml), "");
});

test("closed viewer with no document falls back to a generic title and empty subtitle", () => {
  const html = renderDocViewer({
    doc: null,
    meta: null,
    loading: false,
    error: "",
    html: "",
    escapeHtml,
  });

  assert.match(html, /<span class="doc-viewer-title">Document<small><\/small><\/span>/);
});

test("loading state shows a loading status scoped to the document title, not the body", () => {
  const html = renderDocViewer({
    doc,
    meta: null,
    loading: true,
    error: "unused while loading",
    html: "<p>unused while loading</p>",
    escapeHtml,
  });

  assert.match(html, /doc-viewer-status">Loading CHANGELOG\.md…/);
  assert.doesNotMatch(html, /unused while loading/);
});

test("error state reports the error instead of loading or body content", () => {
  const html = renderDocViewer({
    doc,
    meta: null,
    loading: false,
    error: "network error",
    html: "<p>unused on error</p>",
    escapeHtml,
  });

  assert.match(html, /doc-viewer-status error">network error/);
  assert.doesNotMatch(html, /unused on error/);
});

test("loaded state without frontmatter renders the body with no frontmatter card", () => {
  const html = renderDocViewer({
    doc,
    meta: null,
    loading: false,
    error: "",
    html: "<p>Body content.</p>",
    escapeHtml,
  });

  assert.doesNotMatch(html, /doc-frontmatter/);
  assert.match(html, /markdown-body"><p>Body content\.<\/p><\/article>/);
});

test("loaded state with frontmatter renders the name, version, and description", () => {
  const html = renderDocViewer({
    doc,
    meta: { name: "Changelog", version: "1.2.3", descriptionHtml: "<p>What changed.</p>" },
    loading: false,
    error: "",
    html: "<p>Body content.</p>",
    escapeHtml,
  });

  assert.match(html, /doc-frontmatter/);
  assert.match(html, /<strong>Changelog<\/strong>/);
  assert.match(html, /doc-fm-version">v1\.2\.3/);
  assert.match(html, /doc-fm-desc"><p>What changed\.<\/p>/);
});

test("frontmatter without a version omits the version badge", () => {
  const html = renderDocViewer({
    doc,
    meta: { name: "Changelog", version: "", descriptionHtml: "" },
    loading: false,
    error: "",
    html: "<p>Body content.</p>",
    escapeHtml,
  });

  assert.doesNotMatch(html, /doc-fm-version/);
  assert.doesNotMatch(html, /doc-fm-desc/);
});

test("the document title and subtitle are escaped", () => {
  const html = renderDocViewer({
    doc: { name: "<script>alert(1)</script>", path: "<b>path</b>" },
    meta: null,
    loading: false,
    error: "",
    html: "",
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>/);
  assert.doesNotMatch(html, /<b>path<\/b>/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /&lt;b&gt;path&lt;\/b&gt;/);
});

test("frontmatter name is escaped but the description HTML passes through unescaped", () => {
  const html = renderDocViewer({
    doc,
    meta: { name: "<script>alert(1)</script>", version: "", descriptionHtml: "<p>trusted markdown</p>" },
    loading: false,
    error: "",
    html: "",
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>alert/);
  assert.match(html, /&lt;script&gt;alert/);
  assert.match(html, /<p>trusted markdown<\/p>/);
});
