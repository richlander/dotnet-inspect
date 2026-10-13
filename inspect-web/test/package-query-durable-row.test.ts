import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import type {
  BrowserVocabularyInspection,
} from "../src/facades/inspect-web-catalog.d.ts";
import {
  parseGeneratedStaticJson,
} from "../scripts/generated-static-json.ts";
import {
  packageQueryDurableRowField,
  resolvePackageQueryDurableRowLayout,
  type PackageQueryDurableRowDescriptors,
} from "../src/package-query-durable-row.ts";

async function descriptors(): Promise<PackageQueryDurableRowDescriptors> {
  const source = readFileSync(new URL(
    "../DotnetInspect.Web/facades/inspect-web-package.ts",
    import.meta.url), "utf8");
  const line = source.split("\n").find(candidate =>
    candidate.startsWith(
      "export const jsonSchemaVocabularyDescriptors = "));
  assert.ok(line);
  const initializer = line.slice(
    line.indexOf("=") + 1,
    line.lastIndexOf(" as const;")).trim();
  const values = parseGeneratedStaticJson(initializer);
  if (!isDescriptorSet(values)) {
    throw new Error("Generated Package facade descriptor export is invalid.");
  }
  return values;
}

function isDescriptorSet(
  value: unknown,
): value is PackageQueryDurableRowDescriptors {
  return Array.isArray(value)
    && value.length > 0
    && value.every(descriptor =>
      typeof descriptor === "object"
      && descriptor !== null
      && "schema" in descriptor
      && "bindings" in descriptor);
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

test("Package Query resolves generated positional bindings through Vocabulary", async () => {
  const values = await descriptors();
  const layout = await resolvePackageQueryDurableRowLayout(
    values,
    vocabulary(values));

  assert.equal(layout.fields.length, 13);
  assert.deepEqual(
    layout.fields.map(field => field.ordinal),
    Array.from({ length: 13 }, (_value, index) => index));
  assert.doesNotThrow(() => JSON.stringify(layout));
  assert.equal("schema" in layout.fields[0]!, false);
  assert.equal(
    packageQueryDurableRowField(layout, "total-downloads")
      .term.displayLabel,
    "Label total-downloads");
  assert.equal(
    typeof values[0]?.schema.$defs.BrowserPackageQueryEvidence
      .properties.number.anyOf[0].maximum,
    "bigint");
  assert.equal(
    values[0]?.schema.$defs.BrowserPackageQueryEvidence
      .properties.number.anyOf[0].maximum,
    9223372036854775807n);
});

test("Package Query rejects a stale Vocabulary snapshot", async () => {
  const values = await descriptors();
  const current = vocabulary(values);
  const inspection: BrowserVocabularyInspection = {
    ...current,
    content: {
      ...current.content,
      identity: { value: `sha256:${"0".repeat(64)}` },
    },
  };

  await assert.rejects(
    resolvePackageQueryDurableRowLayout(values, inspection),
    /descriptor and Vocabulary snapshot differ/);
});

test("Package Query rejects runtime schema drift under an exact identity", async () => {
  const values = await descriptors();
  const maximum = values[0]?.schema.$defs.BrowserPackageQueryEvidence
    .properties.number.anyOf[0];
  assert.ok(maximum);
  assert.equal(
    Reflect.set(maximum, "maximum", 9223372036854775808n),
    true);

  await assert.rejects(
    resolvePackageQueryDurableRowLayout(values, vocabulary(values)),
    /schema does not match its exact identity/);
});
