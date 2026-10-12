import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import type {
  BrowserEcosystemCatalog,
  BrowserHomeDemoCatalog,
  BrowserVocabularyCatalogIdentity,
  BrowserVocabularyDefinition,
  BrowserVocabularyDefinitionIdentity,
  BrowserVocabularyDiagnosticSeverity,
  BrowserVocabularyIdentity,
  BrowserVocabularyInspection,
  BrowserVocabularyInspectionShare,
  BrowserVocabularyMapCardinality,
  BrowserVocabularyMapCoverage,
  BrowserVocabularyMapDefinitionIdentity,
  BrowserVocabularyMapTarget,
  BrowserVocabularyMapValue,
  BrowserVocabularySnapshotIdentity,
  BrowserVocabularyScalarKind,
  BrowserVocabularyTermDefinitionIdentity,
  BrowserVocabularyTermIdentity,
  BrowserVocabularyTermSetReference,
} from "./facades/inspect-web-catalog.d.ts";
import type {
  BrowserPackageChangesEcosystemCatalog,
} from "./facades/inspect-web-package.d.ts";
import type {
  PackageQueryFacetBinding,
  PackageQueryFacetData,
  PackageQueryFacetDescriptor,
  PackageQueryFacetOption,
} from "./package-query-source.ts";
import type { BoundedPayloadDecoder } from "./worker-runtime-protocol.ts";

export const engineStartupMaximumJsonCharacters = 1_048_576;

class StartupPayloadError extends Error {}

function record(value: unknown): Record<string, unknown> {
  function isRecord(input: unknown): input is Record<string, unknown> {
    return typeof input === "object" && input !== null && !Array.isArray(input);
  }
  if (!isRecord(value)) throw new StartupPayloadError("Expected a startup result object.");
  return value;
}

function text(value: unknown): string {
  if (typeof value !== "string") throw new StartupPayloadError("Expected startup result text.");
  return value;
}

function nullableText(value: unknown): string | null {
  return value === null ? null : text(value);
}

function number(value: unknown): number {
  if (typeof value !== "number" || !Number.isFinite(value))
    throw new StartupPayloadError("Expected a finite startup result number.");
  return value;
}

function boolean(value: unknown): boolean {
  if (typeof value !== "boolean") throw new StartupPayloadError("Expected a startup result boolean.");
  return value;
}

function array<T>(value: unknown, parse: (entry: unknown) => T): T[] {
  if (!Array.isArray(value)) throw new StartupPayloadError("Expected a startup result array.");
  return value.map(parse);
}

function packageQueryFacetOption(value: unknown): PackageQueryFacetOption {
  const option = record(value);
  return {
    value: text(option.value),
    label: text(option.label),
    summary: text(option.summary),
  };
}

function packageQueryFacet(value: unknown): PackageQueryFacetDescriptor {
  const facet = record(value);
  const facts = record(facet.facts);
  return {
    key: text(facts.key),
    name: text(facts.name),
    summary: text(facts.summary),
    operators: array(facts.operators, text),
    valueKind: text(facts["value-kind"]),
    examples: array(facts.examples, text),
    effects: array(facts.effects, text),
    inputRules: array(facts["input-rules"], text),
    presentationOrder: number(facts["presentation-order"]),
    controlKind: text(facts["control-kind"]),
    presentationKind: text(facts["presentation-kind"]),
    executionClass: text(facts["execution-class"]),
    selectionGroup: nullableText(facts["selection-group"]),
    combinesWithinSelectionGroup:
      boolean(facts["combines-within-selection-group"]),
    replacementGroup: nullableText(facts["replacement-group"]),
    optionSource: nullableText(facts["option-source"]),
    options: array(facts.options, packageQueryFacetOption),
    relatedOperations: array(facet.related_operations, text),
  };
}

function packageQueryFacetBinding(value: unknown): PackageQueryFacetBinding {
  const binding = record(value);
  const facts = record(binding.facts);
  return {
    consumerKind: text(facts["consumer-kind"]),
    exposedFacets: array(binding.exposed_facets, text),
  };
}

function mapped<TValue>(
  value: unknown,
  decode: (value: unknown) => TValue,
): Record<string, TValue> {
  return Object.fromEntries(
    Object.entries(record(value))
      .map(([key, entry]) => [key, decode(entry)]),
  );
}

function packageQueryFacetData(value: unknown): PackageQueryFacetData {
  const data = record(value);
  const selection = record(data.selection);
  if (text(selection.kind) !== "query-facets"
    || text(selection.completeness) !== "Complete") {
    throw new StartupPayloadError(
      "Package Query facet data must be a complete query-facets selection.");
  }
  const facets = mapped(data.facets, packageQueryFacet);
  const facetOrder = array(data.facet_order, text);
  const bindings = mapped(data.bindings, packageQueryFacetBinding);
  for (const [key, facet] of Object.entries(facets)) {
    if (facet.key !== key) {
      throw new StartupPayloadError(
        `Package Query facet '${key}' reports key '${facet.key}'.`);
    }
  }
  if (new Set(facetOrder).size !== facetOrder.length
    || facetOrder.length !== Object.keys(facets).length
    || facetOrder.some(key => !(key in facets))) {
    throw new StartupPayloadError(
      "Package Query facet order must contain every facet exactly once.");
  }
  for (const binding of Object.values(bindings)) {
    if (binding.exposedFacets.some(key => !(key in facets))) {
      throw new StartupPayloadError(
        "Package Query consumer binding exposes an unknown facet.");
    }
  }
  return { facets, facetOrder, bindings };
}

function vocabularyMapCardinality(
  value: unknown,
): BrowserVocabularyMapCardinality {
  if (value === "ExactlyOne"
    || value === "OptionalOne"
    || value === "OneOrMore"
    || value === "ZeroOrMore") return value;
  return number(value);
}

function vocabularyMapCoverage(value: unknown): BrowserVocabularyMapCoverage {
  if (value === "Complete" || value === "Partial") return value;
  return number(value);
}

function vocabularyDiagnosticSeverity(
  value: unknown,
): BrowserVocabularyDiagnosticSeverity {
  if (value === "Information" || value === "Warning" || value === "Error")
    return value;
  return number(value);
}

function vocabularyScalarKind(value: unknown): BrowserVocabularyScalarKind {
  if (value === "Text" || value === "Integer" || value === "Boolean")
    return value;
  return number(value);
}

function vocabularyCatalogIdentity(
  value: unknown,
): BrowserVocabularyCatalogIdentity {
  const data = record(value);
  return { value: text(data.value) };
}

function vocabularyIdentity(value: unknown): BrowserVocabularyIdentity {
  const data = record(value);
  return {
    catalog: vocabularyCatalogIdentity(data.catalog),
    value: text(data.value),
  };
}

function vocabularyTermIdentity(value: unknown): BrowserVocabularyTermIdentity {
  const data = record(value);
  return {
    vocabulary: vocabularyIdentity(data.vocabulary),
    value: text(data.value),
  };
}

function vocabularyDefinitionIdentity(
  value: unknown,
): BrowserVocabularyDefinitionIdentity {
  const data = record(value);
  return { value: text(data.value) };
}

function vocabularyTermDefinitionIdentity(
  value: unknown,
): BrowserVocabularyTermDefinitionIdentity {
  const data = record(value);
  return { value: text(data.value) };
}

function vocabularyMapDefinitionIdentity(
  value: unknown,
): BrowserVocabularyMapDefinitionIdentity {
  const data = record(value);
  return { value: text(data.value) };
}

function vocabularySnapshotIdentity(
  value: unknown,
): BrowserVocabularySnapshotIdentity {
  const data = record(value);
  return { value: text(data.value) };
}

function vocabularyTermSetReference(
  value: unknown,
): BrowserVocabularyTermSetReference {
  const data = record(value);
  const kind = text(data.kind);
  if (kind === "local") {
    return {
      kind,
      vocabulary: vocabularyDefinitionIdentity(data.vocabulary),
    };
  }
  if (kind === "external") {
    return {
      kind,
      snapshot: vocabularySnapshotIdentity(data.snapshot),
      vocabulary: vocabularyIdentity(data.vocabulary),
    };
  }
  throw new StartupPayloadError(
    `Unknown vocabulary term-set reference kind '${kind}'.`);
}

function vocabularyMapTarget(value: unknown): BrowserVocabularyMapTarget {
  const data = record(value);
  const kind = text(data.kind);
  if (kind === "scalar") {
    return {
      kind,
      scalarKind: vocabularyScalarKind(data.scalarKind),
    };
  }
  if (kind === "terms") {
    return {
      kind,
      reference: vocabularyTermSetReference(data.reference),
    };
  }
  throw new StartupPayloadError(
    `Unknown vocabulary map target kind '${kind}'.`);
}

function vocabularyMapValue(value: unknown): BrowserVocabularyMapValue {
  const data = record(value);
  const kind = text(data.kind);
  if (kind === "text") return { kind, value: text(data.value) };
  if (kind === "integer") return { kind, value: number(data.value) };
  if (kind === "boolean") return { kind, value: boolean(data.value) };
  if (kind === "term") {
    return {
      kind,
      identity: vocabularyTermIdentity(data.identity),
    };
  }
  throw new StartupPayloadError(
    `Unknown vocabulary map value kind '${kind}'.`);
}

function vocabularyDefinition(value: unknown): BrowserVocabularyDefinition {
  const data = record(value);
  return {
    identity: vocabularyDefinitionIdentity(data.identity),
    displayLabel: text(data.displayLabel),
    summary: nullableText(data.summary),
    maps: array(data.maps, rawMap => {
      const map = record(rawMap);
      return {
        identity: vocabularyMapDefinitionIdentity(map.identity),
        displayLabel: text(map.displayLabel),
        summary: text(map.summary),
        target: vocabularyMapTarget(map.target),
        cardinality: vocabularyMapCardinality(map.cardinality),
        coverage: vocabularyMapCoverage(map.coverage),
      };
    }),
    terms: array(data.terms, rawTerm => {
      const term = record(rawTerm);
      return {
        identity: vocabularyTermDefinitionIdentity(term.identity),
        displayLabel: text(term.displayLabel),
        summary: nullableText(term.summary),
        mapEntries: array(term.mapEntries, rawEntry => {
          const entry = record(rawEntry);
          return {
            map: vocabularyMapDefinitionIdentity(entry.map),
            values: array(entry.values, vocabularyMapValue),
          };
        }),
      };
    }),
  };
}

function vocabularyShare(value: unknown): BrowserVocabularyInspectionShare {
  const data = record(value);
  const kind = text(data.kind);
  if (kind === "available") {
    return {
      kind,
      fullUrl: text(data.fullUrl),
      packet: text(data.packet),
    };
  }
  if (kind === "nonProjectable") {
    return {
      kind,
      path: text(data.path),
      reason: text(data.reason),
    };
  }
  throw new StartupPayloadError(
    `Unknown vocabulary Share kind '${kind}'.`);
}

function vocabularyInspection(value: unknown): BrowserVocabularyInspection {
  const data = record(value);
  const content = record(data.content);
  return {
    ...data,
    content: {
      formatVersion: number(content.formatVersion),
      catalog: vocabularyCatalogIdentity(content.catalog),
      identity: vocabularySnapshotIdentity(content.identity),
      vocabularies: array(content.vocabularies, vocabularyDefinition),
    },
    share: vocabularyShare(data.share),
    diagnostics: array(data.diagnostics, rawDiagnostic => {
      const diagnostic = record(rawDiagnostic);
      return {
        code: text(diagnostic.code),
        severity: vocabularyDiagnosticSeverity(diagnostic.severity),
        summary: text(diagnostic.summary),
        correspondence: nullableText(diagnostic.correspondence),
      };
    }),
  };
}

function json<T>(parse: (value: unknown) => T): BoundedPayloadDecoder<T> {
  return {
    decode(value) {
      if (typeof value !== "string")
        return { kind: "rejected", reason: "invalid", message: "Expected startup result JSON." };
      if (value.length > engineStartupMaximumJsonCharacters) {
        return {
          kind: "rejected", reason: "oversized",
          message: "Startup result JSON exceeds 1048576 characters.",
        };
      }
      try {
        const parsed: unknown = JSON.parse(value);
        return { kind: "decoded", value: parse(parsed) };
      } catch (error: unknown) {
        if (!(error instanceof SyntaxError) && !(error instanceof StartupPayloadError)) throw error;
        return { kind: "rejected", reason: "invalid", message: error.message };
      }
    },
  };
}

export function encodeEngineStartupResult(value: unknown): string {
  const encoded = JSON.stringify(value);
  if (encoded === undefined) throw new StartupPayloadError("Startup result is not JSON.");
  if (encoded.length > engineStartupMaximumJsonCharacters)
    throw new StartupPayloadError("Startup result JSON exceeds 1048576 characters.");
  return encoded;
}

export const engineStartupInput: BoundedPayloadDecoder<null> = {
  decode: value => value === null
    ? { kind: "decoded", value }
    : { kind: "rejected", reason: "invalid", message: "Startup reads take no arguments." },
};

export const engineStartupOperations = {
  buildIdentity: {
    kind: "host-build-identity",
    value: json<BrowserBuildIdentity>(value => {
      const data = record(value);
      return {
        ...data,
        version: text(data.version),
        commit: nullableText(data.commit),
        builtAtUtc: nullableText(data.builtAtUtc),
        commitUrl: nullableText(data.commitUrl),
      };
    }),
  },
  inspectVocabulary: {
    kind: "catalog-inspect-vocabulary",
    value: json<BrowserVocabularyInspection>(vocabularyInspection),
  },
  listHomeDemos: {
    kind: "catalog-list-home-demos",
    value: json<BrowserHomeDemoCatalog>(value => {
      const data = record(value);
      return {
        ...data,
        demos: array(data.demos, rawDemo => {
          const demo = record(rawDemo);
          return { ...demo, id: text(demo.id), title: text(demo.title), summary: text(demo.summary) };
        }),
      };
    }),
  },
  listEcosystems: {
    kind: "catalog-list-ecosystems",
    value: json<BrowserEcosystemCatalog>(value => {
      const data = record(value);
      return {
        ...data,
        ecosystems: array(data.ecosystems, rawEcosystem => {
          const ecosystem = record(rawEcosystem);
          return {
            ...ecosystem,
            id: text(ecosystem.id),
            title: text(ecosystem.title),
            summary: text(ecosystem.summary),
            corePackageCount: number(ecosystem.corePackageCount),
            namespaceRootCount: number(ecosystem.namespaceRootCount),
            toolPackageCount: number(ecosystem.toolPackageCount),
            demoCount: number(ecosystem.demoCount),
            hasPackageSet: boolean(ecosystem.hasPackageSet),
            hasScanner: boolean(ecosystem.hasScanner),
            hasPopulationLoader: boolean(ecosystem.hasPopulationLoader),
            hasWorkspaceRegistration: boolean(
              ecosystem.hasWorkspaceRegistration),
          };
        }),
      };
    }),
  },
  inspectPackageQueryFacets: {
    kind: "package-inspect-query-facets",
    value: json<PackageQueryFacetData>(packageQueryFacetData),
  },
  listPackageActivityEcosystems: {
    kind: "package-list-changes-ecosystems",
    value: json<BrowserPackageChangesEcosystemCatalog>(value => {
      const data = record(value);
      return {
        ...data,
        version: number(data.version),
        ecosystems: array(data.ecosystems, rawEcosystem => {
          const ecosystem = record(rawEcosystem);
          return {
            ...ecosystem,
            id: text(ecosystem.id),
            title: text(ecosystem.title),
            summary: text(ecosystem.summary),
            order: number(ecosystem.order),
            prefixes: array(ecosystem.prefixes, text),
          };
        }),
      };
    }),
  },
};
