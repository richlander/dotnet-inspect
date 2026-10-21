using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Ecosystems;

public abstract record EcosystemFindIndexLoadResult
{
    private EcosystemFindIndexLoadResult()
    {
    }

    public sealed record Available(EcosystemFindIndexArtifact Artifact)
        : EcosystemFindIndexLoadResult;

    public sealed record Invalid(InertString Detail)
        : EcosystemFindIndexLoadResult;
}

public static class EcosystemFindIndexJson
{
    public static byte[] Serialize(EcosystemFindIndexArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EcosystemFindIndexValidation.Validate(artifact);
        EcosystemFindIndexDocument document = ToDocument(artifact);
        return JsonSerializer.SerializeToUtf8Bytes(
            document,
            EcosystemFindIndexJsonContext.Default.EcosystemFindIndexDocument);
    }

    public static EcosystemFindIndexLoadResult Load(
        ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            EcosystemFindIndexArtifact artifact = Deserialize(utf8Json.Span);
            EcosystemFindIndexValidation.Validate(artifact);
            return new EcosystemFindIndexLoadResult.Available(artifact);
        }
        catch (Exception exception) when (
            exception is JsonException
                or ArgumentException
                or FormatException
                or NotSupportedException
                or OverflowException)
        {
            return new EcosystemFindIndexLoadResult.Invalid(
                new InertString(TextPolicy.Field, exception.Message));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EcosystemFindIndexArtifact Deserialize(
        ReadOnlySpan<byte> utf8Json)
    {
        EcosystemFindIndexDocument document =
            JsonSerializer.Deserialize(
                utf8Json,
                EcosystemFindIndexJsonContext.Default.EcosystemFindIndexDocument)
            ?? throw new JsonException(
                "The Ecosystem Find Index document is null.");
        return FromDocument(document);
    }

    private static EcosystemFindIndexDocument ToDocument(
        EcosystemFindIndexArtifact artifact) =>
        new()
        {
            SchemaVersion = artifact.SchemaVersion,
            BuilderAlgorithmVersion = artifact.BuilderAlgorithmVersion,
            Ecosystem = artifact.Ecosystem.Value,
            ScannerIdentity = artifact.ScannerIdentity,
            ScannerVersion = artifact.ScannerVersion,
            IntegrationCatalogRevision = artifact.IntegrationCatalogRevision,
            SymbolProjectionVersion = artifact.SymbolProjectionVersion,
            PrefixEnumerationPolicyVersion = artifact.PrefixEnumerationPolicyVersion,
            PackageVersionSelectionPolicyVersion =
                artifact.PackageVersionSelectionPolicyVersion,
            LibrarySelectionPolicyVersion = artifact.LibrarySelectionPolicyVersion,
            TypeIdentityFormatVersion = artifact.TypeIdentityFormatVersion,
            MemberAnchorFormatVersion = artifact.MemberAnchorFormatVersion,
            ConfiguredConcepts = [.. artifact.ConfiguredConcepts],
            Prefixes =
            [
                .. artifact.Prefixes.Select(prefix => prefix.Prefix),
            ],
            Sources =
            [
                .. artifact.Sources.Select(ToSource),
            ],
            Symbols =
            [
                .. artifact.Symbols.Select(ToSymbol),
            ],
            Gaps =
            [
                .. artifact.Gaps.Select(ToGap),
            ],
        };

    private static EcosystemFindIndexSourceDocument ToSource(
        EcosystemFindIndexSourceCoordinate source) =>
        new()
        {
            PrefixOrdinal = source.PrefixOrdinal,
            PackageOrdinal = source.PackageOrdinal,
            LibraryOrdinal = source.LibraryOrdinal,
            Package = ToPackage(source.Package),
            TargetFramework = source.TargetFramework,
            Library = source.Library,
            Asset = ToLibrary(source.Asset),
            Assembly = ToAssembly(source.Assembly),
            ModuleVersionId = source.ModuleVersionId,
        };

    private static EcosystemFindIndexSymbolDocument ToSymbol(
        EcosystemFindIndexSymbol symbol) =>
        new()
        {
            SourceIndex = symbol.SourceIndex,
            SymbolOrdinal = symbol.SymbolOrdinal,
            Kind = symbol.Kind,
            Type = symbol.Type is null ? null : ToType(symbol.Type),
            TypeDiscoveryAttributes =
                symbol.TypeDiscoveryAttributes is null
                    ? null
                    : new()
                    {
                        IsEditorBrowsableNever =
                            symbol.TypeDiscoveryAttributes
                                .IsEditorBrowsableNever,
                        IsObsolete =
                            symbol.TypeDiscoveryAttributes.IsObsolete,
                    },
            Member = symbol.Member is null ? null : ToEvidence(symbol.Member),
            Display = symbol.Display.ToString(),
            Concepts =
            [
                .. symbol.Concepts.Select(association => new EcosystemFindIndexConceptDocument
                {
                    ConceptId = association.ConceptId,
                    Kind = association.Kind,
                }),
            ],
        };

    private static EcosystemFindIndexGapDocument ToGap(
        EcosystemFindIndexGap gap) =>
        new()
        {
            Kind = gap.Kind,
            PrefixOrdinal = gap.PrefixOrdinal,
            Package = gap.Package is null ? null : ToPackage(gap.Package),
            PackageOrdinal = gap.PackageOrdinal,
            Library = gap.Library is null ? null : ToLibrary(gap.Library),
            LibraryOrdinal = gap.LibraryOrdinal,
            Detail = gap.Detail.ToString(),
            SignalOrdinal = gap.SignalOrdinal,
        };

    private static EcosystemFindIndexPackageDocument ToPackage(
        PackageCoordinate package) =>
        new()
        {
            PackageId = package.PackageId,
            Version = package.Version
                ?? throw new ArgumentException("An indexed package version is required."),
        };

    private static EcosystemFindIndexLibraryDocument ToLibrary(
        PackageLibraryIdentity library) =>
        new()
        {
            AssetPath = library.AssetPath,
            AssemblySimpleName = library.AssemblySimpleName,
        };

    private static EcosystemFindIndexAssemblyDocument ToAssembly(
        AssemblyReferenceIdentity assembly) =>
        new()
        {
            Name = assembly.Name,
            Version = assembly.Version?.ToString()
                ?? throw new ArgumentException("An indexed assembly version is required."),
            Culture = assembly.Culture,
            PublicKeyToken = assembly.PublicKeyToken,
        };

    private static EcosystemFindIndexTypeDocument ToType(
        MetadataTypeDefinitionName type) =>
        new()
        {
            Namespace = type.Namespace,
            Segments = [.. type.Segments],
        };

    private static EcosystemFindIndexEvidenceDocument ToEvidence(
        EcosystemIntegrationApiEvidence evidence) =>
        new()
        {
            Member = new()
            {
                StableSelector = evidence.Member.StableSelector,
                CanonicalSignature = evidence.Member.CanonicalSignature,
                Fingerprint = evidence.Member.Fingerprint,
                TypeFullName = evidence.Member.TypeFullName,
                MemberName = evidence.Member.MemberName,
            },
            DeclaringType = ToType(evidence.DeclaringType),
            ReceiverType = evidence.ReceiverType is null
                ? null
                : ToNamedType(evidence.ReceiverType),
            ReturnType = evidence.ReturnType is null
                ? null
                : ToNamedType(evidence.ReturnType),
            DisplayKind = evidence.DisplayKind,
            DisplaySignature = evidence.DisplaySignature,
            DeclaringTypeDiscoveryAttributes = new()
            {
                IsEditorBrowsableNever =
                    evidence.DeclaringTypeDiscoveryAttributes
                        .IsEditorBrowsableNever,
                IsObsolete =
                    evidence.DeclaringTypeDiscoveryAttributes
                        .IsObsolete,
            },
            IsEditorBrowsableNever =
                evidence.IsEditorBrowsableNever,
        };

    private static EcosystemFindIndexNamedTypeDocument ToNamedType(
        MetadataNamedTypeReference type) =>
        type.Scope switch
        {
            MetadataTypeReferenceScope.CurrentAssembly =>
                new()
                {
                    Scope = "current-assembly",
                    Type = ToType(type.Type),
                },
            MetadataTypeReferenceScope.IntrinsicCoreLibrary =>
                new()
                {
                    Scope = "intrinsic-core-library",
                    Type = ToType(type.Type),
                },
            MetadataTypeReferenceScope.AssemblyReference reference =>
                new()
                {
                    Scope = "assembly-reference",
                    Type = ToType(type.Type),
                    Assembly = ToAssembly(reference.Assembly),
                },
            MetadataTypeReferenceScope.ModuleReference module =>
                new()
                {
                    Scope = "module-reference",
                    Type = ToType(type.Type),
                    Module = module.Name,
                },
            _ => throw new ArgumentException(
                "Unknown metadata type-reference scope.",
                nameof(type)),
        };

    private static EcosystemFindIndexArtifact FromDocument(
        EcosystemFindIndexDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Prefixes);
        ArgumentNullException.ThrowIfNull(document.Sources);
        ArgumentNullException.ThrowIfNull(document.Symbols);
        ArgumentNullException.ThrowIfNull(document.Gaps);
        ArgumentNullException.ThrowIfNull(document.ConfiguredConcepts);
        if (!EcosystemPackId.TryCreate(document.Ecosystem, out EcosystemPackId? ecosystem))
            throw new JsonException("The artifact has an invalid Ecosystem identity.");
        PackagePrefixDeclaration[] prefixes =
        [
            .. document.Prefixes.Select(prefix => new PackagePrefixDeclaration(prefix)),
        ];
        EcosystemFindIndexSourceCoordinate[] sources =
        [
            .. document.Sources.Select(FromSource),
        ];
        EcosystemFindIndexSymbol[] symbols =
        [
            .. document.Symbols.Select(FromSymbol),
        ];
        EcosystemFindIndexGap[] gaps =
        [
            .. document.Gaps.Select(FromGap),
        ];

        return new EcosystemFindIndexArtifact(
            document.SchemaVersion,
            document.BuilderAlgorithmVersion,
            ecosystem,
            document.ScannerIdentity,
            document.ScannerVersion,
            document.IntegrationCatalogRevision,
            document.SymbolProjectionVersion,
            document.PrefixEnumerationPolicyVersion,
            document.PackageVersionSelectionPolicyVersion,
            document.LibrarySelectionPolicyVersion,
            document.TypeIdentityFormatVersion,
            document.MemberAnchorFormatVersion,
            [.. document.ConfiguredConcepts],
            [.. prefixes],
            [.. sources],
            [.. symbols],
            [.. gaps]);
    }

    private static EcosystemFindIndexSourceCoordinate FromSource(
        EcosystemFindIndexSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Package);
        ArgumentNullException.ThrowIfNull(source.Asset);
        ArgumentNullException.ThrowIfNull(source.Assembly);
        return new(
            source.PrefixOrdinal,
            source.PackageOrdinal,
            source.LibraryOrdinal,
            FromPackage(source.Package),
            source.TargetFramework,
            source.Library,
            FromLibrary(source.Asset),
            FromAssembly(source.Assembly),
            source.ModuleVersionId);
    }

    private static EcosystemFindIndexSymbol FromSymbol(
        EcosystemFindIndexSymbolDocument symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(symbol.Concepts);
        return new(
            symbol.SourceIndex,
            symbol.SymbolOrdinal,
            symbol.Kind,
            symbol.Type is null ? null : FromType(symbol.Type),
            symbol.TypeDiscoveryAttributes is null
                ? null
                : new(
                    symbol.TypeDiscoveryAttributes
                        .IsEditorBrowsableNever,
                    symbol.TypeDiscoveryAttributes.IsObsolete),
            symbol.Member is null ? null : FromEvidence(symbol.Member),
            InertString.FromEncoded(TextPolicy.Field, symbol.Display),
            [
                .. symbol.Concepts.Select(FromConcept),
            ]);
    }

    private static EcosystemFindIndexConceptAssociation FromConcept(
        EcosystemFindIndexConceptDocument concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return new(
            concept.ConceptId,
            concept.Kind);
    }

    private static EcosystemFindIndexGap FromGap(
        EcosystemFindIndexGapDocument gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        return new(
            gap.Kind,
            gap.PrefixOrdinal,
            gap.Package is null ? null : FromPackage(gap.Package),
            gap.PackageOrdinal,
            gap.Library is null ? null : FromLibrary(gap.Library),
            gap.LibraryOrdinal,
            InertString.FromEncoded(TextPolicy.Field, gap.Detail),
            gap.SignalOrdinal);
    }

    private static PackageCoordinate FromPackage(
        EcosystemFindIndexPackageDocument package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(package.PackageId, package.Version);
    }

    private static PackageLibraryIdentity FromLibrary(
        EcosystemFindIndexLibraryDocument library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new(library.AssetPath, library.AssemblySimpleName);
    }

    private static AssemblyReferenceIdentity FromAssembly(
        EcosystemFindIndexAssemblyDocument assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new(
            assembly.Name,
            Version.Parse(assembly.Version),
            assembly.Culture,
            assembly.PublicKeyToken);
    }

    private static MetadataTypeDefinitionName FromType(
        EcosystemFindIndexTypeDocument type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(type.Segments);
        return MetadataTypeDefinitionName.Create(type.Namespace, [.. type.Segments])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : throw new JsonException(
                    "The artifact contains an invalid metadata Type identity.");
    }

    private static EcosystemIntegrationApiEvidence FromEvidence(
        EcosystemFindIndexEvidenceDocument evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Member);
        ArgumentNullException.ThrowIfNull(evidence.DeclaringType);
        ArgumentNullException.ThrowIfNull(
            evidence.DeclaringTypeDiscoveryAttributes);
        return new(
            new MemberAnchor(
                evidence.Member.StableSelector,
                evidence.Member.CanonicalSignature,
                evidence.Member.Fingerprint,
                evidence.Member.TypeFullName,
                evidence.Member.MemberName),
            FromType(evidence.DeclaringType),
            evidence.ReceiverType is null ? null : FromNamedType(evidence.ReceiverType),
            evidence.ReturnType is null ? null : FromNamedType(evidence.ReturnType),
            evidence.DisplayKind,
            evidence.DisplaySignature,
            new(
                evidence.DeclaringTypeDiscoveryAttributes
                    .IsEditorBrowsableNever,
                evidence.DeclaringTypeDiscoveryAttributes.IsObsolete),
            evidence.IsEditorBrowsableNever);
    }

    private static MetadataNamedTypeReference FromNamedType(
        EcosystemFindIndexNamedTypeDocument type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(type.Type);
        MetadataTypeReferenceScope scope = type.Scope switch
        {
            "current-assembly" when type.Assembly is null && type.Module is null =>
                new MetadataTypeReferenceScope.CurrentAssembly(),
            "intrinsic-core-library" when type.Assembly is null && type.Module is null =>
                new MetadataTypeReferenceScope.IntrinsicCoreLibrary(),
            "assembly-reference" when type.Assembly is not null && type.Module is null =>
                new MetadataTypeReferenceScope.AssemblyReference(
                    FromAssembly(type.Assembly)),
            "module-reference" when type.Assembly is null && type.Module is not null =>
                new MetadataTypeReferenceScope.ModuleReference(type.Module),
            _ => throw new JsonException(
                "The artifact contains an inconsistent metadata Type scope."),
        };
        return new MetadataNamedTypeReference(scope, FromType(type.Type));
    }
}

internal sealed record EcosystemFindIndexDocument
{
    public required int SchemaVersion { get; init; }
    public required int BuilderAlgorithmVersion { get; init; }
    public required string Ecosystem { get; init; }
    public required string ScannerIdentity { get; init; }
    public required int ScannerVersion { get; init; }
    public required int IntegrationCatalogRevision { get; init; }
    public required int SymbolProjectionVersion { get; init; }
    public required int PrefixEnumerationPolicyVersion { get; init; }
    public required int PackageVersionSelectionPolicyVersion { get; init; }
    public required int LibrarySelectionPolicyVersion { get; init; }
    public required int TypeIdentityFormatVersion { get; init; }
    public required int MemberAnchorFormatVersion { get; init; }
    public required string[] ConfiguredConcepts { get; init; }
    public required string[] Prefixes { get; init; }
    public required EcosystemFindIndexSourceDocument[] Sources { get; init; }
    public required EcosystemFindIndexSymbolDocument[] Symbols { get; init; }
    public required EcosystemFindIndexGapDocument[] Gaps { get; init; }
}

internal sealed record EcosystemFindIndexSourceDocument
{
    public required int PrefixOrdinal { get; init; }
    public required int PackageOrdinal { get; init; }
    public required int LibraryOrdinal { get; init; }
    public required EcosystemFindIndexPackageDocument Package { get; init; }
    public required string TargetFramework { get; init; }
    public required string Library { get; init; }
    public required EcosystemFindIndexLibraryDocument Asset { get; init; }
    public required EcosystemFindIndexAssemblyDocument Assembly { get; init; }
    public required Guid ModuleVersionId { get; init; }
}

internal sealed record EcosystemFindIndexSymbolDocument
{
    public required int SourceIndex { get; init; }
    public required int SymbolOrdinal { get; init; }
    public required EcosystemFindIndexSymbolKind Kind { get; init; }
    public EcosystemFindIndexTypeDocument? Type { get; init; }
    public EcosystemFindIndexTypeDiscoveryDocument?
        TypeDiscoveryAttributes
    { get; init; }
    public EcosystemFindIndexEvidenceDocument? Member { get; init; }
    public required string Display { get; init; }
    public required EcosystemFindIndexConceptDocument[] Concepts { get; init; }
}

internal sealed record EcosystemFindIndexConceptDocument
{
    public required string ConceptId { get; init; }
    public required string Kind { get; init; }
}

internal sealed record EcosystemFindIndexGapDocument
{
    public required EcosystemFindIndexGapKind Kind { get; init; }
    public required int PrefixOrdinal { get; init; }
    public EcosystemFindIndexPackageDocument? Package { get; init; }
    public int? PackageOrdinal { get; init; }
    public EcosystemFindIndexLibraryDocument? Library { get; init; }
    public int? LibraryOrdinal { get; init; }
    public required string Detail { get; init; }
    public int? SignalOrdinal { get; init; }
}

internal sealed record EcosystemFindIndexPackageDocument
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
}

internal sealed record EcosystemFindIndexLibraryDocument
{
    public required string AssetPath { get; init; }
    public required string AssemblySimpleName { get; init; }
}

internal sealed record EcosystemFindIndexAssemblyDocument
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Culture { get; init; }
    public string? PublicKeyToken { get; init; }
}

internal sealed record EcosystemFindIndexTypeDocument
{
    public required string Namespace { get; init; }
    public required string[] Segments { get; init; }
}

internal sealed record EcosystemFindIndexTypeDiscoveryDocument
{
    public required bool IsEditorBrowsableNever { get; init; }
    public required bool IsObsolete { get; init; }
}

internal sealed record EcosystemFindIndexMemberDocument
{
    public required string StableSelector { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string Fingerprint { get; init; }
    public required string TypeFullName { get; init; }
    public required string MemberName { get; init; }
}

internal sealed record EcosystemFindIndexEvidenceDocument
{
    public required EcosystemFindIndexMemberDocument Member { get; init; }
    public required EcosystemFindIndexTypeDocument DeclaringType { get; init; }
    public EcosystemFindIndexNamedTypeDocument? ReceiverType { get; init; }
    public EcosystemFindIndexNamedTypeDocument? ReturnType { get; init; }
    public required string DisplayKind { get; init; }
    public required string DisplaySignature { get; init; }
    public required EcosystemFindIndexTypeDiscoveryDocument
        DeclaringTypeDiscoveryAttributes { get; init; }
    public required bool IsEditorBrowsableNever { get; init; }
}

internal sealed record EcosystemFindIndexNamedTypeDocument
{
    public required string Scope { get; init; }
    public required EcosystemFindIndexTypeDocument Type { get; init; }
    public EcosystemFindIndexAssemblyDocument? Assembly { get; init; }
    public string? Module { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowDuplicateProperties = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EcosystemFindIndexDocument))]
internal sealed partial class EcosystemFindIndexJsonContext
    : JsonSerializerContext;
