# Metrics visualization prototypes

These are standalone D3.js prototypes for exploring how compiled implementation
metrics might tell a story in Inspect Web. They use real profiles extracted from
the repository's Release assemblies:

- `data/analysis.json`: 13,100 complete bodies from `ILInspector.Analysis`.
- `data/research.json`: 4,008 complete bodies from `ILInspector.Research`.

The prototypes intentionally do not call the production Browser/Wasm contract.
They are visual design experiments over the same kind of Analysis evidence.

## Run

```bash
cd prototypes/metrics-visualizations
npx serve .
```

Open the URL printed by `serve`. The page loads D3 7 from jsDelivr; no D3
dependency is added to the repository.

## Samples

1. **Library map** — a zoomable treemap. Area answers “where is the
   implementation?” and color answers “where is branching density
   concentrated?” without claiming that a color is a defect severity.
2. **Method field** — a dot field that shows the shape of one selected measure.
   Each dot is a physical method body; isolated dots expose a long tail without
   requiring users to understand percentile terminology.
3. **Library comparison** — connected metric ranges compare the two real
   assemblies. It uses p50, p95, and maximum as landmarks while keeping the
   per-metric scale visible.

## Data refresh

The checked-in data was produced with the repository's Analysis service and a
file-based .NET app. The source assemblies were existing Release artifacts, not
synthetic fixtures. Refreshing it is intentionally a manual prototype
operation; the production visualization should consume the typed Research
document instead of reimplementing Analysis.
