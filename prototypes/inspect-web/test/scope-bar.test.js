import assert from "node:assert/strict";
import test from "node:test";
import { renderScopeBar } from "../src/scope-bar.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const typeLenses = [
  ["api", "API"],
  ["metadata", "Metadata"],
  ["source", "Source"],
];

test("package scope marks only the package segment and the active package lens", () => {
  const html = renderScopeBar({
    scope: "package",
    strip: [["overview", "Overview"], ["dependencies", "Dependencies"]],
    activeStripId: "dependencies",
    stripAttribute: "data-package-lens",
    escapeHtml,
  });

  assert.match(html, /data-scope="package" role="tab" aria-selected="true"/);
  assert.match(html, /data-scope="type" role="tab" aria-selected="false"/);
  assert.doesNotMatch(html, /data-scope="member"/);
  assert.match(html, /class="lens active" data-package-lens="dependencies"/);
  assert.doesNotMatch(html, /class="lens active" data-package-lens="overview"/);
});

test("type scope marks the type segment and renders the fixed type lenses", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.match(html, /data-scope="type" role="tab" aria-selected="true"/);
  assert.doesNotMatch(html, /data-scope="member"/);
  assert.match(html, /class="lens active" data-lens="api"/);
  assert.match(html, /data-lens="metadata"/);
  assert.match(html, /data-lens="source"/);
});

test("member scope adds a member segment alongside package and type", () => {
  const html = renderScopeBar({
    scope: "member",
    strip: [["overview", "Overview"], ["facts", "Facts"]],
    activeStripId: "facts",
    stripAttribute: "data-member-section",
    escapeHtml,
  });

  assert.match(html, /data-scope="member" role="tab" aria-selected="true"/);
  assert.match(html, /class="lens active" data-member-section="facts"/);
});

test("lens button labels carry their keyboard shortcut index", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: typeLenses,
    activeStripId: "api",
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.match(html, /API<kbd>1<\/kbd>/);
  assert.match(html, /Metadata<kbd>2<\/kbd>/);
  assert.match(html, /Source<kbd>3<\/kbd>/);
});

test("lens button labels are escaped", () => {
  const html = renderScopeBar({
    scope: "type",
    strip: [["x", '<script>alert(1)</script>']],
    activeStripId: null,
    stripAttribute: "data-lens",
    escapeHtml,
  });

  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /&lt;script&gt;/);
});

test("no strip entry is marked active when nothing matches activeStripId", () => {
  const html = renderScopeBar({
    scope: "package",
    strip: [["overview", "Overview"]],
    activeStripId: null,
    stripAttribute: "data-package-lens",
    escapeHtml,
  });

  assert.doesNotMatch(html, /class="lens active"/);
});
