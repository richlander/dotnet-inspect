I first read the document’s header and schema to check that it covered the requested public API and that its declaration, documentation, and source-link sections were complete. I then used its indexes to find Stream parameters, JsonDocument results, and `RequiresUnreferencedCode` members. Follow-up reads checked cases an index alone might obscure: a nullable `out JsonDocument`, same-named unmarked overloads, and which converter types an outside caller can derive from.

For the two exact-location questions, I read the `ParseAsync` signature and summary and the source lines attached to each Stream `Deserialize` overload. I stopped once those checks matched the complete member records and each question had the requested evidence.

