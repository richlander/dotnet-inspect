import assert from "node:assert/strict";
import test from "node:test";

import {
  isSelectedGroupChip,
  parseMetadataToken,
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

test("metadata-token parsing accepts canonical decimal tokens within UInt32", () => {
  assert.equal(parseMetadataToken("100663297"), 0x06000001);
  // Hexadecimal has no producer and is rejected: the only emitter interpolates a number.
  assert.equal(parseMetadataToken("0x06000001"), null);
  assert.equal(parseMetadataToken("0x1"), null);
  assert.equal(parseMetadataToken("0XFFFFFFFF"), null);
  assert.equal(parseMetadataToken("4294967295"), 0xffff_ffff);
});

test("metadata-token parsing rejects malformed and out-of-range values", () => {
  for (const value of [
    undefined,
    "",
    "-1",
    "00",
    "0100663297",
    "1.5",
    "0x",
    "0x100000000",
    "9007199254740992",
  ]) {
    assert.equal(parseMetadataToken(value), null, String(value));
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
