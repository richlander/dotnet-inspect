# dotnet-inspect metadata-confusion test fixture

This package is test-only evidence for dotnet-inspect metadata inspection. It
is not a supported library and must not be loaded or executed.

The managed assembly contains valid ECMA-335 metadata with adversarial names,
custom-attribute values, paths, P/Invoke data, resources, and user strings.
`content/metadata-fixture.json` records the exact raw value and metadata token
for each specimen. The package does not contact the display-only URLs embedded
in those specimens.
