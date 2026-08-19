import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const packageLock = JSON.parse(
  readFileSync(new URL("../package-lock.json", import.meta.url), "utf8"),
);

test("the package lock pins every registry artifact", () => {
  const missingArtifactIdentity = Object.entries(packageLock.packages)
    .filter(([path, entry]) =>
      path
      && !entry.link
      && (typeof entry.resolved !== "string" || typeof entry.integrity !== "string"))
    .map(([path]) => path);

  assert.deepEqual(missingArtifactIdentity, []);
});
