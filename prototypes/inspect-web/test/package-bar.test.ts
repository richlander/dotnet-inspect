import assert from "node:assert/strict";
import test from "node:test";
import {
  assignPackageWorkspaceIndex,
  bindPackageSelections,
  createPackageBar,
  findPackageTabForQuery,
  packageBarHtml,
  packageIdentityEquals,
  packageTabHtml,
  packageTabsHtml,
  parsePackageQuery,
  platformTabHtml,
} from "../src/package-bar.ts";
import type {
  PackageBarPackage,
} from "../src/package-bar.ts";
import { KeybindingRegistry } from "../src/keybinding-registry.ts";
import { fakeDom } from "./fake-dom.ts";

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
    fakeDom.parentNode(root),
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
    fakeDom.parentNode(new FakeRoot()),
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
  const calls: string[] = [];
  const packageBar = createPackageBar({
    keybindings: new KeybindingRegistry(),
    state: { packages: [], package: null },
    escapeHtml,
    packageIdentityKey,
    runtimePackPackage: () => null,
    selectPackageTab: () => {},
    closePackageTab: () => {},
    openRuntimePack: () => {},
    selectFramework: value => calls.push(`framework:${value}`),
    selectVersion: value => calls.push(`version:${value}`),
  });

  packageBar.bind(fakeDom.parentNode(root));

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

test("package workspaces register keyboard activation with the shared dispatcher", () => {
  const root = new FakeRoot();
  const tab = new FakeElement();
  const packageModel = pkg("System.Text.Json");
  tab.dataset.packageKey = packageIdentityKey(packageModel);
  root.addAll("[data-package-key]", tab);
  const selected: PackageBarPackage[] = [];
  const keybindings = new KeybindingRegistry();
  const packageBar = createPackageBar({
    keybindings,
    state: { packages: [packageModel], package: null },
    escapeHtml,
    packageIdentityKey,
    runtimePackPackage: () => null,
    selectPackageTab: item => selected.push(item),
    closePackageTab: () => {},
    openRuntimePack: () => {},
    selectFramework: () => {},
    selectVersion: () => {},
  });
  packageBar.bind(fakeDom.parentNode(root));

  const target = fakeDom.eventTarget(tab);
  let prevented = false;
  const result = keybindings.dispatch(fakeDom.keyboardEvent({
    altKey: false,
    ctrlKey: false,
    defaultPrevented: false,
    key: "Enter",
    metaKey: false,
    shiftKey: false,
    target,
    composedPath: () => [target],
    preventDefault: () => prevented = true,
  }));

  assert.equal(result.bindingId, "workspace.activate");
  assert.equal(prevented, true);
  assert.deepEqual(selected, [packageModel]);
});

test("package identity equality compares the full coordinate", () => {
  const a = pkg("System.Text.Json");
  const b = pkg("System.Text.Json");
  const c = pkg("System.Text.Json", "2.0.0");

  assert.equal(packageIdentityEquals(a, b, packageIdentityKey), true);
  assert.equal(packageIdentityEquals(a, c, packageIdentityKey), false);
  assert.equal(packageIdentityEquals(null, b, packageIdentityKey), false);
});

test("a package workspace marks only the active coordinate and only it carries a close button", () => {
  const active = pkg("System.Text.Json");
  active.workspaceIndex = 3;
  const other = pkg("Newtonsoft.Json");
  other.workspaceIndex = 4;

  const activeHtml = packageTabHtml(active, active, escapeHtml, packageIdentityKey);
  const inactiveHtml = packageTabHtml(other, active, escapeHtml, packageIdentityKey);

  assert.match(activeHtml, /class="workspace-window active"/);
  assert.match(activeHtml, /aria-selected="true"/);
  assert.match(activeHtml, /<span class="workspace-index">3:<\/span>/);
  assert.match(activeHtml, /class="workspace-active-marker"/);
  assert.match(activeHtml, /data-package-close=/);
  assert.match(inactiveHtml, /aria-selected="false"/);
  assert.doesNotMatch(inactiveHtml, /class="workspace-window active"/);
  assert.doesNotMatch(inactiveHtml, /class="workspace-active-marker"/);
  assert.doesNotMatch(inactiveHtml, /data-package-close=/);
  assert.doesNotMatch(activeHtml, /<small|package-cube/);
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
  assert.match(resident, /class="workspace-window platform active"/);
  assert.match(resident, /aria-selected="true"/);
  assert.match(resident, /<span class="workspace-index">0:<\/span>/);
  assert.match(resident, /data-package-key=/);
  assert.doesNotMatch(resident, /data-platform-open/);

  const absent = platformTabHtml(null, null, escapeHtml, packageIdentityKey);
  assert.match(absent, /class="workspace-window platform"/);
  assert.match(absent, /aria-selected="false"/);
  assert.match(absent, /data-platform-open="1"/);
});

test("the workspace strip renders Platform first, then only non-runtime packages", () => {
  const runtimePack = pkg("Microsoft.NETCore.App", "10.0.0", "net10.0", true);
  const active = pkg("System.Text.Json");
  active.workspaceIndex = 1;
  const state = { packages: [runtimePack, active], package: active };

  const html = packageTabsHtml(state, runtimePack, escapeHtml, packageIdentityKey);
  assert.ok(html.indexOf("platform") < html.indexOf("System.Text.Json"));
  assert.equal(html.match(/workspace-window/g)?.length, 2);
  assert.match(html, /0:[\s\S]*1:/);
});

test("the package bar renders only the indexed workspace strip", () => {
  const active = pkg("System.Text.Json");
  active.workspaceIndex = 1;
  const state = { packages: [active], package: active };
  const html = packageBarHtml(state, null, escapeHtml, packageIdentityKey);

  assert.match(html, /class="workspace-strip" role="tablist"/);
  assert.match(html, /aria-label="Open workspaces"/);
  assert.doesNotMatch(html, /package-query|Package or Package@version/);
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

test("a bare package query activates an already-open tab instead of loading another version", () => {
  const older = pkg("System.Text.Json", "10.0.0");
  const newer = pkg("System.Text.Json", "10.0.11");
  const other = pkg("Newtonsoft.Json", "13.0.4");

  assert.equal(findPackageTabForQuery(
    { packages: [older, newer, other], package: older },
    parsePackageQuery("system.text.json")!,
  ), older);
  assert.equal(findPackageTabForQuery(
    { packages: [older, other], package: other },
    parsePackageQuery("System.Text.Json")!,
  ), older);
  assert.equal(findPackageTabForQuery(
    { packages: [older, newer, other], package: other },
    parsePackageQuery("System.Text.Json")!,
  ), newer);
});

test("an explicit package version activates only a matching open tab", () => {
  const older = pkg("System.Text.Json", "10.0.0");
  const newer = pkg("System.Text.Json", "10.0.11");
  const state = { packages: [older, newer], package: newer };

  assert.equal(findPackageTabForQuery(
    state,
    parsePackageQuery("System.Text.Json@10.0.0")!,
  ), older);
  assert.equal(findPackageTabForQuery(
    state,
    parsePackageQuery("System.Text.Json@9.0.0")!,
  ), null);
  assert.equal(findPackageTabForQuery(
    {
      packages: [pkg("Microsoft.NETCore.App", "10.0.0", "net10.0", true)],
      package: null,
    },
    parsePackageQuery("Microsoft.NETCore.App@10.0.0")!,
  ), null);
});

test("workspace indexes survive replacement and closing another subject", () => {
  const first = pkg("First.Package");
  assert.equal(assignPackageWorkspaceIndex(
    first, [], null, packageIdentityKey), 1);

  const second = pkg("Second.Package");
  assert.equal(assignPackageWorkspaceIndex(
    second, [first], null, packageIdentityKey), 2);

  const replacement = pkg("Second.Package", "2.0.0", "net9.0");
  assert.equal(assignPackageWorkspaceIndex(
    replacement, [first, second], second, packageIdentityKey), 2);

  const afterClose = pkg("After.Close");
  assert.equal(assignPackageWorkspaceIndex(
    afterClose, [replacement], null, packageIdentityKey), 1);
  assert.equal(replacement.workspaceIndex, 2);

  const platform = pkg(
    "Microsoft.NETCore.App", "10.0.0", "net10.0", true);
  assert.equal(assignPackageWorkspaceIndex(
    platform, [afterClose, replacement], null, packageIdentityKey), 0);
});
