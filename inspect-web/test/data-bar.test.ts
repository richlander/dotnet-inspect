import assert from "node:assert/strict";
import test from "node:test";
import {
  AGENT_SKILL_URL,
  CLI_TOOL_URL,
  dataBarHtml,
  fmtBytes,
} from "../src/data-bar.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

test("data bar renders the approved product information in order", () => {
  const html = dataBarHtml({
    buildIdentity: {
      version: "1.2.3",
      commit: "abcdef012345",
      commitUrl: 'https://example.test/?q="<x>&',
      builtAtUtc: "2026-08-19T14:00:00Z",
    },
    producer: {
      kind: "package",
      label: 'Corporate "<mirror>',
    },
  }, escapeHtml);

  assert.match(html, /<footer class="data-bar" aria-label="Product information">/);
  assert.match(html, /dotnet-inspect v1\.2\.3/);
  assert.match(html, /href="https:\/\/example\.test\/\?q=&quot;&lt;x&gt;&amp;"/);
  assert.match(html, /target="_blank" rel="noopener noreferrer">abcdef0<\/a>/);
  assert.match(html, /Aug 19, 2026 UTC/);
  assert.match(html, /Package source: Corporate &quot;&lt;mirror&gt;/);
  assert.match(html, new RegExp(`href="${CLI_TOOL_URL}"[^>]*>CLI tool</a>`));
  assert.match(html, new RegExp(`href="${AGENT_SKILL_URL}"[^>]*>Agent skill</a>`));
  assert.match(html, /href="\/credits">Credits<\/a>/);

  const productIndex = html.indexOf("dotnet-inspect");
  const commitIndex = html.indexOf("abcdef0");
  const dateIndex = html.indexOf("2026 UTC");
  const producerIndex = html.indexOf("Package source:");
  const cliIndex = html.indexOf("CLI tool");
  const skillIndex = html.indexOf("Agent skill");
  const creditsIndex = html.indexOf("Credits");
  assert.ok(
    productIndex < commitIndex
      && commitIndex < dateIndex
      && dateIndex < producerIndex
      && producerIndex < cliIndex
      && cliIndex < skillIndex
      && skillIndex < creditsIndex,
  );
});

test("data bar remains product information when optional provenance is absent", () => {
  const html = dataBarHtml({}, escapeHtml);

  assert.match(html, /dotnet-inspect/);
  assert.match(html, />CLI tool<\/a>/);
  assert.match(html, />Agent skill<\/a>/);
  assert.match(html, />Credits<\/a>/);
  assert.doesNotMatch(html, /built|ready|loading|download|startup|precompute|cache/i);
  assert.doesNotMatch(html, /button|aria-expanded|data-status-bar-toggle/);
});

test("non-package acquisition labels render without a package-source prefix", () => {
  const html = dataBarHtml({
    producer: { kind: "acquisition", label: "Platform" },
  }, escapeHtml);

  assert.match(html, />Platform<\/span>/);
  assert.doesNotMatch(html, /Package source:/);
});

test("shared byte formatting keeps its existing compact contract", () => {
  assert.equal(fmtBytes(0), "—");
  assert.equal(fmtBytes(1536), "1.5 KB");
  assert.equal(fmtBytes(8 * 1024 * 1024), "8.0 MB");
});
