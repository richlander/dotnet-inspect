const INDEX_URL = "/assets/platform-index.json";
export const DEFAULT_PLATFORM_FRAMEWORK = "net11.0";

type PlatformPack = "netcore.app" | "aspnetcore.app" | "netstandard";
type PlatformAssemblyKind = "impl" | "facade" | "ref";

export interface PlatformAssemblyRow {
  readonly tfm: string;
  readonly pack: PlatformPack;
  readonly assembly: string;
  readonly file: string;
  readonly kind: PlatformAssemblyKind;
  readonly forwardsTo: string | null;
  readonly version: string;
  readonly publicTypes: number;
  readonly inReferencePack: boolean;
  readonly hasImplementation: boolean;
  readonly packVersion: string;
}

export interface PlatformCatalogTarget {
  readonly tfm: string;
  readonly version: string;
  readonly rows: readonly PlatformAssemblyRow[];
}

export interface PlatformIndex {
  readonly rows: readonly PlatformAssemblyRow[];
  readonly defaultFramework: string;
  tfms(): string[];
  targets(): PlatformCatalogTarget[];
  target(tfm: string, version?: string): PlatformCatalogTarget | null;
  assembliesFor(tfm: string, pack?: PlatformPack, version?: string): PlatformAssemblyRow[];
  lookup(
    tfm: string,
    assembly: string | null | undefined,
    pack?: PlatformPack,
    version?: string,
  ): PlatformAssemblyRow | null;
  isFacade(tfm: string, assembly: string | null | undefined): boolean;
  forwardsTo(tfm: string, assembly: string | null | undefined): string | null;
  addTarget(target: PlatformCatalogTarget): void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (!isRecord(value)) {
    throw new Error(`Invalid platform catalog ${label}.`);
  }
  return value;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`Invalid platform catalog ${label}.`);
  }
  return value;
}

export function parsePlatformCatalogTarget(input: unknown): PlatformCatalogTarget {
  const target = record(input, "target");
  const tfm = text(target.tfm, "framework");
  const version = text(target.version, "version");
  if (!Array.isArray(target.rows) || target.rows.length === 0) {
    throw new Error("Platform catalog has no library inventory.");
  }
  const identities = new Set<string>();
  const rows = target.rows.map((value: unknown): PlatformAssemblyRow => {
    const row = record(value, "library");
    const assembly = text(row.assembly, "assembly");
    const file = text(row.file, "file");
    const assemblyVersion = text(row.version, "assembly version");
    const pack = row.pack;
    const kind = row.kind;
    if (pack !== "netcore.app" && pack !== "aspnetcore.app" && pack !== "netstandard") {
      throw new Error(`Invalid platform catalog pack for ${assembly}.`);
    }
    if (kind !== "impl" && kind !== "facade" && kind !== "ref") {
      throw new Error(`Invalid platform catalog library kind for ${assembly}.`);
    }
    if (row.tfm !== tfm || row.packVersion !== version) {
      throw new Error(`Platform catalog coordinate mismatch for ${assembly}.`);
    }
    if (typeof row.inReferencePack !== "boolean"
      || typeof row.hasImplementation !== "boolean"
      || (!row.inReferencePack && !row.hasImplementation)
      || (kind === "impl" && !row.hasImplementation)
      || (kind === "ref" && row.hasImplementation)) {
      throw new Error(`Invalid platform catalog membership for ${assembly}.`);
    }
    if (typeof row.publicTypes !== "number"
      || !Number.isSafeInteger(row.publicTypes) || row.publicTypes < 0) {
      throw new Error(`Invalid platform catalog type count for ${assembly}.`);
    }
    if (row.forwardsTo !== null && typeof row.forwardsTo !== "string") {
      throw new Error(`Invalid platform catalog forwarding target for ${assembly}.`);
    }
    const identity = `${pack}\0${assembly.toLowerCase()}`;
    if (identities.has(identity)) {
      throw new Error(`Duplicate platform catalog library ${assembly} in ${pack}.`);
    }
    identities.add(identity);
    return Object.freeze({
      tfm, pack, assembly, file, kind, forwardsTo: row.forwardsTo,
      version: assemblyVersion, publicTypes: row.publicTypes,
      inReferencePack: row.inReferencePack, hasImplementation: row.hasImplementation,
      packVersion: version,
    });
  });
  return Object.freeze({ tfm, version, rows: Object.freeze(rows) });
}

export function parsePlatformIndex(input: unknown): PlatformIndex {
  const catalog = record(input, "document");
  if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.targets)) {
    throw new Error("Unsupported platform catalog format.");
  }
  const defaultFramework = text(catalog.defaultFramework, "default framework");
  const targets = new Map<string, PlatformCatalogTarget>();
  const defaults = new Map<string, PlatformCatalogTarget>();
  const key = (tfm: string, version: string) => `${tfm}\0${version}`;
  for (const value of catalog.targets) {
    const target = parsePlatformCatalogTarget(value);
    const identity = key(target.tfm, target.version);
    if (targets.has(identity)) throw new Error(`Duplicate platform catalog target ${target.tfm}@${target.version}.`);
    targets.set(identity, target);
    if (!defaults.has(target.tfm)) defaults.set(target.tfm, target);
  }
  if (!defaults.has(defaultFramework)) {
    throw new Error("Platform catalog does not contain its default target.");
  }
  return {
    defaultFramework,
    get rows() { return [...targets.values()].flatMap(target => target.rows); },
    tfms: () => [...defaults.keys()],
    targets: () => [...targets.values()],
    target(tfm, version) {
      return (version ? targets.get(key(tfm, version)) : defaults.get(tfm)) ?? null;
    },
    assembliesFor(tfm, pack, version) {
      const rows = this.target(tfm, version)?.rows ?? [];
      return rows.filter(row => !pack || row.pack === pack);
    },
    lookup(tfm, assembly, pack, version) {
      if (!assembly) return null;
      const name = assembly.replace(/\.dll$/i, "").toLowerCase();
      const matches = this.assembliesFor(tfm, pack, version)
        .filter(row => row.assembly.toLowerCase() === name);
      return matches.length === 1 ? matches[0] ?? null : null;
    },
    isFacade(tfm, assembly) {
      return this.lookup(tfm, assembly)?.kind === "facade";
    },
    forwardsTo(tfm, assembly) {
      const row = this.lookup(tfm, assembly);
      return row?.kind === "facade" ? row.forwardsTo : null;
    },
    addTarget(value) {
      const target = parsePlatformCatalogTarget(value);
      const identity = key(target.tfm, target.version);
      const existing = targets.get(identity);
      if (existing) {
        if (JSON.stringify(existing) !== JSON.stringify(target)) {
          throw new Error(`Platform catalog changed for pinned target ${target.tfm}@${target.version}.`);
        }
        return;
      }
      targets.set(identity, target);
      // Discovery never replaces the target already selected from the shipped catalog.
      if (!defaults.has(target.tfm)) defaults.set(target.tfm, target);
    },
  };
}

let indexPromise: Promise<PlatformIndex | null> | null = null;

/** A failed catalog load is retryable and distinct from a confirmed empty inventory. */
export function loadPlatformIndex(): Promise<PlatformIndex | null> {
  if (indexPromise) return indexPromise;
  indexPromise = fetch(INDEX_URL)
    .then(response => {
      if (!response.ok) throw new Error(`Platform catalog HTTP ${response.status}.`);
      return response.json();
    })
    .then((value: unknown) => parsePlatformIndex(value))
    .catch((error: unknown) => {
      indexPromise = null;
      console.warn("Platform catalog load failed", error);
      return null;
    });
  return indexPromise;
}
