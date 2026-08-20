import assert from "node:assert/strict";
import test from "node:test";

import {
  captureMemberFocus,
  resolveMemberFocusSnapshot,
  restoreMemberFocus,
} from "../src/member-focus.ts";

function createDocument() {
  const body = { id: "", dataset: {}, isConnected: true };
  const elements = new Map();
  const document = {
    activeElement: body,
    body,
    querySelector(selector) {
      return elements.get(selector) ?? null;
    },
  };
  const element = (selector, properties = {}) => {
    const value = {
      id: "",
      dataset: {},
      isConnected: true,
      scrollTop: 0,
      focus() {
        document.activeElement = value;
      },
      ...properties,
    };
    elements.set(selector, value);
    return value;
  };
  return { document, element, elements };
}

test("navigation focus and scroll survive completion before loading focus restores", () => {
  const { document, element, elements } = createDocument();
  const initialList = element("#type-list", {
    id: "type-list",
    dataset: { navScope: "members:Type0" },
    scrollTop: 87,
  });
  document.activeElement = initialList;
  const initial = captureMemberFocus(document, value => value);

  initialList.isConnected = false;
  const replacementList = element("#type-list", {
    id: "type-list",
    dataset: { navScope: "members:Type0" },
  });
  document.activeElement = document.body;
  const completion = resolveMemberFocusSnapshot(
    captureMemberFocus(document, value => value),
    initial,
  );

  restoreMemberFocus(document, completion, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementList);
  assert.equal(replacementList.scrollTop, 87);

  const unrelated = element("#unrelated", { id: "unrelated" });
  document.activeElement = unrelated;
  const current = resolveMemberFocusSnapshot(
    captureMemberFocus(document, value => value),
    initial,
  );
  assert.equal(current.selector, "");
  assert.equal(current.focusLost, false);
  assert.equal(elements.get("#type-list").scrollTop, 87);
});

test("navigation scroll is not copied into a different list scope", () => {
  const { document, element } = createDocument();
  element("#type-list", {
    id: "type-list",
    dataset: { navScope: "types" },
    scrollTop: 1200,
  });
  const snapshot = captureMemberFocus(document, value => value);
  const memberList = element("#type-list", {
    id: "type-list",
    dataset: { navScope: "members:Type0" },
  });

  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(memberList.scrollTop, 0);
});

test("member-filter selection survives a replacement render", () => {
  const { document, element } = createDocument();
  const initialInput = element("#member-filter", {
    id: "member-filter",
    selectionStart: 2,
    selectionEnd: 5,
    selectionDirection: "backward",
  });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document, value => value);

  let restoredSelection = null;
  const replacementInput = element("#member-filter", {
    id: "member-filter",
    setSelectionRange(start, end, direction) {
      restoredSelection = { start, end, direction };
    },
  });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementInput);
  assert.deepEqual(
    restoredSelection,
    { start: 2, end: 5, direction: "backward" },
  );
});

test("deferred restoration does not steal intentionally moved focus", () => {
  const { document, element } = createDocument();
  const initialInput = element("#member-filter", { id: "member-filter" });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document, value => value);
  const callbacks = [];
  restoreMemberFocus(document, snapshot, callback => {
    callbacks.push(callback);
    return callbacks.length;
  });

  const unrelated = element("#unrelated", { id: "unrelated" });
  unrelated.focus();
  callbacks.shift()(0);

  assert.equal(document.activeElement, unrelated);
});
