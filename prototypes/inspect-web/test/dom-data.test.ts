import assert from "node:assert/strict";
import test from "node:test";

import {
  isSelectedGroupChip,
  parseExplorerCoordinates,
  parseNonNegativeInteger,
} from "../src/dom-data.ts";

test("DOM integer parsing accepts canonical non-negative safe integers", () => {
  assert.equal(parseNonNegativeInteger("0"), 0);
  assert.equal(parseNonNegativeInteger("42"), 42);
  assert.equal(
    parseNonNegativeInteger(String(Number.MAX_SAFE_INTEGER)),
    Number.MAX_SAFE_INTEGER);
});

test("DOM integer parsing rejects missing and malformed values", () => {
  for (const value of [
    undefined,
    "",
    "-1",
    // The regex is what rejects this one. `Number("-0")` is `-0`, which satisfies both
    // `>= 0` and `Number.isSafeInteger`, so a parser simplified to numeric checks alone
    // would admit it and hand back a negative zero. Pinned because that simplification
    // looks harmless and nothing else here would catch it.
    "-0",
    "00",
    "01",
    "1.5",
    "1e2",
    " 1",
    "9007199254740992",
  ]) {
    assert.equal(parseNonNegativeInteger(value), null, String(value));
  }
});

test("a dependency chip is active only when its payload names the selected group", () => {
  assert.equal(isSelectedGroupChip("2", 2), true);
  assert.equal(isSelectedGroupChip("2", 1), false);
  assert.equal(isSelectedGroupChip("0", 0), true);

  // The hazard: the parser rejects with `null`, and `null` is also "no group is selected".
  // A direct `parse(...) === selected` comparison marks every malformed chip active here.
  for (const malformed of [undefined, "", "x", "-1", "01", "1e0", " 1", "+1"]) {
    assert.equal(
      isSelectedGroupChip(malformed, null),
      false,
      `${JSON.stringify(malformed)} must not be active when no group is selected`);
    assert.equal(isSelectedGroupChip(malformed, 0), false);
  }

  // A valid payload with nothing selected is still not active.
  assert.equal(isSelectedGroupChip("0", null), false);
});

test("metadata explorer coordinates require two canonical integers", () => {
  assert.deepEqual(parseExplorerCoordinates("2:42"), [2, 42]);
  for (const value of [
    undefined,
    "",
    "2",
    "2:42:3",
    "-0:42",
    "02:42",
    "2:4.2",
  ]) {
    assert.equal(parseExplorerCoordinates(value), null, String(value));
  }
});
