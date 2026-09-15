import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { type TestContext } from "node:test";
import { fileURLToPath } from "node:url";
import { publishContentSecurityPolicy } from "../scripts/publish-content-security-policy.ts";

const script = fileURLToPath(new URL("../scripts/publish-content-security-policy.ts", import.meta.url));
const template = readFileSync(new URL("../staticwebapp.config.json", import.meta.url), "utf8");
const map = '\n{\n  "imports": { "./_framework/dotnet.js": "./_framework/dotnet.first.js" }\n}\n';

function createSite(context: TestContext, html: string): string {
  const site = mkdtempSync(join(tmpdir(), "inspect-web-csp-"));
  context.after(() => rmSync(site, { recursive: true, force: true }));
  writeFileSync(join(site, "index.html"), html);
  return site;
}

function expectedConfig(text: string): string {
  const config: unknown = JSON.parse(template.replace(
    "{{IMPORT_MAP_HASH}}", createHash("sha256").update(text).digest("base64"),
  ));
  return `${JSON.stringify(config, null, 2)}\n`;
}

test("publication authorizes the SDK import map without changing other hosting configuration", context => {
  const html = `<script type="importmap">${map}</script><script>const unrelated = 1;</script>`;
  const site = createSite(context, html);
  const result = spawnSync(process.execPath, [script, site], { encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(readFileSync(join(site, "staticwebapp.config.json"), "utf8"), expectedConfig(map));
  assert.equal(readFileSync(join(site, "index.html"), "utf8"), html);
});

for (const newline of ["\r\n", "\r"]) {
  test(`publication hashes browser-normalized ${JSON.stringify(newline)} line endings`, context => {
    const site = createSite(context, `<script type="importmap">${map.replaceAll("\n", newline)}</script>`);
    publishContentSecurityPolicy(site);
    assert.equal(readFileSync(join(site, "staticwebapp.config.json"), "utf8"), expectedConfig(map));
  });
}

test("republication replaces the old hash and remains idempotent", context => {
  const site = createSite(context, `<script type="importmap">${map}</script>`);
  publishContentSecurityPolicy(site);
  const next = map.replace("first", "second");
  writeFileSync(join(site, "index.html"), `<script type="importmap">${next}</script>`);
  for (let attempt = 0; attempt < 2; attempt++) {
    publishContentSecurityPolicy(site);
    assert.equal(readFileSync(join(site, "staticwebapp.config.json"), "utf8"), expectedConfig(next));
  }
});

for (const [name, html] of [
  ["missing", "<html></html>"],
  ["empty", '<script type="importmap"></script>'],
  ["whitespace-only", '<script type="importmap"> \n </script>'],
  ["duplicate", `<script type="importmap">${map}</script><script type="importmap">${map}</script>`],
  ["changed SDK markup", `<script type="importmap" id="changed">${map}</script>`],
] as const) {
  test(`publication fails visibly for a ${name} import map`, context => {
    const site = createSite(context, html);
    assert.throws(() => publishContentSecurityPolicy(site), /Expected one populated SDK import map/);
  });
}
