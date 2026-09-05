import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import type { TestContext } from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { publishRuntimeLoader } from "../scripts/publish-runtime-loader.ts";

const publishScript = fileURLToPath(
  new URL("../scripts/publish-runtime-loader.ts", import.meta.url),
);
const smokeScript = fileURLToPath(
  new URL("../scripts/verify-published-engine-facades.ts", import.meta.url),
);
const sourceRoot = fileURLToPath(new URL("../engine/wwwroot/", import.meta.url));
const target = "./_framework/dotnet.fingerprint.js";
const sdkSource = 'export const dotnet = { identity: "SDK runtime" };\n';

function createSite(context: TestContext): string {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-runtime-loader-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  mkdirSync(join(site, "_framework"));
  writeFileSync(join(site, "package.json"), '{"type":"module"}\n');
  writeFileSync(join(site, target), sdkSource);
  writeFileSync(
    join(site, "index.html"),
    `<script type="importmap">${JSON.stringify({
      imports: { "./_framework/dotnet.js": target },
    }, null, 2)}</script>`,
  );
  return site;
}

test("publication emits the exact SDK runtime target without an import map", async (context) => {
  const site = createSite(context);
  const result = spawnSync(process.execPath, [publishScript, site], {
    encoding: "utf8",
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(
    readFileSync(join(site, "runtime-loader.js"), "utf8"),
    `export { dotnet } from "${target}";\n`,
  );
  const loader: unknown = await import(
    pathToFileURL(join(site, "runtime-loader.js")).href,
  );
  const sdk: unknown = await import(pathToFileURL(join(site, target)).href);
  assert.deepEqual(loader, sdk);
  assert.equal(readFileSync(join(site, target), "utf8"), sdkSource);
  assert.equal(existsSync(join(site, "_framework/dotnet.js")), false);
});

test("publication replaces the loader with a newly selected fingerprint", (context) => {
  const site = createSite(context);
  publishRuntimeLoader(site);
  const nextTarget = "./_framework/dotnet.next-fingerprint.js";
  writeFileSync(join(site, nextTarget), sdkSource);
  writeFileSync(
    join(site, "index.html"),
    `<script type="importmap">${JSON.stringify({
      imports: { "./_framework/dotnet.js": nextTarget },
    })}</script>`,
  );

  publishRuntimeLoader(site);

  assert.equal(
    readFileSync(join(site, "runtime-loader.js"), "utf8"),
    `export { dotnet } from "${nextTarget}";\n`,
  );
});

for (const [label, imports] of [
  ["missing", {}],
  ["non-string", { "./_framework/dotnet.js": 42 }],
  ["unfingerprinted", { "./_framework/dotnet.js": "./_framework/dotnet.js" }],
  ["non-JavaScript", { "./_framework/dotnet.js": "./_framework/dotnet.hash.wasm" }],
] as const) {
  test(`publication fails visibly for a ${label} mapping`, (context) => {
    const site = createSite(context);
    writeFileSync(
      join(site, "index.html"),
      `<script type="importmap">${JSON.stringify({ imports })}</script>`,
    );
    const result = spawnSync(process.execPath, [publishScript, site], {
      encoding: "utf8",
    });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /no valid fingerprinted dotnet\.js mapping/);
    assert.equal(existsSync(join(site, "runtime-loader.js")), false);
  });
}

test("publication fails visibly when the mapped runtime is missing", (context) => {
  const site = createSite(context);
  rmSync(join(site, target));

  const result = spawnSync(process.execPath, [publishScript, site], {
    encoding: "utf8",
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /Published runtime module .* is missing/);
  assert.equal(existsSync(join(site, "runtime-loader.js")), false);
});

test("the source loader re-exports the SDK build runtime", async (context) => {
  const site = createSite(context);
  copyFileSync(join(sourceRoot, "runtime-loader.js"), join(site, "runtime-loader.js"));
  writeFileSync(join(site, "_framework/dotnet.js"), sdkSource);

  const loader: unknown = await import(
    pathToFileURL(join(site, "runtime-loader.js")).href,
  );
  const sdk: unknown = await import(
    pathToFileURL(join(site, "_framework/dotnet.js")).href,
  );

  assert.deepEqual(loader, sdk);
});

test("published facade smoke restores the exact loader after runtime failure", (context) => {
  const site = createSite(context);
  for (const facade of readdirSync(sourceRoot).filter(
    name => name.startsWith("inspect-web-") && name.endsWith(".js"),
  )) {
    copyFileSync(join(sourceRoot, facade), join(site, facade));
  }
  writeFileSync(
    join(site, target),
    'export const dotnet = { create() { throw new Error("runtime fixture failed"); } };\n',
  );
  publishRuntimeLoader(site);
  const original = readFileSync(join(site, "runtime-loader.js"));

  const result = spawnSync(process.execPath, [smokeScript, site, "deployment"], {
    encoding: "utf8",
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /runtime fixture failed/);
  assert.deepEqual(readFileSync(join(site, "runtime-loader.js")), original);
  assert.equal(existsSync(join(site, "_framework/dotnet.js")), false);
});
