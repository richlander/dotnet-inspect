import assert from "node:assert/strict";
import test from "node:test";
import type { BrowserHomeDemoResolved } from "../src/inspect-web-engine.d.ts";
import {
  HOME_DEMO_PENDING_SLOT_COUNT,
  PLATFORM_RUNTIME_PACK,
  callGraphDemoRunnerSpec,
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

const platformResolved: BrowserHomeDemoResolved = {
  id: "platform-list",
  title: ".NET Platform",
  summary: "Inspect platform BCL types",
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

function parseDemoHref(href: string) {
  const url = new URL(href, "https://inspect.local/");
  return {
    url,
    location: parseWorkspaceLocation({
      href: url.href,
      pathname: url.pathname,
      search: url.search,
      hash: url.hash,
    }),
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
    { id: "platform-list", title: ".NET Platform", summary: "Inspect platform BCL types" },
  ]);
  assert.equal(isProductHomeDemoId("stj-serializer"), true);
  assert.equal(isProductHomeDemoId("platform-list"), true);
  assert.equal(isProductHomeDemoId("extensions-callgraph"), true);
  assert.equal(isProductHomeDemoId("stj"), false);
  assert.equal(isProductHomeDemoId("runtime"), false);
  assert.equal(isProductHomeDemoId("callgraph"), false);
  assert.equal(isProductHomeDemoId(""), false);
  assert.equal(isProductHomeDemoId(undefined), false);
});

test("stj-serializer deep link selects JsonSerializer on STJ 10.0.0", () => {
  const href = productHomeDemoLocationHref(stjResolved);
  assert.ok(href);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), "System.Text.Json");
  assert.deepEqual(location.tabs, [{
    id: "System.Text.Json",
    version: "10.0.0",
    framework: "net10.0",
  }]);
  assert.equal(location.active, 0);
  assert.equal(location.type, "System.Text.Json.JsonSerializer");
  assert.equal(location.package, "System.Text.Json");
});

test("platform-list deep link focuses CoreLib List`1 on residual runtime pack", () => {
  const href = productHomeDemoLocationHref(platformResolved);
  assert.ok(href);
  const { url, location } = parseDemoHref(href);
  assert.equal(url.searchParams.get("package"), PLATFORM_RUNTIME_PACK.id);
  assert.deepEqual(location.tabs, [
    {
      id: "System.Text.Json",
      version: "10.0.0",
      framework: "net10.0",
    },
    { ...PLATFORM_RUNTIME_PACK },
  ]);
  assert.equal(location.active, 1);
  assert.equal(location.library, "System.Private.CoreLib");
  assert.equal(location.type, "System.Collections.Generic.List`1");
  assert.equal(location.package, PLATFORM_RUNTIME_PACK.id);
});

test("extensions-callgraph has no deep link and runner spec keeps product pins", () => {
  assert.equal(productHomeDemoLocationHref(callGraphResolved), null);
  const spec = callGraphDemoRunnerSpec(callGraphResolved);
  assert.equal(spec.packages.length, 3);
  assert.equal(spec.memberAnchorDigest, "74b6b4b321");
  assert.equal(spec.memberSection, "call-graph");
  assert.equal(spec.memberKind, "method");
  assert.equal(spec.memberName, "TryAddEnumerable");
  assert.equal(
    requiredItem(spec.packages, 0, "runner package").id,
    "Microsoft.Extensions.DependencyInjection.Abstractions",
  );
  assert.equal(
    spec.focusPackageId,
    "Microsoft.Extensions.DependencyInjection.Abstractions",
  );
});

test("call-graph runner focus follows navigation focusTabIndex", () => {
  const reordered: BrowserHomeDemoResolved = {
    ...callGraphResolved,
    workspaceMembers: [
      requiredItem(callGraphResolved.workspaceMembers, 1, "logging member"),
      requiredItem(callGraphResolved.workspaceMembers, 0, "DI member"),
      requiredItem(callGraphResolved.workspaceMembers, 2, "HTTP member"),
    ],
    tabs: [
      {
        id: "logging",
        member: requiredItem(
          callGraphResolved.workspaceMembers,
          1,
          "logging tab member"),
      },
      {
        id: "di",
        member: requiredItem(
          callGraphResolved.workspaceMembers,
          0,
          "DI tab member"),
      },
    ],
    focusTabIndex: 1,
  };
  const spec = callGraphDemoRunnerSpec(reordered);
  assert.equal(
    requiredItem(spec.packages, 0, "reordered runner package").id,
    "Microsoft.Extensions.Logging");
  assert.equal(
    spec.focusPackageId,
    "Microsoft.Extensions.DependencyInjection.Abstractions",
  );
});

test("platform residual rejects pinned runtime coordinates", () => {
  const pinned = {
    ...platformResolved,
    tabs: [
      requiredItem(platformResolved.tabs, 0, "package tab"),
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
    () => productHomeDemoLocationHref(pinned),
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
