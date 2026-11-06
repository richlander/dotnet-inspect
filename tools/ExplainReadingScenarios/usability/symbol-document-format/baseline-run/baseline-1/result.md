I’ll inspect the NuGet package’s public metadata and XML docs, then check the source locations needed for the Stream overloads.

The package includes the net10.0 DLL and XML docs. I’ll use a small reflection reader to capture signatures and attributes that the XML member IDs don’t spell out.

The metadata check found 30 Stream-accepting APIs, 13 APIs that yield a `JsonDocument`, and 20 groups of APIs marked `RequiresUnreferencedCode`. I’ve also matched the five synchronous Stream `Deserialize` overloads to lines in the package’s source commit.

Wrote [answer.json](answer.json) with all six answers, per-question evidence, and completeness assessments based on the 10.0.14 net10.0 package and its release-commit source.

