import assert from "node:assert/strict";
import test from "node:test";
import {
  MAX_ITEM_ACHIEVEMENTS,
  renderItemAchievementRail,
} from "../src/item-achievements.ts";

const escapeHtml = (value: unknown) => String(value)
  .replaceAll("&", "&amp;")
  .replaceAll('"', "&quot;")
  .replaceAll("<", "&lt;")
  .replaceAll(">", "&gt;");

test("item achievement rail preserves zero to three ordered glyph slots", () => {
  const empty = renderItemAchievementRail([], escapeHtml);
  assert.match(empty, /aria-hidden="true"/);
  assert.doesNotMatch(empty, /item-achievement-glyph/);

  const full = renderItemAchievementRail([
    {
      kind: "surface-mountain-peak",
      description: "surface mountain peak Type",
    },
    {
      kind: "implementation-sea-level",
      description: "implementation sea level Type",
    },
  ], escapeHtml);
  assert.ok(
    full.indexOf("surface-mountain-peak")
      < full.indexOf("implementation-sea-level"),
    "caller-issued achievement order must be preserved",
  );
  assert.match(
    full,
    /aria-label="surface mountain peak Type; implementation sea level Type"/,
  );

  const member = renderItemAchievementRail([
    { kind: "top-leverage", description: "Top Leverage" },
    { kind: "api-diff", description: "API differences" },
    { kind: "implementation-hub", description: "implementation hub" },
  ], escapeHtml);
  assert.equal(MAX_ITEM_ACHIEVEMENTS, 3);
  assert.ok(
    member.indexOf("top-leverage") < member.indexOf("api-diff")
      && member.indexOf("api-diff") < member.indexOf("implementation-hub"),
    "caller-issued member achievement order must be preserved",
  );
  assert.match(
    member,
    /item-achievement-glyph top-leverage/,
  );
  assert.match(
    member,
    /item-achievement-glyph api-diff/,
  );
  assert.match(
    member,
    /item-achievement-glyph implementation-hub/,
  );
  assert.match(
    member,
    /aria-label="Top Leverage; API differences; implementation hub"/,
  );
});

test("item achievement rail rejects overflow and duplicate slots", () => {
  assert.throws(
    () => renderItemAchievementRail([
      { kind: "surface-sea-level", description: "first" },
      { kind: "implementation-mountain-peak", description: "second" },
      { kind: "top-leverage", description: "third" },
      { kind: "api-diff", description: "fourth" },
    ], escapeHtml),
    /at most 3 glyphs/,
  );
  assert.throws(
    () => renderItemAchievementRail([
      { kind: "surface-sea-level", description: "first" },
      { kind: "surface-sea-level", description: "second" },
    ], escapeHtml),
    /distinct glyph kinds/,
  );
});
