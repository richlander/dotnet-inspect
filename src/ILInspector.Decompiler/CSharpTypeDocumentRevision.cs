using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using ILInspector.CSharp;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

static class CSharpTypeDocumentRevision
{
    internal static CSharpDocumentRevision Create(CSharpTypeDocumentData data)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, data);
        }
        return new CSharpDocumentRevision(
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)));
    }

    static void Write(Utf8JsonWriter writer, CSharpTypeDocumentData data)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("type_name");
        writer.WriteStartObject();
        writer.WriteString("namespace", data.TypeName.Namespace);
        writer.WritePropertyName("segments");
        writer.WriteStartArray();
        foreach (string segment in data.TypeName.Segments)
            writer.WriteStringValue(segment);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("type_address");
        writer.WriteStartObject();
        writer.WriteString("module_version_id", data.TypeAddress.ModuleVersionId);
        writer.WriteNumber("definition", data.TypeAddress.Definition.Value);
        writer.WriteEndObject();

        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)data.Source.Kind);
        writer.WriteString("assembly_name", data.Source.AssemblyName);
        writer.WriteBoolean("pdb_supplied", data.Source.PdbSupplied);
        writer.WriteNumber("symbols", (int)data.Source.Symbols);
        writer.WriteString("rendering_policy", data.Source.RenderingPolicy);
        writer.WriteEndObject();

        writer.WritePropertyName("frame");
        writer.WriteStartObject();
        writer.WritePropertyName("prefix_parts");
        WriteParts(writer, data.Frame.PrefixParts);
        writer.WriteString("declaration_separator", data.Frame.DeclarationSeparator);
        writer.WriteString("suffix", data.Frame.Suffix);
        writer.WriteEndObject();

        writer.WritePropertyName("artifacts");
        writer.WriteStartArray();
        foreach (CSharpTypePhysicalArtifact artifact in data.Artifacts)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", artifact.Id);
            writer.WritePropertyName("anchor");
            WriteAnchor(writer, artifact.Anchor);
            writer.WriteNumber("metadata_token", artifact.MetadataToken);
            writer.WriteNumber("kind", (int)artifact.Kind);
            writer.WriteNumber("origin", (int)artifact.Origin);
            writer.WritePropertyName("representation");
            writer.WriteStartObject();
            writer.WriteNumber("kind", (int)artifact.Representation.Kind);
            writer.WriteNumber("role", (int)artifact.Representation.Role);
            if (artifact.Representation.TargetId is { } targetId)
                writer.WriteNumber("target_id", targetId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("bodies");
        writer.WriteStartArray();
        foreach (CSharpTypePhysicalBody body in data.Bodies)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", body.Id);
            writer.WritePropertyName("address");
            WriteAddress(writer, body.Address);
            writer.WriteNumber("artifact_id", body.ArtifactId);
            writer.WriteNumber("role", (int)body.Role);
            writer.WriteBoolean("has_managed_body", body.HasManagedBody);
            writer.WriteNumber("outcome", (int)body.Outcome);
            if (body.Fidelity is { } fidelity)
                writer.WriteNumber("fidelity", (int)fidelity);
            writer.WriteString("fingerprint", body.Fingerprint);
            writer.WritePropertyName("diagnostics");
            writer.WriteStartArray();
            foreach (DecompilerDiagnostic diagnostic in body.Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("id", diagnostic.Id);
                writer.WriteString("message", diagnostic.Message);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("declarations");
        writer.WriteStartArray();
        foreach (CSharpTypeDeclaration declaration in data.Declarations)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", declaration.Id);
            writer.WriteNumber("source_order", declaration.SourceOrder);
            writer.WritePropertyName("anchor");
            WriteAnchor(writer, declaration.Anchor);
            writer.WriteNumber("declaration_token", declaration.DeclarationToken);
            writer.WriteNumber("kind", (int)declaration.Kind);
            writer.WriteNumber("accessibility", (int)declaration.Accessibility);
            writer.WriteNumber("placement", (int)declaration.Placement);
            writer.WriteNumber("origin", (int)declaration.Origin);
            writer.WritePropertyName("parts");
            WriteParts(writer, declaration.Parts);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteNumber("documentation", (int)data.Documentation);
        writer.WriteNumber(
            "contract_relationships",
            (int)data.ContractRelationships);
        writer.WriteEndObject();
        writer.Flush();
    }

    static void WriteParts(
        Utf8JsonWriter writer,
        IReadOnlyList<CSharpTypeRenderPart> parts)
    {
        writer.WriteStartArray();
        foreach (CSharpTypeRenderPart part in parts)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", part.Id);
            writer.WriteNumber("kind", (int)part.Kind);
            writer.WriteNumber("region", (int)part.Region);
            writer.WriteString("full_text", part.FullText);
            writer.WriteString("skeleton_text", part.SkeletonText);
            if (part.ImplementationKind is { } implementationKind)
                writer.WriteNumber("implementation_kind", (int)implementationKind);

            writer.WritePropertyName("owned_bodies");
            writer.WriteStartArray();
            foreach (CSharpTypeOwnedBodyReference reference in part.OwnedBodies)
            {
                writer.WriteStartObject();
                writer.WriteNumber("body_id", reference.BodyId);
                WriteRange(writer, reference.FullRange);
                writer.WriteBoolean(
                    "has_drill_down_destination",
                    reference.HasDrillDownDestination);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("contributions");
            writer.WriteStartArray();
            foreach (CSharpTypeBodyContribution contribution in part.Contributions)
            {
                writer.WriteStartObject();
                writer.WriteNumber("body_id", contribution.BodyId);
                writer.WriteNumber("role", (int)contribution.Role);
                WriteRange(writer, contribution.FullRange);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    static void WriteAnchor(Utf8JsonWriter writer, MemberAnchor anchor)
    {
        writer.WriteStartObject();
        writer.WriteString("stable_selector", anchor.StableSelector);
        writer.WriteString("canonical_signature", anchor.CanonicalSignature);
        writer.WriteString("fingerprint", anchor.Fingerprint);
        writer.WriteString("type_full_name", anchor.TypeFullName);
        writer.WriteString("member_name", anchor.MemberName);
        writer.WriteEndObject();
    }

    static void WriteAddress(
        Utf8JsonWriter writer,
        MetadataMethodAddress address)
    {
        writer.WriteStartObject();
        writer.WriteString("module_version_id", address.ModuleVersionId);
        writer.WriteNumber("token", address.Token);
        writer.WriteEndObject();
    }

    static void WriteRange(Utf8JsonWriter writer, CSharpSourceRange range)
    {
        writer.WriteNumber("start", range.Start);
        writer.WriteNumber("length", range.Length);
    }
}
