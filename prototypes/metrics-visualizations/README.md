# Metrics visualization prototypes

These are standalone D3.js prototypes for exploring how compiled implementation
metrics might tell a story in Inspect Web. They use real profiles extracted from
the repository's Release assemblies:

- `data/analysis.json`: 13,100 complete bodies from `ILInspector.Analysis`.
- `data/research.json`: 4,008 complete bodies from `ILInspector.Research`.
- Each dataset also retains the strongest 500 aggregated internal
  type-to-type direct-call relationships for the relationship views.

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
   concentrated?” Scroll, pinch, and drag expose smaller regions without
   claiming that a color is a defect severity.
2. **Relationship views** — the same direct-call evidence rendered as a Sankey
   flow, an arc diagram, or a chord diagram. These answer “where does the code
   flow?” and make crossings and mutual relationships visible.
3. **Composition views** — a radial namespace/type map paired with a plain
   language size-band donut. These answer “what kind of code fills the
   library?” without requiring percentile vocabulary.
4. **Diverging comparison** — upper-tail metric differences between the two
   real assemblies, with optional namespace/type composition comparisons. This
   is a candidate grammar for library, version, namespace, type, or member
   comparisons.

## Data refresh

The checked-in data was produced with the repository's Analysis service and a
file-based .NET app. The source assemblies were existing Release artifacts, not
synthetic fixtures. Refreshing it is intentionally a manual prototype
operation; the production visualization should consume the typed Research
document instead of reimplementing Analysis.
