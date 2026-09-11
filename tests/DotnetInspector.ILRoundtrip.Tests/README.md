# IL round-trip tests

This executable test project exercises canonical IL assembly round trips. It is
outside the default solution and depends on the vendored managed ILAssembler.

Run commands from the repository root. Use `dotnet run`, not `dotnet test`.

## Restore the ILAssembler dependency

Materialize the vendored project before building or running the tests:

```bash
eng/restore-ilassembler.sh
```

## Run the tests

Run the fast subset used for pull-request validation:

```bash
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release -- \
  --filter-not-trait "Speed=Slow"
```

The fast suite includes canonical array-bound signatures and requires the
vendored assembler to preserve their signature blobs byte-for-byte through the
product-owned `CanonicalIL` scaffold.

Run the full suite, including the assembly-wide sweep:

```bash
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
```
