import type {
  BrowserVocabularyInspection,
  BrowserVocabularyTerm,
} from "./facades/inspect-web-catalog.d.ts";

type PackageFacade = typeof import("./facades/inspect-web-package.d.ts");
type DescriptorSet =
  PackageFacade["jsonSchemaVocabularyDescriptors"];
type Descriptor = DescriptorSet[number];
type Binding = Descriptor["bindings"][number];

export type PackageQueryDurableRowDescriptors = DescriptorSet;
export type PackageQueryDurableRowTerm = Binding["term"];

export interface PackageQueryDurableRowPresentationField {
  readonly ordinal: number;
  readonly schemaLocation: string;
  readonly schema: unknown;
  readonly term: BrowserVocabularyTerm;
}

export interface PackageQueryDurableRowPresentationLayout {
  readonly contract: string;
  readonly schemaIdentity: string;
  readonly descriptorIdentity: string;
  readonly vocabularySnapshotIdentity: string;
  readonly fields:
    readonly PackageQueryDurableRowPresentationField[];
}

interface PackageQueryDurableRowField
  extends PackageQueryDurableRowPresentationField {
  readonly schemaLocation: Binding["schemaLocation"];
}

export interface PackageQueryDurableRowLayout
  extends PackageQueryDurableRowPresentationLayout {
  readonly contract: Descriptor["contract"];
  readonly schemaIdentity: Descriptor["schemaIdentity"];
  readonly descriptorIdentity: Descriptor["descriptorIdentity"];
  readonly vocabularySnapshotIdentity:
    Descriptor["vocabularySnapshotIdentity"];
  readonly fields: readonly PackageQueryDurableRowField[];
}

export async function resolvePackageQueryDurableRowLayout(
  descriptors: DescriptorSet,
  inspection: BrowserVocabularyInspection,
): Promise<PackageQueryDurableRowLayout> {
  const diagnostic = inspection.diagnostics.find(candidate =>
    candidate.severity === "Error");
  if (diagnostic) {
    throw new Error(
      `Vocabulary inspection failed: ${diagnostic.summary}`);
  }

  const matches = descriptors.filter(descriptor =>
    descriptor.contract === "package-query.durable-row"
    && descriptor.direction === "serialize");
  if (matches.length !== 1) {
    throw new Error(
      "Package Query requires exactly one serialized durable-row descriptor.");
  }
  const descriptor = matches[0]!;
  if (descriptor.formatVersion !== 1
    || descriptor.dialect
      !== "https://json-schema.org/draft/2020-12/schema") {
    throw new Error(
      "Package Query durable-row descriptor has an unsupported format.");
  }
  await verifyDescriptorIdentity(descriptor);

  const snapshot = inspection.content;
  if (descriptor.vocabularyCatalog !== snapshot.catalog.value) {
    throw new Error(
      "Package Query durable-row descriptor targets another Vocabulary catalog.");
  }
  if (descriptor.vocabularySnapshotIdentity !== snapshot.identity.value) {
    throw new Error(
      "Package Query durable-row descriptor and Vocabulary snapshot differ.");
  }

  const vocabularies = snapshot.vocabularies.filter(vocabulary =>
    vocabulary.identity.value
      === descriptor.bindings[0]?.vocabulary);
  if (vocabularies.length !== 1) {
    throw new Error(
      "Package Query durable-row Vocabulary must occur exactly once.");
  }
  const vocabulary = vocabularies[0]!;
  const prefixItems = descriptor.schema.prefixItems;
  if (descriptor.schema.type !== "array"
    || !Object.is(descriptor.schema.items, false)
    || descriptor.schema.minItems !== prefixItems.length
    || descriptor.schema.maxItems !== prefixItems.length
    || descriptor.bindings.length !== prefixItems.length) {
    throw new Error(
      "Package Query durable-row schema is not one complete positional row.");
  }

  const ordinals = new Set<number>();
  const terms = new Set<string>();
  const fields = descriptor.bindings.map(binding => {
    if (binding.vocabulary !== vocabulary.identity.value) {
      throw new Error(
        `Package Query binding '${binding.term}' targets another Vocabulary.`);
    }
    const location = /^\/prefixItems\/(0|[1-9][0-9]*)$/u
      .exec(binding.schemaLocation);
    if (location === null) {
      throw new Error(
        `Package Query binding '${binding.term}' has a non-positional location.`);
    }
    const ordinal = Number(location[1]);
    if (ordinal >= prefixItems.length || !ordinals.add(ordinal)) {
      throw new Error(
        `Package Query binding '${binding.term}' has an invalid or duplicate ordinal.`);
    }
    if (!terms.add(binding.term)) {
      throw new Error(
        `Package Query binding term '${binding.term}' occurs more than once.`);
    }
    const termMatches = vocabulary.terms.filter(term =>
      term.identity.value === binding.term);
    if (termMatches.length !== 1) {
      throw new Error(
        `Package Query Vocabulary must contain exactly one '${binding.term}' term.`);
    }
    return {
      ordinal,
      schemaLocation: binding.schemaLocation,
      schema: prefixItems[ordinal],
      term: termMatches[0]!,
    };
  }).sort((left, right) => left.ordinal - right.ordinal);
  if (fields.some((field, index) => field.ordinal !== index)) {
    throw new Error(
      "Package Query durable-row bindings must cover every ordinal.");
  }

  return {
    contract: descriptor.contract,
    schemaIdentity: descriptor.schemaIdentity,
    descriptorIdentity: descriptor.descriptorIdentity,
    vocabularySnapshotIdentity:
      descriptor.vocabularySnapshotIdentity,
    fields,
  };
}

async function verifyDescriptorIdentity(
  descriptor: Descriptor,
): Promise<void> {
  const schemaIdentity = await digestIdentity(
    canonicalJson(descriptor.schema, true));
  if (schemaIdentity !== descriptor.schemaIdentity) {
    throw new Error(
      "Package Query durable-row schema does not match its exact identity.");
  }
  if (descriptor.schema.$id
    !== `urn:dotnet-inspect:json-schema:${schemaIdentity}`) {
    throw new Error(
      "Package Query durable-row schema has an inconsistent $id.");
  }

  const descriptorIdentity = await digestIdentity(canonicalJson({
    formatVersion: descriptor.formatVersion,
    contract: descriptor.contract,
    direction: descriptor.direction,
    dialect: descriptor.dialect,
    schemaIdentity: descriptor.schemaIdentity,
    vocabularyCatalog: descriptor.vocabularyCatalog,
    vocabularySnapshotIdentity:
      descriptor.vocabularySnapshotIdentity,
    schema: descriptor.schema,
    bindings: descriptor.bindings,
  }));
  if (descriptorIdentity !== descriptor.descriptorIdentity) {
    throw new Error(
      "Package Query durable-row descriptor does not match its exact identity.");
  }
}

function canonicalJson(
  value: unknown,
  omitRootId = false,
  isRoot = true,
): string {
  if (value === null) return "null";
  switch (typeof value) {
    case "boolean":
      return value ? "true" : "false";
    case "bigint":
      return value.toString();
    case "number":
      if (!Number.isFinite(value)) {
        throw new Error(
          "Package Query durable-row schema contains a non-finite number.");
      }
      return JSON.stringify(value);
    case "string":
      return JSON.stringify(value);
    case "object": {
      if (Array.isArray(value)) {
        return `[${value.map(item =>
          canonicalJson(item, false, false)).join(",")}]`;
      }
      if (!isRecord(value)) {
        throw new Error(
          "Package Query durable-row schema contains an unsupported object.");
      }
      const keys = Object.keys(value)
        .filter(key => !(isRoot && omitRootId && key === "$id"))
        .sort();
      return `{${keys.map(key =>
        `${JSON.stringify(key)}:${canonicalJson(
          value[key],
          false,
          false,
        )}`).join(",")}}`;
    }
    default:
      throw new Error(
        "Package Query durable-row schema contains an unsupported value.");
  }
}

function isRecord(
  value: unknown,
): value is Readonly<Record<string, unknown>> {
  return typeof value === "object"
    && value !== null
    && !Array.isArray(value);
}

async function digestIdentity(canonicalJsonText: string): Promise<string> {
  const subtle = globalThis.crypto?.subtle;
  if (subtle === undefined) {
    throw new Error(
      "Package Query durable-row identity verification requires Web Crypto.");
  }
  const digest = await subtle.digest(
    "SHA-256",
    new TextEncoder().encode(canonicalJsonText),
  );
  return `sha256:${Array.from(new Uint8Array(digest), byte =>
    byte.toString(16).padStart(2, "0")).join("")}`;
}

export function packageQueryDurableRowField(
  layout: PackageQueryDurableRowPresentationLayout,
  term: PackageQueryDurableRowTerm,
): PackageQueryDurableRowPresentationField {
  const matches = layout.fields.filter(field =>
    field.term.identity.value === term);
  if (matches.length !== 1) {
    throw new Error(
      `Package Query durable-row layout must contain exactly one '${term}' field.`);
  }
  return matches[0]!;
}
