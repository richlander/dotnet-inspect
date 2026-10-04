import assert from "node:assert/strict";
import { test } from "node:test";
import {
  renderCodeEvidenceViewer,
  renderCodeEvidenceViewerFrame,
} from "../src/code-evidence-viewer.ts";

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;")
  .replaceAll('"', "&quot;");

test("code evidence viewer composes controls, content, rail, and detail", () => {
  const html = renderCodeEvidenceViewer({
    backdropId: "evidence-backdrop",
    viewerId: "evidence-viewer",
    labelledBy: "evidence-title",
    header: '<h2 id="evidence-title">Evidence</h2>',
    controls: {
      html: "<button>Mode</button>",
      scrollAttribute: "data-owner-scroll",
      scrollKey: "controls",
    },
    body: {
      kind: "workspace",
      content: {
        html: "<pre>source</pre>",
        label: "Source evidence",
        scrollAttribute: "data-owner-scroll",
        scrollKey: "source",
      },
      rail: {
        html: "<p>inspector</p>",
        label: "Evidence inspector",
        scrollAttribute: "data-owner-scroll",
        scrollKey: "inspector",
      },
    },
    detail: '<section role="dialog">detail</section>',
    escapeHtml,
  });

  assert.match(html, /class="code-evidence-viewer-backdrop"/);
  assert.match(html, /class="code-evidence-viewer"/);
  assert.match(html, /class="code-evidence-viewer-controls"/);
  assert.match(html, /data-owner-scroll="controls"/);
  assert.match(html, /class="code-evidence-viewer-content"/);
  assert.match(html, /aria-label="Source evidence"/);
  assert.match(html, /data-owner-scroll="source"/);
  assert.match(html, /class="code-evidence-viewer-rail"/);
  assert.match(html, /aria-label="Evidence inspector"/);
  assert.match(html, /data-owner-scroll="inspector"/);
  assert.match(html, /<section role="dialog">detail<\/section>/);
});

test("code evidence viewer gives rail-free content the full workspace", () => {
  const html = renderCodeEvidenceViewer({
    backdropId: "evidence-backdrop",
    viewerId: "evidence-viewer",
    labelledBy: "evidence-title",
    header: '<h2 id="evidence-title">Evidence</h2>',
    body: {
      kind: "workspace",
      content: {
        html: "<pre>source</pre>",
        label: "Source evidence",
      },
    },
    escapeHtml,
  });

  assert.match(
    html,
    /class="code-evidence-viewer-workspace code-evidence-viewer-workspace-full"/,
  );
  assert.doesNotMatch(html, /code-evidence-viewer-rail/);
});

test("code evidence viewer frame leaves modal semantics with its host", () => {
  const html = renderCodeEvidenceViewerFrame({
    viewerId: "evidence-viewer",
    labelledBy: "evidence-title",
    header: '<h2 id="evidence-title">Evidence</h2>',
    body: {
      kind: "workspace",
      content: {
        html: "<pre>source</pre>",
        label: "Source evidence",
      },
      rail: {
        html: "<p>context</p>",
        label: "Evidence context",
      },
    },
    escapeHtml,
  });

  assert.match(html, /^<section id="evidence-viewer"/);
  assert.match(html, /class="code-evidence-viewer"/);
  assert.doesNotMatch(html, /code-evidence-viewer-backdrop/);
  assert.doesNotMatch(html, /role="dialog"|aria-modal="true"/);
});

test("code evidence viewer keeps rejection visible inside its frame", () => {
  const html = renderCodeEvidenceViewer({
    backdropId: "evidence-backdrop",
    viewerId: "evidence-viewer",
    labelledBy: "evidence-title",
    header: '<h2 id="evidence-title">Rejected</h2>',
    body: {
      kind: "failure",
      html: "<p>Invalid document</p>",
    },
    escapeHtml,
  });

  assert.match(html, /class="code-evidence-viewer-failure" role="alert"/);
  assert.match(html, /Invalid document/);
  assert.doesNotMatch(html, /code-evidence-viewer-workspace/);
});

test("code evidence viewer rejects invalid owner data attributes", () => {
  assert.throws(
    () => renderCodeEvidenceViewer({
      backdropId: "evidence-backdrop",
      viewerId: "evidence-viewer",
      labelledBy: "evidence-title",
      header: '<h2 id="evidence-title">Evidence</h2>',
      controls: {
        html: "<button>Mode</button>",
        scrollAttribute: 'data-owner-scroll" onclick="bad',
        scrollKey: "controls",
      },
      body: {
        kind: "failure",
        html: "<p>Invalid document</p>",
      },
      escapeHtml,
    }),
    /Invalid data attribute/,
  );
});
