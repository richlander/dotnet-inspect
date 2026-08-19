import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlight,
  nextSpotlightScope,
  nextSpotlightSelection,
} from "../src/spotlight.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function createHarness({ scope = "all", query = "", commandContext = null } = {}) {
  const state = {
    spotlightOpen: false,
    spotlightQuery: query,
    spotlightIndex: 0,
    spotlightScope: scope,
    spotlightFocus: "input",
    spotlightChipIndex: 0,
  };
  const spotlight = createSpotlight({
    state,
    lenses: [["api", "API"], ["metadata", "Metadata"]],
    escapeHtml,
    highlightRanges: (value) => escapeHtml(value),
    kindIcon: () => "C",
    searchResults: () => [],
    pickResult: () => {},
    executeCommand: () => {},
    commandContext: () => commandContext,
    schedulePackageFetch: () => {},
    resetPackageSearch: () => {},
    packageSearchLoading: () => false,
    packageCount: () => 1,
    activeFramework: () => "net10.0",
    render: () => {},
  });
  return { spotlight, state };
}

const packageContext = {
  id: "Example.Package",
  activeFramework: "net10.0",
  types: [{
    id: "Example.JsonSerializer",
    name: "JsonSerializer",
    namespace: "Example",
    kind: "class",
  }],
  frameworks: ["net9.0", "net10.0"],
};

test("Spotlight selection clamps without wrapping and scope cycling wraps", () => {
  assert.equal(nextSpotlightSelection(0, -1, 4), null);
  assert.equal(nextSpotlightSelection(2, 1, 4), 3);
  assert.equal(nextSpotlightSelection(3, 1, 4), 3);
  assert.equal(nextSpotlightSelection(0, 1, 0), null);
  assert.equal(nextSpotlightScope(4, 5, false), 0);
  assert.equal(nextSpotlightScope(0, 5, true), 4);
});

test("workspace Spotlight exposes commands as a dedicated scope", () => {
  const { spotlight } = createHarness({
    scope: "commands",
    commandContext: { command: "", package: packageContext },
  });

  const html = spotlight.modalHtml();
  assert.match(html, /data-sl-scope="commands"[^>]*>Commands/);
  assert.match(html, /aria-label="Run a command"/);
  assert.match(html, /placeholder="Run a command…"/);
  assert.match(html, /data-sl-index="0"/);
  assert.match(html, />type</);
  assert.doesNotMatch(html, /data-sl-pkg-load/);
});

test("home Spotlight keeps the shared typed UI without workspace commands", () => {
  const { spotlight } = createHarness();

  const html = spotlight.inlineHtml(true);
  assert.match(html, /class="home-search-content" inert/);
  assert.match(html, /id="spotlight-input"/);
  assert.match(html, /package, type, or member…/);
  assert.doesNotMatch(html, /or command/);
  assert.match(html, /data-sl-scope="runtime"[^>]*>Platform/);
  assert.doesNotMatch(html, /data-sl-scope="commands"/);
});

test("command queries and command metadata are escaped in Spotlight markup", () => {
  const { spotlight } = createHarness({
    scope: "commands",
    query: 'find <script class="x">',
    commandContext: {
      command: 'find <script class="x">',
      package: packageContext,
    },
  });

  const html = spotlight.modalHtml();
  assert.doesNotMatch(html, /<script/);
  assert.match(html, /find &lt;script class=&quot;x&quot;&gt;/);
});
