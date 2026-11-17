I started with the NuGet package’s net10.0 DLL and XML docs because they could establish the API surface and documentation wording. The XML showed candidate members, but not enough about accessibility, attributes, or required overrides, so I used a small reflection program to check those systematically.

I fetched source only where the questions required exact C# spelling or locations: `JsonDocument.ParseAsync`, converter overrides, and the five Stream `Deserialize` declarations. I stopped when the metadata counts and source declarations agreed, each answer had a citation, and `answer.json` passed a final structure check.

