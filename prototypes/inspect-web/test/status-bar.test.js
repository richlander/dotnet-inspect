import assert from "node:assert/strict";
import test from "node:test";
import {
  buildIdentityHtml,
  fmtBytes,
  fmtMs,
  statusBarHtml,
} from "../src/status-bar.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("status values use stable compact units", () => {
  assert.equal(fmtMs(null), "—");
  assert.equal(fmtMs(999.6), "1000 ms");
  assert.equal(fmtMs(1250), "1.25 s");
  assert.equal(fmtBytes(0), "—");
  assert.equal(fmtBytes(1536), "1.5 KB");
  assert.equal(fmtBytes(8 * 1024 * 1024), "8.0 MB");
});

test("build identity keeps provenance linked and inert", () => {
  const html = buildIdentityHtml({
    version: "1.2.3",
    commit: "abcdef012345",
    commitUrl: 'https://example.test/?q="<x>&',
    builtAtUtc: "2026-08-19T14:00:00Z",
  }, escapeHtml);

  assert.match(html, />v1\.2\.3 · <a /);
  assert.match(html, /href="https:\/\/example\.test\/\?q=&quot;&lt;x&gt;&amp;"/);
  assert.match(html, /target="_blank" rel="noopener noreferrer">abcdef0<\/a>/);
  assert.match(html, /built .* UTC/);
});

test("the data bar renders the complete workspace status at full-bar ownership", () => {
  const html = statusBarHtml({
    buildIdentity: { version: "1.2.3" },
    diagnostics: {
      assets: 4,
      downloadMs: 20,
      transfer: 1024,
      decoded: 2048,
      startupMs: 30,
      precomputeMs: 40,
      totalMs: 90,
    },
    packageCache: {
      packages: 3,
      resident: 2,
      residentBytes: 5 * 1048576,
      workspaces: 1,
    },
    assembly: 'Example"<Assembly>',
    framework: "net10.0",
  }, escapeHtml);

  assert.match(html, /class="statusbar data-bar"/);
  assert.match(html, /browser wasm ready/);
  assert.match(html, /↓ download 20 ms · 1\.0 KB → 2\.0 KB/);
  assert.match(html, /3 packages · 2 resident in cache · 1 workspace/);
  assert.match(html, /Example&quot;&lt;Assembly&gt;/);
  assert.match(html, /net10\.0/);
});

test("the same data bar component renders home readiness and compact diagnostics", () => {
  const html = statusBarHtml({
    variant: "home",
    ready: false,
    buildIdentity: { version: "1.2.3" },
    diagnostics: {
      assets: 4,
      downloadMs: 20,
      transfer: 1024,
      startupMs: 30,
      precomputeMs: 40,
      totalMs: 1250,
    },
    compactDiagnostics: true,
  }, escapeHtml);

  assert.match(html, /class="statusbar data-bar home-foot"/);
  assert.match(html, /class="home-wasm-spinner"/);
  assert.match(html, /browser wasm loading/);
  assert.match(html, /⚙ ready in 1\.25 s/);
  assert.doesNotMatch(html, /public API surface/);
});
