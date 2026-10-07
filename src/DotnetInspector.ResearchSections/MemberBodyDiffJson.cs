using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Presentation;
using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

/// <summary>Complete owning content, independent of a host's display projection.</summary>
public static class MemberBodyDiffJson
{
    public static string Serialize(MemberBodyDiffInventory inventory)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("implementation");
            JsonSerializer.Serialize(writer, inventory.Implementation,
                ImplementationDiffJsonContext.Default.ImplementationDiffDocument);
            writer.WritePropertyName("api");
            JsonSerializer.Serialize(writer, inventory.Api, LibraryApiDiffJsonContext.Default.LibraryApiDiffDocument);
            writer.WritePropertyName("members");
            JsonSerializer.Serialize(writer, inventory.Members.ToArray(), MemberBodyDiffJsonContext.Default.MemberBodyDiffMemberArray);
            writer.WritePropertyName("destinations");
            JsonSerializer.Serialize(writer, inventory.Destinations.ToArray(), MemberBodyDiffJsonContext.Default.MemberBodyDiffMemberArray);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Serialize(MemberBodyDiffMemberDocument member)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("subject");
            JsonSerializer.Serialize(writer, member.Subject, MemberBodyDiffJsonContext.Default.ResearchSubjectKey);
            writer.WritePropertyName("apiRelation");
            JsonSerializer.Serialize(writer, member.ApiRelation, MemberBodyDiffJsonContext.Default.LibraryApiMemberRelation);
            writer.WritePropertyName("document");
            writer.WriteRawValue(AnnotatedSourceDiffJson.Serialize(member.Document, false));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(MemberBodyDiffMember[]))]
[JsonSerializable(typeof(ResearchSubjectKey))]
[JsonSerializable(typeof(LibraryApiMemberRelation))]
internal sealed partial class MemberBodyDiffJsonContext : JsonSerializerContext;
