import type {
  BrowserVocabularyDefinition,
  BrowserVocabularyDefinitionIdentity,
  BrowserVocabularyInspection,
  BrowserVocabularyMapDefinition,
  BrowserVocabularyMapEntry,
  BrowserVocabularyMapValue,
  BrowserVocabularyScalarKind,
  BrowserVocabularyTerm,
  BrowserVocabularyTermIdentity,
  BrowserVocabularyIdentity,
} from "./facades/inspect-web-catalog.d.ts";

const styleChoicesId = "csharp.style-choices";
const styleTiersId = "csharp.style-tiers";
const tierMapId = "tier";
const conflictMapId = "conflict_group";
const tierByteDivergentMapId = "byte_divergent";
const oracleEndorsedMapId = "oracle_endorsed";

export interface ResolvedStyleChoice {
  readonly term: BrowserVocabularyTerm;
  readonly conflictGroup: string | null;
  readonly oracleEndorsed: boolean;
}

export interface ResolvedStyleTier {
  readonly term: BrowserVocabularyTerm;
  readonly byteDivergent: boolean;
  readonly choices: readonly ResolvedStyleChoice[];
}

export interface ResolvedStyleCatalog {
  readonly snapshotIdentity: string;
  readonly tiers: readonly ResolvedStyleTier[];
  readonly choices: readonly ResolvedStyleChoice[];
}

export function resolveStyleCatalog(
  inspection: BrowserVocabularyInspection,
): ResolvedStyleCatalog {
  const error = inspection.diagnostics.find(diagnostic =>
    diagnostic.severity === "Error");
  if (error) throw new Error(`Vocabulary inspection failed: ${error.summary}`);

  const snapshot = inspection.content;
  const tiers = requireVocabulary(snapshot.vocabularies, styleTiersId);
  const choices = requireVocabulary(snapshot.vocabularies, styleChoicesId);
  const tierMap = requireTermsMap(
    choices,
    tierMapId,
    "ExactlyOne",
    "Complete");
  if (tierMap.target.kind !== "terms"
    || tierMap.target.reference.kind !== "local"
    || tierMap.target.reference.vocabulary.value !== tiers.identity.value) {
    throw new Error(
      `Vocabulary map '${styleChoicesId}/${tierMapId}' must target `
      + `'${styleTiersId}'.`);
  }

  requireScalarMap(
    choices,
    conflictMapId,
    "Text",
    "OptionalOne",
    "Complete");
  requireScalarMap(
    choices,
    oracleEndorsedMapId,
    "Boolean",
    "ExactlyOne",
    "Complete");
  requireScalarMap(
    tiers,
    tierByteDivergentMapId,
    "Boolean",
    "ExactlyOne",
    "Complete");

  const tierTerms = new Map(
    tiers.terms.map(term => [term.identity.value, term]));
  const resolvedChoices = choices.terms.map(term => {
    const tierIdentity = requireTermValue(term, tierMapId);
    if (!sameVocabulary(
      tierIdentity,
      snapshot.catalog,
      tiers.identity)) {
      throw new Error(
        `Style choice '${term.identity.value}' targets an unexpected tier vocabulary.`);
    }
    if (!tierTerms.has(tierIdentity.value)) {
      throw new Error(
        `Style choice '${term.identity.value}' targets missing tier `
        + `'${tierIdentity.value}'.`);
    }
    return {
      term,
      conflictGroup: optionalTextValue(term, conflictMapId),
      oracleEndorsed: requireBooleanValue(term, oracleEndorsedMapId),
    };
  });
  const choicesByTier = new Map<string, ResolvedStyleChoice[]>();
  for (const choice of resolvedChoices) {
    const tier = requireTermValue(choice.term, tierMapId).value;
    const grouped = choicesByTier.get(tier) ?? [];
    grouped.push(choice);
    choicesByTier.set(tier, grouped);
  }

  return {
    snapshotIdentity: snapshot.identity.value,
    tiers: tiers.terms.map(term => ({
      term,
      byteDivergent: requireBooleanValue(
        term,
        tierByteDivergentMapId),
      choices: choicesByTier.get(term.identity.value) ?? [],
    })),
    choices: resolvedChoices,
  };
}

export function reconcileStyleTaste(
  taste: readonly string[],
  catalog: ResolvedStyleCatalog,
): string[] {
  const currentIds = new Set(
    catalog.choices.map(choice => choice.term.identity.value));
  return taste.filter(id => currentIds.has(id));
}

export function findStyleChoice(
  catalog: ResolvedStyleCatalog | null,
  id: string,
): ResolvedStyleChoice | undefined {
  return catalog?.choices.find(
    choice => choice.term.identity.value === id);
}

function requireVocabulary(
  vocabularies: readonly BrowserVocabularyDefinition[],
  identity: string,
): BrowserVocabularyDefinition {
  const matches = vocabularies.filter(
    vocabulary => vocabulary.identity.value === identity);
  if (matches.length !== 1) {
    throw new Error(
      `Vocabulary snapshot must contain exactly one '${identity}' vocabulary.`);
  }
  return matches[0]!;
}

function requireTermsMap(
  vocabulary: BrowserVocabularyDefinition,
  identity: string,
  cardinality: BrowserVocabularyMapDefinition["cardinality"],
  coverage: BrowserVocabularyMapDefinition["coverage"],
): BrowserVocabularyMapDefinition {
  const map = requireMap(vocabulary, identity);
  if (map.target.kind !== "terms"
    || map.cardinality !== cardinality
    || map.coverage !== coverage) {
    throw new Error(
      `Vocabulary map '${vocabulary.identity.value}/${identity}' has an `
      + "unexpected term-reference contract.");
  }
  return map;
}

function requireScalarMap(
  vocabulary: BrowserVocabularyDefinition,
  identity: string,
  scalarKind: BrowserVocabularyScalarKind,
  cardinality: BrowserVocabularyMapDefinition["cardinality"],
  coverage: BrowserVocabularyMapDefinition["coverage"],
): BrowserVocabularyMapDefinition {
  const map = requireMap(vocabulary, identity);
  if (map.target.kind !== "scalar"
    || map.target.scalarKind !== scalarKind
    || map.cardinality !== cardinality
    || map.coverage !== coverage) {
    throw new Error(
      `Vocabulary map '${vocabulary.identity.value}/${identity}' has an `
      + "unexpected scalar contract.");
  }
  return map;
}

function requireMap(
  vocabulary: BrowserVocabularyDefinition,
  identity: string,
): BrowserVocabularyMapDefinition {
  const matches = vocabulary.maps.filter(map => map.identity.value === identity);
  if (matches.length !== 1) {
    throw new Error(
      `Vocabulary '${vocabulary.identity.value}' must contain exactly one `
      + `'${identity}' map.`);
  }
  return matches[0]!;
}

function requireEntry(
  term: BrowserVocabularyTerm,
  map: string,
): BrowserVocabularyMapEntry {
  const matches = term.mapEntries.filter(entry => entry.map.value === map);
  if (matches.length !== 1) {
    throw new Error(
      `Vocabulary term '${term.identity.value}' must contain exactly one `
      + `entry for map '${map}'.`);
  }
  return matches[0]!;
}

function requireSingleValue(
  term: BrowserVocabularyTerm,
  map: string,
): BrowserVocabularyMapValue {
  const values = requireEntry(term, map).values;
  if (values.length !== 1) {
    throw new Error(
      `Vocabulary term '${term.identity.value}' map '${map}' must contain `
      + "exactly one value.");
  }
  return values[0]!;
}

function requireTermValue(
  term: BrowserVocabularyTerm,
  map: string,
) {
  const value = requireSingleValue(term, map);
  if (value.kind !== "term") {
    throw new Error(
      `Vocabulary term '${term.identity.value}' map '${map}' must contain `
      + "a term reference.");
  }
  return value.identity;
}

function requireBooleanValue(
  term: BrowserVocabularyTerm,
  map: string,
): boolean {
  const value = requireSingleValue(term, map);
  if (value.kind !== "boolean") {
    throw new Error(
      `Vocabulary term '${term.identity.value}' map '${map}' must contain `
      + "a Boolean value.");
  }
  return value.value;
}

function optionalTextValue(
  term: BrowserVocabularyTerm,
  map: string,
): string | null {
  const values = requireEntry(term, map).values;
  if (values.length === 0) return null;
  const value = values[0];
  if (values.length !== 1 || value?.kind !== "text") {
    throw new Error(
      `Vocabulary term '${term.identity.value}' map '${map}' must contain `
      + "zero or one text value.");
  }
  return value.value;
}

function sameVocabulary(
  term: BrowserVocabularyTermIdentity,
  catalog: BrowserVocabularyIdentity["catalog"],
  vocabulary: BrowserVocabularyDefinitionIdentity,
): boolean {
  return term.vocabulary.catalog.value === catalog.value
    && term.vocabulary.value === vocabulary.value;
}
