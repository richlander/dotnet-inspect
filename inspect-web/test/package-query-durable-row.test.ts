import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import type {
  BrowserVocabularyInspection,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  packageQueryDurableRowField,
  resolvePackageQueryDurableRowLayout,
  type PackageQueryDurableRowDescriptors,
} from "../src/package-query-durable-row.ts";

function descriptors(): PackageQueryDurableRowDescriptors {
  const source = readFileSync(new URL(
    "../DotnetInspect.Web/facades/inspect-web-package.ts",
    import.meta.url), "utf8");
  const line = source.split("\n").find(candidate =>
    candidate.startsWith(
      "export const jsonSchemaVocabularyDescriptors = "));
  assert.ok(line);
  const json = line.slice(
    line.indexOf("=") + 1,
    line.lastIndexOf(" as const;")).trim();
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return JSON.parse(json) as PackageQueryDurableRowDescriptors;
}

function vocabulary(
  values: PackageQueryDurableRowDescriptors,
): BrowserVocabularyInspection {
  const descriptor = values[0];
  assert.ok(descriptor);
  return {
    content: {
      formatVersion: 1,
      catalog: { value: descriptor.vocabularyCatalog },
      identity: { value: descriptor.vocabularySnapshotIdentity },
      vocabularies: [{
        identity: { value: descriptor.bindings[0].vocabulary },
        displayLabel: "Package Query durable row",
        summary: "Durable Package Query fields.",
        maps: [],
        terms: descriptor.bindings.map(binding => ({
          identity: { value: binding.term },
          displayLabel: `Label ${binding.term}`,
          summary: `Summary ${binding.term}.`,
          mapEntries: [],
        })),
      }],
    },
    share: {
      kind: "nonProjectable",
      path: "vocabulary/share",
      reason: "Static catalog.",
    },
    diagnostics: [],
  };
}

test("Package Query resolves generated positional bindings through Vocabulary", () => {
  const values = descriptors();
  const layout = resolvePackageQueryDurableRowLayout(
    values,
    vocabulary(values));

  assert.equal(layout.fields.length, 12);
  assert.deepEqual(
    layout.fields.map(field => field.ordinal),
    Array.from({ length: 12 }, (_value, index) => index));
  assert.equal(
    packageQueryDurableRowField(layout, "total-downloads")
      .term.displayLabel,
    "Label total-downloads");
});

test("Package Query rejects a stale Vocabulary snapshot", () => {
  const values = descriptors();
  const current = vocabulary(values);
  const inspection: BrowserVocabularyInspection = {
    ...current,
    content: {
      ...current.content,
      identity: { value: `sha256:${"0".repeat(64)}` },
    },
  };

  assert.throws(
    () => resolvePackageQueryDurableRowLayout(values, inspection),
    /descriptor and Vocabulary snapshot differ/);
});
