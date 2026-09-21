using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

/// <summary>Owned JSON boundary for detached structured C# Type documents.</summary>
public static class CSharpTypeDocumentJson
{
    const int SchemaVersion = 1;
    internal const int MaxSerializedCharacters =
        MetadataSafetyPolicy.MaxStructuralSignatureWorkChars * 8;
    const string ContractError =
        "Structured C# Type document JSON violates the wire contract.";

    /// <summary>Serializes one validated document using the owned wire contract.</summary>
    public static string Serialize(CSharpTypeDocument document, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        CSharpTypeDocumentWire wire = ToWire(document);
        return indented
            ? JsonSerializer.Serialize(
                wire,
                CSharpTypeDocumentJsonContext.Default.CSharpTypeDocumentWire)
            : JsonSerializer.Serialize(
                wire,
                CSharpTypeDocumentCompactJsonContext.Default.CSharpTypeDocumentWire);
    }

    /// <summary>
    /// Reads an untrusted detached document, revalidates its model, and verifies
    /// its product-issued revision.
    /// </summary>
    public static CSharpTypeDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxSerializedCharacters)
        {
            throw new JsonException(
                "Structured C# Type document JSON exceeds the input-size limit.");
        }
        ValidateJsonShape(json);
        try
        {
            CSharpTypeDocumentWire wire = JsonSerializer.Deserialize(
                json,
                CSharpTypeDocumentStrictJsonContext.Default.CSharpTypeDocumentWire)
                ?? throw new JsonException(ContractError);
            if (wire.SchemaVersion != SchemaVersion)
                throw new JsonException(ContractError);

            CSharpTypeDocumentData data = ToData(wire);
            CSharpTypeDocument document = CSharpTypeDocument.Create(
                data.TypeName,
                data.TypeAddress,
                data.Source,
                data.Frame,
                data.Artifacts,
                data.Bodies,
                data.Declarations,
                data.ContractRelationships);
            var suppliedRevision = new CSharpDocumentRevision(wire.Revision);
            if (document.Revision != suppliedRevision)
            {
                throw new JsonException(
                    "Structured C# Type document revision does not match its payload.");
            }
            return document;
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception error)
            when (error is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            throw new JsonException(ContractError);
        }
    }

    static void ValidateJsonShape(string json)
    {
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new JsonException("Structured C# Type document JSON is malformed.");
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException(ContractError);
            ValidateNoDuplicateProperties(parsed.RootElement);
        }
    }

    static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException(ContractError);
                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item);
        }
    }

    static CSharpTypeDocumentWire ToWire(CSharpTypeDocument document)
        => new(
            SchemaVersion,
            document.Revision.Sha256,
            new(
                document.TypeName.Namespace,
                document.TypeName.Segments),
            new(
                document.TypeAddress.ModuleVersionId,
                document.TypeAddress.Definition.Value),
            new(
                document.Source.Kind,
                document.Source.AssemblyName,
                document.Source.PdbSupplied,
                document.Source.Symbols,
                document.Source.RenderingPolicy),
            new(
                [.. document.Frame.PrefixParts.Select(ToWire)],
                document.Frame.DeclarationSeparator,
                document.Frame.Suffix),
            [.. document.Artifacts.Select(static artifact => new CSharpTypeArtifactWire(
                artifact.Id,
                ToWire(artifact.Anchor),
                artifact.MetadataToken,
                artifact.Kind,
                artifact.Origin,
                artifact.Representation.Kind,
                artifact.Representation.Role,
                artifact.Representation.TargetId))],
            [.. document.Bodies.Select(static body => new CSharpTypeBodyWire(
                body.Id,
                new(body.Address.ModuleVersionId, body.Address.Token),
                body.ArtifactId,
                body.Role,
                body.HasManagedBody,
                body.Outcome,
                body.Fidelity,
                body.Fingerprint,
                [.. body.Diagnostics.Select(static diagnostic =>
                    new CSharpTypeDiagnosticWire(
                        diagnostic.Id,
                        diagnostic.Message))]))],
            [.. document.Declarations.Select(static declaration =>
                new CSharpTypeDeclarationWire(
                    declaration.Id,
                    declaration.SourceOrder,
                    ToWire(declaration.Anchor),
                    declaration.DeclarationToken,
                    declaration.Kind,
                    declaration.Accessibility,
                    declaration.Placement,
                    declaration.Origin,
                    [.. declaration.Parts.Select(ToWire)]))],
            document.ContractRelationships);

    static CSharpTypeRenderPartWire ToWire(CSharpTypeRenderPart part)
        => new(
            part.Id,
            part.Kind,
            part.Region,
            part.FullText,
            part.SkeletonText,
            part.ImplementationKind,
            [.. part.OwnedBodies.Select(static body => new CSharpTypeOwnedBodyWire(
                body.BodyId,
                new(body.FullRange.Start, body.FullRange.Length),
                body.HasDrillDownDestination))],
            [.. part.Contributions.Select(static contribution =>
                new CSharpTypeContributionWire(
                    contribution.BodyId,
                    contribution.Role,
                    new(
                        contribution.FullRange.Start,
                        contribution.FullRange.Length)))]);

    static CSharpTypeAnchorWire ToWire(MemberAnchor anchor)
        => new(
            anchor.StableSelector,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            anchor.TypeFullName,
            anchor.MemberName);

    static CSharpTypeDocumentData ToData(CSharpTypeDocumentWire wire)
    {
        RequireInitialized(wire.TypeName, nameof(wire.TypeName));
        RequireInitialized(wire.TypeAddress, nameof(wire.TypeAddress));
        RequireInitialized(wire.Source, nameof(wire.Source));
        RequireInitialized(wire.Frame, nameof(wire.Frame));
        RequireArray(wire.Artifacts, nameof(wire.Artifacts));
        RequireArray(wire.Bodies, nameof(wire.Bodies));
        RequireArray(wire.Declarations, nameof(wire.Declarations));

        return new(
            CreateTypeName(wire.TypeName),
            MetadataTypeDefinitionAddress.FromToken(
                wire.TypeAddress.ModuleVersionId,
                wire.TypeAddress.Definition),
            new CSharpTypeDocumentSource(
                wire.Source.Kind,
                wire.Source.AssemblyName,
                wire.Source.PdbSupplied,
                wire.Source.Symbols,
                wire.Source.RenderingPolicy),
            new CSharpTypeFrame(
                [.. MapParts(
                    RequireArray(wire.Frame.PrefixParts, "frame.prefix_parts"))],
                wire.Frame.DeclarationSeparator,
                wire.Frame.Suffix),
            [.. wire.Artifacts.Select(static artifact =>
            {
                RequireInitialized(artifact, "artifacts[]");
                return new CSharpTypePhysicalArtifact(
                    artifact.Id,
                    FromWire(artifact.Anchor),
                    artifact.MetadataToken,
                    artifact.Kind,
                    artifact.Origin,
                    new(
                        artifact.RepresentationKind,
                        artifact.Role,
                        artifact.TargetId));
            })],
            [.. wire.Bodies.Select(static body =>
            {
                RequireInitialized(body, "bodies[]");
                RequireInitialized(body.Address, "bodies[].address");
                return new CSharpTypePhysicalBody(
                    body.Id,
                    CreateMethodAddress(body.Address),
                    body.ArtifactId,
                    body.Role,
                    body.HasManagedBody,
                    body.Outcome,
                    body.Fidelity,
                    body.Fingerprint,
                    [.. RequireArray(
                        body.Diagnostics,
                        "bodies[].diagnostics")
                        .Select(static diagnostic =>
                        {
                            RequireInitialized(
                                diagnostic,
                                "bodies[].diagnostics[]");
                            return new DecompilerDiagnostic(
                                diagnostic.Id,
                                diagnostic.Message);
                        })]);
            })],
            [.. wire.Declarations.Select(static declaration =>
            {
                RequireInitialized(declaration, "declarations[]");
                return new CSharpTypeDeclaration(
                    declaration.Id,
                    declaration.SourceOrder,
                    FromWire(declaration.Anchor),
                    declaration.DeclarationToken,
                    declaration.Kind,
                    declaration.Accessibility,
                    declaration.Placement,
                    declaration.Origin,
                    [.. MapParts(RequireArray(
                        declaration.Parts,
                        "declarations[].parts"))]);
            })],
            wire.ContractRelationships);
    }

    static IEnumerable<CSharpTypeRenderPart> MapParts(
        ImmutableArray<CSharpTypeRenderPartWire> parts)
        => parts.Select(static part =>
        {
            RequireInitialized(part, "parts[]");
            return new CSharpTypeRenderPart(
                part.Id,
                part.Kind,
                part.Region,
                part.FullText,
                part.SkeletonText,
                part.ImplementationKind,
                [.. RequireArray(part.OwnedBodies, "parts[].owned_bodies")
                    .Select(static body =>
                    {
                        RequireInitialized(body, "owned_bodies[]");
                        RequireInitialized(body.Range, "owned_bodies[].range");
                        return new CSharpTypeOwnedBodyReference(
                            body.BodyId,
                            new(body.Range.Start, body.Range.Length),
                            body.HasDrillDownDestination);
                    })],
                [.. RequireArray(part.Contributions, "parts[].contributions")
                    .Select(static contribution =>
                    {
                        RequireInitialized(contribution, "contributions[]");
                        RequireInitialized(
                            contribution.Range,
                            "contributions[].range");
                        return new CSharpTypeBodyContribution(
                            contribution.BodyId,
                            contribution.Role,
                            new(
                                contribution.Range.Start,
                                contribution.Range.Length));
                    })]);
        });

    static MetadataTypeDefinitionName CreateTypeName(CSharpTypeNameWire wire)
    {
        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(
                wire.Namespace,
                RequireArray(wire.Segments, "type_name.segments"));
        return result is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new ArgumentException(
                "type_name is invalid.",
                nameof(wire));
    }

    static MetadataMethodAddress CreateMethodAddress(
        CSharpTypeMethodAddressWire wire)
    {
        if ((wire.Token & unchecked((int)0xFF000000)) != 0x06000000
            || (wire.Token & 0x00FFFFFF) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wire),
                "A physical body address requires a non-zero MethodDef token.");
        }
        return new(
            wire.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(wire.Token & 0x00FFFFFF));
    }

    static MemberAnchor FromWire(CSharpTypeAnchorWire? anchor)
    {
        RequireInitialized(anchor, "anchor");
        return new(
            anchor.StableSelector,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            anchor.TypeFullName,
            anchor.MemberName);
    }

    static ImmutableArray<T> RequireArray<T>(
        ImmutableArray<T> value,
        string name)
    {
        if (value.IsDefault)
            throw new ArgumentException($"{name} must be initialized.", name);
        return value;
    }

    static void RequireInitialized<T>([NotNull] T? value, string name)
        where T : class
    {
        if (value is null)
            throw new ArgumentException($"{name} must be initialized.", name);
    }
}

internal sealed record CSharpTypeDocumentWire(
    int SchemaVersion,
    string Revision,
    CSharpTypeNameWire TypeName,
    CSharpTypeDefinitionAddressWire TypeAddress,
    CSharpTypeSourceWire Source,
    CSharpTypeFrameWire Frame,
    ImmutableArray<CSharpTypeArtifactWire> Artifacts,
    ImmutableArray<CSharpTypeBodyWire> Bodies,
    ImmutableArray<CSharpTypeDeclarationWire> Declarations,
    CSharpTypeContractRelationshipCapability ContractRelationships);

internal sealed record CSharpTypeNameWire(
    string Namespace,
    ImmutableArray<string> Segments);

internal sealed record CSharpTypeDefinitionAddressWire(
    Guid ModuleVersionId,
    int Definition);

internal sealed record CSharpTypeSourceWire(
    CSharpTypeSourceKind Kind,
    string AssemblyName,
    bool PdbSupplied,
    DecompilerSymbolSource Symbols,
    string RenderingPolicy);

internal sealed record CSharpTypeFrameWire(
    ImmutableArray<CSharpTypeRenderPartWire> PrefixParts,
    string DeclarationSeparator,
    string Suffix);

internal sealed record CSharpTypeArtifactWire(
    int Id,
    CSharpTypeAnchorWire Anchor,
    int MetadataToken,
    CSharpTypeArtifactKind Kind,
    CSharpTypeOrigin Origin,
    CSharpTypeArtifactRepresentationKind RepresentationKind,
    CSharpTypeArtifactRole Role,
    int? TargetId);

internal sealed record CSharpTypeBodyWire(
    int Id,
    CSharpTypeMethodAddressWire Address,
    int ArtifactId,
    CSharpTypeBodyRole Role,
    bool HasManagedBody,
    CSharpTypeBodyOutcome Outcome,
    DecompilationFidelity? Fidelity,
    string Fingerprint,
    ImmutableArray<CSharpTypeDiagnosticWire> Diagnostics);

internal sealed record CSharpTypeDiagnosticWire(string Id, string Message);

internal sealed record CSharpTypeMethodAddressWire(Guid ModuleVersionId, int Token);

internal sealed record CSharpTypeDeclarationWire(
    int Id,
    int SourceOrder,
    CSharpTypeAnchorWire Anchor,
    int DeclarationToken,
    CSharpTypeDeclarationKind Kind,
    CSharpTypeAccessibility Accessibility,
    CSharpTypeDeclarationPlacement Placement,
    CSharpTypeOrigin Origin,
    ImmutableArray<CSharpTypeRenderPartWire> Parts);

internal sealed record CSharpTypeRenderPartWire(
    int Id,
    CSharpTypeRenderPartKind Kind,
    CSharpTypeRegionRole Region,
    string FullText,
    string SkeletonText,
    CSharpTypeImplementationKind? ImplementationKind,
    ImmutableArray<CSharpTypeOwnedBodyWire> OwnedBodies,
    ImmutableArray<CSharpTypeContributionWire> Contributions);

internal sealed record CSharpTypeOwnedBodyWire(
    int BodyId,
    CSharpTypeRangeWire Range,
    bool HasDrillDownDestination);

internal sealed record CSharpTypeContributionWire(
    int BodyId,
    CSharpTypeBodyContributionRole Role,
    CSharpTypeRangeWire Range);

internal sealed record CSharpTypeRangeWire(int Start, int Length);

internal sealed record CSharpTypeAnchorWire(
    string StableSelector,
    string CanonicalSignature,
    string Fingerprint,
    string TypeFullName,
    string MemberName);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(CSharpTypeDocumentWire))]
internal sealed partial class CSharpTypeDocumentJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false)]
[JsonSerializable(typeof(CSharpTypeDocumentWire))]
internal sealed partial class CSharpTypeDocumentCompactJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CSharpTypeDocumentWire))]
internal sealed partial class CSharpTypeDocumentStrictJsonContext : JsonSerializerContext;
