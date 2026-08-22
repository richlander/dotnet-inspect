import assert from "node:assert/strict";
import test from "node:test";

import {
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
    "1.5",
    "1e2",
    " 1",
    "9007199254740992",
  ]) {
    assert.equal(parseNonNegativeInteger(value), null, String(value));
  }
});

test("metadata-token parsing accepts decimal and hexadecimal tokens", () => {
  assert.equal(parseMetadataToken("100663297"), 0x06000001);
  assert.equal(parseMetadataToken("0x06000001"), 0x06000001);
  assert.equal(parseMetadataToken("0XFFFFFFFF"), 0xffff_ffff);
});

test("metadata-token parsing rejects malformed and out-of-range values", () => {
  for (const value of [
    undefined,
    "",
    "-1",
    "1.5",
    "0x",
    "0x100000000",
    "9007199254740992",
  ]) {
    assert.equal(parseMetadataToken(value), null, String(value));
  }
});
