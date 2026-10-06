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
  BrowserPackageQueryCatalog,
  BrowserPackageQueryAcquisitionTier,
  BrowserPackageQueryExecutionClass,
} from "./facades/inspect-web-package.d.ts";
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

function tier(value: unknown): BrowserPackageQueryAcquisitionTier {
  if (value === "Nuspec" || value === "PackageContent" || value === "SearchMetadata") return value;
  return number(value);
}

function executionClass(value: unknown): BrowserPackageQueryExecutionClass {
  if (value === "SearchMetadata"
    || value === "Nuspec"
    || value === "NuspecExpensive"
    || value === "PackageContent"
    || value === "Metadata"
    || value === "MetadataExpensive") return value;
  return number(value);
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
  listPackageQueryCatalog: {
    kind: "package-list-query-catalog",
    value: json<BrowserPackageQueryCatalog>(value => {
      const data = record(value);
      return {
        ...data,
        presets: array(data.presets, rawPreset => {
          const preset = record(rawPreset);
          return {
            ...preset,
            key: text(preset.key),
            operator: text(preset.operator),
            value: text(preset.value),
            label: text(preset.label),
            summary: text(preset.summary),
            weight: number(preset.weight),
            tier: tier(preset.tier),
            executionClass: executionClass(preset.executionClass),
            selectionGroupId: nullableText(preset.selectionGroupId),
            combinesWithinSelectionGroup: boolean(
              preset.combinesWithinSelectionGroup),
            replacementGroupId: nullableText(preset.replacementGroupId),
            displayGroupId: nullableText(preset.displayGroupId),
            displayGroupLabel: nullableText(preset.displayGroupLabel),
          };
        }),
        terms: array(data.terms, rawTerm => {
          const term = record(rawTerm);
          return {
            ...term,
            key: text(term.key),
            label: text(term.label),
            summary: text(term.summary),
            weight: number(term.weight),
            tier: tier(term.tier),
            executionClass: executionClass(term.executionClass),
            operators: array(term.operators, text),
            valueKind: text(term.valueKind),
            example: text(term.example),
            multiline: boolean(term.multiline),
          };
        }),
      };
    }),
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
