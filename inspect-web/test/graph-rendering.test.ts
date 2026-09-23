import assert from "node:assert/strict";
import test from "node:test";
import { mermaidLabel } from "../src/data.ts";
import {
  buildAnnotatedRelationshipGraphMermaid,
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid,
  resolveMermaidCssVariables,
  styleCallGraphMermaid,
} from "../src/graph-mermaid.ts";

import {
  packageAt,
} from "./composition-root-test-fixture.ts";
import { sampleInvocationTarget } from "./annotated-source-result-fixture.ts";
import { callGraphLegendHtml } from "../src/graph-legends.ts";

test("Annotated Source relationship graphs aggregate stable edges without losing occurrences", () => {
  const graph = buildAnnotatedRelationshipGraphMermaid([
    {
      edgeRow: 7,
      factId: 3,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 0,
      operandToken: 0x0A000001,
      kind: "Call",
      inLoop: false,
      target: sampleInvocationTarget,
    },
    {
      edgeRow: 7,
      factId: 4,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 1,
      operandToken: 0x0A000001,
      kind: "CallVirtual",
      inLoop: true,
      target: sampleInvocationTarget,
    },
  ]);

  assert.ok(graph);
  assert.equal(graph.edges.length, 1);
  assert.deepEqual(graph.edges[0]?.factIds, [3, 4]);
  assert.deepEqual(graph.edges[0]?.kinds, ["Call", "CallVirtual"]);
  assert.equal(graph.edges[0]?.inLoop, true);
  assert.equal(graph.edges[0]?.destinations.length, 1);
  assert.deepEqual(graph.edges[0]?.destinations[0]?.factIds, [3, 4]);
  assert.equal((graph.definition.match(/ar0 -->/g) ?? []).length, 1);
  assert.match(graph.definition, /Call \/ Virtual call ×2 · loop/);
  assert.match(
    graph.definition,
    /Example\.Targets\.Target\(System\.Int32\)/,
  );
});

test("Annotated Source relationship graphs retain version-distinct occurrence targets", () => {
  const firstTarget = {
    ...sampleInvocationTarget,
    assemblyVersion: "1.0.0.0",
    surfaceAssemblyId: "surface-v1",
  };
  const graph = buildAnnotatedRelationshipGraphMermaid([
    {
      edgeRow: 7,
      factId: 3,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 0,
      operandToken: 0x0A000001,
      kind: "Call",
      inLoop: false,
      target: firstTarget,
    },
    {
      edgeRow: 7,
      factId: 4,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 1,
      operandToken: 0x0A000001,
      kind: "Call",
      inLoop: false,
      target: {
        ...firstTarget,
        assemblyVersion: "2.0.0.0",
        surfaceAssemblyId: "surface-v2",
      },
    },
  ]);

  assert.ok(graph);
  assert.equal(graph.edges.length, 1);
  assert.deepEqual(
    graph.edges[0]?.destinations.map(destination => [
      destination.relationshipIndex,
      destination.factIds,
      destination.target.assemblyVersion,
      destination.target.surfaceAssemblyId,
    ]),
    [
      [0, [3], "1.0.0.0", "surface-v1"],
      [1, [4], "2.0.0.0", "surface-v2"],
    ],
  );
});

test("Mermaid labels contain grammar-significant metadata", () => {
  const encoded = mermaidLabel(
    "A\"B\n<x>&\\\u2028\u202E\u200D\uD800X\uDC00\u{E0001}-Caf\u00E9\u{1F600}");

  assert.equal(
    encoded,
    "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028"
      + "&#92;u202E&#92;u200D&#92;uD800X&#92;uDC00"
      + "&#92;uDB40&#92;uDC01-Caf\u00E9\u{1F600}");
  for (const character of [
    '"', "\n", "<", ">", "\\", "\u2028", "\u202E", "\u200D", "\uD800", "\uDC00"
  ]) {
    assert.equal(encoded.includes(character), false);
  }
  assert.equal(encoded.endsWith("-Caf\u00E9\u{1F600}"), true);
});

test("type graph rendering contains artifact labels", () => {
  const definition = buildTypeGraphMermaid({
    graphNodes: [
      {
        id: "self",
        displayName: "Example.A\u202E\uD800-Caf\u00E9\u{1F600}",
        role: "self"
      },
      { id: "base", displayName: "Example.Base", role: "base" }
    ],
    graphEdges: [{ fromId: "self", toId: "base" }]
  });
  assert.ok(definition, "the fixture graph must render a mermaid definition");

  assert.match(
    definition,
    /t0\["A&#92;u202E&#92;uD800-Café😀"\]:::self/);
  assert.equal(definition.includes("\u202E"), false);
  assert.equal(definition.includes("\uD800"), false);
  assert.match(
    definition,
    /classDef self fill:var\(--graph-target-fill\),stroke:var\(--graph-target-stroke\),color:var\(--graph-target-text\)/);
});

test("Mermaid resolves the current theme without inventing missing colors", () => {
  assert.equal(
    resolveMermaidCssVariables(
      "classDef self fill:var(--accent-soft),stroke:var(--accent);",
      name => name === "--accent-soft" ? " #abcdef " : ""),
    "classDef self fill:#abcdef,stroke:var(--accent);");
});

test("Call graph rendering lowers production roles to the legend palette", () => {
  const definition = styleCallGraphMermaid(
    `graph LR
      n0[Process]:::focus --> n1[Same type]:::normal
      n0 --> n2[Same assembly]:::normal
      n0 --> n3[Different assembly]:::external
      n0 --> n4[Same type name, different assembly]:::external`,
    [
      {
        id: "n0", assembly: "Example", assemblyVersion: "1.0.0.0",
        typeDefinitionId: "Example.Worker", kind: "focus",
      },
      {
        id: "n1", assembly: "Example", assemblyVersion: "1.0.0.0",
        typeDefinitionId: "Example.Worker", kind: "normal",
      },
      {
        id: "n2", assembly: "Example", assemblyVersion: "1.0.0.0",
        typeDefinitionId: "Example.Helper", kind: "normal",
      },
      {
        id: "n3", assembly: "Other", assemblyVersion: "2.0.0.0",
        typeDefinitionId: "Other.Helper", kind: "external",
      },
      {
        id: "n4", assembly: "Other", assemblyVersion: "2.0.0.0",
        typeDefinitionId: "Example.Worker", kind: "external",
      },
    ]);
  assert.match(definition, /classDef target fill:var\(--graph-target-fill\)/);
  assert.match(definition, /class n0 target;/);
  assert.match(definition, /class n1 sameType;/);
  assert.match(definition, /class n2 differentType;/);
  assert.match(definition, /class n3,n4 differentAssembly;/);
});

test("Supply-chain call graphs preserve connector and boundary roles", () => {
  const definition = styleCallGraphMermaid(
    `graph LR
      n0[Target] --> n1[Microsoft.Extensions]
      n1 --> n2[OpenTelemetry.Api]
      n0 --> n3[Unknown]`,
    [
      {
        id: "n0", assembly: "Root", kind: "focus",
      },
      {
        id: "n1", assembly: "Microsoft.Extensions.Options", kind: "connector",
      },
      {
        id: "n2", assembly: "OpenTelemetry.Api", kind: "boundary",
      },
      {
        id: "n3", assembly: "System.Private.CoreLib",
        kind: "unclassified-boundary",
      },
    ]);

  assert.match(definition, /class n1 baselineConnector;/);
  assert.match(definition, /class n2 supplyChainBoundary;/);
  assert.match(definition, /class n3 unclassifiedBoundary;/);
  const legend = callGraphLegendHtml(true);
  assert.match(legend, /baseline connector/);
  assert.match(legend, /highlighted dependency/);
  assert.match(legend, /unclassified boundary/);
});

test("dependency graph rendering contains artifact labels", async () => {
  const root = packageAt("1.0.0", "net8.0");
  const definition = await buildDependencyGraphMermaid(
    {
      package: root,
      packages: [root],
      packageDependencies: {
        dependencyGroupError: "",
        dependencyGroups: [{
          index: 0,
          framework: "net8.0",
          isActive: true,
          dependencies: [{
            id: "Dependency\u200D\uDC00-Caf\u00E9\u{1F600}",
            versionRange: ""
          }]
        }]
      },
      dependenciesGroupIndex: 0,
      workspaceDependencies: {}
    },
    () => null,
    (inspectedPackageId, packageIds) => packageIds.map(packageId =>
      packageId === inspectedPackageId ? "inspected" : "external"));
  assert.ok(definition, "the fixture graph must render a mermaid definition");

  assert.match(
    definition.definition,
    /d1\["Dependency&#92;u200D&#92;uDC00-Café😀"\]:::external/);
  assert.equal(definition.definition.includes("\u200D"), false);
  assert.equal(definition.definition.includes("\uDC00"), false);
  assert.match(
    definition.definition,
    /classDef inspected fill:var\(--graph-target-fill\),stroke:var\(--graph-target-stroke\),color:var\(--graph-target-text\)/);
});
