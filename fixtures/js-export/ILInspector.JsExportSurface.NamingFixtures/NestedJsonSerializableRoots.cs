using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NamingFixtures;

public sealed class NestedJsonSerializableRootFixture
{
    public sealed record Leaf(string Value);
}

[JsonSerializable(typeof(NestedJsonSerializableRootFixture.Leaf))]
public sealed partial class NestedJsonSerializableRootFixtureContext
    : JsonSerializerContext;

public sealed record NestedJsonSerializableRootCollision(string Value);

public sealed class NestedJsonSerializableRootCollisionContainer
{
    public sealed record NestedJsonSerializableRootCollision(string Value);
}

/// <summary>
/// Compiler-produced serializer use and JS export for the collision gate.
/// </summary>
#pragma warning disable CA1416 // Compiler-produced browser export evidence.
public static partial class NestedJsonSerializableRootCollisionSerializer
{
    [JSExport]
    public static string Serialize() =>
        JsonSerializer.Serialize(
            new NestedJsonSerializableRootCollision("collision"),
            NestedJsonSerializableRootCollisionContext.Default
                .NestedJsonSerializableRootCollision);
}
#pragma warning restore CA1416

#pragma warning disable SYSLIB1031 // The collision is the source-generator boundary under test.
[JsonSerializable(typeof(NestedJsonSerializableRootCollision))]
[JsonSerializable(
    typeof(NestedJsonSerializableRootCollisionContainer
        .NestedJsonSerializableRootCollision))]
public sealed partial class NestedJsonSerializableRootCollisionContext
    : JsonSerializerContext;
#pragma warning restore SYSLIB1031
