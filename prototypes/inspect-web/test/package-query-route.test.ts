import assert from "node:assert/strict";
import test from "node:test";

import {
  isPackageQueryPath,
  validPackageQueryPrefix,
} from "../src/package-query-route.ts";

test("package query route recognizes only its canonical path", () => {
  assert.equal(isPackageQueryPath("/query"), true);
  assert.equal(isPackageQueryPath("/query/"), true);
  assert.equal(isPackageQueryPath("/packages/query"), false);
});

test("package query prefix validation trims useful input and rejects invalid shapes", () => {
  assert.equal(validPackageQueryPrefix(" Microsoft.Extensions. "), "Microsoft.Extensions.");
  assert.equal(validPackageQueryPrefix("Microsoft-*"), "Microsoft-*");
  assert.equal(validPackageQueryPrefix(""), "");
  assert.equal(validPackageQueryPrefix("contains space"), "contains space");
  assert.equal(validPackageQueryPrefix("../escape"), "../escape");
  assert.equal(validPackageQueryPrefix("a".repeat(101)), "");
});
