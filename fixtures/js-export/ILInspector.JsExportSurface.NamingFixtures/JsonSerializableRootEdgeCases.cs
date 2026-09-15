using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NamingFixtures;

/// <summary>
/// Source-generator naming fixtures for vector, multidimensional, and jagged
/// array roots. Only the vector is supported by System.Text.Json at runtime;
/// the other calls exist for metadata/body-evidence correlation tests and are
/// not invoked except by the runtime-boundary assertion.
/// </summary>
#pragma warning disable CA1416 // Compiler-produced browser export evidence.
public static partial class ArrayRootNamingFixtureExports
{
    [JSExport]
    public static string SerializeIntVector() =>
        JsonSerializer.Serialize(
            new[] { 1, 2 },
            ArrayRootNamingFixtureContext.Default.Int32Array);

    [JSExport]
    public static string SerializeIntMatrix() =>
        JsonSerializer.Serialize(
            new int[1, 1],
            ArrayRootNamingFixtureContext.Default.Int32Array2D);

    [JSExport]
    public static string SerializeIntCube() =>
        JsonSerializer.Serialize(
            new int[1, 1, 1],
            ArrayRootNamingFixtureContext.Default.Int32Array3D);

    [JSExport]
    public static string SerializeIntArrayMatrix() =>
        JsonSerializer.Serialize(
            (int[][,])null!,
            ArrayRootNamingFixtureContext.Default.Int32Array2DArray);

    [JSExport]
    public static string SerializeIntMatrixArray() =>
        JsonSerializer.Serialize(
            (int[,][])null!,
            ArrayRootNamingFixtureContext.Default.Int32ArrayArray2D);
}
#pragma warning restore CA1416

[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(int[,]))]
[JsonSerializable(typeof(int[,,]))]
[JsonSerializable(typeof(int[][]))]
[JsonSerializable(typeof(int[][,]))]
[JsonSerializable(typeof(int[,][]))]
public sealed partial class ArrayRootNamingFixtureContext
    : JsonSerializerContext;
