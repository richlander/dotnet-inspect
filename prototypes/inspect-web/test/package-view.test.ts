import assert from "node:assert/strict";
import test from "node:test";
import {
  bindPackageDependencyList,
  bindPackageView,
} from "../src/package-view.ts";
import type {
  PackagePerformanceTarget,
  PackageViewBindingActions,
} from "../src/package-view.ts";
import { fakeDom } from "./fake-dom.ts";

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  private readonly listeners = new Map<string, EventListener[]>();
  onclick: EventListener | null = null;

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string) {
    const event = fakeDom.event({ target: this });
    if (type === "click") this.onclick?.(event);
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class FakeRoot {
  private readonly multiple = new Map<string, FakeElement[]>();

  addAll(selector: string, ...elements: FakeElement[]) {
    this.multiple.set(selector, elements);
  }

  querySelectorAll(selector: string) {
    return this.multiple.get(selector) ?? [];
  }
}

function recordingActions(calls: string[]): PackageViewBindingActions {
  return {
    onDependencyGroupSelect: value => calls.push(`dependency-group:${value}`),
    onDependencyLoad: (id, version) =>
      calls.push(`dependency-load:${id}@${version}`),
    onDependencyOpen: value => calls.push(`dependency-open:${value}`),
    onGraphTypeSelect: value => calls.push(`graph-type:${value}`),
    onKindJump: value => calls.push(`kind:${value}`),
    onLibraryScopeSelect: (library, kind) =>
      calls.push(`library:${library}:${kind}`),
    onNamespaceJump: value => calls.push(`namespace:${value}`),
    onPerformanceMemberSelect: (target: PackagePerformanceTarget) =>
      calls.push(
        `performance:${target.metadataToken}:${target.assembly}:${target.typeId}`),
  };
}

test("package view bindings decode navigation controls without eager work", () => {
  const root = new FakeRoot();
  const group = new FakeElement({ depGroup: "2" });
  const invalidGroups = [
    new FakeElement(),
    new FakeElement({ depGroup: "02" }),
    new FakeElement({ depGroup: "1.5" }),
  ];
  const open = new FakeElement({ depOpen: "Example@1.0.0::net10.0" });
  const secondOpen = new FakeElement({ depOpen: "Other@2.0.0::net9.0" });
  const emptyOpen = new FakeElement({ depOpen: "" });
  const load = new FakeElement({
    depLoad: "Other.Package",
    depVersion: "[2.0.0,)",
  });
  const defaultVersion = new FakeElement({ depLoad: "Default.Package" });
  const emptyLoad = new FakeElement({ depLoad: "" });
  const kind = new FakeElement({ kindJump: "class" });
  const defaultKind = new FakeElement();
  const namespace = new FakeElement({ namespaceJump: "System.Text" });
  const defaultNamespace = new FakeElement();
  const library = new FakeElement({
    libScope: "System.Text.Json",
    libKind: "class",
  });
  const defaultLibrary = new FakeElement();
  const graphType = new FakeElement({ graphType: "System.String" });
  const defaultGraphType = new FakeElement();
  // The producer interpolates `member.metadataToken`, a number, so a rendered payload is
  // always decimal. The hexadecimal spelling these fixtures used to carry had no producer.
  const performance = new FakeElement({
    perfToken: "100663297",
    perfAssembly: "Example.dll",
    perfType: "Example.Type",
  });
  const invalidPerformanceToken = new FakeElement({
    perfToken: "0100663297",
    perfAssembly: "Example.dll",
    perfType: "Example.Type",
  });
  const hexPerformanceToken = new FakeElement({
    perfToken: "0x06000001",
    perfAssembly: "Example.dll",
    perfType: "Example.Type",
  });
  const invalidPerformanceAssembly = new FakeElement({
    perfToken: "100663298",
    perfAssembly: "",
    perfType: "Example.Type",
  });
  const invalidPerformanceType = new FakeElement({
    perfToken: "100663299",
    perfAssembly: "Example.dll",
    perfType: "",
  });
  root.addAll("[data-dep-group]", group, ...invalidGroups);
  root.addAll("[data-dep-open]", open, secondOpen, emptyOpen);
  root.addAll("[data-dep-load]", load, defaultVersion, emptyLoad);
  root.addAll("[data-kind-jump]", kind, defaultKind);
  root.addAll("[data-namespace-jump]", namespace, defaultNamespace);
  root.addAll("[data-lib-scope]", library, defaultLibrary);
  root.addAll("[data-graph-type]", graphType, defaultGraphType);
  root.addAll(
    "[data-perf-token]",
    performance,
    hexPerformanceToken,
    invalidPerformanceToken,
    invalidPerformanceAssembly,
    invalidPerformanceType);
  const calls: string[] = [];

  bindPackageView(
    fakeDom.parentNode(root),
    recordingActions(calls));

  assert.deepEqual(calls, []);
  group.dispatch("click");
  for (const invalidGroup of invalidGroups) invalidGroup.dispatch("click");
  open.dispatch("click");
  secondOpen.dispatch("click");
  emptyOpen.dispatch("click");
  load.dispatch("click");
  defaultVersion.dispatch("click");
  emptyLoad.dispatch("click");
  kind.dispatch("click");
  defaultKind.dispatch("click");
  namespace.dispatch("click");
  defaultNamespace.dispatch("click");
  library.dispatch("click");
  defaultLibrary.dispatch("click");
  graphType.dispatch("click");
  defaultGraphType.dispatch("click");
  performance.dispatch("click");
  invalidPerformanceToken.dispatch("click");
  invalidPerformanceAssembly.dispatch("click");
  invalidPerformanceType.dispatch("click");

  assert.deepEqual(calls, [
    "dependency-group:2",
    "dependency-open:Example@1.0.0::net10.0",
    "dependency-open:Other@2.0.0::net9.0",
    "dependency-load:Other.Package@[2.0.0,)",
    "dependency-load:Default.Package@",
    "kind:class",
    "kind:",
    "namespace:System.Text",
    "namespace:",
    "library:System.Text.Json:class",
    "library:undefined:",
    "graph-type:System.String",
    "graph-type:",
    "performance:100663297:Example.dll:Example.Type",
  ]);
});

test("dependency list binding reconnects only replacement list controls", () => {
  const root = new FakeRoot();
  const open = new FakeElement({ depOpen: "Example@1.0.0::net10.0" });
  const secondOpen = new FakeElement({ depOpen: "Other@2.0.0::net9.0" });
  const load = new FakeElement({
    depLoad: "Other.Package",
    depVersion: "2.0.0",
  });
  root.addAll("[data-dep-open]", open, secondOpen);
  root.addAll("[data-dep-load]", load);
  const calls: string[] = [];

  bindPackageDependencyList(
    fakeDom.parentNode(root),
    recordingActions(calls));
  bindPackageDependencyList(
    fakeDom.parentNode(root),
    recordingActions(calls));

  assert.deepEqual(calls, []);
  open.dispatch("click");
  secondOpen.dispatch("click");
  load.dispatch("click");
  assert.deepEqual(calls, [
    "dependency-open:Example@1.0.0::net10.0",
    "dependency-open:Other@2.0.0::net9.0",
    "dependency-load:Other.Package@2.0.0",
  ]);
});

test("package view binding tolerates an inactive surface", () => {
  assert.doesNotThrow(() => bindPackageView(
    fakeDom.parentNode(new FakeRoot()),
    recordingActions([])));
});
