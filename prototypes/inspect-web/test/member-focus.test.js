import assert from "node:assert/strict";
import test from "node:test";

import {
  captureMemberFocus,
  createMemberFocusRestorer,
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
    dataset: {
      navScope: "members:Type0",
      navSelection: "member:Build",
    },
    scrollTop: 87,
  });
  document.activeElement = initialList;
  const initial = captureMemberFocus(document, value => value);

  initialList.isConnected = false;
  const replacementList = element("#type-list", {
    id: "type-list",
    dataset: {
      navScope: "members:Type0",
      navSelection: "member:Build",
    },
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
  assert.equal(current.selector, "#unrelated");
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

test("navigation scroll yields to a changed selection in the same scope", () => {
  const { document, element } = createDocument();
  element("#type-list", {
    id: "type-list",
    dataset: {
      navScope: "members:Type0",
      navSelection: "member:Build",
    },
    scrollTop: 87,
  });
  const snapshot = captureMemberFocus(document, value => value);
  const memberList = element("#type-list", {
    id: "type-list",
    dataset: {
      navScope: "members:Type0",
      navSelection: "member:Run",
    },
  });
  memberList.scrollTop = 300;

  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(memberList.scrollTop, 300);
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

test("type-filter selection survives a replacement render", () => {
  const { document, element } = createDocument();
  const initialInput = element("#type-filter", {
    id: "type-filter",
    selectionStart: 2,
    selectionEnd: 5,
    selectionDirection: "backward",
  });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document, value => value);

  let restoredSelection = null;
  const replacementInput = element("#type-filter", {
    id: "type-filter",
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

test("stable workbench controls survive a replacement render", () => {
  const { document, element } = createDocument();
  const initialButton = element("#copy-name", { id: "copy-name" });
  document.activeElement = initialButton;
  const snapshot = captureMemberFocus(document, value => value);

  const replacementButton = element("#copy-name", { id: "copy-name" });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementButton);
});

test("scope and lens controls survive a replacement render", () => {
  const cases = [
    ["[data-scope=\"member\"]", { scope: "member" }],
    ["[data-lens=\"metadata\"]", { lens: "metadata" }],
    ["[data-member-section=\"facts\"]", { memberSection: "facts" }],
    ["[data-package-lens=\"analysis\"]", { packageLens: "analysis" }],
  ];

  for (const [selector, dataset] of cases) {
    const { document, element } = createDocument();
    const initialButton = element(selector, { dataset });
    document.activeElement = initialButton;
    const snapshot = captureMemberFocus(document, value => value);

    const replacementButton = element(selector, { dataset });
    document.activeElement = document.body;
    restoreMemberFocus(document, snapshot, callback => {
      callback(0);
      return 1;
    });

    assert.equal(document.activeElement, replacementButton);
  }
});

test("member and overload rows survive activation renders", () => {
  const cases = [
    ["[data-nav-member=\"method:Build\"]", { navMember: "method:Build" }],
    ["[data-nav-overload=\"1\"]", { navOverload: "1" }],
  ];

  for (const [selector, dataset] of cases) {
    const { document, element } = createDocument();
    const initialButton = element(selector, { dataset });
    document.activeElement = initialButton;
    const snapshot = captureMemberFocus(document, value => value);

    const replacementButton = element(selector, { dataset });
    document.activeElement = document.body;
    restoreMemberFocus(document, snapshot, callback => {
      callback(0);
      return 1;
    });

    assert.equal(document.activeElement, replacementButton);
  }
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

test("newer caret restoration invalidates older queued callbacks", () => {
  const { document, element } = createDocument();
  let restoredSelection = null;
  const input = element("#member-filter", {
    id: "member-filter",
    selectionStart: 1,
    selectionEnd: 1,
    selectionDirection: "none",
    setSelectionRange(start, end, direction) {
      restoredSelection = { start, end, direction };
    },
  });
  const callbacks = [];
  const requestFrame = callback => {
    callbacks.push(callback);
    return callbacks.length;
  };
  const restorer = createMemberFocusRestorer();

  document.activeElement = input;
  const older = captureMemberFocus(document, value => value);
  restorer.schedule(document, older, requestFrame);

  input.selectionStart = 5;
  input.selectionEnd = 5;
  const newer = captureMemberFocus(document, value => value);
  restorer.schedule(document, newer, requestFrame);

  document.activeElement = document.body;
  const completion = restorer.resolve(
    captureMemberFocus(document, value => value),
    older,
  );
  restorer.schedule(document, completion, requestFrame);

  for (const callback of callbacks)
    callback(0);

  assert.deepEqual(
    restoredSelection,
    { start: 5, end: 5, direction: "none" },
  );
});

test("a new render without a fallback does not revive an older focus target", () => {
  const { document, element } = createDocument();
  const input = element("#member-filter", { id: "member-filter" });
  document.activeElement = input;
  const older = captureMemberFocus(document, value => value);
  const restorer = createMemberFocusRestorer();
  restorer.schedule(document, older, () => 1);

  document.activeElement = document.body;
  const current = restorer.resolve(
    captureMemberFocus(document, value => value),
    null,
  );

  assert.equal(current.selector, "");
  assert.equal(current.focusLost, true);
});
