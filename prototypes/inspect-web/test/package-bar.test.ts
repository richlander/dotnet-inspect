import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageSelections,
  createPackageBar,
  packageBarHtml,
  packageIdentityEquals,
  packageTabHtml,
  packageTabsHtml,
  parsePackageQuery,
  platformTabHtml,
} from "../src/package-bar.ts";
import type { PackageBarPackage } from "../src/package-bar.ts";

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function packageIdentityKey(packageModel: PackageBarPackage) {
  return `${packageModel.id}@${packageModel.version}::${packageModel.activeFramework}`;
}

function pkg(
  id: string,
  version = "1.0.0",
  activeFramework = "net10.0",
  isRuntimePack = false,
): PackageBarPackage {
  return { id, version, activeFramework, isRuntimePack };
}

class FakeElement {
  readonly dataset: Record<string, string | undefined> = {};
  value = "";
  private readonly listeners = new Map<string, EventListener[]>();

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ target: this } as unknown as Event);
    }
  }
}

class FakeRoot {
  private readonly single = new Map<string, FakeElement>();
  private readonly multiple = new Map<string, FakeElement[]>();

  add(selector: string, element: FakeElement) {
    this.single.set(selector, element);
    return element;
  }

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
    return elements;
  }

  querySelector(selector: string) {
    return this.single.get(selector) ?? null;
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

test("package selection bindings map overview and header controls without eager dispatch", () => {
  const root = new FakeRoot();
  const chip = new FakeElement();
  chip.dataset.frameworkChip = "net9.0";
  const secondChip = new FakeElement();
  secondChip.dataset.frameworkChip = "net8.0";
  root.addAll("[data-framework-chip]", chip, secondChip);
  const framework = root.add("#framework", new FakeElement());
  framework.value = "net9.0";
  const version = root.add("#package-version", new FakeElement());
  version.value = "10.0.0";
  const calls: string[] = [];

  bindPackageSelections(
    root as unknown as ParentNode,
    {
      onFrameworkSelect: value => calls.push(`framework:${value}`),
      onVersionSelect: value => calls.push(`version:${value}`),
    });

  assert.deepEqual(calls, []);
  chip.dispatch("click");
  secondChip.dispatch("click");
  assert.deepEqual(calls, [
    "framework:net9.0",
    "framework:net8.0",
  ]);
  framework.value = "net10.0";
  framework.dispatch("change");
  assert.deepEqual(calls, [
    "framework:net9.0",
    "framework:net8.0",
    "framework:net10.0",
  ]);
  version.value = "10.0.1";
  version.dispatch("change");
  assert.deepEqual(calls, [
    "framework:net9.0",
    "framework:net8.0",
    "framework:net10.0",
    "version:10.0.1",
  ]);
});

test("package selection binding tolerates an inactive surface with no controls", () => {
  const calls: string[] = [];
  bindPackageSelections(
    new FakeRoot() as unknown as ParentNode,
    {
      onFrameworkSelect: value => calls.push(`framework:${value}`),
      onVersionSelect: value => calls.push(`version:${value}`),
    });

  assert.deepEqual(calls, []);
});

test("package bar connects package selection controls to its typed options", () => {
  const root = new FakeRoot();
  const chip = new FakeElement();
  chip.dataset.frameworkChip = "net9.0";
  const secondChip = new FakeElement();
  secondChip.dataset.frameworkChip = "net8.0";
  root.addAll("[data-framework-chip]", chip, secondChip);
  const framework = root.add("#framework", new FakeElement());
  framework.value = "net9.0";
  const version = root.add("#package-version", new FakeElement());
  version.value = "10.0.0";
  root.add("#package-query", new FakeElement());
  const calls: string[] = [];
  const packageBar = createPackageBar({
    state: { packages: [], package: null },
    escapeHtml,
    packageIdentityKey,
    runtimePackPackage: () => null,
    selectPackageTab: () => {},
    closePackageTab: () => {},
    openRuntimePack: () => {},
    openPackage: () => {},
    selectFramework: value => calls.push(`framework:${value}`),
    selectVersion: value => calls.push(`version:${value}`),
    showToast: () => {},
  });

  packageBar.bind(root as unknown as ParentNode);

  assert.deepEqual(calls, []);
  chip.dispatch("click");
  secondChip.dispatch("click");
  framework.value = "net10.0";
  framework.dispatch("change");
  version.value = "10.0.1";
  version.dispatch("change");
  assert.deepEqual(calls, [
    "framework:net9.0",
    "framework:net8.0",
    "framework:net10.0",
    "version:10.0.1",
  ]);
});

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

test("parsing the open-package query accepts an id and an id@version, and rejects an empty query or an empty version", () => {
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

// A leading "@" (no package id) is not a rejection case: it is an existing quirk of the
// inline handler this module replaces, preserved deliberately rather than special-cased.
test("a leading '@' has no package id to reject, so it is preserved as the whole package id", () => {
  assert.deepEqual(parsePackageQuery("@1.0.0"), {
    packageId: "@1.0.0",
    version: "latest",
  });
});
