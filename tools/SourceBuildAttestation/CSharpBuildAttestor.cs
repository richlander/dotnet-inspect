using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DotnetInspector.SourceHouse.BuildAttestation;

public enum CSharpBuildSourceKind
{
    Authored,
    Generated,
}

public sealed record CSharpBuildSource
{
    public CSharpBuildSource(
        string path,
        ReadOnlySpan<byte> bytes,
        CSharpBuildSourceKind kind = CSharpBuildSourceKind.Authored)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Path = path;
        Bytes = ImmutableArray.CreateRange(bytes.ToArray());
        Kind = kind;
    }

    public string Path { get; }
    public ImmutableArray<byte> Bytes { get; }
    public CSharpBuildSourceKind Kind { get; }
}

public sealed record CSharpBuildAttestationLimits
{
    public CSharpBuildAttestationLimits(
        int maximumSourceFiles,
        int maximumSourceBytes,
        int maximumDeclarations,
        int maximumPeBytes,
        int maximumPortablePdbBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSourceFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumPortablePdbBytes);

        MaximumSourceFiles = maximumSourceFiles;
        MaximumSourceBytes = maximumSourceBytes;
        MaximumDeclarations = maximumDeclarations;
        MaximumPeBytes = maximumPeBytes;
        MaximumPortablePdbBytes = maximumPortablePdbBytes;
    }

    public int MaximumSourceFiles { get; }
    public int MaximumSourceBytes { get; }
    public int MaximumDeclarations { get; }
    public int MaximumPeBytes { get; }
    public int MaximumPortablePdbBytes { get; }

    public static CSharpBuildAttestationLimits Default { get; } =
        new(
            maximumSourceFiles: 10_000,
            maximumSourceBytes: 64 * 1024 * 1024,
            maximumDeclarations: 1_000_000,
            maximumPeBytes: 64 * 1024 * 1024,
            maximumPortablePdbBytes: 64 * 1024 * 1024);
}

public sealed record CSharpBuildAttestationRequest
{
    public CSharpBuildAttestationRequest(
        string assemblyName,
        IReadOnlyList<CSharpBuildSource> sources,
        IReadOnlyList<string> referencePaths,
        SourceHouseCapabilityIdentity capability,
        SourceHouseAttestationIssuerIdentity issuer,
        SourceHouseAttestationProfileIdentity profile,
        SourceHouseAttestationGeneration generation,
        CSharpBuildAttestationLimits? limits = null,
        bool enableImplicitUsings = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(referencePaths);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(generation);
        if (sources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Build sources cannot contain null.",
                nameof(sources));
        }
        if (referencePaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Reference paths cannot contain empty values.",
                nameof(referencePaths));
        }

        AssemblyName = assemblyName;
        Sources = ImmutableArray.CreateRange(sources);
        ReferencePaths = ImmutableArray.CreateRange(referencePaths);
        Capability = capability;
        Issuer = issuer;
        Profile = profile;
        Generation = generation;
        Limits = limits ?? CSharpBuildAttestationLimits.Default;
        EnableImplicitUsings = enableImplicitUsings;
    }

    public string AssemblyName { get; }
    public IReadOnlyList<CSharpBuildSource> Sources { get; }
    public IReadOnlyList<string> ReferencePaths { get; }
    public SourceHouseCapabilityIdentity Capability { get; }
    public SourceHouseAttestationIssuerIdentity Issuer { get; }
    public SourceHouseAttestationProfileIdentity Profile { get; }
    public SourceHouseAttestationGeneration Generation { get; }
    public CSharpBuildAttestationLimits Limits { get; }
    public bool EnableImplicitUsings { get; }
}

public enum CSharpBuildAttestationIncompleteBoundary
{
    SourceFiles,
    SourceBytes,
    Declarations,
    PeBytes,
    PortablePdbBytes,
}

public abstract class CSharpBuildAttestationOutcome
{
    private protected CSharpBuildAttestationOutcome()
    {
    }

    public sealed class Available(SourceHouseBuildAttestation attestation)
        : CSharpBuildAttestationOutcome
    {
        public SourceHouseBuildAttestation Attestation { get; } =
            attestation
            ?? throw new ArgumentNullException(nameof(attestation));
    }

    public sealed class Failed(IReadOnlyList<string> diagnostics)
        : CSharpBuildAttestationOutcome
    {
        public IReadOnlyList<string> Diagnostics { get; } =
            ImmutableArray.CreateRange(
                diagnostics
                ?? throw new ArgumentNullException(nameof(diagnostics)));
    }

    public sealed class Incomplete(
        CSharpBuildAttestationIncompleteBoundary boundary,
        long observed)
        : CSharpBuildAttestationOutcome
    {
        public CSharpBuildAttestationIncompleteBoundary Boundary { get; } =
            boundary;
        public long Observed { get; } = observed;
    }
}

public static class CSharpBuildAttestor
{
    public static CSharpBuildAttestationOutcome EmitAndAttest(
        CSharpBuildAttestationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CSharpBuildSource[] effectiveSources =
            request.EnableImplicitUsings
                ?
                [
                    .. request.Sources,
                    ImplicitUsings(request.AssemblyName),
                ]
                : [.. request.Sources];
        CSharpBuildAttestationLimits limits = request.Limits;
        if (effectiveSources.Length > limits.MaximumSourceFiles)
        {
            return new CSharpBuildAttestationOutcome.Incomplete(
                CSharpBuildAttestationIncompleteBoundary.SourceFiles,
                effectiveSources.Length);
        }

        long sourceBytes = effectiveSources.Sum(
            static source => (long)source.Bytes.Length);
        if (sourceBytes > limits.MaximumSourceBytes)
        {
            return new CSharpBuildAttestationOutcome.Incomplete(
                CSharpBuildAttestationIncompleteBoundary.SourceBytes,
                sourceBytes);
        }

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.Diagnose,
            SourceCodeKind.Regular);
        var inputs = new List<PhysicalInput>(effectiveSources.Length);
        foreach (CSharpBuildSource source in effectiveSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceHousePhysicalSourceEncoding sourceEncoding =
                SourceHousePhysicalSourceEncodings.Detect(
                    source.Bytes.AsSpan());
            if (!SourceHousePhysicalSourceEncodings.IsValid(
                    source.Bytes.AsSpan(),
                    sourceEncoding))
            {
                return new CSharpBuildAttestationOutcome.Failed(
                    [$"{source.Path}: source encoding is invalid."]);
            }

            SourceText text;
            try
            {
                using var stream = new MemoryStream(
                    source.Bytes.ToArray(),
                    writable: false);
                text = SourceText.From(
                    stream,
                    encoding: null,
                    checksumAlgorithm: SourceHashAlgorithm.Sha256,
                    throwIfBinaryDetected: true,
                    canBeEmbedded: false);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or DecoderFallbackException
                    or IOException)
            {
                return new CSharpBuildAttestationOutcome.Failed(
                    [$"{source.Path}: {exception.Message}"]);
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                text,
                parseOptions,
                source.Path,
                cancellationToken);
            inputs.Add(
                new(
                    source,
                    tree,
                    SourceHousePhysicalSourceInputIdentity.Create(),
                    SourceHouseSha256Digest.Compute(
                        source.Bytes.AsSpan()),
                    sourceEncoding));
        }

        MetadataReference[] references;
        try
        {
            references =
            [
                .. request.ReferencePaths
                    .Distinct(StringComparer.Ordinal)
                    .Select(static path =>
                        MetadataReference.CreateFromFile(path)),
            ];
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or BadImageFormatException)
        {
            return new CSharpBuildAttestationOutcome.Failed(
                [exception.Message]);
        }

        var compilation = CSharpCompilation.Create(
            request.AssemblyName,
            inputs.Select(static input => input.Tree),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable,
                concurrentBuild: false));

        var sourceDeclarations = new List<SourceDeclaration>();
        foreach (PhysicalInput input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Source.Kind == CSharpBuildSourceKind.Generated)
                continue;

            SemanticModel semanticModel =
                compilation.GetSemanticModel(
                    input.Tree,
                    ignoreAccessibility: true);
            SyntaxNode root = input.Tree.GetRoot(cancellationToken);
            foreach (SyntaxNode declaration in root
                .DescendantNodesAndSelf()
                .Where(IsCandidateDeclaration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ISymbol? symbol = semanticModel.GetDeclaredSymbol(
                    declaration,
                    cancellationToken);
                if (!IsSupported(symbol, declaration))
                    continue;

                string? xmlIdentity =
                    symbol!.GetDocumentationCommentId();
                if (string.IsNullOrWhiteSpace(xmlIdentity))
                    continue;

                sourceDeclarations.Add(
                    new(
                        new(xmlIdentity),
                        symbol is INamedTypeSymbol
                            ? SourceHouseTargetKind.Type
                            : SourceHouseTargetKind.Member,
                        input,
                        new(
                            declaration.Span.Start,
                            declaration.Span.Length),
                        SourceHouseDeclarationSyntaxKind.Create(
                            declaration.Kind().ToString())));
                if (sourceDeclarations.Count
                    > limits.MaximumDeclarations)
                {
                    return new CSharpBuildAttestationOutcome.Incomplete(
                        CSharpBuildAttestationIncompleteBoundary
                            .Declarations,
                        sourceDeclarations.Count);
                }
            }
        }

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        EmitResult emit = compilation.Emit(
            peStream,
            pdbStream,
            options: new EmitOptions(
                debugInformationFormat:
                    DebugInformationFormat.PortablePdb,
                pdbFilePath: request.AssemblyName + ".pdb"),
            cancellationToken: cancellationToken);
        if (!emit.Success)
        {
            return new CSharpBuildAttestationOutcome.Failed(
                emit.Diagnostics
                    .Where(static diagnostic =>
                        diagnostic.Severity
                            is DiagnosticSeverity.Error
                                or DiagnosticSeverity.Warning)
                    .Select(static diagnostic =>
                        diagnostic.ToString())
                    .ToArray());
        }

        byte[] peImage = peStream.ToArray();
        byte[] pdbImage = pdbStream.ToArray();
        if (peImage.Length > limits.MaximumPeBytes)
        {
            return new CSharpBuildAttestationOutcome.Incomplete(
                CSharpBuildAttestationIncompleteBoundary.PeBytes,
                peImage.Length);
        }
        if (pdbImage.Length > limits.MaximumPortablePdbBytes)
        {
            return new CSharpBuildAttestationOutcome.Incomplete(
                CSharpBuildAttestationIncompleteBoundary
                    .PortablePdbBytes,
                pdbImage.Length);
        }

        ImmutableArray<SourceHouseBuildDeclarationEvidence> declarations =
            BindEmittedDeclarations(
                peImage,
                sourceDeclarations,
                cancellationToken,
                out int compilerIdentityCollisions);
        return new CSharpBuildAttestationOutcome.Available(
            new SourceHouseBuildAttestation(
                request,
                inputs,
                peImage,
                pdbImage,
                declarations,
                compilerIdentityCollisions));
    }

    private static ImmutableArray<SourceHouseBuildDeclarationEvidence>
        BindEmittedDeclarations(
            byte[] peImage,
            IReadOnlyList<SourceDeclaration> sourceDeclarations,
            CancellationToken cancellationToken,
            out int compilerIdentityCollisions)
    {
        using var peReader = new PEReader(
            new MemoryStream(peImage, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        var emitted = new Dictionary<
            XmlDocMemberIdentity,
            HashSet<SourceHousePhysicalTargetAddress>>();
        foreach (ApiType type in surface.Types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.MetadataToken is { } typeToken
                && ApiMemberIdentity.TryGetXmlDocTypeIdentity(
                    type,
                    out XmlDocMemberIdentity typeIdentity))
            {
                EntityHandle handle =
                    MetadataTokens.EntityHandle(typeToken);
                if (handle.Kind == HandleKind.TypeDefinition)
                {
                    Add(
                        typeIdentity,
                        new SourceHousePhysicalTargetAddress.Type(
                            MetadataTypeDefinitionAddress.FromHandle(
                                reader,
                                (TypeDefinitionHandle)handle)));
                }
            }

            foreach (ApiMember member in type.Members)
            {
                if (member.MetadataToken is not { } memberToken
                    || MetadataTokens.EntityHandle(memberToken).Kind
                        != HandleKind.MethodDefinition
                    || !ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        type,
                        member,
                        out XmlDocMemberIdentity memberIdentity))
                {
                    continue;
                }

                Add(
                    memberIdentity,
                    new SourceHousePhysicalTargetAddress.Method(
                        new MetadataMethodAddress(
                            reader.GetGuid(
                                reader.GetModuleDefinition().Mvid),
                            (MethodDefinitionHandle)
                                MetadataTokens.EntityHandle(
                                    memberToken))));
            }
        }

        SourceHouseSha256Digest moduleDigest =
            SourceHouseSha256Digest.Compute(peImage);
        compilerIdentityCollisions = 0;
        var result =
            ImmutableArray.CreateBuilder<
                SourceHouseBuildDeclarationEvidence>();
        foreach (IGrouping<XmlDocMemberIdentity, SourceDeclaration> group
            in sourceDeclarations.GroupBy(
                static declaration => declaration.XmlIdentity))
        {
            SourceDeclaration[] declarations = [.. group];
            if (declarations.Length != 1)
            {
                compilerIdentityCollisions++;
                continue;
            }
            if (!emitted.TryGetValue(
                    group.Key,
                    out HashSet<SourceHousePhysicalTargetAddress>?
                        targetAddresses))
            {
                continue;
            }
            if (targetAddresses.Count != 1)
            {
                compilerIdentityCollisions++;
                continue;
            }

            SourceDeclaration declaration = declarations[0];
            SourceHousePhysicalTargetAddress target =
                targetAddresses.Single();
            if ((declaration.TargetKind
                    == SourceHouseTargetKind.Type
                    && target
                        is not SourceHousePhysicalTargetAddress.Type)
                || (declaration.TargetKind
                    == SourceHouseTargetKind.Member
                    && target
                        is not SourceHousePhysicalTargetAddress.Method))
            {
                continue;
            }

            result.Add(
                new(
                    moduleDigest,
                    target,
                    declaration.XmlIdentity,
                    declaration.Input.Identity,
                    declaration.Input.ContentDigest,
                    declaration.Input.Encoding,
                    declaration.Span,
                    declaration.SyntaxKind));
        }

        return result.ToImmutable();

        void Add(
            XmlDocMemberIdentity identity,
            SourceHousePhysicalTargetAddress address)
        {
            if (!emitted.TryGetValue(identity, out var addresses))
            {
                addresses = [];
                emitted.Add(identity, addresses);
            }

            addresses.Add(address);
        }
    }

    private static bool IsCandidateDeclaration(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax
            or BaseMethodDeclarationSyntax;

    private static CSharpBuildSource ImplicitUsings(
        string assemblyName) =>
        new(
            assemblyName + ".GlobalUsings.g.cs",
            Encoding.UTF8.GetBytes(
                """
                global using global::System;
                global using global::System.Collections.Generic;
                global using global::System.IO;
                global using global::System.Linq;
                global using global::System.Net.Http;
                global using global::System.Threading;
                global using global::System.Threading.Tasks;
                """),
            CSharpBuildSourceKind.Generated);

    private static bool IsSupported(
        ISymbol? symbol,
        SyntaxNode declaration)
    {
        if (symbol is null
            || symbol.IsImplicitlyDeclared
            || symbol.DeclaringSyntaxReferences.Length != 1
            || symbol.DeclaringSyntaxReferences[0].SyntaxTree
                != declaration.SyntaxTree)
        {
            return false;
        }
        if (declaration is TypeDeclarationSyntax type
            && type.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return false;
        }
        if (declaration is MethodDeclarationSyntax method
            && method.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return false;
        }
        if (declaration is BaseMethodDeclarationSyntax body
            && body.Body is null
            && body.ExpressionBody is null)
        {
            return false;
        }

        return symbol is INamedTypeSymbol
            or IMethodSymbol
        {
            MethodKind: MethodKind.Ordinary
                    or MethodKind.Constructor
                    or MethodKind.StaticConstructor
                    or MethodKind.Destructor
                    or MethodKind.UserDefinedOperator
                    or MethodKind.Conversion,
        };
    }

    internal sealed record PhysicalInput(
        CSharpBuildSource Source,
        SyntaxTree Tree,
        SourceHousePhysicalSourceInputIdentity Identity,
        SourceHouseSha256Digest ContentDigest,
        SourceHousePhysicalSourceEncoding Encoding);

    private sealed record SourceDeclaration(
        XmlDocMemberIdentity XmlIdentity,
        SourceHouseTargetKind TargetKind,
        PhysicalInput Input,
        SourceHousePhysicalDeclarationSpan Span,
        SourceHouseDeclarationSyntaxKind SyntaxKind);

}

public sealed record SourceHouseBuildDeclarationEvidence(
    SourceHouseSha256Digest ModuleDigest,
    SourceHousePhysicalTargetAddress Target,
    XmlDocMemberIdentity XmlIdentity,
    SourceHousePhysicalSourceInputIdentity SourceInput,
    SourceHouseSha256Digest SourceDigest,
    SourceHousePhysicalSourceEncoding SourceEncoding,
    SourceHousePhysicalDeclarationSpan Span,
    SourceHouseDeclarationSyntaxKind SyntaxKind);

public sealed record SourceHouseBuildSourceEvidence(
    string Path,
    SourceHousePhysicalSourceInputIdentity Identity,
    SourceHouseSha256Digest ContentDigest,
    SourceHousePhysicalSourceEncoding Encoding,
    CSharpBuildSourceKind Kind,
    int ByteLength);

public sealed class SourceHouseBuildAttestation
    : ISourceHousePhysicalDeclarationCapability
{
    private readonly IReadOnlyDictionary<
        string,
        ImmutableArray<SourceEntry>> _sources;
    private readonly ImmutableArray<SourceHouseBuildDeclarationEvidence>
        _declarations;

    internal SourceHouseBuildAttestation(
        CSharpBuildAttestationRequest request,
        IReadOnlyList<CSharpBuildAttestor.PhysicalInput> inputs,
        byte[] peImage,
        byte[] portablePdbImage,
        ImmutableArray<SourceHouseBuildDeclarationEvidence> declarations,
        int compilerIdentityCollisions)
    {
        Identity = request.Capability;
        Issuer = request.Issuer;
        Profile = request.Profile;
        Generation = request.Generation;
        PeImage = ImmutableArray.CreateRange(peImage);
        PortablePdbImage = ImmutableArray.CreateRange(portablePdbImage);
        Sources =
        [
            .. inputs.Select(static input =>
                new SourceHouseBuildSourceEvidence(
                    input.Source.Path,
                    input.Identity,
                    input.ContentDigest,
                    input.Encoding,
                    input.Source.Kind,
                    input.Source.Bytes.Length)),
        ];
        _sources = inputs
            .GroupBy(
                static input => input.Source.Path,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static input =>
                        new SourceEntry(
                            input.Source.Bytes,
                            input.Identity))
                    .ToImmutableArray(),
                StringComparer.Ordinal);
        _declarations = declarations;
        Declarations = declarations;
        CompilerIdentityCollisionCount =
            compilerIdentityCollisions;
    }

    public SourceHouseCapabilityIdentity Identity { get; }
    public SourceHouseCapabilityCategory Category =>
        SourceHouseCapabilityCategory.Local;
    public SourceHouseAttestationIssuerIdentity Issuer { get; }
    public SourceHouseAttestationProfileIdentity Profile { get; }
    public SourceHouseAttestationGeneration Generation { get; }
    public ImmutableArray<byte> PeImage { get; }
    public ImmutableArray<byte> PortablePdbImage { get; }
    public IReadOnlyList<SourceHouseBuildSourceEvidence> Sources { get; }
    public IReadOnlyList<SourceHouseBuildDeclarationEvidence> Declarations
    { get; }
    public int DeclarationCount => _declarations.Length;
    public int CompilerIdentityCollisionCount { get; }
    public IReadOnlyList<XmlDocMemberIdentity>
        AttestedXmlDocumentationIdentities =>
        _declarations
            .Select(static declaration => declaration.XmlIdentity)
            .ToArray();

    public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
        SourceHouseSourceCandidate candidate,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sources.TryGetValue(
                candidate.Document.OriginalPath,
                out ImmutableArray<SourceEntry> sources))
        {
            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                new SourceHouseCapabilityOutcome.Unavailable(
                    new("PhysicalCompilerInputUnavailable")));
        }
        if (sources.Length != 1)
        {
            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                new SourceHouseCapabilityOutcome.Unavailable(
                    new("PhysicalCompilerInputAmbiguous")));
        }

        SourceEntry source = sources[0];
        if (source.Bytes.Length > maximumBytes)
        {
            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                new SourceHouseCapabilityOutcome.Incomplete(
                    new("SourceByteLimitExceeded")));
        }

        return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
            new SourceHouseCapabilityOutcome.Available(
                source.Bytes.AsSpan(),
                source.Identity,
                new("DirectCompilerInput")));
    }

    public ValueTask<SourceHouseAttestationCapabilityOutcome>
        ReadAttestationsAsync(
            SourceHousePhysicalDeclarationRequest request,
            int maximumContributions,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        SourceHouseBuildDeclarationEvidence[] matching =
        [
            .. _declarations.Where(declaration =>
                declaration.XmlIdentity
                    == request.Target.XmlDocumentationIdentity),
        ];
        if (matching.Length > maximumContributions)
        {
            return ValueTask.FromResult<
                SourceHouseAttestationCapabilityOutcome>(
                    new SourceHouseAttestationCapabilityOutcome.Incomplete(
                        new("AttestationContributionLimitExceeded"),
                        matching.Length));
        }

        return ValueTask.FromResult<
            SourceHouseAttestationCapabilityOutcome>(
                new SourceHouseAttestationCapabilityOutcome.Available(
                    matching.Select(declaration =>
                        new SourceHousePhysicalDeclarationAttestation(
                            request.Request,
                            request.Library,
                            request.SelectedAssembly,
                            request.OperationPlan,
                            request.PolicyGeneration,
                            Issuer,
                            Profile,
                            Generation,
                            declaration.ModuleDigest,
                            declaration.Target,
                            declaration.XmlIdentity,
                            request.Source.Result,
                            declaration.SourceInput,
                            declaration.SourceDigest,
                            declaration.SourceEncoding,
                            declaration.Span,
                            declaration.SyntaxKind))
                    .ToArray()));
    }

    private sealed record SourceEntry(
        ImmutableArray<byte> Bytes,
        SourceHousePhysicalSourceInputIdentity Identity);
}
