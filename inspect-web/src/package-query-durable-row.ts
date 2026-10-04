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

export interface PackageQueryDurableRowField
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

export function resolvePackageQueryDurableRowLayout(
  descriptors: DescriptorSet,
  inspection: BrowserVocabularyInspection,
): PackageQueryDurableRowLayout {
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
    || descriptor.schema.items !== false
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
    const matches = vocabulary.terms.filter(term =>
      term.identity.value === binding.term);
    if (matches.length !== 1) {
      throw new Error(
        `Package Query Vocabulary must contain exactly one '${binding.term}' term.`);
    }
    return {
      ordinal,
      schemaLocation: binding.schemaLocation,
      schema: prefixItems[ordinal],
      term: matches[0]!,
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
