import assert from "node:assert/strict";
import test from "node:test";
import {
  adjacentSlideTarget,
  resolveSlideStrip,
  slideStripMinimumWidth,
  type SlideStripItem,
  type SlideStripItemMeasurement,
  type SlideStripMode,
  type SlideStripPolicy,
} from "../src/slide-strip.ts";

const items = [
  { id: "overview", label: "Overview", shortLabel: "OV", icon: "◫" },
  { id: "call-graph", label: "Call graph", shortLabel: "CG", icon: "⑂" },
  { id: "facts", label: "Facts", shortLabel: "FX", icon: "·" },
  { id: "source", label: "Source", shortLabel: "SRC", icon: "⌘" },
] as const satisfies readonly SlideStripItem[];

const modes = [
  { kind: "label", minimumVisible: 2, gap: 2 },
  { kind: "short-label", minimumVisible: 2, gap: 2 },
  { kind: "icon", minimumVisible: 2, gap: 2 },
  { kind: "index", minimumVisible: 2, gap: 2 },
] as const satisfies readonly SlideStripMode[];

const policy: SlideStripPolicy = {
  modes,
  initialAnchor: "overview",
  preferredDirection: "after",
  continuityKey: "inspectors-v1",
  fallbackVisibilityFloor: 24,
  oversizedAlignment: "start",
};

const labelPolicy: SlideStripPolicy = {
  ...policy,
  modes: [{ kind: "label", minimumVisible: 1, gap: 2 }],
};

function measured(
  widths: readonly [
    number,
    number,
    number,
    number,
  ],
): readonly SlideStripItemMeasurement[] {
  return items.map((item, index) => {
    const label = widths[index];
    if (label === undefined) {
      throw new Error(`Missing test width at index ${index}.`);
    }
    return {
      id: item.id,
      widths: {
        label,
        "short-label": 30,
        icon: 22,
        index: 24,
      },
    };
  });
}

test("empty inventory has one empty state", () => {
  assert.equal(resolveSlideStrip({
    items: [],
    measurements: [],
    policy: { ...policy, initialAnchor: "" },
    viewportWidth: 100,
  }), null);
});

test("a visible window uses one representation and remains contiguous", () => {
  const result = resolveSlideStrip({
    items,
    measurements: measured([80, 90, 60, 70]),
    policy,
    viewportWidth: 174,
  });

  assert.equal(result?.mode, "label");
  assert.deepEqual(result?.visibleIds, ["overview", "call-graph"]);
  assert.equal(result?.leadingHidden, false);
  assert.equal(result?.trailingHidden, true);
});

test("optional mode is skipped when one item omits its representation", () => {
  const incomplete = items.map(item => item.id === "facts"
    ? { id: item.id, label: item.label, icon: item.icon }
    : item);
  const result = resolveSlideStrip({
    items: incomplete,
    measurements: measured([90, 90, 90, 90]),
    policy,
    viewportWidth: 62,
  });

  assert.equal(result?.mode, "icon");
  assert.deepEqual(result?.visibleIds, ["overview", "call-graph"]);
});

test("compact mode requires a density benefit over failed Label", () => {
  const noBenefit = measured([50, 50, 50, 50]).map(item => ({
    ...item,
    widths: {
      label: 50,
      "short-label": 50,
      icon: 50,
      index: 50,
    },
  }));
  const result = resolveSlideStrip({
    items,
    measurements: noBenefit,
    policy,
    viewportWidth: 50,
  });

  assert.equal(result?.mode, "label");
  assert.equal(result?.visibleCount, 1);
});

test("non-monotonic requested counts select the first qualifying mode", () => {
  const nonMonotonicPolicy: SlideStripPolicy = {
    ...policy,
    modes: [
      { kind: "label", minimumVisible: 2, gap: 2 },
      { kind: "short-label", minimumVisible: 4, gap: 2 },
      { kind: "index", minimumVisible: 2, gap: 2 },
    ],
  };
  const result = resolveSlideStrip({
    items,
    measurements: measured([80, 80, 80, 80]),
    policy: nonMonotonicPolicy,
    viewportWidth: 76,
  });

  assert.equal(result?.mode, "index");
  assert.equal(result?.visibleCount, 3);
});

test("retained leading identity wins equal-count placement", () => {
  const result = resolveSlideStrip({
    items,
    measurements: measured([50, 50, 50, 50]),
    policy,
    viewportWidth: 102,
    retainedLeadingId: "call-graph",
  });

  assert.deepEqual(result?.visibleIds, ["call-graph", "facts"]);
  assert.equal(result?.leadingHidden, true);
  assert.equal(result?.trailingHidden, true);
});

test("directional slide pins the adjacent hidden item and retains focus", () => {
  const measurements = measured([40, 40, 40, 40]);
  const current = resolveSlideStrip({
    items,
    measurements,
    policy: labelPolicy,
    viewportWidth: 82,
  });
  assert.ok(current);
  const retainedLeadingId = current.visibleIds[0];
  assert.ok(retainedLeadingId);
  const target = adjacentSlideTarget(items, current, "after");
  assert.deepEqual(target, { id: "facts", edge: "after" });

  const result = resolveSlideStrip({
    items,
    measurements,
    policy: labelPolicy,
    viewportWidth: 82,
    retainedLeadingId,
    focusedId: "call-graph",
    ...(target ? { windowTarget: target } : {}),
  });

  assert.deepEqual(result?.visibleIds, ["call-graph", "facts"]);
  assert.equal(result?.pendingFocusId, undefined);
});

test("directional slide transfers in-strip focus when it cannot coexist", () => {
  const measurements = measured([40, 80, 40, 40]);
  const current = resolveSlideStrip({
    items,
    measurements,
    policy: labelPolicy,
    viewportWidth: 82,
  });
  assert.ok(current);
  const retainedLeadingId = current.visibleIds[0];
  assert.ok(retainedLeadingId);
  const target = adjacentSlideTarget(items, current, "after");
  assert.ok(target);

  const result = resolveSlideStrip({
    items,
    measurements,
    policy: labelPolicy,
    viewportWidth: 40,
    retainedLeadingId,
    focusedId: "overview",
    windowTarget: target,
  });

  assert.deepEqual(result?.visibleIds, ["call-graph"]);
  assert.equal(result?.pendingFocusId, "call-graph");
});

test("external-focus slide installs an oversized adjacent singleton", () => {
  const twoItems = items.slice(0, 2);
  const measurements = measured([40, 200, 40, 40]).slice(0, 2);
  const current = resolveSlideStrip({
    items: twoItems,
    measurements,
    policy: { ...labelPolicy, initialAnchor: "overview" },
    viewportWidth: 100,
  });
  const target = adjacentSlideTarget(twoItems, current, "after");
  assert.deepEqual(target, { id: "call-graph", edge: "after" });

  const result = resolveSlideStrip({
    items: twoItems,
    measurements,
    policy: { ...labelPolicy, initialAnchor: "overview" },
    viewportWidth: 100,
    retainedLeadingId: "overview",
    ...(target ? { windowTarget: target } : {}),
  });

  assert.deepEqual(result?.visibleIds, ["call-graph"]);
  assert.equal(result?.fallback, true);
  assert.equal(result?.requiredWidth, 200);
  assert.equal(result?.pendingFocusId, undefined);
});

test("focus navigation outranks retained placement", () => {
  const result = resolveSlideStrip({
    items,
    measurements: measured([50, 50, 50, 50]),
    policy,
    viewportWidth: 102,
    retainedLeadingId: "overview",
    pendingFocusId: "source",
  });

  assert.deepEqual(result?.visibleIds, ["facts", "source"]);
  assert.equal(result?.pendingFocusId, "source");
});

test("focus navigation may replace the previously focused window", () => {
  const result = resolveSlideStrip({
    items,
    measurements: measured([50, 50, 50, 50]),
    policy,
    viewportWidth: 102,
    retainedLeadingId: "overview",
    focusedId: "overview",
    pendingFocusId: "source",
  });

  assert.deepEqual(result?.visibleIds, ["facts", "source"]);
  assert.equal(result?.fallback, false);
  assert.equal(result?.pendingFocusId, "source");
});

test("minimum width chooses a viable policy minimum", () => {
  assert.equal(
    slideStripMinimumWidth(
      items,
      measured([80, 90, 60, 70]),
      policy),
    46);
});

test("minimum width is resolved around the effective required identity", () => {
  assert.equal(
    slideStripMinimumWidth(
      items,
      measured([20, 20, 100, 20]),
      labelPolicy,
      { focusedId: "facts" }),
    100);
  assert.equal(
    slideStripMinimumWidth(
      items,
      measured([100, 20, 20, 20]),
      {
        ...labelPolicy,
        modes: [{ kind: "label", minimumVisible: 2, gap: 2 }],
      },
      { retainedLeadingId: "overview" }),
    122);
});

test("invalid policies and inventories fail visibly", () => {
  assert.throws(() => resolveSlideStrip({
    items,
    measurements: measured([50, 50, 50, 50]),
    policy: { ...policy, modes: modes.slice(1) },
    viewportWidth: 100,
  }), /begin with exactly one Label/);
  assert.throws(() => resolveSlideStrip({
    items,
    measurements: measured([50, 50, 50, 50]),
    policy: { ...policy, initialAnchor: "missing" },
    viewportWidth: 100,
  }), /initial anchor/);
  assert.throws(() => resolveSlideStrip({
    items: [{ id: "empty", label: "" }],
    measurements: [{ id: "empty", widths: { label: 20 } }],
    policy: { ...policy, initialAnchor: "empty" },
    viewportWidth: 100,
  }), /requires a Label/);
});
