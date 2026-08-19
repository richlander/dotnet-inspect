import assert from "node:assert/strict";
import test from "node:test";
import {
  packageBarHtml,
  packageIdentityEquals,
  packageTabHtml,
  packageTabsHtml,
  parsePackageQuery,
  platformTabHtml,
} from "../src/package-bar.ts";

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function packageIdentityKey(pkg) {
  return `${pkg.id}@${pkg.version}::${pkg.activeFramework}`;
}

function pkg(id, version = "1.0.0", activeFramework = "net10.0", isRuntimePack = false) {
  return { id, version, activeFramework, isRuntimePack };
}

test("package identity equality compares the full coordinate", () => {
  const a = pkg("System.Text.Json");
  const b = pkg("System.Text.Json");
  const c = pkg("System.Text.Json", "2.0.0");

  assert.equal(packageIdentityEquals(a, b, packageIdentityKey), true);
  assert.equal(packageIdentityEquals(a, c, packageIdentityKey), false);
  assert.equal(packageIdentityEquals(null, b, packageIdentityKey), false);
});

test("a package tab marks only the active tab and only the active tab carries a close button", () => {
  const active = pkg("System.Text.Json");
  const other = pkg("Newtonsoft.Json");

  const activeHtml = packageTabHtml(active, active, escapeHtml, packageIdentityKey);
  const inactiveHtml = packageTabHtml(other, active, escapeHtml, packageIdentityKey);

  assert.match(activeHtml, /class="package-tab active"/);
  assert.match(activeHtml, /data-package-close=/);
  assert.doesNotMatch(inactiveHtml, /class="package-tab active"/);
  assert.doesNotMatch(inactiveHtml, /data-package-close=/);
});

test("package tab markup escapes untrusted package identity text", () => {
  const untrusted = pkg('Evil"<Package>', '1.0.0"<T>');
  const html = packageTabHtml(untrusted, untrusted, escapeHtml, packageIdentityKey);

  assert.doesNotMatch(html, /Evil"<Package>/);
  assert.match(html, /Evil&quot;&lt;Package&gt;/);
});

test("the platform tab reflects a resident runtime pack, and lazily opens when absent", () => {
  const runtimePack = pkg("Microsoft.NETCore.App", "10.0.0", "net10.0", true);

  const resident = platformTabHtml(runtimePack, runtimePack, escapeHtml, packageIdentityKey);
  assert.match(resident, /class="package-tab platform active"/);
  assert.match(resident, /data-package-key=/);
  assert.doesNotMatch(resident, /data-platform-open/);

  const absent = platformTabHtml(null, null, escapeHtml, packageIdentityKey);
  assert.match(absent, /class="package-tab platform "/);
  assert.match(absent, /data-platform-open="1"/);
  assert.match(absent, /<small>load<\/small>/);
});

test("the tab strip renders the platform tab first, then only non-runtime packages", () => {
  const runtimePack = pkg("Microsoft.NETCore.App", "10.0.0", "net10.0", true);
  const active = pkg("System.Text.Json");
  const state = { packages: [runtimePack, active], package: active };

  const html = packageTabsHtml(state, runtimePack, escapeHtml, packageIdentityKey);
  assert.ok(html.indexOf("platform") < html.indexOf("System.Text.Json"));
  assert.equal(html.match(/package-tab/g)?.length, 2);
});

test("the package bar wraps the tab strip and open-package form", () => {
  const active = pkg("System.Text.Json");
  const state = { packages: [active], package: active };
  const html = packageBarHtml(state, null, escapeHtml, packageIdentityKey);

  assert.match(html, /class="package-tabs" role="tablist"/);
  assert.match(html, /id="package-query"/);
  assert.match(html, /id="package-query-input"/);
});

test("parsing the open-package query accepts an id, an id@version, and rejects a bare version", () => {
  assert.deepEqual(parsePackageQuery("System.Text.Json"), {
    packageId: "System.Text.Json",
    version: "latest",
  });
  assert.deepEqual(parsePackageQuery("System.Text.Json@8.0.0"), {
    packageId: "System.Text.Json",
    version: "8.0.0",
  });
  assert.equal(parsePackageQuery(""), null);
  assert.equal(parsePackageQuery("   "), null);
  assert.equal(parsePackageQuery("System.Text.Json@"), null);
});
