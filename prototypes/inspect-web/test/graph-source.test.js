import assert from "node:assert/strict";
import test from "node:test";
import { renderGraphSource } from "../src/graph-source.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function highlightCSharp(value) {
  return `<mark>${escapeHtml(value)}</mark>`;
}

test("loading state shows a status scoped to the title, not stale source or error", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: true,
    source: { provider: "original", provenance: "unused while loading", url: "", text: "unused" },
    error: "unused while loading",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status">Resolving source for Widget\.Render\(\)…/);
  assert.doesNotMatch(html, /unused/);
});

test("loaded PDB source renders provenance, an open-source link, and highlighted text", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: false,
    source: {
      provider: "pdb",
      provenance: "github.com/example/widget",
      url: "https://github.com/example/widget/blob/main/Widget.cs",
      text: "void Render() {}",
    },
    error: "",
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
    title: "Widget.Render()",
    loading: false,
    source: {
      provider: "decompiled",
      provenance: "decompiled from IL",
      url: null,
      pdbSourceLimitation: "<checksum mismatch>",
      text: "void Render() {}",
    },
    error: "",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<strong>Decompiled source<\/strong>/);
  assert.doesNotMatch(html, /open source/);
  assert.match(html, /PDB source unavailable: &lt;checksum mismatch&gt;/);
});

test("error state without a source shows the error message, falling back to a default", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: false,
    source: null,
    error: "",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status error">No source was returned\.</);
});

test("error state with an explicit message renders that message escaped", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: false,
    source: null,
    error: "<script>alert(1)</script>",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-status error">&lt;script&gt;alert\(1\)&lt;\/script&gt;</);
});

test("provenance and url are escaped", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: false,
    source: {
      provider: "original",
      provenance: '<b>"evil"</b>',
      url: 'https://example.com/"><script>alert(1)</script>',
      text: "void Render() {}",
    },
    error: "",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<span>&lt;b&gt;&quot;evil&quot;&lt;\/b&gt;<\/span>/);
  assert.match(html, /href="https:\/\/example\.com\/&quot;&gt;&lt;script&gt;alert\(1\)&lt;\/script&gt;"/);
});

test("the title is escaped in both the header and the loading status", () => {
  const html = renderGraphSource({
    title: "<b>Evil</b>",
    loading: true,
    source: null,
    error: "",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /graph-source-title">&lt;b&gt;Evil&lt;\/b&gt;</);
  assert.match(html, /Resolving source for &lt;b&gt;Evil&lt;\/b&gt;…/);
});

test("markup carries the modal dialog scaffolding and close button", () => {
  const html = renderGraphSource({
    title: "Widget.Render()",
    loading: false,
    source: null,
    error: "boom",
    escapeHtml,
    highlightCSharp,
  });

  assert.match(html, /<div class="graph-source-backdrop" id="graph-source-backdrop">/);
  assert.match(html, /role="dialog" aria-modal="true" aria-label="Member source"/);
  assert.match(html, /<button id="graph-source-close" type="button" aria-label="Close">esc<\/button>/);
});
