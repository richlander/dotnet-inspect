import assert from "node:assert/strict";
import test from "node:test";

import {
  captureMemberFocus as captureMemberFocusImpl,
  createMemberFocusRestorer,
} from "../src/member-focus.ts";
import {
  NAVIGATION_LIST_SELECTOR,
  createNavigationScrollKeeper,
  revealRowAtTop,
  type RevealableDocument,
  type RevealableList,
  type RevealableRow,
} from "../src/member-list-reveal.ts";

const ROW_HEIGHT = 24;
const LIST_TOP = 100;

interface ListSpec {
  scope: string;
  member: boolean;
  rows: number;
  viewportRows: number;
  // The row carrying `.active-group`/`.selected`, or null when nothing is selected yet.
  revealedRow: number | null;
  selection?: string;
}

interface MockList extends RevealableList {
  readonly dataset: { navScope?: string; navSelection?: string };
  readonly id: string;
  isConnected: boolean;
  focus(): void;
}

// The page as `render()` leaves it: each render rebuilds `#type-list` from markup, so the new
// element starts at scrollTop 0 exactly as an innerHTML replacement does in the browser.
function createPage() {
  let list: MockList | null = null;
  const listeners = new Map<string, Array<() => void>>();
  const body = { id: "", dataset: {}, isConnected: true, scrollTop: 0, focus() {} };
  const document = {
    activeElement: body,
    body,
    querySelector(selector: string) {
      return selector === NAVIGATION_LIST_SELECTOR ? list : null;
    },
    querySelectorAll() {
      return [];
    },
    addEventListener(type: string, listener: () => void) {
      listeners.set(type, [...(listeners.get(type) ?? []), listener]);
    },
  };
  const keeper = createNavigationScrollKeeper();
  const restorer = createMemberFocusRestorer();
  const frames: FrameRequestCallback[] = [];
  const revealable: RevealableDocument = document;
  // member-focus.ts reads the `Document` subset this page models.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const domDocument = document as unknown as Document;

  const build = (spec: ListSpec): MockList => {
    const built: MockList = {
      id: "type-list",
      isConnected: true,
      focus() {},
      dataset: {
        navScope: spec.scope,
        navSelection: spec.selection ?? (spec.revealedRow === null ? "" : `member:${spec.revealedRow}`),
      },
      classList: {
        contains: token => token === "member-list" && spec.member,
      },
      scrollTop: 0,
      scrollHeight: spec.rows * ROW_HEIGHT,
      clientHeight: spec.viewportRows * ROW_HEIGHT,
      getBoundingClientRect: () => ({ top: LIST_TOP }),
      querySelector(selector: string): RevealableRow | null {
        assert.equal(selector, ".active-group, .selected");
        const index = spec.revealedRow;
        return index === null
          ? null
          : { getBoundingClientRect: () => ({ top: LIST_TOP + index * ROW_HEIGHT - built.scrollTop }) };
      },
    };
    return built;
  };

  return {
    get list() {
      assert.ok(list);
      return list;
    },
    render(spec: ListSpec | null) {
      keeper.beforeRender(revealable);
      if (list) list.isConnected = false;
      list = spec === null ? null : build(spec);
      keeper.afterRender(revealable);
    },
    // renderPreservingMemberFocus: capture, render, then restore in the next frame.
    renderPreservingFocus(spec: ListSpec) {
      const snapshot = restorer.resolve(captureMemberFocusImpl(domDocument), null);
      this.render(spec);
      restorer.schedule(domDocument, snapshot, callback => frames.push(callback));
    },
    flushFrames() {
      for (const callback of frames.splice(0)) callback(0);
    },
    userInput(type: "pointerdown" | "keydown") {
      for (const listener of listeners.get(type) ?? []) listener();
    },
  };
}

const jsonElement = (revealedRow: number | null): ListSpec => ({
  scope: "members:System.Text.Json.JsonElement",
  member: true,
  rows: 62,
  viewportRows: 20,
  revealedRow,
});

test("a restored selection stays at the top through the member detail renders that follow", () => {
  // The reported URL: JsonElement restored with a member 30 rows down. Documentation and
  // declaration loading each re-render with focus preservation before the next frame.
  const page = createPage();
  page.render(jsonElement(30));
  page.renderPreservingFocus(jsonElement(30));
  page.renderPreservingFocus(jsonElement(30));
  page.flushFrames();

  assert.equal(page.list.scrollTop, 30 * ROW_HEIGHT);
});

test("a selection near the end scrolls only as far as the list allows", () => {
  const page = createPage();
  page.render(jsonElement(58));

  assert.equal(page.list.scrollTop, (62 - 20) * ROW_HEIGHT);
});

test("a selection that arrives after the list is revealed on that render", () => {
  // A graph-member deep link renders the member list before the graph member's row exists.
  const page = createPage();
  page.render(jsonElement(null));
  page.render(jsonElement(30));

  assert.equal(page.list.scrollTop, 30 * ROW_HEIGHT);
});

test("user input cancels a pending reveal", () => {
  const page = createPage();
  page.render(jsonElement(null));
  page.list.scrollTop = 96;
  page.userInput("pointerdown");
  page.render(jsonElement(44));

  // The click changed the selection, so the rebuilt list starts at the top (the click handler
  // then brings the row into view); it is not moved to the reveal offset.
  assert.equal(page.list.scrollTop, 0);
  assert.notEqual(page.list.scrollTop, 44 * ROW_HEIGHT);
});

test("a render that keeps the selection keeps the reader's scroll position", () => {
  const page = createPage();
  page.render(jsonElement(30));
  page.list.scrollTop = 400;
  page.userInput("pointerdown");
  page.renderPreservingFocus(jsonElement(30));
  page.flushFrames();

  assert.equal(page.list.scrollTop, 400);
});

test("a filter that selects another row starts the rebuilt list at the top", () => {
  // Filtering selects the first match; the rebuilt list must not keep the old offset.
  const page = createPage();
  const types = (rows: number, selected: string): ListSpec =>
    ({ scope: "types", member: false, rows, viewportRows: 20, revealedRow: 0, selection: selected });
  page.render(types(500, "type:A.Deep.Type"));
  page.list.scrollTop = 4000;
  page.userInput("keydown");
  page.render(types(120, "type:A.First.Match"));

  assert.equal(page.list.scrollTop, 0);
});

test("a hidden member list keeps its reveal pending until it has a height", () => {
  const page = createPage();
  page.render({ ...jsonElement(30), viewportRows: 0 });
  assert.equal(page.list.scrollTop, 0);

  page.render(jsonElement(30));

  assert.equal(page.list.scrollTop, 30 * ROW_HEIGHT);
});

test("returning to a type after leaving its member list reveals the selection again", () => {
  const page = createPage();
  page.render(jsonElement(30));
  page.render({ scope: "types", member: false, rows: 90, viewportRows: 20, revealedRow: 12 });
  page.render(jsonElement(30));

  assert.equal(page.list.scrollTop, 30 * ROW_HEIGHT);
});

test("the type list keeps its scroll position across renders without revealing", () => {
  const page = createPage();
  const types: ListSpec = {
    scope: "types",
    member: false,
    rows: 90,
    viewportRows: 20,
    revealedRow: 70,
    selection: "type:Example.Type",
  };
  page.render(types);
  assert.equal(page.list.scrollTop, 0);

  page.list.scrollTop = 312;
  page.render(types);

  assert.equal(page.list.scrollTop, 312);
});

test("revealRowAtTop measures from the list's current scroll position", () => {
  const page = createPage();
  page.render(jsonElement(null));
  const list = page.list;
  list.scrollTop = 240;
  const row = { getBoundingClientRect: () => ({ top: LIST_TOP + 30 * ROW_HEIGHT - list.scrollTop }) };

  revealRowAtTop(list, row);

  assert.equal(list.scrollTop, 30 * ROW_HEIGHT);
});
