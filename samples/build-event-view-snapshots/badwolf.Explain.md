# Explain

Selected diagnostics: 2
Matched clusters: 1

## Syntax or parser errors

Cluster: `syntax-errors`

The compiler cannot parse one or more source lines.

### Applies to

| Severity | Code | Count | Example |
| --- | --- | ---: | --- |
| error | CS1003 | 1 | Syntax error, ',' expected |
| error | CS1013 | 1 | Invalid number |

### Likely cause

The source contains invalid tokens, missing punctuation, malformed literals, or unbalanced delimiters.

### First fixes

1. Fix the earliest syntax diagnostic first; later syntax diagnostics may be cascades.
2. Use Details on the first diagnostic to inspect the exact source span.
3. Rebuild after fixing the first parse error before changing many nearby lines.

### Useful follow-up

- `dotnet-inspect build <log> -S Errors --tsv`
- `dotnet-inspect build <log> -S Details --markdown`
