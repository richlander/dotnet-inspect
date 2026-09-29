import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserVocabularyDefinitionIdentity,
  BrowserVocabularyInspection,
  BrowserVocabularyMapDefinitionIdentity,
  BrowserVocabularyIdentity,
} from "../src/facades/inspect-web-catalog.d.ts";
import { resolveStyleCatalog } from "../src/style-vocabulary.ts";

const catalog = { value: "dotnet-inspect.product" };

function vocabulary(value: string): BrowserVocabularyDefinitionIdentity {
  return { value };
}

function map(
  _sourceVocabulary: BrowserVocabularyDefinitionIdentity,
  value: string,
): BrowserVocabularyMapDefinitionIdentity {
  return { value };
}

function fullVocabulary(value: string): BrowserVocabularyIdentity {
  return { catalog, value };
}

function fixture(
  tierTarget: string = "csharp.style-tiers",
): BrowserVocabularyInspection {
  const tiers = vocabulary("csharp.style-tiers");
  const choices = vocabulary("csharp.style-choices");
  return {
    content: {
      formatVersion: 1,
      catalog,
      identity: { value: `sha256:${"0".repeat(64)}` },
      vocabularies: [
        {
          identity: tiers,
          displayLabel: "Style tiers",
          summary: "Decompiler style tiers.",
          maps: [{
            identity: map(tiers, "byte_divergent"),
            displayLabel: "Byte Divergent",
            summary: "Whether the tier may change emitted bytes.",
            target: { kind: "scalar", scalarKind: "Boolean" },
            cardinality: "ExactlyOne",
            coverage: "Complete",
          }],
          terms: [{
            identity: { value: "naming" },
            displayLabel: "Naming",
            summary: "How identifiers are spelled.",
            mapEntries: [{
              map: map(tiers, "byte_divergent"),
              values: [{ kind: "boolean", value: false }],
            }],
          }],
        },
        {
          identity: choices,
          displayLabel: "Style choices",
          summary: "Selectable decompiler styles.",
          maps: [
            {
              identity: map(choices, "tier"),
              displayLabel: "Tier",
              summary: "Owning style tier.",
              target: {
                kind: "terms",
                reference: {
                  kind: "local",
                  vocabulary: vocabulary(tierTarget),
                },
              },
              cardinality: "ExactlyOne",
              coverage: "Complete",
            },
            {
              identity: map(choices, "oracle_endorsed"),
              displayLabel: "Oracle Endorsed",
              summary: "Whether the runtime oracle endorses the choice.",
              target: { kind: "scalar", scalarKind: "Boolean" },
              cardinality: "ExactlyOne",
              coverage: "Complete",
            },
            {
              identity: map(choices, "conflict_group"),
              displayLabel: "Conflict Group",
              summary: "Mutually exclusive selection group.",
              target: { kind: "scalar", scalarKind: "Text" },
              cardinality: "OptionalOne",
              coverage: "Complete",
            },
          ],
          terms: [{
            identity: { value: "readable-locals" },
            displayLabel: "Readable local names",
            summary: "Synthesize readable local names.",
            mapEntries: [
              {
                map: map(choices, "tier"),
                values: [{
                  kind: "term",
                  identity: {
                    vocabulary: fullVocabulary("csharp.style-tiers"),
                    value: "naming",
                  },
                }],
              },
              {
                map: map(choices, "oracle_endorsed"),
                values: [{ kind: "boolean", value: true }],
              },
              {
                map: map(choices, "conflict_group"),
                values: [{ kind: "text", value: "local-names" }],
              },
            ],
          }],
        },
      ],
    },
    share: {
      kind: "nonProjectable",
      path: "vocabulary/share",
      reason: "Static catalog.",
    },
    diagnostics: [],
  };
}

test("Settings resolves generated terms through the declared tier map", () => {
  const resolved = resolveStyleCatalog(fixture());

  assert.equal(resolved.snapshotIdentity, `sha256:${"0".repeat(64)}`);
  assert.equal(resolved.tiers.length, 1);
  assert.equal(resolved.tiers[0]!.term.identity.value, "naming");
  assert.equal(resolved.tiers[0]!.choices.length, 1);
  assert.equal(
    resolved.tiers[0]!.choices[0]!.term.identity.value,
    "readable-locals");
  assert.equal(resolved.tiers[0]!.choices[0]!.oracleEndorsed, true);
  assert.equal(
    resolved.tiers[0]!.choices[0]!.conflictGroup,
    "local-names");
});

test("Settings rejects a corrupted tier target instead of matching strings", () => {
  assert.throws(
    () => resolveStyleCatalog(fixture("csharp.style-choices")),
    /must target 'csharp\.style-tiers'/);
});
