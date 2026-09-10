import assert from "node:assert/strict";
import test from "node:test";

import {
  captureMemberFocus as captureMemberFocusImpl,
  createMemberFocusRestorer as createMemberFocusRestorerImpl,
  focusPlatformGraphError as focusPlatformGraphErrorImpl,
  resolveMemberFocusSnapshot,
  restoreMemberFocus as restoreMemberFocusImpl,
} from "../src/member-focus.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";

interface MockElement {
  id: string;
  dataset: Record<string, string | undefined>;
  isConnected: boolean;
  scrollTop: number;
  selectionStart?: number | null;
  selectionEnd?: number | null;
  selectionDirection?: "forward" | "backward" | "none" | null;
  setSelectionRange?(start: number | null, end: number | null, direction?: string | null): void;
  focus(): void;
}

// The library owns the real `Document`/`Element` contract; this harness models only the
// mutable pieces member-focus.ts reads and writes, and the wrappers below present that shape
// as `Document`/`Element` at the one boundary where the library calls through.
interface MockDocument {
  activeElement: MockElement | null;
  body: MockElement;
  querySelector(selector: string): MockElement | null;
  querySelectorAll(selector: string): MockElement[];
}

function captureMemberFocus(document: MockDocument): MemberFocusSnapshot {
  // The harness supplies the exact DOM subset the product reads.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return captureMemberFocusImpl(document as unknown as Document);
}

function restoreMemberFocus(
  document: MockDocument,
  snapshot: MemberFocusSnapshot,
  requestFrame: (callback: FrameRequestCallback) => number,
  isCurrent?: () => boolean,
): void {
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  restoreMemberFocusImpl(document as unknown as Document, snapshot, requestFrame, isCurrent);
}

function focusPlatformGraphError(document: MockDocument): boolean {
  // The harness supplies the exact DOM subset the product reads.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return focusPlatformGraphErrorImpl(document as unknown as Document);
}

function createMemberFocusRestorer() {
  const restorer = createMemberFocusRestorerImpl();
  return {
    resolve: (current: MemberFocusSnapshot, fallback: MemberFocusSnapshot | null) =>
      restorer.resolve(current, fallback),
    schedule(
      document: MockDocument,
      snapshot: MemberFocusSnapshot,
      requestFrame: (callback: FrameRequestCallback) => number,
      isCurrent?: () => boolean,
    ) {
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      const domDocument = document as unknown as Document;
      restorer.schedule(
        domDocument,
        snapshot,
        requestFrame,
        isCurrent);
    },
  };
}

function createDocument() {
  const body: MockElement = { id: "", dataset: {}, isConnected: true, scrollTop: 0, focus() {} };
  const elements = new Map<string, MockElement>();
  const document: MockDocument = {
    activeElement: body,
    body,
    querySelector(selector: string) {
      return elements.get(selector) ?? null;
    },
    querySelectorAll(selector: string) {
      const key = selector.match(/^\[data-([a-z-]+)\]$/)?.[1]
        ?.replace(
          /-([a-z])/g,
          (_match: string, letter: string) => letter.toUpperCase());
      return key
        ? [...elements.values()].filter(value =>
          value.isConnected && value.dataset[key] !== undefined)
        : [];
    },
  };
  const element = (selector: string, properties: Partial<MockElement> = {}) => {
    const value: MockElement = {
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

test("a blocked graph refusal receives focus after replacement render", () => {
  const { document, element } = createDocument();
  const oldNode = element("#old-graph-node", {
    id: "flowchart-n1-0",
  });
  document.activeElement = oldNode;

  oldNode.isConnected = false;
  document.activeElement = document.body;
  const error = element("#platform-drill-error", {
    id: "platform-drill-error",
  });

  assert.equal(focusPlatformGraphError(document), true);
  assert.equal(document.activeElement, error);
});

test("a missing graph refusal does not disturb current focus", () => {
  const { document, element } = createDocument();
  const unrelated = element("#unrelated", { id: "unrelated" });
  document.activeElement = unrelated;

  assert.equal(focusPlatformGraphError(document), false);
  assert.equal(document.activeElement, unrelated);
});

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
  const initial = captureMemberFocus(document);

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
    captureMemberFocus(document),
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
    captureMemberFocus(document),
    initial,
  );
  assert.equal(current.selector, "#unrelated");
  assert.equal(current.focusLost, false);
  assert.equal(elements.get("#type-list")!.scrollTop, 87);
});

test("a focused Type row survives an asynchronous replacement render", () => {
  const { document, element } = createDocument();
  element("#type-list", {
    id: "type-list",
    dataset: {
      navScope: "types",
      navSelection: "type:Example.Type",
    },
  });
  const initialRow = element("#initial-type", {
    dataset: { type: "Example.Type" },
  });
  document.activeElement = initialRow;
  const snapshot = captureMemberFocus(document);

  initialRow.isConnected = false;
  document.activeElement = document.body;
  const replacementRow = element("#replacement-type", {
    dataset: { type: "Example.Type" },
  });

  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementRow);
});

test("focused Type filter controls survive replacement renders", () => {
  for (const [key, value] of [
    ["kindFilter", "class"],
    ["namespace", "System.Text.Json"],
    ["accessChip", "public"],
    ["libraryChip", "System.Text.Json"],
  ] as const) {
    const { document, element } = createDocument();
    const initial = element(`#initial-${key}`, {
      dataset: { [key]: value },
    });
    document.activeElement = initial;
    const snapshot = captureMemberFocus(document);

    initial.isConnected = false;
    document.activeElement = document.body;
    const replacement = element(`#replacement-${key}`, {
      dataset: { [key]: value },
    });

    restoreMemberFocus(document, snapshot, callback => {
      callback(0);
      return 1;
    });

    assert.equal(document.activeElement, replacement);
  }
});

test("a focused filter disclosure survives an asynchronous replacement render", () => {
  const { document, element } = createDocument();
  const initialSummary = element("#type-filter-summary", {
    id: "type-filter-summary",
  });
  document.activeElement = initialSummary;
  const snapshot = captureMemberFocus(document);

  initialSummary.isConnected = false;
  document.activeElement = document.body;
  const replacementSummary = element("#type-filter-summary", {
    id: "type-filter-summary",
  });

  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementSummary);
});

test("stable annotated source segments retain focus across member completion renders", () => {
  const { document, element } = createDocument();
  const selector = "#annotated-source-modal-segment-42";
  const initialSegment = element(selector, {
    id: "annotated-source-modal-segment-42",
  });
  document.activeElement = initialSegment;
  const snapshot = captureMemberFocus(document);

  initialSegment.isConnected = false;
  const replacementSegment = element(selector, {
    id: "annotated-source-modal-segment-42",
  });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(snapshot.selector, selector);
  assert.equal(document.activeElement, replacementSegment);
});

test("navigation scroll is not copied into a different list scope", () => {
  const { document, element } = createDocument();
  element("#type-list", {
    id: "type-list",
    dataset: { navScope: "types" },
    scrollTop: 1200,
  });
  const snapshot = captureMemberFocus(document);
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
  const snapshot = captureMemberFocus(document);
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
    setSelectionRange() {},
  });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document);

  let restoredSelection = null;
  const replacementInput = element("#member-filter", {
    id: "member-filter",
    selectionStart: null,
    selectionEnd: null,
    selectionDirection: null,
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
    setSelectionRange() {},
  });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document);

  let restoredSelection = null;
  const replacementInput = element("#type-filter", {
    id: "type-filter",
    selectionStart: null,
    selectionEnd: null,
    selectionDirection: null,
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
  const initialButton = element("#share", { id: "share" });
  document.activeElement = initialButton;
  const snapshot = captureMemberFocus(document);

  const replacementButton = element("#share", { id: "share" });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementButton);
});

test("scope and lens controls survive a replacement render", () => {
  const cases: [string, Record<string, string>][] = [
    ["[data-scope=\"member\"]", { scope: "member" }],
    ["[data-lens=\"metadata\"]", { lens: "metadata" }],
    ["[data-member-section=\"facts\"]", { memberSection: "facts" }],
    ["[data-package-lens=\"analysis\"]", { packageLens: "analysis" }],
  ];

  for (const [selector, dataset] of cases) {
    const { document, element } = createDocument();
    const initialButton = element(selector, { dataset });
    document.activeElement = initialButton;
    const snapshot = captureMemberFocus(document);

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
  const cases: [string, Record<string, string>][] = [
    ["[data-nav-member=\"method:Build\"]", { navMember: "method:Build" }],
    ["[data-nav-overload=\"1\"]", { navOverload: "1" }],
  ];

  for (const [selector, dataset] of cases) {
    const { document, element } = createDocument();
    const initialButton = element(selector, { dataset });
    document.activeElement = initialButton;
    const snapshot = captureMemberFocus(document);

    const replacementButton = element(selector, { dataset });
    document.activeElement = document.body;
    restoreMemberFocus(document, snapshot, callback => {
      callback(0);
      return 1;
    });

    assert.equal(document.activeElement, replacementButton);
  }
});

test("taste controls survive source completion renders", () => {
  const { document, element } = createDocument();
  const selector = "[data-taste=\"prefer-var\"]";
  const initialCheckbox = element(selector, {
    dataset: { taste: "prefer-var" },
  });
  document.activeElement = initialCheckbox;
  const snapshot = captureMemberFocus(document);

  const replacementCheckbox = element(selector, {
    dataset: { taste: "prefer-var" },
  });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementCheckbox);
});

test("deferred restoration does not steal intentionally moved focus", () => {
  const { document, element } = createDocument();
  const initialInput = element("#member-filter", { id: "member-filter" });
  document.activeElement = initialInput;
  const snapshot = captureMemberFocus(document);
  const callbacks: FrameRequestCallback[] = [];
  restoreMemberFocus(document, snapshot, callback => {
    callbacks.push(callback);
    return callbacks.length;
  });

  const unrelated = element("#unrelated", { id: "unrelated" });
  unrelated.focus();
  callbacks.shift()!(0);

  assert.equal(document.activeElement, unrelated);
});

test("external focus authority invalidates a queued restoration", () => {
  const { document, element } = createDocument();
  const initial = element("#initial", { id: "initial" });
  const replacement = element("#type-list", { id: "type-list" });
  document.activeElement = initial;
  const snapshot = {
    ...captureMemberFocus(document),
    selector: "#type-list",
  };
  const callbacks: FrameRequestCallback[] = [];
  let current = true;
  const restorer = createMemberFocusRestorer();

  restorer.schedule(
    document,
    snapshot,
    callback => {
      callbacks.push(callback);
      return callbacks.length;
    },
    () => current);
  document.activeElement = document.body;
  current = false;
  callbacks.shift()!(0);

  assert.notEqual(document.activeElement, replacement);
  assert.equal(document.activeElement, document.body);
});

test("newer caret restoration invalidates older queued callbacks", () => {
  const { document, element } = createDocument();
  let restoredSelection: { start: number | null; end: number | null; direction: string | null } | null = null;
  const input = element("#member-filter", {
    id: "member-filter",
    selectionStart: 1,
    selectionEnd: 1,
    selectionDirection: "none",
    setSelectionRange(start, end, direction) {
      restoredSelection = { start, end, direction: direction ?? null };
    },
  });
  const callbacks: FrameRequestCallback[] = [];
  const requestFrame = (callback: FrameRequestCallback) => {
    callbacks.push(callback);
    return callbacks.length;
  };
  const restorer = createMemberFocusRestorer();

  document.activeElement = input;
  const older = captureMemberFocus(document);
  restorer.schedule(document, older, requestFrame);

  input.selectionStart = 5;
  input.selectionEnd = 5;
  const newer = captureMemberFocus(document);
  restorer.schedule(document, newer, requestFrame);

  document.activeElement = document.body;
  const completion = restorer.resolve(
    captureMemberFocus(document),
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
  const older = captureMemberFocus(document);
  const restorer = createMemberFocusRestorer();
  restorer.schedule(document, older, () => 1);

  document.activeElement = document.body;
  const current = restorer.resolve(
    captureMemberFocus(document),
    null,
  );

  assert.equal(current.selector, "");
  assert.equal(current.focusLost, true);
});

test("metadata member identities restore without becoming CSS selectors", () => {
  const { document, element } = createDocument();
  const identity = "method:Build\"quoted\\name\nnext";
  const initialButton = element("#initial-member", {
    dataset: { navMember: identity },
  });
  document.activeElement = initialButton;
  const snapshot = captureMemberFocus(document);

  assert.equal(snapshot.selector, "");
  assert.deepEqual(snapshot.dataTarget, {
    selector: "[data-nav-member]",
    key: "navMember",
    value: identity,
  });

  initialButton.isConnected = false;
  const replacementButton = element("#replacement-member", {
    dataset: { navMember: identity },
  });
  document.activeElement = document.body;
  restoreMemberFocus(document, snapshot, callback => {
    callback(0);
    return 1;
  });

  assert.equal(document.activeElement, replacementButton);
});
