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
/// Compiler-produced serializer use for the collision gate. The test supplies
/// its JS-export marker separately so this fixture remains isolated from the
/// test host's command fixture assembly.
/// </summary>
public static class NestedJsonSerializableRootCollisionSerializer
{
    public static string Serialize() =>
        JsonSerializer.Serialize(
            new NestedJsonSerializableRootCollision("collision"),
            NestedJsonSerializableRootCollisionContext.Default
                .NestedJsonSerializableRootCollision);
}

#pragma warning disable SYSLIB1031 // The collision is the source-generator boundary under test.
[JsonSerializable(typeof(NestedJsonSerializableRootCollision))]
[JsonSerializable(
    typeof(NestedJsonSerializableRootCollisionContainer
        .NestedJsonSerializableRootCollision))]
public sealed partial class NestedJsonSerializableRootCollisionContext
    : JsonSerializerContext;
#pragma warning restore SYSLIB1031
