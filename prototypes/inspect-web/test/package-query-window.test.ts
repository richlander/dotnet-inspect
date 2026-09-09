import assert from "node:assert/strict";
import test from "node:test";

import {
  calculatePackageQueryResultWindow,
  createPackageQueryWindowState,
  packageQueryResultWindowMatches,
  PACKAGE_QUERY_MAX_RENDERED_ROWS,
} from "../src/package-query-window.ts";
import { fakeDom } from "./fake-dom.ts";

test("small retained outcomes render without spacers", () => {
  const state = createPackageQueryWindowState();

  assert.deepEqual(
    calculatePackageQueryResultWindow(20, { start: 0, height: 800 }, state),
    {
      start: 0,
      end: 20,
      topSpacerHeight: 0,
      bottomSpacerHeight: 0,
    });
});

test("large outcomes render a measured visible range plus overscan", () => {
  const state = createPackageQueryWindowState();
  state.estimatedRowHeight = 100;

  const window = calculatePackageQueryResultWindow(
    100,
    { start: 5500, height: 800 },
    state);

  assert.deepEqual(window, {
    start: 44,
    end: 63,
    topSpacerHeight: 4840,
    bottomSpacerHeight: 4060,
  });
  assert.ok(window.end - window.start <= PACKAGE_QUERY_MAX_RENDERED_ROWS);
});

test("the final viewport keeps a bounded tail and no bottom spacer", () => {
  const state = createPackageQueryWindowState();
  state.estimatedRowHeight = 100;

  const window = calculatePackageQueryResultWindow(
    100,
    { start: 10200, height: 800 },
    state);

  assert.deepEqual(window, {
    start: 70,
    end: 100,
    topSpacerHeight: 7700,
    bottomSpacerHeight: 0,
  });
});

test("a logical row anchor survives changed unseen-row estimates", () => {
  const state = createPackageQueryWindowState();
  state.estimatedRowHeight = 230;

  const window = calculatePackageQueryResultWindow(
    100,
    { start: 5500, height: 800 },
    state,
    { rowIndex: 50, viewportOffset: 200 });

  assert.ok(window.start <= 50);
  assert.ok(window.end > 50);
  const anchorTop = 50 * 240;
  const anchoredViewportStart = anchorTop - 200;
  assert.equal(
    window.topSpacerHeight - anchoredViewportStart,
    (window.start * 240) - anchoredViewportStart);
  assert.ok(window.end - window.start <= PACKAGE_QUERY_MAX_RENDERED_ROWS);
});

test("a rendered window matches only the same range and rounded spacers", () => {
  const list = {
    dataset: {
      queryWindowStart: "40",
      queryWindowEnd: "50",
      queryWindowTop: "7200",
      queryWindowBottom: "9000",
    },
  };
  const root = fakeDom.parentNode({
    querySelector(selector: string) {
      return selector === ".query-list" ? list : null;
    },
  });

  assert.equal(packageQueryResultWindowMatches(root, {
    start: 40,
    end: 50,
    topSpacerHeight: 7200.2,
    bottomSpacerHeight: 8999.8,
  }), true);
  assert.equal(packageQueryResultWindowMatches(root, {
    start: 41,
    end: 51,
    topSpacerHeight: 7380,
    bottomSpacerHeight: 8820,
  }), false);
});
