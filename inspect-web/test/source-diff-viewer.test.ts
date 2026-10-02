import assert from "node:assert/strict";
import test from "node:test";
import {
  bindSourceDiffViewer,
  type SourceDiffViewerMode,
} from "../src/source-diff-viewer.ts";
import type { BrowserSourceDiff } from "../src/source-diff-transport.ts";
import { fakeDom } from "./fake-dom.ts";

function diff(): BrowserSourceDiff {
  return {
    version: 1,
    before: {
      label: "Before",
      lines: ["a", "old", "c"],
      finalLineTerminator: "Present",
    },
    after: {
      label: "After",
      lines: ["a", "new", "c", "tail"],
      finalLineTerminator: "Absent",
    },
    relations: [],
    statistics: {
      added: 2,
      removed: 1,
      changedBefore: 1,
      changedAfter: 1,
      movedBefore: 0,
      movedAfter: 0,
    },
    changes: [
      {
        before: { start: 1, count: 1 },
        after: { start: 1, count: 1 },
        innerMappings: [],
        annotations: [],
      },
      {
        before: { start: 3, count: 0 },
        after: { start: 3, count: 1 },
        innerMappings: [],
        annotations: [],
      },
    ],
  };
}

interface InteractiveElement {
  readonly element: HTMLElement & { disabled: boolean };
  readonly attributes: Map<string, string>;
  trigger(type: string, event?: Event): void;
}

function interactiveElement(
  values: Record<string, unknown> = {},
): InteractiveElement {
  const handlers = new Map<string, EventListener[]>();
  const attributes = new Map<string, string>();
  // The test fake provides the button state read by the viewer binder.
  /* oxlint-disable typescript/no-unsafe-type-assertion */
  const element = fakeDom.htmlElement({
    dataset: {},
    textContent: "",
    disabled: false,
    offsetParent: {},
    tagName: "BUTTON",
    isContentEditable: false,
    addEventListener: (
      type: string,
      listener: EventListenerOrEventListenerObject,
    ) => {
      if (typeof listener !== "function") return;
      const existing = handlers.get(type);
      if (existing === undefined) handlers.set(type, [listener]);
      else existing.push(listener);
    },
    setAttribute: (name: string, value: string) => {
      attributes.set(name, value);
    },
    ...values,
  }) as HTMLElement & { disabled: boolean };
  /* oxlint-enable typescript/no-unsafe-type-assertion */
  return {
    element,
    attributes,
    trigger(type, event = fakeDom.event()) {
      for (const handler of handlers.get(type) ?? [])
        handler(event);
    },
  };
}

test("viewer binding retains mode, navigates changes, and copies exact sides",
  async () => {
    const previous = interactiveElement();
    const next = interactiveElement();
    const position = interactiveElement({ tagName: "SPAN" });
    const announcement = interactiveElement({ tagName: "P" });
    const unified = interactiveElement({
      dataset: { sourceDiffMode: "unified" },
    });
    const split = interactiveElement({
      dataset: { sourceDiffMode: "side-by-side" },
    });
    const copyBefore = interactiveElement({
      dataset: { sourceDiffCopy: "before" },
    });
    const copyAfter = interactiveElement({
      dataset: { sourceDiffCopy: "after" },
    });
    let focusedChange = -1;
    let scrolledChange = -1;
    const rows = [0, 1].map(change => interactiveElement({
      tagName: "DIV",
      focus: () => {
        focusedChange = change;
      },
      scrollIntoView: () => {
        scrolledChange = change;
      },
    }));
    const viewer = interactiveElement({
      tagName: "SECTION",
      dataset: { mode: "unified" },
      querySelector: (selector: string) => {
        if (selector === "[data-source-diff-position]")
          return position.element;
        if (selector === "[data-source-diff-previous]")
          return previous.element;
        if (selector === "[data-source-diff-next]")
          return next.element;
        if (selector === "[data-source-diff-announcement]")
          return announcement.element;
        return null;
      },
      querySelectorAll: (selector: string) => {
        if (selector === "[data-source-diff-mode]")
          return [unified.element, split.element];
        if (selector === "[data-source-diff-copy]")
          return [copyBefore.element, copyAfter.element];
        const match = /data-source-diff-change="(\d+)"/.exec(selector);
        return match === null ? [] : [rows[Number(match[1])]!.element];
      },
    });
    const root = fakeDom.parentNode({
      querySelector: (selector: string) =>
        selector === "[data-source-diff-viewer]"
          ? viewer.element
          : null,
    });
    const modes: SourceDiffViewerMode[] = [];
    const copied: string[] = [];

    bindSourceDiffViewer(root, diff(), {
      mode: "side-by-side",
      onModeChanged: mode => modes.push(mode),
      writeClipboardText: value => {
        copied.push(value);
        return Promise.resolve();
      },
    });

    assert.equal(viewer.element.dataset.mode, "side-by-side");
    assert.equal(position.element.textContent, "1 of 2");
    assert.equal(previous.element.disabled, true);
    assert.equal(next.element.disabled, false);

    next.trigger("click");
    assert.equal(position.element.textContent, "2 of 2");
    assert.equal(previous.element.disabled, false);
    assert.equal(next.element.disabled, true);
    assert.equal(focusedChange, 1);
    assert.equal(scrolledChange, 1);
    assert.match(
      announcement.element.textContent ?? "",
      /no Before lines; After lines 4 through 4/,
    );

    viewer.trigger("keydown", fakeDom.keyboardEvent({
      key: "p",
      target: viewer.element,
      preventDefault: () => undefined,
    }));
    assert.equal(position.element.textContent, "1 of 2");

    unified.trigger("click");
    assert.equal(viewer.element.dataset.mode, "unified");
    assert.deepEqual(modes, ["unified"]);
    assert.equal(unified.attributes.get("aria-pressed"), "true");
    assert.equal(split.attributes.get("aria-pressed"), "false");

    copyBefore.trigger("click");
    copyAfter.trigger("click");
    await Promise.resolve();
    assert.deepEqual(copied, ["a\nold\nc\n", "a\nnew\nc\ntail"]);
    assert.equal(
      announcement.element.textContent,
      "After source copied.",
    );
  });
