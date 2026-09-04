import assert from "node:assert/strict";
import test from "node:test";
import {
  bindContentFrame,
  contentFrameFocusOwnerFor,
  decideContentFrameResize,
  focusContentNavigation,
  focusContentNavigationToggle,
  renderContentNavigationBar,
  renderContentNavigationCloseButton,
} from "../src/content-frame.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  focused = false;
  scrolled = false;
  private listener: (() => void) | null = null;

  addEventListener(_type: string, listener: () => void) {
    this.listener = listener;
  }

  click() {
    this.listener?.();
  }

  focus() {
    this.focused = true;
  }

  scrollIntoView() {
    this.scrolled = true;
  }
}

test("the content navigation bar exposes one local pane switch", () => {
  const html = renderContentNavigationBar("Members");

  assert.match(html, /id="content-navigation-toggle"/);
  assert.match(html, /aria-controls="content-navigation-pane"/);
  assert.match(html, /<span>Members<\/span>/);
  assert.doesNotMatch(html, /dialog|modal/);
});

test("the inventory exposes a return path when it has no rows", () => {
  const html = renderContentNavigationCloseButton();

  assert.match(html, /id="content-navigation-close"/);
  assert.match(html, /aria-label="Show details"/);
});

test("content frame binding and focus target the visible pane", () => {
  const toggle = new FakeElement();
  const close = new FakeElement();
  const list = new FakeElement();
  const selected = new FakeElement();
  const root = {
    querySelector(selector: string) {
      if (selector === "#content-navigation-toggle") return toggle;
      if (selector === "#content-navigation-close") return close;
      if (selector === "#type-list") return list;
      if (selector === "#type-list .selected") return selected;
      return null;
    },
  };
  let opened = 0;
  let closed = 0;

  const parent = fakeDom.parentNode(root);
  bindContentFrame(parent, {
    onShowDetail: () => closed++,
    onShowNavigation: () => opened++,
  });
  toggle.click();
  close.click();
  focusContentNavigation(parent);
  focusContentNavigationToggle(parent);

  assert.equal(opened, 1);
  assert.equal(closed, 1);
  assert.equal(list.focused, true);
  assert.equal(selected.scrolled, true);
  assert.equal(toggle.focused, true);
});

test("content frame resize follows focus and replaces a removed toggle", () => {
  assert.deepEqual(
    decideContentFrameResize("detail", true, "navigation"),
    { pane: "navigation", render: true, focus: "navigation" });
  assert.deepEqual(
    decideContentFrameResize("navigation", true, "detail"),
    { pane: "detail", render: true, focus: "navigation-toggle" });
  assert.deepEqual(
    decideContentFrameResize("detail", true, null),
    { pane: "detail", render: false, focus: null });
  assert.deepEqual(
    decideContentFrameResize("detail", false, "navigation-toggle"),
    { pane: "detail", render: false, focus: "navigation" });
  assert.deepEqual(
    decideContentFrameResize("navigation", false, "detail-toggle"),
    { pane: "navigation", render: false, focus: "navigation" });
});

test("content frame focus ownership follows the active pane or local switch", () => {
  const element = (
    id: string,
    closest: (selector: string) => Element | null,
  ) => fakeDom.element({ id, closest });

  assert.equal(
    contentFrameFocusOwnerFor(element(
      "content-navigation-toggle",
      () => null)),
    "navigation-toggle");
  assert.equal(
    contentFrameFocusOwnerFor(element(
      "",
      selector => selector === "#content-navigation-pane"
        ? fakeDom.element({})
        : null)),
    "navigation");
  assert.equal(
    contentFrameFocusOwnerFor(element(
      "",
      selector => selector === ".detail-pane" ? fakeDom.element({}) : null)),
    "detail");
  assert.equal(contentFrameFocusOwnerFor(element("", () => null)), null);
});
