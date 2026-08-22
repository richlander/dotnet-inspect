import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlight,
  nextSpotlightScope,
  nextSpotlightSelection,
  visibleSpotlightPackageHits,
} from "../src/spotlight.ts";
import type { SpotlightResult, SpotlightState } from "../src/spotlight.ts";
import type { CommandContext } from "../src/command-bar.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

interface HarnessOptions {
  scope?: string;
  query?: string;
  commandContext?: CommandContext | null;
  focusAfterDismiss?: () => void;
  searchResults?: () => SpotlightResult[];
}

// The library owns the real DOM event/element contract; this harness models only the
// mutable pieces spotlight.ts reads, and callers cast to the real DOM types at the one
// boundary where the library calls through.
interface MockKeyboardEvent {
  key: string;
  currentTarget: unknown;
  shiftKey?: boolean;
  preventDefault(): void;
}

interface MockInputElement {
  value: string;
  selectionStart: number;
  selectionEnd: number;
  addEventListener(name: string, listener: (event: MockKeyboardEvent) => void): void;
  focus(): void;
  setAttribute(): void;
  setSelectionRange(): void;
}

interface MockElement {
  classList?: { toggle(className: string, force?: boolean): void };
  scrollIntoView?(): void;
  setAttribute?(): void;
}

interface MockParentNode {
  querySelector(selector: string): MockInputElement | MockElement | null;
  querySelectorAll(selector: string): MockElement[];
}

function createHarness({
  scope = "all",
  query = "",
  commandContext = null,
  focusAfterDismiss = () => {},
  searchResults = () => [],
}: HarnessOptions = {}) {
  const state: SpotlightState = {
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
    searchResults,
    pickResult: () => {},
    executeCommand: () => undefined,
    reportCommandError: () => {},
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

const packageContext: CommandContext["package"] = {
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
  const first: SpotlightResult = {
    kind: "type",
    pkg,
    type: { id: "Example.First", name: "First", kind: "class" },
    ranges: [],
  };
  const selected: SpotlightResult = {
    kind: "type",
    pkg,
    type: { id: "Example.Selected", name: "Selected", kind: "class" },
    ranges: [],
  };
  let results: SpotlightResult[] = [first, selected];
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
  const rows: SpotlightResult[] = ["First", "Second", "Third"].map(name => ({
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

  const listeners = new Map<string, (event: MockKeyboardEvent) => void>();
  const input: MockInputElement = {
    value: "Example",
    selectionStart: 7,
    selectionEnd: 7,
    addEventListener: (name, listener) => {
      listeners.set(name, listener);
    },
    focus: () => {},
    setAttribute: () => {},
    setSelectionRange: () => {},
  };
  const domRows: MockElement[] = rows.map(() => ({
    classList: { toggle: () => {} },
    scrollIntoView: () => {},
    setAttribute: () => {},
  }));
  const container: MockParentNode = {
    querySelector: () => null,
    querySelectorAll: selector => selector === ".spotlight-item" ? domRows : [],
  };
  const root: MockParentNode = {
    querySelector: selector => selector === "#spotlight-input" ? input : null,
    querySelectorAll: () => [],
  };
  // The harness temporarily installs its minimal DOM globals and restores them below.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const globals = globalThis as unknown as {
    document?: Document;
    requestAnimationFrame?: (callback: FrameRequestCallback) => number;
  };
  const previousDocument = globals.document;
  const previousRequestAnimationFrame = globals.requestAnimationFrame;
  // oxlint-disable typescript/no-unsafe-type-assertion
  globals.document = {
    querySelector: (selector: string) => {
      if (selector === "#spotlight-input") return input;
      if (selector === "#spotlight-results") return container;
      return null;
    },
  } as unknown as Document;
  // oxlint-enable typescript/no-unsafe-type-assertion
  globals.requestAnimationFrame = (callback: FrameRequestCallback) => {
    callback(0);
    return 0;
  };

  try {
    // The root implements the exact ParentNode query surface Spotlight consumes.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    spotlight.bind(root as unknown as ParentNode, "modal");
    for (let index = 0; index < 4; index++) {
      listeners.get("keydown")!({
        key: "ArrowDown",
        currentTarget: input,
        preventDefault: () => {},
      });
    }
  } finally {
    if (previousDocument === undefined) delete globals.document;
    else globals.document = previousDocument;
    if (previousRequestAnimationFrame === undefined) {
      delete globals.requestAnimationFrame;
    } else {
      globals.requestAnimationFrame = previousRequestAnimationFrame;
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

test("home Spotlight keeps the shared typed UI without workspace commands", () => {
  const { spotlight } = createHarness();

  const pendingHtml = spotlight.inlineHtml(true);
  assert.match(pendingHtml, /class="home-search-content" inert/);
  assert.match(pendingHtml, /id="spotlight-input"/);
  assert.match(pendingHtml, /package, type, or member…/);
  assert.doesNotMatch(pendingHtml, /or command/);
  assert.match(pendingHtml, /data-sl-scope="runtime"[^>]*>Platform/);
  assert.doesNotMatch(pendingHtml, /data-sl-scope="commands"/);
  assert.doesNotMatch(pendingHtml, /home-search-glint/);

  const readyHtml = spotlight.inlineHtml(false, true);
  assert.match(readyHtml, /class="home-search-glint" aria-hidden="true"/);
  assert.match(readyHtml, /class="home-search-glint-glow" pathLength="1"/);
  assert.match(readyHtml, /class="home-search-glint-line" pathLength="1"/);
  assert.doesNotMatch(spotlight.inlineHtml(false), /home-search-glint/);
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
