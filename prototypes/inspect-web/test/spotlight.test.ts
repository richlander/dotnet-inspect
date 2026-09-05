import assert from "node:assert/strict";
import test from "node:test";

import {
  createSpotlight,
  nextSpotlightScope,
  nextSpotlightSelection,
  visibleSpotlightPackageHits,
} from "../src/spotlight.ts";
import type {
  SpotlightResult,
  SpotlightPackageResult,
  SpotlightScope,
  SpotlightState,
  RemovableSpotlightResult,
} from "../src/spotlight.ts";
import type { CommandContext } from "../src/command-bar.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { fakeDom } from "./fake-dom.ts";
import type { TypeLens } from "../src/data.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

interface HarnessOptions {
  scope?: SpotlightScope;
  query?: string;
  commandContext?: CommandContext | null;
  focusAfterDismiss?: () => void;
  captureFocusAfterDismiss?: () => () => void;
  executeCommand?: () => Promise<unknown> | undefined;
  searchResults?: () => SpotlightResult[];
  lenses?: () => readonly (readonly [string, string])[];
  removeResult?: (result: RemovableSpotlightResult) => boolean;
  pickResult?: (result: SpotlightResult) => void;
  packageSearchError?: () => string;
  packageSearchLoading?: () => boolean;
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
  captureFocusAfterDismiss,
  executeCommand = () => undefined,
  searchResults = () => [],
  lenses = () => [["api", "API"], ["metadata", "Metadata"]],
  removeResult,
  pickResult = () => {},
  packageSearchError,
  packageSearchLoading = () => false,
}: HarnessOptions = {}) {
  const state: SpotlightState = {
    spotlightOpen: false,
    spotlightQuery: query,
    spotlightIndex: 0,
    spotlightScope: scope,
    spotlightFocus: "input",
    spotlightChipIndex: 0,
  };
  const keybindings = new KeybindingRegistry();
  const spotlight = createSpotlight({
    keybindings,
    state,
    lenses,
    escapeHtml,
    highlightRanges: (value) => escapeHtml(value),
    kindIcon: () => "C",
    searchResults,
    pickResult,
    ...(removeResult ? { removeResult } : {}),
    executeCommand,
    reportCommandError: () => {},
    commandContext: () => commandContext,
    schedulePackageFetch: () => {},
    resetPackageSearch: () => {},
    packageSearchLoading,
    ...(packageSearchError ? { packageSearchError } : {}),
    packageCount: () => 1,
    activeFramework: () => "net10.0",
    render: () => {},
    focusAfterDismiss,
    ...(captureFocusAfterDismiss ? { captureFocusAfterDismiss } : {}),
  });
  return { keybindings, spotlight, state };
}

// open() ends by scheduling input focus through the real DOM. These tests are about
// scope admission, so they install the minimal focus target and restore it after.
function withStubbedFocusTarget(
  body: () => void,
  document = fakeDom.document({ querySelector: () => null }),
): void {
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const globals = globalThis as unknown as {
    document?: Document;
    requestAnimationFrame?: (callback: FrameRequestCallback) => number;
  };
  const previousDocument = globals.document;
  const previousRequestAnimationFrame = globals.requestAnimationFrame;
  globals.document = document;
  globals.requestAnimationFrame = (callback: FrameRequestCallback) => {
    callback(0);
    return 0;
  };
  try {
    body();
  } finally {
    if (previousDocument === undefined) delete globals.document;
    else globals.document = previousDocument;
    if (previousRequestAnimationFrame === undefined) {
      delete globals.requestAnimationFrame;
    } else {
      globals.requestAnimationFrame = previousRequestAnimationFrame;
    }
  }
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

function withBoundSpotlight(
  harness: ReturnType<typeof createHarness>,
  body: (dom: {
    input: ReturnType<typeof inputElement>;
    press: (key: string, shiftKey?: boolean) => boolean;
    clickRow: (index: number) => void;
    cancel: () => void;
    backdrop: () => void;
    activeId: () => string | undefined;
  }) => void,
): void {
  let activeElement: { id: string } | null = null;
  function element(id: string) {
    const listeners = new Map<string, EventListener>();
    const result = {
      id,
      listeners,
      addEventListener: (name: string, listener: EventListener) => {
        listeners.set(name, listener);
      },
      focus: () => { activeElement = result; },
    };
    return result;
  }
  function inputElement() {
    return {
      ...element("spotlight-input"),
      value: harness.state.spotlightQuery,
      selectionStart: 0,
      selectionEnd: 0,
      selectionDirection: "none",
      setAttribute: () => {},
      removeAttribute: () => {},
      setSelectionRange(start: number, end: number) {
        this.selectionStart = start;
        this.selectionEnd = end;
      },
    };
  }
  const input = inputElement();
  // Keep the active element's identity, not the element spread used above.
  input.focus = () => { activeElement = input; };
  const cancel = element("spotlight-cancel");
  const backdrop = element("spotlight-backdrop");
  const rows = [...harness.spotlight.modalHtml().matchAll(/data-sl-index="(\d+)"/g)]
    .map(match => ({
      ...element(`spotlight-result-${match[1]}`),
      dataset: { slIndex: match[1] },
      classList: { toggle: () => {} },
      setAttribute: () => {},
      scrollIntoView: () => {},
    }));
  const results = {
    innerHTML: "",
    querySelector: () => null,
    querySelectorAll: (selector: string) =>
      selector === ".spotlight-item" || selector === "[data-sl-index]" ? rows : [],
  };
  const root = {
    querySelector: (selector: string) => {
      if (selector === "#spotlight-input") return input;
      if (selector === "#spotlight-cancel") return cancel;
      if (selector === "#spotlight-backdrop") return backdrop;
      if (selector === "#spotlight-results") return results;
      return null;
    },
    querySelectorAll: (selector: string) => selector === "[data-sl-index]" ? rows : [],
  };
  const document = fakeDom.document({
    ...root,
    get activeElement() { return activeElement; },
  });
  withStubbedFocusTarget(() => {
    harness.spotlight.bind(fakeDom.parentNode(root), "modal");
    body({
      input,
      press: (key, shiftKey = false) => {
        const target = fakeDom.eventTarget(activeElement ?? input);
        return harness.keybindings.dispatch(fakeDom.keyboardEvent({
          key, shiftKey, target,
          altKey: false, ctrlKey: false, metaKey: false, defaultPrevented: false,
          composedPath: () => [target, fakeDom.eventTarget(backdrop)],
          preventDefault: () => {},
        })).handled;
      },
      clickRow: index => rows[index]?.listeners.get("click")?.(fakeDom.event()),
      cancel: () => cancel.listeners.get("click")?.(fakeDom.event()),
      backdrop: () => backdrop.listeners.get("mousedown")?.(
        fakeDom.event({ target: backdrop })),
      activeId: () => activeElement?.id,
    });
  }, document);
}

const packageRows: SpotlightPackageResult[] = [
  { kind: "pkg-loaded", pkg: { id: "Alpha", version: "1.0.0" }, ranges: [] },
  { kind: "pkg-nuget", hit: { id: "Beta", version: "2.0.0" }, ranges: [] },
  { kind: "pkg-recent", entry: { id: "Gamma", version: "3.0.0" }, ranges: [] },
];

test("Add package is a named package-only picker without commands or removal", () => {
  const pkg = { id: "Platform", version: "10.0.0", isRuntimePack: true };
  const type = { id: "System.Object", name: "Object", kind: "class" };
  const harness = createHarness({
    scope: "commands",
    commandContext: { command: "", package: packageContext },
    removeResult: () => true,
    searchResults: () => [
      ...packageRows,
      { kind: "command", action: "complete", command: "show", value: "show", hint: "Show", category: "choice" },
      { kind: "pkg-loaded", pkg, ranges: [] },
      { kind: "package-query", prefix: "" },
      { kind: "rtpack-suggest" },
      { kind: "rtpack-status", loading: true },
      { kind: "platform-lib", assembly: "System.Runtime", pack: "netcore.app", publicTypes: 1, ranges: [] },
      { kind: "type", pkg, type, ranges: [] },
      { kind: "member", pkg, type, memberKey: "ToString", name: "ToString", ranges: [] },
    ],
  });
  withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
    pickResult: () => {},
    focusAfterDismiss: () => {},
  }));

  assert.equal(harness.state.spotlightScope, "packages");
  assert.deepEqual(harness.spotlight.results(), packageRows);
  const html = harness.spotlight.modalHtml();
  assert.match(html, /role="dialog" aria-modal="true" aria-label="Add package"/);
  assert.match(html, /<strong>Add package<\/strong>/);
  assert.match(html, /id="spotlight-input" aria-label="Add package"/);
  assert.match(html, /Add <kbd>Enter<\/kbd>/);
  assert.match(html, /id="spotlight-cancel">Cancel<\/button>/);
  assert.match(html, /1\.0\.0 · already in Workspace/);
  assert.equal(html.match(/tabindex="-1"/g)?.length, 3);
  assert.doesNotMatch(html, /data-sl-scope|data-sl-remove|Shift\+Delete|Commands|Platform|Package query/);
});

test("Add package dispatches rendered loaded, NuGet and recent rows only to Add", () => {
  let current = packageRows;
  const picked: SpotlightPackageResult[] = [];
  let normalPicks = 0;
  const harness = createHarness({
    searchResults: () => current,
    pickResult: () => { normalPicks++; },
  });
  withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
    pickResult: result => picked.push(result),
    focusAfterDismiss: () => {},
  }));
  withBoundSpotlight(harness, dom => {
    current = packageRows.slice(1);
    dom.clickRow(0);
    dom.clickRow(1);
    dom.clickRow(2);
    assert.equal(dom.press("ArrowDown"), true);
    assert.equal(dom.press("Enter"), true);
  });
  assert.deepEqual(picked, [...packageRows, packageRows[1]]);
  assert.equal(normalPicks, 0);
});

test("Add package keeps selection identity as pending results change", () => {
  let current = [packageRows[0]!, packageRows[2]!];
  const harness = createHarness({ searchResults: () => current });
  withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
    pickResult: () => {},
    focusAfterDismiss: () => {},
  }));
  harness.state.spotlightIndex = 1;
  harness.spotlight.modalHtml();
  current = [...packageRows];
  assert.match(harness.spotlight.modalHtml(), /aria-activedescendant="spotlight-result-2"/);
  assert.equal(harness.state.spotlightIndex, 2);
});

test("Add package keeps arrows in results, preserves text selection, and tabs to Cancel", () => {
  let removed = 0;
  let dismissed = 0;
  const harness = createHarness({
    searchResults: () => packageRows,
    removeResult: () => { removed++; return true; },
  });
  withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
    pickResult: () => {},
    focusAfterDismiss: () => { dismissed++; },
  }));
  withBoundSpotlight(harness, dom => {
    dom.input.value = "Alpha";
    dom.input.selectionStart = 1;
    dom.input.selectionEnd = 4;
    assert.equal(dom.press("ArrowRight"), false);
    assert.deepEqual([dom.input.selectionStart, dom.input.selectionEnd], [1, 4]);
    dom.input.selectionStart = dom.input.selectionEnd = 5;
    assert.equal(dom.press("ArrowRight"), false);
    assert.equal(dom.press("ArrowUp"), true);
    assert.equal(harness.state.spotlightIndex, 0);
    assert.equal(harness.state.spotlightFocus, "input");
    assert.equal(dom.press("Delete", true), false);
    assert.equal(removed, 0);
    for (const backward of [false, true]) {
      assert.equal(dom.press("Tab", backward), true);
      assert.equal(dom.activeId(), "spotlight-cancel");
      assert.equal(dom.press("Enter"), false); // Native button activation, not a result pick.
      assert.equal(dom.press("Tab", backward), true);
      assert.equal(dom.activeId(), "spotlight-input");
    }
    assert.equal(harness.state.spotlightScope, "packages");
    dom.press("Tab");
    dom.cancel();
  });
  assert.equal(dismissed, 1);
  assert.equal(harness.state.spotlightOpen, false);
});

test("Add package dismissal uses its focus callback for Cancel, Escape and backdrop", () => {
  for (const dismiss of ["cancel", "input-escape", "cancel-escape", "backdrop"] as const) {
    let restored = 0;
    let normalRestored = 0;
    const harness = createHarness({
      focusAfterDismiss: () => { normalRestored++; },
      captureFocusAfterDismiss: () => () => { normalRestored++; },
    });
    withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
      pickResult: () => assert.fail("Dismissal must not pick a result"),
      focusAfterDismiss: () => { restored++; },
    }));
    withBoundSpotlight(harness, dom => {
      if (dismiss === "cancel") dom.cancel();
      else if (dismiss === "backdrop") dom.backdrop();
      else {
        if (dismiss === "cancel-escape") dom.press("Tab");
        assert.equal(dom.press("Escape"), true);
      }
    });
    assert.equal(restored, 1, dismiss);
    assert.equal(normalRestored, 0, dismiss);
    assert.equal(harness.state.spotlightOpen, false, dismiss);
    assert.doesNotMatch(harness.spotlight.modalHtml(), /Add package|spotlight-cancel/);
  }
});

test("Add package with no rows keeps Enter inert and preserves selection across rebinding", () => {
  const harness = createHarness({ packageSearchLoading: () => true });
  withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
    pickResult: () => assert.fail("No package is available to pick"),
    focusAfterDismiss: () => assert.fail("Enter must not cancel an empty picker"),
  }));
  harness.state.spotlightQuery = "Alpha";
  withBoundSpotlight(harness, dom => {
    assert.equal(dom.press("Enter"), true);
    assert.equal(harness.state.spotlightOpen, true);
    dom.input.selectionStart = 1;
    dom.input.selectionEnd = 4;
  });
  withBoundSpotlight(harness, dom => {
    assert.deepEqual([dom.input.selectionStart, dom.input.selectionEnd], [1, 4]);
    assert.equal(harness.state.spotlightScope, "packages");
  });
});

test("ordinary open and reset clear the Add callback and restore Search scopes and removal", () => {
  for (const action of ["open", "reset"] as const) {
    let normalPicks = 0;
    let normalRestored = 0;
    const harness = createHarness({
      searchResults: () => packageRows,
      removeResult: () => true,
      pickResult: () => { normalPicks++; },
      focusAfterDismiss: () => { normalRestored++; },
      commandContext: { command: "", package: packageContext },
    });
    withStubbedFocusTarget(() => {
      harness.spotlight.openForPackageAddition({
        pickResult: () => assert.fail("Old Add callback leaked"),
        focusAfterDismiss: () => assert.fail("Old Add focus callback leaked"),
      });
      harness.spotlight[action]();
    });
    const html = harness.spotlight.modalHtml();
    assert.match(html, /aria-label="Go to anything"/);
    assert.match(html, /data-sl-scope="commands"/);
    assert.match(html, /data-sl-remove/);
    assert.match(html, /1\.0\.0 · open/);
    assert.doesNotMatch(html, /already in Workspace|spotlight-cancel/);
    withBoundSpotlight(harness, dom => {
      dom.clickRow(0);
      assert.equal(dom.press("Tab"), true);
      assert.equal(harness.state.spotlightScope, "packages");
      dom.press("Tab", true);
      assert.equal(harness.state.spotlightScope, "all");
      dom.press("Escape");
    });
    assert.equal(normalPicks, 1);
    assert.equal(normalRestored, 1);
  }
});

test("ordinary command open ends Add package purpose", () => {
  const harness = createHarness({
    commandContext: { command: "", package: packageContext },
  });
  withStubbedFocusTarget(() => {
    harness.spotlight.openForPackageAddition({
      pickResult: () => assert.fail("Old Add callback leaked"),
      focusAfterDismiss: () => assert.fail("Old Add focus callback leaked"),
    });
    harness.spotlight.open("", "commands");
  });
  assert.equal(harness.state.spotlightScope, "commands");
  assert.match(harness.spotlight.modalHtml(), /aria-label="Run a command"/);
  assert.ok(harness.spotlight.results().every(result => result.kind === "command"));
  harness.spotlight.close();
});

test("package source errors are escaped, coexist with local results and replace Nothing matches", () => {
  for (const addition of [false, true]) {
    let rows = [...packageRows];
    let error = 'NuGet failed: <script title="bad">&. Edit the search to retry.';
    const harness = createHarness({
      query: "Missing",
      searchResults: () => rows,
      packageSearchError: () => error,
    });
    if (addition) withStubbedFocusTarget(() => harness.spotlight.openForPackageAddition({
      pickResult: () => {},
      focusAfterDismiss: () => {},
    }));
    harness.state.spotlightQuery = "Missing";
    for (const render of [
      () => harness.spotlight.modalHtml(),
      () => harness.spotlight.inlineHtml(false),
    ]) {
      const html = render();
      assert.match(html, /role="status">NuGet failed: &lt;script title=&quot;bad&quot;&gt;&amp;/);
      assert.match(html, /data-sl-pkg-open="Alpha"/);
      assert.match(html, /data-sl-pkg-recent="Gamma"/);
      assert.doesNotMatch(html, /<script|Nothing matches/);
    }
    rows = [];
    assert.doesNotMatch(harness.spotlight.modalHtml(), /Nothing matches/);
    assert.match(harness.spotlight.modalHtml(), /Edit the search to retry/);
    error = "";
    assert.match(harness.spotlight.modalHtml(), /Nothing matches/);
  }
});

test("Spotlight selection clamps without wrapping and scope cycling wraps", () => {
  assert.equal(nextSpotlightSelection(0, -1, 4), null);
  assert.equal(nextSpotlightSelection(2, 1, 4), 3);
  assert.equal(nextSpotlightSelection(3, 1, 4), 3);
  assert.equal(nextSpotlightSelection(0, 1, 0), null);
  assert.equal(nextSpotlightScope(4, 5, false), 0);
  assert.equal(nextSpotlightScope(0, 5, true), 4);
});

test("Spotlight gives open and recent package rows separate named removal buttons", () => {
  const { spotlight } = createHarness({
    removeResult: () => true,
    searchResults: () => [
      { kind: "pkg-loaded", pkg: { id: "Alpha", version: "1.0.0", activeFramework: "net10.0" }, ranges: [] },
      { kind: "pkg-recent", entry: { id: "Beta" }, ranges: [] },
      { kind: "pkg-loaded", pkg: { id: "Platform", version: "10.0.0", isRuntimePack: true }, ranges: [] },
      { kind: "pkg-nuget", hit: { id: "Gamma" }, ranges: [] },
    ],
  });
  const html = spotlight.inlineHtml(false);
  assert.match(html, /aria-label="Remove Alpha 1\.0\.0 net10\.0 from Workspace"/);
  assert.match(html, /aria-label="Forget Beta from recent packages"/);
  assert.equal(html.match(/data-sl-remove=/g)?.length, 2);
  assert.match(html, /<\/button><button[^>]*class="package-row-remove"/);
});

test("NuGet hits are visible only for their resolved query and survive a query round trip", () => {
  const hits = [{ id: "Alpha", version: "1.0.0" }];

  assert.deepEqual(visibleSpotlightPackageHits("alpha", "alpha", hits), hits);
  assert.deepEqual(visibleSpotlightPackageHits("alphabet", "alpha", hits), []);
  assert.deepEqual(visibleSpotlightPackageHits("alpha", "alpha", hits), hits);
});

test("Spotlight renders the package-query action with its seeded prefix identity", () => {
  const { spotlight } = createHarness({
    query: "Microsoft.Extensions.",
    searchResults: () => [{
      kind: "package-query",
      prefix: "Microsoft.Extensions.",
      ranges: [],
    }],
  });

  const html = spotlight.modalHtml();

  assert.match(html, /Package query/);
  assert.match(html, /Microsoft\.Extensions\./);
  assert.match(html, /data-sl-package-query="1"/);
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
  const { keybindings, spotlight, state } = createHarness({
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
    const target = fakeDom.eventTarget(input);
    for (let index = 0; index < 4; index++) {
      keybindings.dispatch(fakeDom.keyboardEvent({
        altKey: false,
        ctrlKey: false,
        defaultPrevented: false,
        key: "ArrowDown",
        metaKey: false,
        shiftKey: false,
        target,
        composedPath: () => [target],
        preventDefault: () => {},
      }));
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

test("newer document focus blocks delayed command focus restoration", async () => {
  let resolveCommand: (() => void) | undefined;
  const command = new Promise<void>(resolve => {
    resolveCommand = resolve;
  });
  let documentFocusGeneration = 0;
  let restored = 0;
  let click: (() => void) | undefined;
  const { spotlight, state } = createHarness({
    scope: "commands",
    query: "show metadata",
    commandContext: {
      command: "show metadata",
      package: packageContext,
    },
    executeCommand: () => {
      return command;
    },
    captureFocusAfterDismiss: () => {
      const generation = documentFocusGeneration;
      return () => {
        if (generation === documentFocusGeneration) restored++;
      };
    },
  });
  state.spotlightOpen = true;
  const results = spotlight.results();
  const commandIndex = results.findIndex(result =>
    result.kind === "command" && result.action === "execute");
  assert.notEqual(commandIndex, -1);
  spotlight.modalHtml();
  const row = {
    dataset: { slIndex: String(commandIndex) },
    addEventListener: (_name: string, listener: () => void) => {
      click = listener;
    },
  };
  const input = {
    value: state.spotlightQuery,
    selectionStart: state.spotlightQuery.length,
    selectionEnd: state.spotlightQuery.length,
    addEventListener: () => {},
    focus: () => {},
    setAttribute: () => {},
    setSelectionRange: () => {},
  };
  const root = {
    querySelector: (selector: string) =>
      selector === "#spotlight-input" ? input : null,
    querySelectorAll: (selector: string) =>
      selector === "[data-sl-index]" ? [row] : [],
  };

  withStubbedFocusTarget(() =>
    // The mock implements the exact ParentNode query surface Spotlight consumes.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    spotlight.bind(root as unknown as ParentNode, "modal"));
  click?.();
  documentFocusGeneration++;
  resolveCommand?.();
  await command;
  await Promise.resolve();

  assert.equal(restored, 0);
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
  assert.match(
    html,
    /class="spotlight-foot"[^>]*>[\s\S]*<kbd>Ctrl P<\/kbd> search/,
  );
  assert.match(html, />type</);
  assert.doesNotMatch(html, /data-sl-pkg-load/);
});

test("workspace command lenses are resolved from the current package", () => {
  let lenses: readonly (readonly [TypeLens, string])[] =
    [["api", "API"], ["metadata", "Metadata"]];
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

test("Spotlight rejects a scope the current context does not offer", () => {
  const { spotlight, state } = createHarness();

  // "commands" is a well-typed SpotlightScope, but scopes() only offers it when a
  // command context exists. Without one, open() must fall back rather than seat a
  // scope whose results() branch can only ever return an empty list.
  withStubbedFocusTarget(() => spotlight.open("", "commands"));

  assert.equal(state.spotlightScope, "all");
  assert.doesNotMatch(spotlight.modalHtml(), /data-sl-scope="commands"/);
});

test("Spotlight accepts a scope the current context does offer", () => {
  const { spotlight, state } = createHarness({
    commandContext: { command: "", package: packageContext },
  });

  withStubbedFocusTarget(() => spotlight.open("", "commands"));

  assert.equal(state.spotlightScope, "commands");
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
