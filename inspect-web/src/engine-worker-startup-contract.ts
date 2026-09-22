import type { BrowserBuildIdentity } from "./facades/inspect-web-host.d.ts";
import type {
  BrowserHomeDemoCatalog,
  BrowserVocabularyDocument,
} from "./facades/inspect-web-catalog.d.ts";
import type {
  BrowserPackageChangesPackageSetCatalog,
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
  listVocabulary: {
    kind: "catalog-list-vocabulary",
    value: json<BrowserVocabularyDocument>(value => {
      const data = record(value);
      return {
        ...data,
        schema_version: number(data.schema_version),
        sections: array(data.sections, rawSection => {
          const section = record(rawSection);
          return {
            ...section,
            id: text(section.id), name: text(section.name), summary: text(section.summary),
            accepted_by: array(section.accepted_by, text),
            fields: array(section.fields, rawField => {
              const field = record(rawField);
              return {
                ...field,
                id: text(field.id), label: text(field.label), summary: text(field.summary),
                type: text(field.type), operators: array(field.operators, text),
              };
            }),
            values: array(section.values, entry => entry),
          };
        }),
      };
    }),
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
  listPackageActivityPackageSets: {
    kind: "package-list-changes-package-sets",
    value: json<BrowserPackageChangesPackageSetCatalog>(value => {
      const data = record(value);
      return {
        ...data,
        version: number(data.version),
        packageSets: array(data.packageSets, rawPackageSet => {
          const packageSet = record(rawPackageSet);
          return {
            ...packageSet,
            id: text(packageSet.id),
            title: text(packageSet.title),
            summary: text(packageSet.summary),
            order: number(packageSet.order),
          };
        }),
      };
    }),
  },
};
