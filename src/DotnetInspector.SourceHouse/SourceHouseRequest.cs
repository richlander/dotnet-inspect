using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Libraries;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

public sealed class SourceHouseRequestIdentity
{
    private SourceHouseRequestIdentity(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseRequestIdentity Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class SourceHouseOperationPlanIdentity
{
    private SourceHouseOperationPlanIdentity(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseOperationPlanIdentity Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class SourceHousePolicyGeneration
{
    private SourceHousePolicyGeneration(string name) => Name = name;

    public string Name { get; }

    public static SourceHousePolicyGeneration Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class SourceHouseCapabilityIdentity
{
    private SourceHouseCapabilityIdentity(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseCapabilityIdentity Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public enum SourceHouseTargetKind
{
    Type,
    Member,
}

public enum SourceHouseMemberSourceForm
{
    DeclarationText,
    DocumentParts,
}

public abstract class SourceHouseTarget
{
    private protected SourceHouseTarget(
        SourceHouseTargetKind kind,
        MetadataTypeDefinitionName type)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(type);

        Kind = kind;
        Type = type;
    }

    public SourceHouseTargetKind Kind { get; }
    public MetadataTypeDefinitionName Type { get; }

    public sealed class TypeTarget : SourceHouseTarget
    {
        public TypeTarget(
            MetadataTypeDefinitionName type,
            string? originalDocumentPath = null)
            : base(SourceHouseTargetKind.Type, type)
        {
            if (originalDocumentPath is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(originalDocumentPath);

            OriginalDocumentPath = originalDocumentPath;
        }

        /// <summary>Exact original PDB path, or null to select the primary document.</summary>
        public string? OriginalDocumentPath { get; }
    }

    public sealed class MemberTarget : SourceHouseTarget
    {
        public MemberTarget(
            MetadataTypeDefinitionName type,
            MemberAnchor member,
            int metadataToken,
            SourceHouseMemberSourceForm sourceForm = SourceHouseMemberSourceForm.DeclarationText)
            : base(SourceHouseTargetKind.Member, type)
        {
            ArgumentNullException.ThrowIfNull(member);
            if (MetadataTokens.EntityHandle(metadataToken).Kind
                != HandleKind.MethodDefinition)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(metadataToken),
                    "Authored member source requires one exact MethodDef token.");
            }
            if (!Enum.IsDefined(sourceForm))
                throw new ArgumentOutOfRangeException(nameof(sourceForm));

            Member = member;
            MetadataToken = metadataToken;
            SourceForm = sourceForm;
        }

        public MemberAnchor Member { get; }
        public int MetadataToken { get; }
        public SourceHouseMemberSourceForm SourceForm { get; }
    }
}

public sealed class SourceHouseLimits
{
    public SourceHouseLimits(
        int maximumAssemblyBytes,
        int maximumPortablePdbBytes,
        ApiSurfaceExtractionBounds targetBounds,
        SourceLinkReadLimits sourceLinkReadLimits,
        int maximumDocuments,
        int maximumTargetMappings,
        int maximumCandidateAttempts,
        int maximumSourceBytes,
        int maximumSourceTextCharacters)
    {
        ValidateArrayBound(maximumAssemblyBytes, nameof(maximumAssemblyBytes));
        ValidateArrayBound(
            maximumPortablePdbBytes,
            nameof(maximumPortablePdbBytes));
        ArgumentNullException.ThrowIfNull(targetBounds);
        ArgumentNullException.ThrowIfNull(sourceLinkReadLimits);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumTargetMappings);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumCandidateAttempts);
        ValidateArrayBound(maximumSourceBytes, nameof(maximumSourceBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumSourceTextCharacters);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumPortablePdbBytes = maximumPortablePdbBytes;
        MaximumEmbeddedPdbBytes = Math.Min(
            maximumPortablePdbBytes,
            sourceLinkReadLimits.MaxEmbeddedPdbBytes);
        TargetBounds = targetBounds;
        SourceLinkReadLimits = sourceLinkReadLimits;
        EffectiveSourceLinkReadLimits = new SourceLinkReadLimits(
            MaximumEmbeddedPdbBytes,
            sourceLinkReadLimits.MaxMapBytes,
            sourceLinkReadLimits.MaxMappings,
            sourceLinkReadLimits.EmbeddedPdbBudget);
        MaximumDocuments = maximumDocuments;
        MaximumTargetMappings = maximumTargetMappings;
        MaximumCandidateAttempts = maximumCandidateAttempts;
        MaximumSourceBytes = maximumSourceBytes;
        MaximumSourceTextCharacters = maximumSourceTextCharacters;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumPortablePdbBytes { get; }
    public int MaximumEmbeddedPdbBytes { get; }
    public ApiSurfaceExtractionBounds TargetBounds { get; }
    public SourceLinkReadLimits SourceLinkReadLimits { get; }
    public int MaximumDocuments { get; }
    public int MaximumTargetMappings { get; }
    public int MaximumCandidateAttempts { get; }
    public int MaximumSourceBytes { get; }
    public int MaximumSourceTextCharacters { get; }

    internal SourceLinkReadLimits EffectiveSourceLinkReadLimits { get; }

    private static void ValidateArrayBound(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value,
            Array.MaxLength,
            parameterName);
    }
}

public sealed class SourceHouseDecompilationLimits
{
    public SourceHouseDecompilationLimits(
        int maximumAssemblyBytes,
        int maximumPortablePdbBytes,
        ApiSurfaceExtractionBounds targetBounds,
        SourceLinkReadLimits embeddedPdbReadLimits)
    {
        ValidateArrayBound(
            maximumAssemblyBytes,
            nameof(maximumAssemblyBytes));
        ValidateArrayBound(
            maximumPortablePdbBytes,
            nameof(maximumPortablePdbBytes));
        ArgumentNullException.ThrowIfNull(targetBounds);
        ArgumentNullException.ThrowIfNull(embeddedPdbReadLimits);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumPortablePdbBytes = maximumPortablePdbBytes;
        TargetBounds = targetBounds;
        EmbeddedPdbReadLimits = new(
            Math.Min(
                maximumPortablePdbBytes,
                embeddedPdbReadLimits.MaxEmbeddedPdbBytes),
            embeddedPdbReadLimits.MaxMapBytes,
            embeddedPdbReadLimits.MaxMappings,
            embeddedPdbReadLimits.EmbeddedPdbBudget);
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumPortablePdbBytes { get; }
    public ApiSurfaceExtractionBounds TargetBounds { get; }
    public SourceLinkReadLimits EmbeddedPdbReadLimits { get; }

    private static void ValidateArrayBound(
        int value,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            value,
            parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value,
            Array.MaxLength,
            parameterName);
    }
}

public enum SourceHouseCapabilityCategory
{
    Local,
    Repository,
    Remote,
}

public sealed record SourceHouseCapabilityObservation
{
    public SourceHouseCapabilityObservation(
        string code,
        string? detail = null)
    {
        Code = SourceHouseContractName.Validate(code);
        DetailWasTruncated =
            detail is
            {
                Length: >
                SourceHouseContractText.MaximumDiagnosticCharacters
            };
        Detail = detail is null
            ? null
            : SourceHouseContractText.CaptureDiagnostic(detail);
    }

    public string Code { get; }
    public string? Detail { get; }
    public bool DetailWasTruncated { get; }
}

public sealed record SourceHouseSourceCandidate
{
    public SourceHouseSourceCandidate(
        SourceHouseTarget target,
        SourceDocumentObservation document,
        SourceHouseAuthoredMapping mapping,
        string? repositoryUrl,
        string? revision)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(mapping);

        Target = target;
        Document = document;
        Mapping = mapping;
        RepositoryUrl = repositoryUrl;
        Revision = revision;
    }

    public SourceHouseTarget Target { get; }
    public SourceDocumentObservation Document { get; }
    public SourceHouseAuthoredMapping Mapping { get; }
    public string? RepositoryUrl { get; }
    public string? Revision { get; }
}

public abstract class SourceHouseCapabilityOutcome
{
    private protected SourceHouseCapabilityOutcome(
        SourceHouseCapabilityObservation? observation)
    {
        Observation = observation;
    }

    public SourceHouseCapabilityObservation? Observation { get; }

    public sealed class Available : SourceHouseCapabilityOutcome
    {
        public Available(
            ReadOnlySpan<byte> bytes,
            SourceHouseCapabilityObservation? observation = null)
            : base(observation)
        {
            Bytes = ImmutableArray.CreateRange(bytes.ToArray());
        }

        public ImmutableArray<byte> Bytes { get; }
    }

    public sealed class Unavailable : SourceHouseCapabilityOutcome
    {
        public Unavailable(SourceHouseCapabilityObservation observation)
            : base(
                observation
                ?? throw new ArgumentNullException(nameof(observation)))
        {
        }
    }

    public sealed class Rejected : SourceHouseCapabilityOutcome
    {
        public Rejected(SourceHouseCapabilityObservation observation)
            : base(
                observation
                ?? throw new ArgumentNullException(nameof(observation)))
        {
        }
    }

    public sealed class Failed : SourceHouseCapabilityOutcome
    {
        public Failed(SourceHouseCapabilityObservation observation)
            : base(
                observation
                ?? throw new ArgumentNullException(nameof(observation)))
        {
        }
    }

    public sealed class Incomplete : SourceHouseCapabilityOutcome
    {
        public Incomplete(SourceHouseCapabilityObservation observation)
            : base(
                observation
                ?? throw new ArgumentNullException(nameof(observation)))
        {
        }
    }
}

public interface ISourceHouseSourceCapability
{
    SourceHouseCapabilityIdentity Identity { get; }
    SourceHouseCapabilityCategory Category { get; }

    ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
        SourceHouseSourceCandidate candidate,
        int maximumBytes,
        CancellationToken cancellationToken);
}

public sealed class SourceHouseOperationPlan
{
    private readonly ISourceHouseSourceCapability[] _capabilities;

    public SourceHouseOperationPlan(
        SourceHouseOperationPlanIdentity identity,
        SourceHousePolicyGeneration policyGeneration,
        SourceHouseLimits limits,
        DateTimeOffset deadline,
        IReadOnlyList<ISourceHouseSourceCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (deadline == DateTimeOffset.MinValue
            || deadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                "SourceHouse settlement requires a finite deadline.");
        }

        ISourceHouseSourceCapability[] supplied = [.. capabilities];
        if (supplied.Any(static capability => capability is null))
        {
            throw new ArgumentException(
                "Source capabilities cannot contain null.",
                nameof(capabilities));
        }
        foreach (ISourceHouseSourceCapability capability in supplied)
        {
            ArgumentNullException.ThrowIfNull(capability.Identity);
            if (!Enum.IsDefined(capability.Category))
            {
                throw new ArgumentException(
                    "A source capability has an unknown category.",
                    nameof(capabilities));
            }
        }

        Identity = identity;
        PolicyGeneration = policyGeneration;
        Limits = limits;
        Deadline = deadline;
        _capabilities =
        [
            .. supplied
                .Select(
                    static (capability, index) =>
                        (Capability: capability, Index: index))
                .OrderBy(
                    static item => item.Capability.Category)
                .ThenBy(static item => item.Index)
                .Select(static item => item.Capability),
        ];
    }

    public SourceHouseOperationPlanIdentity Identity { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseLimits Limits { get; }
    public DateTimeOffset Deadline { get; }

    internal IReadOnlyList<ISourceHouseSourceCapability> Capabilities =>
        _capabilities;
}

public sealed class SourceHouseAuthoredRequest
{
    public SourceHouseAuthoredRequest(
        SourceHouseRequestIdentity identity,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseTarget target,
        SourceHouseOperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        Identity = identity;
        Library = library;
        SelectedAssembly = selectedAssembly;
        Target = target;
        Plan = plan;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget Target { get; }
    public SourceHouseOperationPlan Plan { get; }
}

public sealed class SourceHouseDecompilationPlan
{
    public SourceHouseDecompilationPlan(
        SourceHouseOperationPlanIdentity identity,
        SourceHousePolicyGeneration policyGeneration,
        SourceHouseDecompilationLimits limits,
        IAssemblyBindingPolicy bindingPolicy,
        PrinterOptions? printerOptions = null,
        int maximumBodyProjections =
            CSharpDecompilerService.DefaultMaxBodyProjections)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumBodyProjections);

        Identity = identity;
        PolicyGeneration = policyGeneration;
        Limits = limits;
        BindingPolicy = bindingPolicy;
        PrinterOptions = printerOptions;
        MaximumBodyProjections = maximumBodyProjections;
    }

    public SourceHouseOperationPlanIdentity Identity { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseDecompilationLimits Limits { get; }
    public IAssemblyBindingPolicy BindingPolicy { get; }
    public PrinterOptions? PrinterOptions { get; }
    public int MaximumBodyProjections { get; }
}

public enum SourceHouseDecompilationProduct
{
    SourceText,
    StructuredTypeDocument,
}

public sealed class SourceHouseDecompilationRequest
{
    public SourceHouseDecompilationRequest(
        SourceHouseRequestIdentity identity,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseTarget target,
        SourceHouseDecompilationPlan plan,
        SourceHouseDecompilationProduct product =
            SourceHouseDecompilationProduct.SourceText)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);
        if (!Enum.IsDefined(product))
        {
            throw new ArgumentOutOfRangeException(
                nameof(product));
        }
        if (target is SourceHouseTarget.TypeTarget
            {
                OriginalDocumentPath: not null,
            })
        {
            throw new ArgumentException(
                "An authored document selection is not a decompilation target.",
                nameof(target));
        }
        if (product
                == SourceHouseDecompilationProduct.StructuredTypeDocument
            && target is not SourceHouseTarget.TypeTarget)
        {
            throw new ArgumentException(
                "A structured Type document requires an exact Type target.",
                nameof(target));
        }

        Identity = identity;
        Library = library;
        SelectedAssembly = selectedAssembly;
        Target = target;
        Plan = plan;
        Product = product;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget Target { get; }
    public SourceHouseDecompilationPlan Plan { get; }
    public SourceHouseDecompilationProduct Product { get; }
}

internal static class SourceHouseContractName
{
    internal static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                "SourceHouse identity names cannot exceed 256 characters.");
        }

        return name;
    }
}

internal static class SourceHouseContractText
{
    internal const int MaximumDiagnosticCharacters = 4_096;

    internal static string CaptureDiagnostic(string detail) =>
        detail.Length <= MaximumDiagnosticCharacters
            ? detail
            : detail[..MaximumDiagnosticCharacters];
}
