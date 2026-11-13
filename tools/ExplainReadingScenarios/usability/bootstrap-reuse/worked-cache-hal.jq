["BreakStatement", "ObjectCreationExpression"] as $needed
| ._embedded.vocabularies[] | select(.id == "csharp.body-kinds")
| {catalog_values, summary,
   values: [._embedded.values[] | select(.id as $id | $needed | index($id))]}
