# Annotated source viewer

This dependency-free browser prototype consumes the exact
`AnnotatedSourceDocument` JSON emitted by `dotnet-inspect`. It demonstrates the
portable contract without re-running the decompiler:

- lines and line numbers are derived from the canonical `text` buffer;
- JavaScript string indexes consume the document's UTF-16 span coordinates
  directly;
- clicking a fact follows `fact → target → node → spans → text`;
- clicking source text chooses the tightest structural node at that offset;
- selecting a node kind highlights every matching syntax span;
- one node can highlight several separated spans without selecting interleaved
  IL;
- C# and IL lines can be hidden independently without rebasing coordinates;
- facts with no targets remain visible as explicitly unanchored observations.

## Run

```bash
cd prototypes/annotated-source-viewer
npm test
npm run dev
```

Open <http://127.0.0.1:5199>. The built-in sample includes a multi-span C#
`ForStatement`, two IL instructions, a cross-medium allocation fact, and an
unanchored member-header fact.

Load a real document produced by the CLI:

```bash
dotnet-inspect member MyType --library MyLibrary.dll MyMethod:1 \
  -S "Annotated Source Document" --json > document.json
```

Use **load JSON** in the viewer and select `document.json`. Input strings are
HTML-escaped before rendering, and the viewer validates IDs, targets, spans,
bounds, and UTF-16 before accepting a payload.
