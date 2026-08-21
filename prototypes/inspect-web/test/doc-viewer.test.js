import assert from "node:assert/strict";
import test from "node:test";
import { renderDocViewer } from "../src/doc-viewer.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const doc = { name: "CHANGELOG.md", path: "docs/CHANGELOG.md" };

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
