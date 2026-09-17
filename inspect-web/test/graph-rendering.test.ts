import assert from "node:assert/strict";
import test from "node:test";
import { mermaidLabel } from "../src/data.ts";
import {
  buildDependencyGraphMermaid,
  buildTypeGraphMermaid,
  resolveMermaidCssVariables,
  styleCallGraphMermaid,
} from "../src/graph-mermaid.ts";

import {
  packageAt,
} from "./composition-root-test-fixture.ts";
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
