import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { parseSync } from "oxc-parser";
import {
  createDependencyGraphPendingState,
  createDependencyGraphRenderSequence,
  dependencyCoordinateCandidates,
  dependencyGraphRenderSignature,
  packageIdentityKey,
} from "../src/data.ts";
import { buildDependencyGraphMermaid, resolveMermaidCssVariables } from "../src/graph-mermaid.ts";
import type {
  BrowserDependencyCoordinateMatch,
  BrowserPackageDependencyGroup,
} from "../src/facades/inspect-web-package.d.ts";
import { createNavigationSequence } from "../src/workspace-navigation.ts";

const source = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const parsed = parseSync("dotnet-inspect.ts", source);
assert.deepEqual(parsed.errors, []);
const names = [
  "uniqueCompatiblePackage", "dependencyListSectionHtml", "renderPackageDependencyList",
  "resolveDependenciesGroupIndex", "renderDependencyGraph", "openDependencyPackage",
  "errorMessage", "isRecord", "escapeHtml",
];
const functions = parsed.program.body.filter(node =>
  node.type === "FunctionDeclaration" && names.includes(node.id?.name ?? ""));
assert.equal(functions.length, names.length);
const declarations = stripTypeScriptTypes(
  functions.map(node => source.slice(node.start, node.end)).join("\n"));

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((accept, deny) => { resolve = accept; reject = deny; });
  return { promise, resolve, reject };
}

const root = { id: "Root", version: "1.0.0", activeFramework: "net10.0" };
const dependency = { id: "Dependency", version: "2.0.0", activeFramework: "net10.0" };
const unique: BrowserDependencyCoordinateMatch = {
  outcome: "Unique", candidateKey: packageIdentityKey(dependency),
};
const noMatch: BrowserDependencyCoordinateMatch = { outcome: "NoMatch", candidateKey: null };
const groups: BrowserPackageDependencyGroup[] = [
  { index: 0, framework: "net10.0", isActive: true,
    dependencies: [{ id: "Dependency", versionRange: "[2.0,3.0)" }] },
  { index: 1, framework: "net9.0", isActive: false,
    dependencies: [{ id: "External", versionRange: "[4.0]" }] },
];

class Container {
  dataset: Record<string, string> = {};
  innerHTML = "";
  outerHTML = "";
  viewport = { innerHTML: "" };
  removedErrors = 0;
  attributes: Record<string, string> = {};

  querySelector(selector: string) {
    if (selector === ".graph-viewport" && this.innerHTML.includes("graph-viewport"))
      return this.viewport;
    if (selector === ".graph-render-error" && this.innerHTML.includes("graph-render-error"))
      return { remove: () => { this.removedErrors++; } };
    return null;
  }

  setAttribute(name: string, value: string) { this.attributes[name] = value; }
  insertAdjacentHTML(_position: string, html: string) { this.innerHTML += html; }
}

type Match = (
  id: string, range: string | null, candidates: string,
) => Promise<BrowserDependencyCoordinateMatch>;

function harness(match: Match = async id => id === "Dependency" ? unique : noMatch) {
  let list = new Container();
  list.dataset.dependencyMatchState = "pending";
  const graph = new Container();
  const state = {
    packages: [{ ...root }, { ...dependency }],
    package: { ...root },
    packageDependencies: { dependencyGroups: groups },
    dependenciesGroupIndex: 0,
    workspaceDependencies: {},
    theme: "light", loading: false, error: "", retryAction: null,
    loadingMessage: "", loadingSubtitle: "", atPackageRoot: true,
    atLibraryRoot: false, packageLens: "dependencies",
  };
  const navigationSequence = createNavigationSequence();
  const calls: [string, string | null, string][] = [];
  const switches: string[] = [];
  const notices: string[] = [];
  const versions: string[] = [];
  const loads: unknown[] = [];
  const diagrams: string[] = [];
  let bindings = 0;
  let diagramResult = Promise.resolve({ svg: "<svg>current graph</svg>" });
  const context = {
    state, navigationSequence,
    engineClient: { package: {
      matchPackageDependencyCoordinate: (...args: Parameters<Match>) => {
        calls.push(args);
        return match(...args);
      },
    } },
    document: {
      documentElement: {},
      querySelector: (selector: string) => selector === "#dep-list-section" ? list : graph,
    },
    getComputedStyle: () => ({ getPropertyValue: () => "#fff" }),
    dependencyCoordinateCandidates, packageIdentityKey,
    createDependencyGraphPendingState, dependencyGraphRenderSignature,
    buildDependencyGraphMermaid, resolveMermaidCssVariables,
    depGraphRenderSequence: createDependencyGraphRenderSequence(),
    bindPackageDependencyListEvents: () => { bindings++; },
    mermaidModule: Promise.resolve({ default: {
      initialize() {},
      render: (_id: string, definition: string) => {
        diagrams.push(definition);
        return diagramResult;
      },
    } }),
    bindGraphPanZoom() {},
    keybindings: {},
    closeGraphExplorerForNavigation() {},
    render() {},
    switchToPackageForDependencies: (key: string) => {
      switches.push(key);
      navigationSequence.invalidate();
      state.loading = false;
    },
    resolveDependencyVersion: async (id: string) => {
      versions.push(id);
      return "4.0.0";
    },
    loadPackage: async (...args: unknown[]) => {
      loads.push(args);
      state.loading = false;
      return { ...dependency };
    },
    appendQueryNotice: (notice: string) => { notices.push(notice); },
    friendlyLoadError: (error: Error) => ({ message: error.message }),
  };
  runInNewContext(declarations, context);
  const host = {
    dependencyListSectionHtml: (dependencyGroups: BrowserPackageDependencyGroup[], index: number) => {
      const html: unknown = runInNewContext(
        "dependencyListSectionHtml(dependencyGroups, index)",
        { ...context, dependencyGroups, index });
      assert.ok(typeof html === "string");
      return html;
    },
    renderPackageDependencyList: () => Promise.resolve<unknown>(
      runInNewContext("renderPackageDependencyList()", context)),
    renderDependencyGraph: () => Promise.resolve<unknown>(
      runInNewContext("renderDependencyGraph()", context)),
    openDependencyPackage: (id: string, range: string | null) => Promise.resolve<unknown>(
      runInNewContext("openDependencyPackage(id, range)", { ...context, id, range })),
  };
  return {
    host, state, graph, calls, switches, notices, versions, loads, diagrams,
    navigationSequence,
    get list() { return list; },
    get bindings() { return bindings; },
    replaceList() {
      list = new Container();
      list.dataset.dependencyMatchState = "pending";
    },
    holdDiagram(result: Promise<{ svg: string }>) { diagramResult = result; },
  };
}

test("synchronous dependency HTML does not dispatch matching or invent load links", () => {
  const h = harness();
  const html = h.host.dependencyListSectionHtml(groups, 0);
  assert.equal(h.calls.length, 0);
  assert.match(html, /disabled title="Matching open packages/);
  assert.doesNotMatch(html, /data-dep-(?:open|load)=/);
});

test("dependency links await matching once and retain exact coordinate inputs", { timeout: 10_000 }, async () => {
  const pending = deferred<BrowserDependencyCoordinateMatch>();
  const h = harness(() => pending.promise);
  const operation = h.host.renderPackageDependencyList();
  await h.host.renderPackageDependencyList();
  assert.equal(h.calls.length, 1);
  assert.deepEqual(h.calls[0], [
    "Dependency", "[2.0,3.0)",
    JSON.stringify(dependencyCoordinateCandidates([root, dependency])),
  ]);
  assert.equal(h.bindings, 0);
  assert.equal(h.list.outerHTML, "");
  pending.resolve(unique);
  await operation;
  assert.match(h.list.outerHTML, /data-dep-open=/);
  assert.doesNotMatch(h.list.outerHTML, /data-dep-load=/);
  assert.equal(h.bindings, 1);
});

for (const outcome of ["NoMatch", "Ambiguous"] as const) {
  test(`${outcome} retains dependency acquisition rather than guessing a loaded package`, async () => {
    const h = harness(async () => ({ outcome, candidateKey: null }));
    await h.host.renderPackageDependencyList();
    assert.match(h.list.outerHTML, /data-dep-load="Dependency"/);
    assert.doesNotMatch(h.list.outerHTML, /data-dep-open=/);
  });
}

test("dependency matching failure is visible and does not become an acquisition link", async () => {
  const h = harness(async () => { throw new Error("Unavailable <engine>"); });
  await h.host.renderPackageDependencyList();
  assert.equal(h.list.dataset.dependencyMatchState, "failed");
  assert.equal(h.list.attributes["aria-busy"], "false");
  assert.match(h.list.innerHTML, /Dependency matching failed: Unavailable &lt;engine&gt;/);
  assert.equal(h.list.outerHTML, "");
  assert.equal(h.bindings, 0);
});

for (const change of ["framework", "workspace"] as const) {
  for (const completion of ["success", "failure"] as const) {
    test(`late dependency-list ${completion} cannot replace a newer ${change}`, { timeout: 10_000 }, async () => {
      const pending = deferred<BrowserDependencyCoordinateMatch>();
      let issued = 0;
      const h = harness(() => ++issued === 1 ? pending.promise : Promise.resolve(noMatch));
      const oldList = h.list;
      const operation = h.host.renderPackageDependencyList();
      if (change === "framework") h.state.dependenciesGroupIndex = 1;
      else h.state.packages = [{ ...root }];
      h.replaceList();
      await h.host.renderPackageDependencyList();
      const current = h.list.outerHTML;
      assert.match(current, /data-dep-load=/);
      if (completion === "success") pending.resolve(unique);
      else pending.reject(new Error("stale"));
      await operation;
      assert.equal(h.list.outerHTML, current);
      assert.equal(h.list.innerHTML, "");
      assert.equal(oldList.outerHTML, "");
      assert.equal(oldList.innerHTML, "");
      assert.equal(h.bindings, 1);
    });
  }
}

test("dependency navigation reserves authority before matching and reuses the loaded coordinate", { timeout: 10_000 }, async () => {
  const pending = deferred<BrowserDependencyCoordinateMatch>();
  const h = harness(() => pending.promise);
  const oldSequence = h.navigationSequence.begin();
  const operation = h.host.openDependencyPackage("Dependency", "[2.0,3.0)");
  assert.equal(h.navigationSequence.isCurrent(oldSequence), false);
  assert.equal(h.state.loading, true);
  assert.equal(h.versions.length, 0);
  pending.resolve(unique);
  await operation;
  assert.deepEqual(h.switches, [packageIdentityKey(dependency)]);
  assert.equal(h.loads.length, 0);
  assert.equal(h.state.loading, false);
});

test("unmatched dependency navigation awaits the existing version and package acquisition", async () => {
  const h = harness(async () => noMatch);
  await h.host.openDependencyPackage("External", "[4.0]");
  assert.deepEqual(h.versions, ["External"]);
  assert.equal(h.loads.length, 1);
  assert.equal(h.switches.length, 0);
  assert.equal(h.state.packageLens, "dependencies");
});

for (const completion of ["success", "failure"] as const) {
  test(`superseded dependency navigation ignores a late match ${completion}`, { timeout: 10_000 }, async () => {
    const pending = deferred<BrowserDependencyCoordinateMatch>();
    const h = harness(() => pending.promise);
    const operation = h.host.openDependencyPackage("Dependency", null);
    h.navigationSequence.begin();
    h.state.loading = false;
    if (completion === "success") pending.resolve(unique);
    else pending.reject(new Error("stale"));
    await operation;
    assert.deepEqual(h.switches, []);
    assert.deepEqual(h.notices, []);
    assert.deepEqual(h.versions, []);
    assert.equal(h.state.loading, false);
  });
}

test("current dependency navigation matching failure follows the existing notice path", async () => {
  const h = harness(async () => { throw new Error("Matcher unavailable"); });
  await h.host.openDependencyPackage("Dependency", null);
  assert.deepEqual(h.notices, ["Matcher unavailable"]);
  assert.equal(h.state.loading, false);
  assert.deepEqual(h.versions, []);
});

test("duplicate dependency graphs share pending matching and do not cancel their own Mermaid render", { timeout: 10_000 }, async () => {
  const pending = deferred<BrowserDependencyCoordinateMatch>();
  const diagram = deferred<{ svg: string }>();
  const h = harness(() => pending.promise);
  h.holdDiagram(diagram.promise);
  const operation = h.host.renderDependencyGraph();
  await h.host.renderDependencyGraph();
  assert.equal(h.calls.length, 1);
  assert.equal(h.diagrams.length, 0);
  pending.resolve(unique);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.diagrams.length, 1);
  await h.host.renderDependencyGraph();
  assert.equal(h.calls.length, 1);
  diagram.resolve({ svg: "<svg>finished</svg>" });
  await operation;
  assert.equal(h.graph.viewport.innerHTML, "<svg>finished</svg>");
  assert.equal(h.graph.dataset.graphPending, undefined);
});

for (const change of ["framework", "workspace"] as const) {
  for (const completion of ["success", "failure"] as const) {
    test(`late graph matching ${completion} cannot replace the newer ${change} graph`, { timeout: 10_000 }, async () => {
      const pending = deferred<BrowserDependencyCoordinateMatch>();
      let issued = 0;
      const h = harness(() => ++issued === 1 ? pending.promise : Promise.resolve(noMatch));
      const old = h.host.renderDependencyGraph();
      if (change === "framework") h.state.dependenciesGroupIndex = 1;
      else h.state.packages = [{ ...root }];
      await h.host.renderDependencyGraph();
      const current = h.graph.innerHTML;
      const signature = h.graph.dataset.graphDef;
      assert.equal(h.diagrams.length, 1);
      assert.match(h.diagrams[0]!, change === "framework" ? /External/ : /Dependency/);
      if (completion === "success") pending.resolve(unique);
      else pending.reject(new Error("stale"));
      await old;
      assert.equal(h.graph.innerHTML, current);
      assert.equal(h.graph.dataset.graphDef, signature);
      assert.equal(h.diagrams.length, 1);
      assert.equal(h.graph.dataset.graphPending, undefined);
    });
  }
}

test("graph matching failure remains visible when retaining an existing diagram", async () => {
  const h = harness(async () => { throw new Error("Matcher unavailable"); });
  h.graph.innerHTML = '<div class="graph-viewport">previous</div>';
  await h.host.renderDependencyGraph();
  assert.match(h.graph.innerHTML, /previous/);
  assert.match(h.graph.innerHTML, /Dependency matching failed/);
  assert.match(h.graph.innerHTML, /Matcher unavailable/);
  assert.equal(h.diagrams.length, 0);
  assert.equal(h.graph.dataset.graphPending, undefined);
});

test("incoming graph edges await the generated matching result", { timeout: 10_000 }, async () => {
  const pending = deferred<typeof dependency | null>();
  const invoked = deferred<void>();
  const operation = buildDependencyGraphMermaid({
    package: dependency,
    packages: [dependency, root],
    packageDependencies: {
      dependencyGroups: [{ index: 0, framework: "net10.0", isActive: true, dependencies: [] }],
    },
    dependenciesGroupIndex: 0,
    workspaceDependencies: { "root@1.0.0@net10.0": { dependencyGroups: groups } },
  }, () => { invoked.resolve(); return pending.promise; });
  await invoked.promise;
  pending.resolve(dependency);
  const graph = await operation;
  assert.ok(graph);
  assert.match(graph.definition, /d1 --> d0/);
  assert.deepEqual([...graph.nodeInfoById.values()].map(node => node.id), ["Dependency", "Root"]);
});

test("awaited dependency graph matching preserves the existing node bound", async () => {
  let matches = 0;
  const graph = await buildDependencyGraphMermaid({
    package: root,
    packages: [root],
    packageDependencies: {
      dependencyGroups: [{
        index: 0, framework: "net10.0", isActive: true,
        dependencies: Array.from({ length: 100 }, (_, index) => ({
          id: `Dependency${index}`, versionRange: "[1.0]",
        })),
      }],
    },
    dependenciesGroupIndex: 0,
    workspaceDependencies: {},
  }, async () => { matches++; return null; });
  assert.ok(graph);
  assert.equal(graph.nodeLimit, 80);
  assert.equal(graph.nodeInfoById.size, 80);
  assert.equal(graph.truncated, true);
  assert.equal(matches, 80);
});
