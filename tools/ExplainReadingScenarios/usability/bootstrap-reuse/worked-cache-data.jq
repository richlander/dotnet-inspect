.vocabularies["csharp.body-kinds"]
| {catalog_values, summary,
   values: (.values | with_entries(select(.key == "BreakStatement" or .key == "ObjectCreationExpression")))}
