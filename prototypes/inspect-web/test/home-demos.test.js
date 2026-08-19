import assert from "node:assert/strict";
import test from "node:test";

import {
  base64UrlDecode,
  buildSharePacket,
  deepLinkFromLocation,
  resolveMemberDeepLink
} from "../src/data.js";
import {
  compileHomeDemo,
  HOME_DEMO_ORDER,
  HOME_DEMO_SCENARIOS,
  homeDemoCatalog
} from "../src/home-demos.js";

test("home demo catalog covers the three workbench entry points", () => {
  assert.deepEqual(HOME_DEMO_ORDER, ["stj", "callgraph", "runtime"]);
  const catalog = homeDemoCatalog();
  assert.equal(catalog.length, 3);
  assert.deepEqual(catalog.map(demo => demo.id), HOME_DEMO_ORDER);
  for (const demo of catalog) {
    assert.ok(demo.href.startsWith("?package="));
    assert.match(demo.href, /&w=/);
    assert.equal(demo.location.tabs.length, demo.packet.t.length);
  }
});

test("STJ and platform demos preserve the prior curated selection shape", () => {
  const stj = compileHomeDemo("stj");
  assert.equal(stj.location.package, "System.Text.Json");
  assert.equal(stj.location.type, "System.Text.Json.JsonSerializer");
  assert.equal(stj.location.tabs.length, 1);
  assert.equal(stj.deepLink.memberAnchor, null);

  const runtime = compileHomeDemo("runtime");
  assert.equal(runtime.location.package, "Microsoft.NETCore.App");
  assert.equal(runtime.location.library, "System.Private.CoreLib");
  assert.equal(runtime.location.type, "System.Collections.Generic.List`1");
  assert.equal(runtime.location.tabs.length, 2);
  assert.equal(runtime.location.active, 1);
});

test("call-graph demo is a multi-package scenario selected by MemberAnchor digest", () => {
  const demo = compileHomeDemo("callgraph");
  assert.ok(demo);
  assert.deepEqual(
    demo.location.tabs.map(tab => tab.id),
    [
      "Microsoft.Extensions.DependencyInjection.Abstractions",
      "Microsoft.Extensions.Logging",
      "Microsoft.Extensions.Http"
    ]);
  assert.equal(demo.location.package, "Microsoft.Extensions.DependencyInjection.Abstractions");
  assert.equal(
    demo.location.type,
    "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions");
  assert.equal(demo.location.member, "method:TryAddEnumerable");
  assert.equal(demo.location.memberAnchor, "74b6b4b321");
  assert.equal(demo.location.section, "call-graph");
  assert.equal(demo.location.overload, null);
  assert.equal(demo.packet.d, "74b6b4b321");
  assert.equal(demo.packet.c, "call-graph");
  assert.equal("o" in demo.packet, false);

  // Compiled href round-trips through the same base64 packet shape the workbench restores.
  const encoded = new URL(demo.href, "https://inspect.example/").searchParams.get("w");
  const packet = JSON.parse(base64UrlDecode(encoded));
  assert.deepEqual(packet, demo.packet);
});

test("buildSharePacket prefers MemberAnchor digest over positional overload index", () => {
  const packages = [
    { id: "A", version: "1.0.0", activeFramework: "net10.0" },
    { id: "B", version: "1.0.0", activeFramework: "net10.0" }
  ];
  const packet = buildSharePacket({
    packages,
    activePackage: packages[0],
    selectedTypeId: "T",
    selectedMemberKey: "method:M",
    selectedOverloadIndex: 2,
    selectedOverloadDigest: "deadbeef01",
    memberSection: "call-graph"
  });
  assert.equal(packet.d, "deadbeef01");
  assert.equal("o" in packet, false);
  assert.equal(packet.m, "method:M");
  assert.equal(packet.c, "call-graph");

  const legacy = buildSharePacket({
    packages,
    activePackage: packages[0],
    selectedTypeId: "T",
    selectedMemberKey: "method:M",
    selectedOverloadIndex: 2,
    selectedOverloadDigest: null
  });
  assert.equal(legacy.o, 2);
  assert.equal("d" in legacy, false);
});

test("resolveMemberDeepLink selects by digest and keeps positional overload fallback", () => {
  const groups = [
    {
      key: "method:TryAddEnumerable",
      overloads: [
        { anchorDigest: "aaaaaaaaaa" },
        { anchorDigest: "74b6b4b321" },
        { anchorDigest: "bbbbbbbbbb" }
      ]
    },
    {
      key: "method:Other",
      overloads: [{ anchorDigest: "cccccccccc" }]
    }
  ];

  assert.deepEqual(
    resolveMemberDeepLink(groups, {
      member: "method:TryAddEnumerable",
      memberAnchor: "74b6b4b321"
    }),
    { memberKey: "method:TryAddEnumerable", overloadIndex: 1 });

  assert.deepEqual(
    resolveMemberDeepLink(groups, {
      memberAnchor: "74b6b4b321"
    }),
    { memberKey: "method:TryAddEnumerable", overloadIndex: 1 });

  assert.deepEqual(
    resolveMemberDeepLink(groups, {
      member: "method:TryAddEnumerable",
      overload: "2"
    }),
    { memberKey: "method:TryAddEnumerable", overloadIndex: 2 });

  assert.deepEqual(
    resolveMemberDeepLink(groups, {
      member: "method:Missing",
      overload: "0"
    }),
    { memberKey: "", overloadIndex: null });
});

test("deepLinkFromLocation carries memberAnchor", () => {
  assert.deepEqual(
    deepLinkFromLocation({
      type: "T",
      member: "method:M",
      memberAnchor: "abc",
      overload: "1",
      section: "call-graph"
    }),
    {
      type: "T",
      member: "method:M",
      memberAnchor: "abc",
      overload: "1",
      section: "call-graph"
    });
});

test("home demo scenarios stay closed record kinds from the workspace design", () => {
  for (const id of HOME_DEMO_ORDER) {
    const scenario = HOME_DEMO_SCENARIOS[id];
    assert.equal(scenario.kind, "scenario");
    assert.equal(scenario.workspace.kind, "workspace");
    assert.equal(scenario.navigation.kind, "navigation");
    assert.equal(scenario.view.kind, "view");
    assert.ok(scenario.workspace.contexts.length >= 1);
    assert.ok(scenario.navigation.tabs.length >= 1);
  }
});
