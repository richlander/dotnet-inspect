import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageSelections,
  createPackageControls,
  findOpenPackageForQuery,
  packageIdentityEquals,
  parsePackageQuery,
} from "../src/package-controls.ts";
import type {
  PackageControlPackage,
} from "../src/package-controls.ts";
import { fakeDom } from "./fake-dom.ts";

function packageIdentityKey(packageModel: PackageControlPackage) {
  return `${packageModel.id}@${packageModel.version}::${packageModel.activeFramework}`;
}

function pkg(
  id: string,
  version = "1.0.0",
  activeFramework = "net10.0",
  isRuntimePack = false,
): PackageControlPackage {
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
      listener(fakeDom.event({ target: this, preventDefault() {} }));
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

test("package selection bindings map Package content controls without eager dispatch", () => {
  const root = new FakeRoot();
  const framework = root.add("#framework", new FakeElement());
  framework.value = "net9.0";
  const version = root.add("#package-version", new FakeElement());
  version.value = "10.0.0";
  const calls: string[] = [];

  bindPackageSelections(
    fakeDom.parentNode(root),
    {
      onFrameworkSelect: value => calls.push(`framework:${value}`),
      onVersionSelect: value => calls.push(`version:${value}`),
    });

  assert.deepEqual(calls, []);
  framework.value = "net10.0";
  framework.dispatch("change");
  assert.deepEqual(calls, [
    "framework:net10.0",
  ]);
  version.value = "10.0.1";
  version.dispatch("change");
  assert.deepEqual(calls, [
    "framework:net10.0",
    "version:10.0.1",
  ]);
});

test("package selection binding tolerates an inactive surface with no controls", () => {
  const calls: string[] = [];
  bindPackageSelections(
    fakeDom.parentNode(new FakeRoot()),
    {
      onFrameworkSelect: value => calls.push(`framework:${value}`),
      onVersionSelect: value => calls.push(`version:${value}`),
    });

  assert.deepEqual(calls, []);
});

test("package controls connect selection events to their typed options", () => {
  const root = new FakeRoot();
  const framework = root.add("#framework", new FakeElement());
  framework.value = "net9.0";
  const version = root.add("#package-version", new FakeElement());
  version.value = "10.0.0";
  const calls: string[] = [];
  const packageControls = createPackageControls({
    selectFramework: value => calls.push(`framework:${value}`),
    selectVersion: value => calls.push(`version:${value}`),
  });

  packageControls.bind(fakeDom.parentNode(root));

  assert.deepEqual(calls, []);
  framework.value = "net10.0";
  framework.dispatch("change");
  version.value = "10.0.1";
  version.dispatch("change");
  assert.deepEqual(calls, [
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

test("parsing the open-package query accepts an id and an id@version, and rejects an empty query or an empty version", () => {
  assert.deepEqual(parsePackageQuery("System.Text.Json"), {
    packageId: "System.Text.Json",
    version: "latest",
    explicitVersion: false,
  });
  assert.deepEqual(parsePackageQuery("System.Text.Json@8.0.0"), {
    packageId: "System.Text.Json",
    version: "8.0.0",
    explicitVersion: true,
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
    explicitVersion: false,
  });
});

test("a bare package query activates an already-open coordinate instead of loading another version", () => {
  const older = pkg("System.Text.Json", "10.0.0");
  const newer = pkg("System.Text.Json", "10.0.11");
  const other = pkg("Newtonsoft.Json", "13.0.4");

  assert.equal(findOpenPackageForQuery(
    { packages: [older, newer, other], package: older },
    parsePackageQuery("system.text.json")!,
  ), older);
  assert.equal(findOpenPackageForQuery(
    { packages: [older, other], package: other },
    parsePackageQuery("System.Text.Json")!,
  ), older);
  assert.equal(findOpenPackageForQuery(
    { packages: [older, newer, other], package: other },
    parsePackageQuery("System.Text.Json")!,
  ), newer);
});

test("an explicit package version activates only a matching open coordinate", () => {
  const older = pkg("System.Text.Json", "10.0.0");
  const newer = pkg("System.Text.Json", "10.0.11");
  const state = { packages: [older, newer], package: newer };

  assert.equal(findOpenPackageForQuery(
    state,
    parsePackageQuery("System.Text.Json@10.0.0")!,
  ), older);
  assert.equal(findOpenPackageForQuery(
    state,
    parsePackageQuery("System.Text.Json@9.0.0")!,
  ), null);
  assert.equal(findOpenPackageForQuery(
    {
      packages: [pkg("Microsoft.NETCore.App", "10.0.0", "net10.0", true)],
      package: null,
    },
    parsePackageQuery("Microsoft.NETCore.App@10.0.0")!,
  ), null);
});
