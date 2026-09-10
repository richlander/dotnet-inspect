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
const sourceRoot = fileURLToPath(new URL("../DotnetInspect.Web/wwwroot/", import.meta.url));
const runtimeModules = [
  {
    specifier: "./_framework/dotnet.js",
    target: "./_framework/dotnet.fingerprint.js",
    source: 'export const dotnet = { identity: "SDK runtime" };\n',
  },
  {
    specifier: "./_framework/dotnet.native.js",
    target: "./_framework/dotnet.native.fingerprint.js",
    source: "export default function createNativeRuntime() {}\n",
  },
  {
    specifier: "./_framework/dotnet.runtime.js",
    target: "./_framework/dotnet.runtime.fingerprint.js",
    source: "export function configureRuntimeStartup() {}\n",
  },
] as const;

function createSite(context: TestContext): string {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-runtime-loader-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  mkdirSync(join(site, "_framework"));
  writeFileSync(join(site, "package.json"), '{"type":"module"}\n');
  for (const module of runtimeModules) {
    writeFileSync(join(site, module.target), module.source);
  }
  writeFileSync(
    join(site, "index.html"),
    `<script type="importmap">${JSON.stringify({
      imports: Object.fromEntries(
        runtimeModules.map(module => [module.specifier, module.target]),
      ),
    }, null, 2)}</script>`,
  );
  return site;
}

test("publication emits stable exact SDK runtime modules for a Worker", async (context) => {
  const site = createSite(context);
  const result = spawnSync(process.execPath, [publishScript, site], {
    encoding: "utf8",
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(
    readFileSync(join(site, "runtime-loader.js"), "utf8"),
    'export { dotnet } from "./_framework/dotnet.js";\n',
  );
  const loader: unknown = await import(
    pathToFileURL(join(site, "runtime-loader.js")).href,
  );
  const sdk: unknown = await import(
    pathToFileURL(join(site, "_framework/dotnet.js")).href,
  );
  assert.deepEqual(loader, sdk);
  for (const module of runtimeModules) {
    assert.deepEqual(
      readFileSync(join(site, module.specifier)),
      readFileSync(join(site, module.target)),
    );
  }
});

test("publication refreshes aliases from newly selected fingerprints", (context) => {
  const site = createSite(context);
  publishRuntimeLoader(site);
  const nextModules = runtimeModules.map(module => ({
    ...module,
    target: module.target.replace(".fingerprint.js", ".next-fingerprint.js"),
    source: `${module.source}// next fingerprint\n`,
  }));
  for (const module of nextModules) {
    writeFileSync(join(site, module.target), module.source);
  }
  writeFileSync(
    join(site, "index.html"),
    `<script type="importmap">${JSON.stringify({
      imports: Object.fromEntries(
        nextModules.map(module => [module.specifier, module.target]),
      ),
    })}</script>`,
  );

  publishRuntimeLoader(site);

  assert.equal(
    readFileSync(join(site, "runtime-loader.js"), "utf8"),
    'export { dotnet } from "./_framework/dotnet.js";\n',
  );
  for (const module of nextModules) {
    assert.deepEqual(
      readFileSync(join(site, module.specifier)),
      readFileSync(join(site, module.target)),
    );
  }
});

for (const [label, target] of [
  ["missing", undefined],
  ["non-string", 42],
  ["unfingerprinted", "./_framework/dotnet.js"],
  ["non-JavaScript", "./_framework/dotnet.hash.wasm"],
] as const) {
  test(`publication fails visibly for a ${label} mapping`, (context) => {
    const site = createSite(context);
    const validImports = Object.fromEntries(
      runtimeModules.map(module => [module.specifier, module.target]),
    );
    writeFileSync(
      join(site, "index.html"),
      `<script type="importmap">${JSON.stringify({
        imports: {
          ...validImports,
          "./_framework/dotnet.js": target,
        },
      })}</script>`,
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
  rmSync(join(site, runtimeModules[1].target));

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
  writeFileSync(join(site, "_framework/dotnet.js"), runtimeModules[0].source);

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
    join(site, runtimeModules[0].target),
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
  assert.deepEqual(
    readFileSync(join(site, "_framework/dotnet.js")),
    readFileSync(join(site, runtimeModules[0].target)),
  );
});
