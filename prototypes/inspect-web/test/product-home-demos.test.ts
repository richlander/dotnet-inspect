import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserHomeDemoResolved,
  BrowserWorkspaceShareState,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  HOME_DEMO_PENDING_SLOT_COUNT,
  PLATFORM_RUNTIME_PACK,
  homeDemoRowHtml,
  isProductHomeDemoId,
  productHomeDemoLocationHref,
  setProductHomeDemoCatalog,
} from "../src/product-home-demos.ts";
import { parseWorkspaceLocation } from "../src/workspace-navigation.ts";

const stjResolved: BrowserHomeDemoResolved = {
  id: "stj-serializer",
  title: "System.Text.Json",
  summary: "Browse a real package API",
  workspaceMembers: [
    {
      kind: "package",
      id: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
  ],
  tabs: [
    {
      id: "stj",
      member: {
        kind: "package",
        id: "System.Text.Json",
        version: "10.0.0",
        framework: "net10.0",
        assembly: null,
      },
    },
  ],
  focusTabIndex: 0,
  view: {
    library: null,
    type: "System.Text.Json.JsonSerializer",
    memberAnchor: null,
    memberKey: null,
    section: "Methods",
  },
};

/** Synthetic residual fixture — not a product home demo. */
const unversionedRuntimeResolved: BrowserHomeDemoResolved = {
  id: "synthetic-unversioned-runtime",
  title: "Synthetic platform residual",
  summary: "Host residual mapping only",
  workspaceMembers: [
    {
      kind: "package",
      id: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
    {
      kind: "platform",
      id: "runtime",
      version: null,
      framework: null,
      assembly: null,
    },
  ],
  tabs: [
    {
      id: "stj",
      member: {
        kind: "package",
        id: "System.Text.Json",
        version: "10.0.0",
        framework: "net10.0",
        assembly: null,
      },
    },
    {
      id: "runtime",
      member: {
        kind: "platform",
        id: "runtime",
        version: null,
        framework: null,
        assembly: null,
      },
    },
  ],
  focusTabIndex: 1,
  view: {
    library: "System.Private.CoreLib",
    type: "System.Collections.Generic.List`1",
    memberAnchor: null,
    memberKey: null,
    section: "Methods",
  },
};

const callGraphResolved: BrowserHomeDemoResolved = {
  id: "extensions-callgraph",
  title: "Cross-package call graph",
  summary: "Trace calls across three packages",
  workspaceMembers: [
    {
      kind: "package",
      id: "Microsoft.Extensions.DependencyInjection.Abstractions",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
    {
      kind: "package",
      id: "Microsoft.Extensions.Logging",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
    {
      kind: "package",
      id: "Microsoft.Extensions.Http",
      version: "10.0.0",
      framework: "net10.0",
      assembly: null,
    },
  ],
  tabs: [
    {
      id: "di",
      member: {
        kind: "package",
        id: "Microsoft.Extensions.DependencyInjection.Abstractions",
        version: "10.0.0",
        framework: "net10.0",
        assembly: null,
      },
    },
  ],
  focusTabIndex: 0,
  view: {
    library: null,
    type: "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
    memberAnchor: "74b6b4b321",
    memberKey: "method:TryAddEnumerable",
    section: "Call Graph",
  },
};

let encodedDemoState: BrowserWorkspaceShareState | null = null;

function demoHref(resolved: BrowserHomeDemoResolved): string | null {
  encodedDemoState = null;
  return productHomeDemoLocationHref(resolved, stateJson => {
    const value: unknown = JSON.parse(stateJson);
    assert.ok(isBrowserWorkspaceShareState(value));
    encodedDemoState = value;
    return {
      succeeded: true,
      packet: "canonical-demo",
      failure: null,
    };
  });
}

function isBrowserWorkspaceShareState(
  value: unknown,
): value is BrowserWorkspaceShareState {
  return value !== null
    && typeof value === "object"
    && "tabs" in value
    && Array.isArray(value.tabs)
    && "contexts" in value
    && Array.isArray(value.contexts)
    && "activeTabId" in value
    && (value.activeTabId === null || typeof value.activeTabId === "string")
    && "selectedContextId" in value
    && (value.selectedContextId === null
      || typeof value.selectedContextId === "string")
    && "view" in value
    && value.view !== null
    && typeof value.view === "object";
}

function parseDemoHref(href: string) {
  const url = new URL(href, "https://inspect.local/");
  return {
    url,
    location: parseWorkspaceLocation({
      href: url.href,
      pathname: url.pathname,
      search: url.search,
      hash: url.hash,
    }, () => ({
      succeeded: true,
      state: encodedDemoState,
      failure: null,
    })),
  };
}

function requiredItem<T>(values: readonly T[], index: number, label: string): T {
  const value = values[index];
  assert.ok(value, `missing ${label} at index ${index}`);
  return value;
}

test("isProductHomeDemoId uses the installed engine catalog", () => {
  setProductHomeDemoCatalog([
    { id: "stj-serializer", title: "System.Text.Json", summary: "Browse a real package API" },
    { id: "extensions-callgraph", title: "Cross-package call graph", summary: "Trace calls across three packages" },
    { id: "stj-serialize-callgraph", title: "Serialize call graph", summary: "Dense package-local STJ graph" },
    { id: "config-bind-callgraph", title: "Configuration Bind", summary: "Recursive binder call graph" },
    { id: "options-add-callgraph", title: "Options hub", summary: "Inbound fan-in at AddOptions" },
    { id: "di-tryadd-callgraph", title: "DI TryAdd hub", summary: "Keyed/scoped Try* fan-in" },
    { id: "http-addhttpclient-callgraph", title: "AddHttpClient", summary: "HttpClient factory registration" },
    { id: "stj-getdecimal-callgraph", title: "JsonElement.GetDecimal", summary: "STJ number parse path" },
  ]);
  assert.equal(isProductHomeDemoId("stj-serializer"), true);
  assert.equal(isProductHomeDemoId("extensions-callgraph"), true);
  assert.equal(isProductHomeDemoId("stj-serialize-callgraph"), true);
  assert.equal(isProductHomeDemoId("config-bind-callgraph"), true);
  assert.equal(isProductHomeDemoId("options-add-callgraph"), true);
  assert.equal(isProductHomeDemoId("di-tryadd-callgraph"), true);
  assert.equal(isProductHomeDemoId("http-addhttpclient-callgraph"), true);
  assert.equal(isProductHomeDemoId("stj-getdecimal-callgraph"), true);
  assert.equal(isProductHomeDemoId("platform-list"), false);
  assert.equal(isProductHomeDemoId("stj"), false);
  assert.equal(isProductHomeDemoId("runtime"), false);
  assert.equal(isProductHomeDemoId("callgraph"), false);
  assert.equal(isProductHomeDemoId(""), false);
  assert.equal(isProductHomeDemoId(undefined), false);
});

test("stj-serializer deep link selects JsonSerializer on STJ 10.0.0", () => {
  const href = demoHref(stjResolved);
  assert.ok(href);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), "System.Text.Json");
  assert.deepEqual(location.tabs, [{
    id: "System.Text.Json",
    version: "10.0.0",
    framework: "net10.0",
    shareId: "t0",
    shareKind: "package",
    shareSource: "System.Text.Json",
    runtimeIdentifier: null,
  }]);
  assert.equal(location.active, 0);
  assert.equal(location.type, "System.Text.Json.JsonSerializer");
  assert.equal(location.package, "System.Text.Json");
});

test("unversioned platform residual maps to the browser runtime pack", () => {
  const href = demoHref(unversionedRuntimeResolved);
  assert.ok(href);
  assert.deepEqual(encodedDemoState?.contexts, [{
    id: "g0",
    tabIds: ["t1", "t0"],
  }]);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), "Microsoft.NETCore.App");
  assert.deepEqual(location.tabs, [
    {
      id: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
      shareId: "t0",
      shareKind: "package",
      shareSource: "System.Text.Json",
      runtimeIdentifier: null,
    },
    {
      id: "Microsoft.NETCore.App",
      version: PLATFORM_RUNTIME_PACK.version,
      framework: PLATFORM_RUNTIME_PACK.framework,
      shareId: "t1",
      shareKind: "group",
      shareSource: ":Platform",
      runtimeIdentifier: null,
    },
  ]);
  assert.equal(location.active, 1);
  assert.equal(location.library, "System.Private.CoreLib");
  assert.equal(location.type, "System.Collections.Generic.List`1");
  assert.equal(location.package, "Microsoft.NETCore.App");
});

test("extensions-callgraph delegates execution to the engine instead of encoding a location", () => {
  assert.equal(demoHref(callGraphResolved), null);
});

test("single-package Call Graph demos also delegate to the engine", () => {
  const serializeResolved: BrowserHomeDemoResolved = {
    id: "stj-serialize-callgraph",
    title: "Serialize call graph",
    summary: "Dense package-local STJ graph",
    workspaceMembers: [
      {
        kind: "package",
        id: "System.Text.Json",
        version: "10.0.0",
        framework: "net10.0",
        assembly: null,
      },
    ],
    tabs: [
      {
        id: "stj",
        member: {
          kind: "package",
          id: "System.Text.Json",
          version: "10.0.0",
          framework: "net10.0",
          assembly: null,
        },
      },
    ],
    focusTabIndex: 0,
    view: {
      library: null,
      type: "System.Text.Json.JsonSerializer",
      memberAnchor: "1dc14dd1fb",
      memberKey: "method:Serialize",
      section: "Call Graph",
    },
  };
  assert.equal(demoHref(serializeResolved), null);
});

test("platform residual rejects pinned runtime coordinates", () => {
  const pinned = {
    ...unversionedRuntimeResolved,
    tabs: [
      requiredItem(unversionedRuntimeResolved.tabs, 0, "package tab"),
      {
        id: "runtime",
        member: {
          kind: "platform" as const,
          id: "runtime",
          version: "11.0.0",
          framework: "net11.0",
          assembly: "System.Private.CoreLib",
        },
      },
    ],
  };
  assert.throws(
    () => demoHref(pinned),
    /unversioned shape/,
  );
});

test("home demo row keeps pending slots before the engine catalog installs", () => {
  setProductHomeDemoCatalog([]);
  const pending = homeDemoRowHtml(true, value => value);
  assert.equal(
    (pending.match(/home-demo-pending/g) || []).length,
    HOME_DEMO_PENDING_SLOT_COUNT,
  );
  assert.match(pending, /disabled/);
  assert.equal(homeDemoRowHtml(false, value => value), "");

  setProductHomeDemoCatalog([
    { id: "stj-serializer", title: "System.Text.Json", summary: "Browse a real package API" },
  ]);
  const ready = homeDemoRowHtml(false, value => value);
  assert.match(ready, /data-home-demo="stj-serializer"/);
  assert.doesNotMatch(ready, /home-demo-pending/);
  assert.doesNotMatch(ready, /disabled/);
  const stillLoading = homeDemoRowHtml(true, value => value);
  assert.match(stillLoading, /disabled/);
});
