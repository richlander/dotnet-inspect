import assert from "node:assert/strict";
import test from "node:test";

import {
  capturePackageQueryViewport,
  PACKAGE_QUERY_DEFAULT_ROW_EXTENT_PX,
  PACKAGE_QUERY_RENDERED_ROW_LIMIT,
  resolvePackageQueryRowWindow,
  restorePackageQueryViewport,
  type PackageQueryViewportSnapshot,
} from "../src/package-query-window.ts";

function rect(
  top: number,
  height: number,
): DOMRect {
  return {
    x: 0,
    y: top,
    top,
    right: 100,
    bottom: top + height,
    left: 0,
    width: 100,
    height,
    toJSON: () => ({}),
  };
}

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  scrollTop = 0;
  clientHeight = 0;
  private bounds: DOMRect;
  private readonly elements = new Map<string, FakeElement[]>();

  constructor(
    bounds: DOMRect,
    dataset: Record<string, string | undefined> = {},
  ) {
    this.bounds = bounds;
    this.dataset = dataset;
  }

  setBounds(bounds: DOMRect) {
    this.bounds = bounds;
  }

  add(selector: string, ...elements: FakeElement[]) {
    this.elements.set(selector, elements);
  }

  getBoundingClientRect() {
    return this.bounds;
  }

  // Test fake implements the selector subset consumed by the viewport helper.
  // oxlint-disable-next-line typescript/no-unnecessary-type-parameters
  querySelector<T extends Element>(selector: string): T | null {
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return (this.elements.get(selector)?.[0] ?? null) as T | null;
  }

  querySelectorAll<T extends Element>(selector: string): NodeListOf<T> {
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return (this.elements.get(selector) ?? []) as unknown as NodeListOf<T>;
  }
}

class FakeRoot extends FakeElement {
  constructor() {
    super(rect(0, 0));
  }
}

test("row-window resolution caps mounted rows and keeps five-row overscan", () => {
  assert.deepEqual(resolvePackageQueryRowWindow(20), {
    start: 0,
    end: 20,
    beforeHeight: 0,
    afterHeight: 0,
    rowExtent: PACKAGE_QUERY_DEFAULT_ROW_EXTENT_PX,
  });

  const viewport: PackageQueryViewportSnapshot = {
    scrollTop: 50 * 180,
    clientHeight: 800,
    surfaceTop: 0,
    rowExtent: 180,
    anchorRowIndex: null,
    anchorOffsetTop: null,
  };
  const middle = resolvePackageQueryRowWindow(100, viewport);
  assert.equal(middle.start, 45);
  assert.equal(middle.end, 75);
  assert.equal(middle.end - middle.start, PACKAGE_QUERY_RENDERED_ROW_LIMIT);
  assert.equal(middle.beforeHeight, 45 * 180);
  assert.equal(middle.afterHeight, 25 * 180);

  const anchored = resolvePackageQueryRowWindow(100, {
    ...viewport,
    scrollTop: 0,
    anchorRowIndex: 98,
  });
  assert.equal(anchored.start, 70);
  assert.equal(anchored.end, 100);
});

test("viewport capture measures mounted rows and records the first visible anchor", () => {
  const root = new FakeRoot();
  const main = new FakeElement(rect(100, 500));
  main.scrollTop = 1_000;
  main.clientHeight = 500;
  const surface = new FakeElement(
    rect(300, 2_000),
    { queryRowExtent: "180" });
  const rows = [
    new FakeElement(rect(300, 150), { queryRowIndex: "10" }),
    new FakeElement(rect(490, 150), { queryRowIndex: "11" }),
    new FakeElement(rect(680, 150), { queryRowIndex: "12" }),
  ];
  surface.add("[data-query-row-index]", ...rows);
  root.add(".query-main", main);
  root.add("#package-query-row-window", surface);

  // Test fake implements the ParentNode subset consumed by the helper.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  const captured = capturePackageQueryViewport(root as unknown as ParentNode);

  assert.deepEqual(captured, {
    scrollTop: 1_000,
    clientHeight: 500,
    surfaceTop: 1_200,
    rowExtent: 190,
    anchorRowIndex: 10,
    anchorOffsetTop: 200,
  });
});

test("viewport restoration keeps the anchored row at the same visual offset", () => {
  const root = new FakeRoot();
  const main = new FakeElement(rect(100, 500));
  const anchor = new FakeElement(rect(260, 150), { queryRowIndex: "11" });
  root.add(".query-main", main);
  root.add('[data-query-row-index="11"]', anchor);
  const snapshot: PackageQueryViewportSnapshot = {
    scrollTop: 900,
    clientHeight: 500,
    surfaceTop: 300,
    rowExtent: 180,
    anchorRowIndex: 11,
    anchorOffsetTop: 100,
  };

  // Test fake implements the ParentNode subset consumed by the helper.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  restorePackageQueryViewport(root as unknown as ParentNode, snapshot);

  assert.equal(main.scrollTop, 60);
});
