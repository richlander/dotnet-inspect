# Text characterization assets

Real-source excerpts that `TextDiffCharacterizationTests` uses as motivating
evidence for the text whitespace and move characterization designs.

| File | Source | License |
| --- | --- | --- |
| `polly-meterevent-8.5.2.txt` | App-vNext/Polly `4083d906f46dd69e7db54597526b44f5a2ec8943`, `src/Polly.Extensions/Telemetry/TelemetryListenerImpl.cs`, method `MeterEvent` (Polly.Extensions 8.5.2) | BSD-3-Clause |
| `polly-meterevent-8.6.0.txt` | App-vNext/Polly `3fb089717fb63c52b51d66a378cdad08b3f6335f`, same path and method (Polly.Extensions 8.6.0) | BSD-3-Clause |
| `newtonsoft-jsonconvert-tostring-pdb.txt` | Newtonsoft.Json 13.0.3 `JsonConvert.ToString(object?)` PDB comparison text (JamesNK/Newtonsoft.Json `0a2e291c0d9c0c7675d445703e51750363a549ef`, `Src/Newtonsoft.Json/JsonConvert.cs`), as projected by `dotnet-inspect member … -S "Source Diff"` | MIT |
| `newtonsoft-jsonconvert-tostring-decompiled.txt` | The decompiled comparison text of the same member from the same command | MIT |
