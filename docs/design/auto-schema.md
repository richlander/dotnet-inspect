# Auto-generated `DocumentSchema`

## Status

**Historical implementation note.**

This document originally recorded the first migration from command-local schema
maps to Markout-generated structural schema. That migration established the
durable default:

```csharp
DocumentSchema schema = ViewContext.Default
    .GetSchemaInfo<ViewDocument>()!
    .ToDocumentSchema();
```

The old view-model list, call-site count, and generated-versus-manual migration
plan are no longer current architecture. Product documents now combine several
legitimate schema forms: generated view schema, explicit multi-view
composition, owner-authored augmentation for dynamic structure, effective
filtering, and formatter-event render manifests.

[Schema query](schema-query.md) is the sole current owner for those boundaries,
discovery behavior, projection diagnostics, pathologies, and gates. Consult
this file's Git history when the original migration evidence is needed; do not
use it as current implementation guidance.
