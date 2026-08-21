import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlight,
  nextSpotlightScope,
  nextSpotlightSelection,
  visibleSpotlightPackageHits,
} from "../src/spotlight.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function createHarness({
  scope = "all",
  query = "",
  commandContext = null,
  focusAfterDismiss = () => {},
  searchResults = () => [],
  lenses = () => [["api", "API"], ["metadata", "Metadata"]],
} = {}) {
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
    lenses,
    escapeHtml,
    highlightRanges: (value) => escapeHtml(value),
    kindIcon: () => "C",
    searchResults,
    pickResult: () => {},
    executeCommand: () => {},
    commandContext: () => commandContext,
    schedulePackageFetch: () => {},
    resetPackageSearch: () => {},
    packageSearchLoading: () => false,
    packageCount: () => 1,
    activeFramework: () => "net10.0",
    render: () => {},
    focusAfterDismiss,
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

test("NuGet hits are visible only for their resolved query and survive a query round trip", () => {
  const hits = [{ id: "Alpha", version: "1.0.0" }];

  assert.deepEqual(visibleSpotlightPackageHits("alpha", "alpha", hits), hits);
  assert.deepEqual(visibleSpotlightPackageHits("alphabet", "alpha", hits), []);
  assert.deepEqual(visibleSpotlightPackageHits("alpha", "alpha", hits), hits);
});

test("Spotlight keeps the selected result when async rows are inserted before it", () => {
  const pkg = { id: "Example.Package", version: "1.0.0" };
  const first = {
    kind: "type",
    pkg,
    type: { id: "Example.First", name: "First", kind: "class" },
    ranges: [],
  };
  const selected = {
    kind: "type",
    pkg,
    type: { id: "Example.Selected", name: "Selected", kind: "class" },
    ranges: [],
  };
  let results = [first, selected];
  const { spotlight, state } = createHarness({
    query: "Example",
    searchResults: () => results,
  });
  state.spotlightIndex = 1;
  spotlight.modalHtml();

  results = [{
    kind: "pkg-nuget",
    hit: { id: "Example.New", version: "2.0.0" },
    ranges: [],
  }, first, selected];
  const html = spotlight.modalHtml();

  assert.equal(state.spotlightIndex, 2);
  assert.match(html, /aria-activedescendant="spotlight-result-2"/);
  assert.match(
    html,
    /id="spotlight-result-2" class="spotlight-item selected"[^>]*data-sl-type="Example\.Selected"/);
});

test("modal arrow navigation reuses rendered results", () => {
  const pkg = { id: "Example.Package", version: "1.0.0" };
  const rows = ["First", "Second", "Third"].map(name => ({
    kind: "type",
    pkg,
    type: { id: `Example.${name}`, name, kind: "class" },
    ranges: [],
  }));
  let searchCount = 0;
  const { spotlight, state } = createHarness({
    query: "Example",
    searchResults: () => {
      searchCount++;
      return rows;
    },
  });
  spotlight.modalHtml();

  const listeners = new Map();
  const input = {
    value: "Example",
    selectionStart: 7,
    selectionEnd: 7,
    addEventListener: (name, listener) => listeners.set(name, listener),
    focus: () => {},
    setAttribute: () => {},
    setSelectionRange: () => {},
  };
  const domRows = rows.map(() => ({
    classList: { toggle: () => {} },
    scrollIntoView: () => {},
    setAttribute: () => {},
  }));
  const container = {
    querySelector: () => null,
    querySelectorAll: selector => selector === ".spotlight-item" ? domRows : [],
  };
  const root = {
    querySelector: selector => selector === "#spotlight-input" ? input : null,
    querySelectorAll: () => [],
  };
  const previousDocument = globalThis.document;
  const previousRequestAnimationFrame = globalThis.requestAnimationFrame;
  globalThis.document = {
    querySelector: selector => {
      if (selector === "#spotlight-input") return input;
      if (selector === "#spotlight-results") return container;
      return null;
    },
  };
  globalThis.requestAnimationFrame = callback => callback();

  try {
    spotlight.bind(root, "modal");
    for (let index = 0; index < 4; index++) {
      listeners.get("keydown")({
        key: "ArrowDown",
        currentTarget: input,
        preventDefault: () => {},
      });
    }
  } finally {
    if (previousDocument === undefined) delete globalThis.document;
    else globalThis.document = previousDocument;
    if (previousRequestAnimationFrame === undefined) {
      delete globalThis.requestAnimationFrame;
    } else {
      globalThis.requestAnimationFrame = previousRequestAnimationFrame;
    }
  }

  assert.equal(searchCount, 1);
  assert.equal(state.spotlightIndex, 2);
});

test("closing Spotlight restores focus through the application boundary", () => {
  let restored = false;
  const { spotlight, state } = createHarness({
    focusAfterDismiss: () => { restored = true; },
  });
  state.spotlightOpen = true;
  state.spotlightQuery = "Json";

  spotlight.close();

  assert.equal(restored, true);
  assert.equal(state.spotlightOpen, false);
  assert.equal(state.spotlightQuery, "");
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
  assert.match(html, /aria-activedescendant="spotlight-result-0"/);
  assert.match(html, /id="spotlight-result-0"/);
  assert.match(html, /data-sl-index="0"/);
  assert.match(html, />type</);
  assert.doesNotMatch(html, /data-sl-pkg-load/);
});

test("workspace command lenses are resolved from the current package", () => {
  let lenses = [["api", "API"], ["metadata", "Metadata"]];
  const harness = createHarness({
    scope: "commands",
    query: "show ",
    commandContext: { command: "show ", package: packageContext },
    lenses: () => lenses,
  });

  assert.match(harness.spotlight.modalHtml(), />show metadata</);
  lenses = [["api", "API"]];
  assert.doesNotMatch(harness.spotlight.modalHtml(), />show metadata</);
  assert.match(harness.spotlight.modalHtml(), />show api</);
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
