// Client loader for the static platform-assembly/facade index (Pillar A).
//
// The index is a compact TSV (assets/platform-index.tsv) produced offline by
// tools/gen-platform-index. It maps every .NET platform assembly per target
// framework and shared-framework pack (netcore.app, aspnetcore.app, netstandard)
// to its file/assembly name, whether it is a facade, and the implementation
// assembly a facade forwards to. Loading it gives the UI a first-take hint about
// platform libraries with no pack download or decode.
//
// This module only loads and indexes the data; it renders nothing. UI wiring
// (Spotlight badges, the library-scope selector) builds on these helpers.

const INDEX_URL = "/assets/platform-index.tsv";

export type PlatformPack = "netcore.app" | "aspnetcore.app" | "netstandard";
export type PlatformAssemblyKind = "impl" | "facade" | "ref";

export interface PlatformAssemblyRow {
  tfm: string;
  pack: PlatformPack;
  assembly: string;
  file: string;
  kind: PlatformAssemblyKind;
  forwardsTo: string | null;
  version: string;
  publicTypes: number;
}

export interface PlatformIndex {
  rows: PlatformAssemblyRow[];
  tfms(): string[];
  /** All assembly rows for a target framework, optionally scoped to one pack
   * ("netcore.app" | "aspnetcore.app" | "netstandard"). Sorted as generated. */
  assembliesFor(tfm: string, pack?: PlatformPack): PlatformAssemblyRow[];
  /** The row for one assembly in one framework, or null. */
  lookup(tfm: string, assembly: string | null | undefined): PlatformAssemblyRow | null;
  isFacade(tfm: string, assembly: string | null | undefined): boolean;
  /** For a facade, the implementation assembly its exported types resolve to. */
  forwardsTo(tfm: string, assembly: string | null | undefined): string | null;
}

interface ParsedIndex {
  rows: PlatformAssemblyRow[];
  byTfm: Map<string, PlatformAssemblyRow[]>;
  byKey: Map<string, PlatformAssemblyRow>;
}

let indexPromise: Promise<PlatformIndex | null> | null = null;

function parseTsv(text: string): ParsedIndex {
  const rows: PlatformAssemblyRow[] = [];
  const byTfm = new Map<string, PlatformAssemblyRow[]>();
  const byKey = new Map<string, PlatformAssemblyRow>(); // `${tfm}\u0000${assembly}` -> row
  const lines = text.split("\n");
  // lines[0] is the header: tfm pack assembly file kind forwardsTo version publicTypes
  for (let i = 1; i < lines.length; i++) {
    const line = lines[i];
    if (!line) continue;
    const parts = line.split("\t");
    if (parts.length < 8) continue;
    const row: PlatformAssemblyRow = {
      tfm: parts[0],
      pack: parts[1] as PlatformPack,
      assembly: parts[2],
      file: parts[3],
      kind: parts[4] as PlatformAssemblyKind,
      forwardsTo: parts[5] || null,
      version: parts[6],
      publicTypes: Number(parts[7]) || 0
    };
    rows.push(row);
    let bucket = byTfm.get(row.tfm);
    if (!bucket) byTfm.set(row.tfm, (bucket = []));
    bucket.push(row);
    byKey.set(`${row.tfm}\u0000${row.assembly}`, row);
  }
  return { rows, byTfm, byKey };
}

function makeIndex(parsed: ParsedIndex): PlatformIndex {
  const { rows, byTfm, byKey } = parsed;
  const tfms = [...byTfm.keys()];
  return {
    rows,
    tfms: () => tfms.slice(),
    assembliesFor(tfm, pack) {
      const bucket = byTfm.get(tfm) || [];
      return pack ? bucket.filter(row => row.pack === pack) : bucket.slice();
    },
    lookup(tfm, assembly) {
      if (!assembly) return null;
      const name = assembly.endsWith(".dll") ? assembly.slice(0, -4) : assembly;
      return byKey.get(`${tfm}\u0000${name}`) || null;
    },
    isFacade(tfm, assembly) {
      return this.lookup(tfm, assembly)?.kind === "facade";
    },
    forwardsTo(tfm, assembly) {
      const row = this.lookup(tfm, assembly);
      return row && row.kind === "facade" ? row.forwardsTo : null;
    }
  };
}

/**
 * Lazily fetch and index the platform assembly map. Cached after the first call;
 * a failed load is not cached so a later call can retry. Returns null on failure
 * so callers degrade gracefully (the index is a hint, never load-bearing).
 */
export function loadPlatformIndex(): Promise<PlatformIndex | null> {
  if (indexPromise) return indexPromise;
  indexPromise = fetch(INDEX_URL)
    .then(response => {
      if (!response.ok) throw new Error(`platform index HTTP ${response.status}`);
      return response.text();
    })
    .then(text => makeIndex(parseTsv(text)))
    .catch(error => {
      indexPromise = null; // allow retry
      console.warn("platform index load failed", error);
      return null;
    });
  return indexPromise;
}
