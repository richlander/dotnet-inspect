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

let indexPromise = null;

function parseTsv(text) {
  const rows = [];
  const byTfm = new Map();
  const byKey = new Map(); // `${tfm}\u0000${assembly}` -> row
  const lines = text.split("\n");
  // lines[0] is the header: tfm pack assembly file kind forwardsTo version publicTypes
  for (let i = 1; i < lines.length; i++) {
    const line = lines[i];
    if (!line) continue;
    const parts = line.split("\t");
    if (parts.length < 8) continue;
    const row = {
      tfm: parts[0],
      pack: parts[1], // "netcore.app" | "aspnetcore.app" | "netstandard"
      assembly: parts[2],
      file: parts[3],
      kind: parts[4], // "impl" | "facade" | "ref"
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

function makeIndex(parsed) {
  const { rows, byTfm, byKey } = parsed;
  const tfms = [...byTfm.keys()];
  return {
    rows,
    tfms: () => tfms.slice(),
    /** All assembly rows for a target framework, optionally scoped to one pack
     * ("netcore.app" | "aspnetcore.app" | "netstandard"). Sorted as generated. */
    assembliesFor(tfm, pack) {
      const bucket = byTfm.get(tfm) || [];
      return pack ? bucket.filter(row => row.pack === pack) : bucket.slice();
    },
    /** The row for one assembly in one framework, or null. */
    lookup(tfm, assembly) {
      if (!assembly) return null;
      const name = assembly.endsWith(".dll") ? assembly.slice(0, -4) : assembly;
      return byKey.get(`${tfm}\u0000${name}`) || null;
    },
    isFacade(tfm, assembly) {
      return this.lookup(tfm, assembly)?.kind === "facade";
    },
    /** For a facade, the implementation assembly its exported types resolve to. */
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
export function loadPlatformIndex() {
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
