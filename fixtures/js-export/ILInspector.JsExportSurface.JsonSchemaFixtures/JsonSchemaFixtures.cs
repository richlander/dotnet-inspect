using TsJsExport;

namespace ILInspector.JsExportSurface.JsonSchemaFixtures;

[JsExportJsonSchema(
    "fixture.schema",
    "serialize",
    "fixture.catalog",
    "sha256:0000000000000000000000000000000000000000000000000000000000000000")]
public sealed record JsonSchemaMetadataFixture(
    [property: JsExportJsonSchemaSlot(
        0,
        "value",
        "fixture.rows",
        "value",
        Displayable = false)]
    string Value);

[JsExportJsonSchema(
    "fixture.missing-slot",
    "serialize",
    "fixture.catalog",
    "sha256:0000000000000000000000000000000000000000000000000000000000000000")]
public sealed record MissingSchemaSlotFixture(
    [property: JsExportJsonSchemaSlot(
        0,
        "first",
        "fixture.rows",
        "first")]
    string First,
    string Second);

[JsExportJsonSchema(
    "fixture.duplicate-order",
    "serialize",
    "fixture.catalog",
    "sha256:0000000000000000000000000000000000000000000000000000000000000000")]
public sealed record DuplicateSchemaOrderFixture(
    [property: JsExportJsonSchemaSlot(
        0,
        "first",
        "fixture.rows",
        "first")]
    string First,
    [property: JsExportJsonSchemaSlot(
        0,
        "second",
        "fixture.rows",
        "second")]
    string Second);

[JsExportJsonSchema(
    "fixture.skipped-order",
    "serialize",
    "fixture.catalog",
    "sha256:0000000000000000000000000000000000000000000000000000000000000000")]
public sealed record SkippedSchemaOrderFixture(
    [property: JsExportJsonSchemaSlot(
        0,
        "first",
        "fixture.rows",
        "first")]
    string First,
    [property: JsExportJsonSchemaSlot(
        2,
        "second",
        "fixture.rows",
        "second")]
    string Second);
