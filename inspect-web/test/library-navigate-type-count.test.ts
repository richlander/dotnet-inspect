import assert from "node:assert/strict";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { functionSource } from "./composition-root-test-fixture.ts";

const source = stripTypeScriptTypes(
  functionSource("libraryNavigateTypeCount"),
);

type Presentation = {
  key: string;
  ready: boolean;
  view: { forwarders: readonly unknown[] } | null;
} | null;

function count(
  pkg: {
    isRuntimePack: boolean;
    types?: readonly { id: string; accessibilityId: string }[];
  },
  definitions: number | null,
  presentation: Presentation,
  selectedKey: string | null = "xml",
  accessibility = "public",
): number | null {
  const result: unknown = runInNewContext(
    `${source}\nlibraryNavigateTypeCount(pkg, definitions);`,
    {
      pkg: { ...pkg, types: pkg.types ?? [] },
      definitions,
      platformForwarderPresentation: presentation,
      selectedPlatformForwarderKey: () => selectedKey,
      selectedTypeAccessibility: () => accessibility,
      isDefaultAccessibility: (type: { accessibilityId: string }) =>
        type.accessibilityId === "public",
      accessibilityBuckets: () => [
        { id: "public", isDefault: true },
        { id: "internal", isDefault: false },
      ],
    },
  );
  assert.ok(result === null || typeof result === "number");
  return result;
}

const xmlForwarders = {
  key: "xml",
  ready: true,
  view: { forwarders: [{ id: "System.Xml:System.Xml.XmlReader" }] },
};

test("package Libraries count their public Type definitions", () => {
  assert.equal(count({ isRuntimePack: false }, 90, null), 90);
  assert.equal(count({ isRuntimePack: false }, null, null), null);
});

test("a Platform facade counts the forwarded Types its Type list shows", () => {
  assert.equal(count({ isRuntimePack: true }, 0, xmlForwarders), 1);
  assert.equal(count({ isRuntimePack: true }, 3, xmlForwarders), 4);
  assert.equal(
    count({ isRuntimePack: true }, 5, { key: "xml", ready: true, view: { forwarders: [] } }),
    5);
});

test("a reference-declared forwarded Type counts only once", () => {
  const pkg = {
    isRuntimePack: true,
    types: [{ id: "System.Xml:System.Xml.XmlReader", accessibilityId: "public" }],
  };
  assert.equal(count(pkg, 1, xmlForwarders), 1);
  assert.equal(count(pkg, 1, {
    ...xmlForwarders,
    view: { forwarders: [
      ...xmlForwarders.view.forwarders,
      { id: "System.Xml:System.Xml.XmlWriter" },
    ] },
  }), 2);
});

test("a Platform Library stays unnumbered until its forwarder inventory is known", () => {
  assert.equal(count({ isRuntimePack: true }, 0, null), null);
  assert.equal(
    count({ isRuntimePack: true }, 0, { ...xmlForwarders, ready: false }),
    null);
  assert.equal(count({ isRuntimePack: true }, 0, xmlForwarders, "other"), null);
});

test("a non-default accessibility selection leaves Navigate unnumbered", () => {
  assert.equal(count({ isRuntimePack: false }, 90, null, "xml", "internal"), null);
  assert.equal(count({ isRuntimePack: false }, 90, null, "xml", ""), null);
  assert.equal(count({ isRuntimePack: true }, 0, xmlForwarders, "xml", "internal"), null);
});
