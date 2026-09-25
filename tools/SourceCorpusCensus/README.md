# Source corpus census

`SourceCorpusCensus` is the reproducible, observational probe behind the
Source continuation policy. It does not run in PR CI and does not define
product behavior.

The document mode reads the pinned authored-source correspondence corpora,
deduplicates physical documents by SourceLink URL plus PDB checksum identity,
fetches every document, verifies it through the product SourceLink checksum
implementation, decodes it through the product source decoder, and measures
document, line, and candidate-segment distributions:

```bash
bash eng/restore-authored-source-corpus.sh
dotnet run --project tools/SourceCorpusCensus -c Release -- \
  documents \
  external/authored-source-corpus/civil/corpus.jsonl \
  external/authored-source-corpus/evil/corpus.jsonl \
  external/authored-source-corpus/oracle/corpus.jsonl \
  > artifacts/source-corpus-census/documents.json
```

The type mode reproduces the pinned broad package pool, acquires available
Portable PDBs, and measures correlated physical documents per exact metadata
type separately from single-file name inference. Copied sweep assembly paths
are joined back to manifest package identities before symbol acquisition, so a
ranked directory such as `019-npgsql` does not replace the package ID:

```bash
bash eng/prepare-evil-corpus.sh \
  artifacts/source-corpus-census/evil-pool
dotnet run --project tools/SourceCorpusCensus -c Release -- \
  types \
  artifacts/source-corpus-census/evil-pool/assemblies.txt \
  artifacts/source-corpus-census/evil-pool/sweep-manifest.json \
  artifacts/source-corpus-census/pdb-cache \
  > artifacts/source-corpus-census/type-mappings.json
```

Both modes write structured JSON to stdout and progress to stderr. Missing
PDBs remain explicit assembly outcomes. Document retrieval or checksum
failures produce classified report rows and a nonzero exit code. Document
downloads are capped at 64 MiB even when the server omits or understates
`Content-Length`.

Run the deterministic local mechanism check with:

```bash
dotnet run --project tools/SourceCorpusCensus -c Release -- --validate
```

The check covers line and percentile semantics, bounded source reads, and the
copied-sweep package-identity join used by type mode.
