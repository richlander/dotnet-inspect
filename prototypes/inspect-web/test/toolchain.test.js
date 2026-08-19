import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { verifySiteArtifact } from "../scripts/verify-site-artifact.js";

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

test("the site artifact rejects a missing Vite output", (context) => {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-artifact-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  mkdirSync(join(site, "assets"));
  const manifest = {
    "index.html": {
      file: "assets/index.js",
      css: ["assets/index.css"],
      dynamicImports: ["src/app.js"],
      isEntry: true,
    },
    "src/app.js": {
      file: "assets/app.js",
      isDynamicEntry: true,
    },
  };
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  writeFileSync(
    join(site, "index.html"),
    '<script type="module" src="/assets/index.js"></script>'
      + '<link rel="stylesheet" href="/assets/index.css">',
  );
  writeFileSync(join(site, "assets/index.js"), "");
  writeFileSync(join(site, "assets/index.css"), "");
  writeFileSync(join(site, "assets/app.js"), "");

  assert.doesNotThrow(() => verifySiteArtifact(site));
  delete manifest["src/app.js"];
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  assert.throws(
    () => verifySiteArtifact(site),
    /entry 'index\.html' imports missing entry 'src\/app\.js'/,
  );

  manifest["src/app.js"] = {
    file: "assets/app.js",
    isDynamicEntry: true,
  };
  manifest["index.html"].file = "assets/../index.html";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest contains invalid asset 'assets\/\.\.\/index\.html'/,
  );

  manifest["index.html"].file = "assets/index.js";
  writeFileSync(join(site, "manifest.json"), JSON.stringify(manifest));
  unlinkSync(join(site, "assets/index.js"));
  assert.throws(
    () => verifySiteArtifact(site),
    /manifest references missing asset 'assets\/index\.js'/,
  );
});
