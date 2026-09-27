import assert from "node:assert/strict";
import test from "node:test";

import {
  MEMBER_LIST_SELECTOR,
  createMemberListRevealer,
  revealRowAtTop,
  type RevealableDocument,
  type RevealableList,
  type RevealableRow,
} from "../src/member-list-reveal.ts";

const ROW_HEIGHT = 24;

// A member list whose rows sit at fixed offsets inside a scrollable viewport, like
// `#type-list.member-list`. Row rectangles move with `scrollTop`, as the browser's do.
function memberList(options: {
  scope: string;
  rows: number;
  viewportRows: number;
  revealedRow: number | null;
  scrollTop?: number;
}): RevealableList {
  const listTop = 100;
  const list: RevealableList = {
    dataset: { navScope: options.scope },
    scrollTop: options.scrollTop ?? 0,
    scrollHeight: options.rows * ROW_HEIGHT,
    clientHeight: options.viewportRows * ROW_HEIGHT,
    getBoundingClientRect: () => ({ top: listTop }),
    querySelector(selector: string): RevealableRow | null {
      assert.equal(selector, ".active-group, .selected");
      const index = options.revealedRow;
      return index === null
        ? null
        : { getBoundingClientRect: () => ({ top: listTop + index * ROW_HEIGHT - list.scrollTop }) };
    },
  };
  return list;
}

function documentWith(list: RevealableList | null): RevealableDocument {
  return {
    querySelector(selector: string) {
      assert.equal(selector, MEMBER_LIST_SELECTOR);
      return list;
    },
  };
}

test("a restored member selection scrolls to the top of its member list", () => {
  // JsonElement restored from a URL with a member 30 rows down a 62-row list.
  const list = memberList({
    scope: "members:System.Text.Json.JsonElement",
    rows: 62,
    viewportRows: 20,
    revealedRow: 30,
  });

  createMemberListRevealer().afterRender(documentWith(list));

  assert.equal(list.scrollTop, 30 * ROW_HEIGHT);
});

test("a selection near the end scrolls only as far as the list allows", () => {
  const list = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 58,
  });

  createMemberListRevealer().afterRender(documentWith(list));

  assert.equal(list.scrollTop, (62 - 20) * ROW_HEIGHT);
});

test("a selection in a list shorter than its viewport does not scroll", () => {
  const list = memberList({
    scope: "members:Example.Small",
    rows: 8,
    viewportRows: 20,
    revealedRow: 5,
  });

  createMemberListRevealer().afterRender(documentWith(list));

  assert.equal(list.scrollTop, 0);
});

test("later renders of the same member list keep the reader's scroll position", () => {
  const revealer = createMemberListRevealer();
  const first = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 30,
  });
  revealer.afterRender(documentWith(first));

  // A click selects another member in the same list; the list must not jump.
  const clicked = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 44,
    scrollTop: 400,
  });
  revealer.afterRender(documentWith(clicked));

  assert.equal(clicked.scrollTop, 400);
});

test("a member list that first appears without a selection is not moved later", () => {
  const revealer = createMemberListRevealer();
  revealer.afterRender(documentWith(memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: null,
  })));

  const clicked = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 44,
    scrollTop: 400,
  });
  revealer.afterRender(documentWith(clicked));

  assert.equal(clicked.scrollTop, 400);
});

test("returning to a type after leaving the member list reveals its selection again", () => {
  const revealer = createMemberListRevealer();
  revealer.afterRender(documentWith(memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 30,
  })));
  revealer.afterRender(documentWith(null));

  const restored = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 30,
  });
  revealer.afterRender(documentWith(restored));

  assert.equal(restored.scrollTop, 30 * ROW_HEIGHT);
});

test("revealRowAtTop measures from the list's current scroll position", () => {
  const list = memberList({
    scope: "members:Example.Type",
    rows: 62,
    viewportRows: 20,
    revealedRow: 30,
    scrollTop: 240,
  });
  const row = list.querySelector(".active-group, .selected");
  assert.ok(row);

  revealRowAtTop(list, row);

  assert.equal(list.scrollTop, 30 * ROW_HEIGHT);
});
