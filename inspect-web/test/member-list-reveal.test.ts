import assert from "node:assert/strict";
import test from "node:test";

import {
  captureMemberFocus as captureMemberFocusImpl,
  createMemberFocusRestorer,
} from "../src/member-focus.ts";
import {
  NAVIGATION_LIST_SELECTOR,
  createMemberListRevealer,
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
  const revealer = createMemberListRevealer();
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
      if (list) list.isConnected = false;
      list = spec === null ? null : build(spec);
      revealer.afterRender(revealable);
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
  page.userInput("pointerdown");
  page.render(jsonElement(44));

  assert.equal(page.list.scrollTop, 0);
});

test("user input ends a held reveal, so later rebuilds behave as before", () => {
  const page = createPage();
  page.render(jsonElement(30));
  assert.equal(page.list.scrollTop, 30 * ROW_HEIGHT);

  page.userInput("keydown");
  page.render(jsonElement(30));

  assert.equal(page.list.scrollTop, 0);
});

test("the member focus restore still returns the reader to their position", () => {
  const page = createPage();
  page.render(jsonElement(30));
  page.userInput("pointerdown");
  page.list.scrollTop = 400;
  page.renderPreservingFocus(jsonElement(30));
  page.flushFrames();

  assert.equal(page.list.scrollTop, 400);
});

test("typing in the type filter starts the rebuilt type list at the top", () => {
  // The type list is never revealed or held; a same-selection filter rebuild resets it.
  const page = createPage();
  const types = (rows: number): ListSpec => ({
    scope: "types",
    member: false,
    rows,
    viewportRows: 20,
    revealedRow: 0,
    selection: "type:System.Text.Json.JsonDocument",
  });
  page.render(types(400));
  page.list.scrollTop = 3000;
  page.userInput("keydown");
  page.render(types(250));

  assert.equal(page.list.scrollTop, 0);
});

test("a selection change ends a held reveal", () => {
  const page = createPage();
  page.render(jsonElement(30));
  page.render({ ...jsonElement(44), selection: "member:44" });

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

test("revealRowAtTop measures from the list's current scroll position", () => {
  const page = createPage();
  page.render(jsonElement(null));
  const list = page.list;
  list.scrollTop = 240;
  const row = { getBoundingClientRect: () => ({ top: LIST_TOP + 30 * ROW_HEIGHT - list.scrollTop }) };

  revealRowAtTop(list, row);

  assert.equal(list.scrollTop, 30 * ROW_HEIGHT);
});
