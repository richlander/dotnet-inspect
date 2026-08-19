// Declarative home demos for inspect-web.
// Shape follows docs/design/workspace-definitions.md (scenario + peer records).
// Activation lowers to the existing share-packet restore path — no demo-only loader.

import { base64UrlEncode } from "./data.js";

const pkg = (id, version, framework) => ({
  kind: "package",
  id,
  version,
  framework
});

const diAbstractions = pkg(
  "Microsoft.Extensions.DependencyInjection.Abstractions",
  "10.0.0",
  "net10.0");
const logging = pkg("Microsoft.Extensions.Logging", "10.0.0", "net10.0");
const http = pkg("Microsoft.Extensions.Http", "10.0.0", "net10.0");
const stj = pkg("System.Text.Json", "10.0.0", "net10.0");
const runtime = {
  kind: "platform",
  // Browser still addresses the platform via the Microsoft.NETCore.App pseudo id
  // in the current share packet; keep that wire form until group subscriptions ship.
  id: "Microsoft.NETCore.App",
  version: "10.0.10",
  framework: "net10.0",
  family: "runtime"
};

/** @type {Record<string, object>} */
export const HOME_DEMO_SCENARIOS = Object.freeze({
  stj: Object.freeze({
    schemaVersion: 1,
    kind: "scenario",
    id: "stj",
    title: "System.Text.Json",
    summary: "Browse a real package API",
    workspace: Object.freeze({
      schemaVersion: 1,
      kind: "workspace",
      id: "stj-serializer-tour",
      contexts: Object.freeze([
        Object.freeze({
          name: "stj",
          members: Object.freeze([stj])
        })
      ])
    }),
    navigation: Object.freeze({
      schemaVersion: 1,
      kind: "navigation",
      id: "stj-navigation",
      tabs: Object.freeze([
        Object.freeze({ id: "stj", coordinate: stj })
      ]),
      focus: "stj"
    }),
    view: Object.freeze({
      schemaVersion: 1,
      kind: "view",
      id: "stj-serializer-view",
      type: "System.Text.Json.JsonSerializer"
    })
  }),
  runtime: Object.freeze({
    schemaVersion: 1,
    kind: "scenario",
    id: "runtime",
    title: ".NET Platform",
    summary: "Inspect platform BCL types",
    workspace: Object.freeze({
      schemaVersion: 1,
      kind: "workspace",
      id: "platform-list-tour",
      contexts: Object.freeze([
        Object.freeze({
          name: "platform",
          members: Object.freeze([stj, runtime])
        })
      ])
    }),
    navigation: Object.freeze({
      schemaVersion: 1,
      kind: "navigation",
      id: "platform-navigation",
      tabs: Object.freeze([
        Object.freeze({ id: "stj", coordinate: stj }),
        Object.freeze({ id: "runtime", coordinate: runtime })
      ]),
      focus: "runtime"
    }),
    view: Object.freeze({
      schemaVersion: 1,
      kind: "view",
      id: "platform-list-view",
      library: "System.Private.CoreLib",
      type: "System.Collections.Generic.List`1"
    })
  }),
  callgraph: Object.freeze({
    schemaVersion: 1,
    kind: "scenario",
    id: "callgraph",
    title: "Cross-package call graph",
    summary: "Trace calls across three packages",
    workspace: Object.freeze({
      schemaVersion: 1,
      kind: "workspace",
      id: "extensions-callgraph",
      contexts: Object.freeze([
        Object.freeze({
          name: "extensions",
          members: Object.freeze([diAbstractions, logging, http])
        })
      ])
    }),
    navigation: Object.freeze({
      schemaVersion: 1,
      kind: "navigation",
      id: "extensions-callgraph-navigation",
      tabs: Object.freeze([
        Object.freeze({ id: "di", coordinate: diAbstractions }),
        Object.freeze({ id: "logging", coordinate: logging }),
        Object.freeze({ id: "http", coordinate: http })
      ]),
      focus: "di"
    }),
    view: Object.freeze({
      schemaVersion: 1,
      kind: "view",
      id: "try-add-enumerable-call-graph",
      type: "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
      // member group key used by the workbench member index (kind:name)
      memberKey: "method:TryAddEnumerable",
      // MemberAnchor fingerprint — stable overload selector (not positional index)
      memberAnchor: "74b6b4b321",
      section: "call-graph"
    })
  })
});

export const HOME_DEMO_ORDER = Object.freeze(["stj", "callgraph", "runtime"]);

function coordinateToTab(coordinate) {
  return {
    id: coordinate.id,
    version: coordinate.version || "latest",
    framework: coordinate.framework || ""
  };
}

/**
 * Lower a registered home-demo scenario to the current restore location + share URL.
 * This is the browser projection of a scenario composition until the product-owned
 * definition loader and packet v1 ship.
 */
export function compileHomeDemo(kind) {
  const scenario = HOME_DEMO_SCENARIOS[kind];
  if (!scenario) return null;

  const navTabs = scenario.navigation?.tabs ?? [];
  if (!navTabs.length) return null;

  const tabs = navTabs.map(tab => coordinateToTab(tab.coordinate));
  const focusIndex = Math.max(0, navTabs.findIndex(tab => tab.id === scenario.navigation.focus));
  const active = focusIndex >= 0 ? focusIndex : 0;
  const focus = tabs[active];
  const view = scenario.view || {};

  const packet = {
    t: tabs.map(tab => [tab.id, tab.version, tab.framework]),
    a: active
  };
  if (view.library) packet.l = view.library;
  if (view.lens) packet.v = view.lens;
  if (view.type) packet.y = view.type;
  if (view.memberKey) packet.m = view.memberKey;
  if (view.memberAnchor) packet.d = view.memberAnchor;
  else if (view.overload != null && view.overload !== "") packet.o = view.overload;
  if (view.section) packet.c = view.section;

  const encoded = base64UrlEncode(JSON.stringify(packet));
  const href = `?package=${encodeURIComponent(focus.id)}&w=${encoded}`;

  return {
    id: scenario.id,
    title: scenario.title,
    summary: scenario.summary,
    href,
    packet,
    location: {
      package: focus.id,
      version: focus.version,
      framework: focus.framework,
      tabs,
      active,
      type: view.type ?? null,
      member: view.memberKey ?? null,
      memberAnchor: view.memberAnchor != null ? String(view.memberAnchor) : null,
      overload: view.memberAnchor
        ? null
        : (view.overload != null && view.overload !== "" ? String(view.overload) : null),
      section: view.section ?? null,
      library: view.library ?? null,
      lens: null,
      atPackageRoot: false,
      packageLens: null,
      workspaceNotice: ""
    },
    deepLink: {
      type: view.type ?? null,
      member: view.memberKey ?? null,
      memberAnchor: view.memberAnchor != null ? String(view.memberAnchor) : null,
      overload: view.memberAnchor
        ? null
        : (view.overload != null && view.overload !== "" ? String(view.overload) : null),
      section: view.section ?? null
    }
  };
}

export function homeDemoCatalog() {
  return HOME_DEMO_ORDER
    .map(id => compileHomeDemo(id))
    .filter(Boolean);
}
