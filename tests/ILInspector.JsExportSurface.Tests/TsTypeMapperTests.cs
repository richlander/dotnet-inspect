using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Verifies <see cref="TsTypeMapper"/>: all TS-specific "personality" (Task/ValueTask unwrap to
/// Promise, array/nullable mapping, C# primitive to TS primitive, record-name passthrough) lives
/// here per the repo's architecture decision that the OM stays C#-faithful.
/// </summary>
public sealed class TsTypeMapperTests
{
    private static readonly HashSet<string> RecordNames = new(StringComparer.Ordinal) { "WidgetDto" };

    [Theory]
    [InlineData("string", "string")]
    [InlineData("System.String", "string")]
    [InlineData("bool", "boolean")]
    [InlineData("System.Boolean", "boolean")]
    [InlineData("int", "number")]
    [InlineData("System.Int32", "number")]
    [InlineData("long", "number")]
    [InlineData("double", "number")]
    [InlineData("void", "void")]
    public void MapReturnType_MapsCSharpPrimitivesToTsPrimitives(string csharpType, string expected)
    {
        Assert.Equal(expected, TsTypeMapper.MapReturnType(csharpType, RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericTaskToPromise()
    {
        Assert.Equal("Promise<string>", TsTypeMapper.MapReturnType("Task<string>", RecordNames));
        Assert.Equal(
            "Promise<string>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task<string>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsGenericValueTaskToPromise()
    {
        Assert.Equal("Promise<number>", TsTypeMapper.MapReturnType("ValueTask<int>", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("Task", RecordNames));
        Assert.Equal(
            "Promise<void>",
            TsTypeMapper.MapReturnType("System.Threading.Tasks.Task", RecordNames));
    }

    [Fact]
    public void MapReturnType_UnwrapsNonGenericValueTaskToPromiseVoid()
    {
        Assert.Equal("Promise<void>", TsTypeMapper.MapReturnType("ValueTask", RecordNames));
    }

    [Fact]
    public void Map_ArrayTypeMapsToTsArraySyntax()
    {
        Assert.Equal("number[]", TsTypeMapper.MapParameterType("int[]", RecordNames));
    }

    [Fact]
    public void Map_NullableTypeMapsToUnionWithNull()
    {
        Assert.Equal("WidgetDto | null", TsTypeMapper.MapParameterType("WidgetDto?", RecordNames));
    }

    [Fact]
    public void Map_KnownRecordNamePassesThroughByName()
    {
        Assert.Equal("WidgetDto", TsTypeMapper.MapParameterType("WidgetDto", RecordNames));
        Assert.Equal(
            "WidgetDto",
            TsTypeMapper.MapParameterType("ILInspector.JsExportSurface.Fixtures.WidgetDto", RecordNames));
    }

    [Fact]
    public void Map_UnknownTypeMapsToUnknownAndReportsDiagnostic()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType("SomeUnmappedType", RecordNames, diagnostics, "WidgetDto.Property"));
        Assert.Collection(
            diagnostics.UnmappedTypes,
            d =>
            {
                Assert.Equal("WidgetDto.Property", d.Location);
                Assert.Equal("SomeUnmappedType", d.CSharpType);
            });
    }

    [Fact]
    public void Map_ArrayOfNullableRecordParenthesizesTheUnion()
    {
        // "WidgetDto | null[]" would bind as "WidgetDto | (null[])" in TS; the array of a union
        // must be parenthesized: "(WidgetDto | null)[]".
        Assert.Equal("(WidgetDto | null)[]", TsTypeMapper.MapParameterType("WidgetDto?[]", RecordNames));
    }

    [Fact]
    public void Map_NullableValueTypeUnwrapsSystemNullable()
    {
        // Nullable<T> value types (e.g. `int?`) surface in signature text as "System.Nullable<T>",
        // not the "T?" suffix form used for nullable reference types.
        Assert.Equal("number | null", TsTypeMapper.MapParameterType("System.Nullable<int>", RecordNames));
        Assert.Equal("number | null", TsTypeMapper.MapParameterType("Nullable<int>", RecordNames));
    }

    [Theory]
    [InlineData("byte", "number")]
    [InlineData("sbyte", "number")]
    [InlineData("short", "number")]
    [InlineData("ushort", "number")]
    [InlineData("uint", "number")]
    [InlineData("long", "number")]
    [InlineData("ulong", "number")]
    [InlineData("float", "number")]
    [InlineData("decimal", "number")]
    [InlineData("char", "string")]
    public void Map_MapsAllCommonCSharpPrimitives(string csharpType, string expected)
    {
        Assert.Equal(expected, TsTypeMapper.MapParameterType(csharpType, RecordNames));
    }

    [Fact]
    public void Map_DictionaryOfStringKeysMapsToRecord()
    {
        Assert.Equal(
            "Record<string, string>",
            TsTypeMapper.MapParameterType("IReadOnlyDictionary<string, string>", RecordNames));
    }

    [Fact]
    public void Map_DictionaryWithNonStringKeyReportsUnmappedType()
    {
        var diagnostics = new TsBindGenDiagnostics();

        Assert.Equal(
            "unknown",
            TsTypeMapper.MapParameterType(
                "Dictionary<int, string>",
                RecordNames,
                diagnostics,
                "WidgetCatalog.OwnersByKey"));
        Assert.Contains(
            diagnostics.UnmappedTypes,
            d => d.Location == "WidgetCatalog.OwnersByKey" && d.CSharpType == "Dictionary<int, string>");
    }

}
