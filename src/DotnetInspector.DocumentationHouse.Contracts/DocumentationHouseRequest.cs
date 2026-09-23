using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.DocumentationHouse;

public sealed class DocumentationHouseRequestIdentity
{
    private DocumentationHouseRequestIdentity(string name) => Name = name;

    public string Name { get; }

    public static DocumentationHouseRequestIdentity Create(string name) =>
        new(DocumentationHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class DocumentationHouseOperationPlanIdentity
{
    private DocumentationHouseOperationPlanIdentity(string name) => Name = name;

    public string Name { get; }

    public static DocumentationHouseOperationPlanIdentity Create(string name) =>
        new(DocumentationHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class DocumentationHousePolicyGeneration
{
    private DocumentationHousePolicyGeneration(string name) => Name = name;

    public string Name { get; }

    public static DocumentationHousePolicyGeneration Create(string name) =>
        new(DocumentationHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public enum DocumentationSourceKind
{
    Package,
    Platform,
    DirectLibrary,
    SourceHouse,
}

/// <summary>Opaque resource-free evidence issued by a source adapter.</summary>
public sealed class DocumentationSourceReference
{
    private DocumentationSourceReference(
        DocumentationSourceKind kind,
        string name)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Kind = kind;
        Name = DocumentationHouseContractName.Validate(name);
    }

    public DocumentationSourceKind Kind { get; }
    public string Name { get; }

    public static DocumentationSourceReference Create(
        DocumentationSourceKind kind,
        string name) =>
        new(kind, name);

    public override string ToString() => Name;
}

/// <summary>
/// One exact Metadata subject and compiler XML identity in one realized
/// Library.
/// </summary>
public sealed class DocumentationSubjectReference
{
    private DocumentationSubjectReference(
        ApiAssemblyIdentity metadataAssembly,
        MetadataTypeDefinitionName typeIdentity,
        MemberAnchor? memberIdentity,
        int? metadataToken,
        ApiMethodSemanticsKind? methodSemantics,
        XmlDocMemberIdentity compiledXmlIdentity,
        LibraryApiSurfaceCorrespondence apiSurfaceCorrespondence)
    {
        MetadataAssembly = metadataAssembly;
        TypeIdentity = typeIdentity;
        MemberIdentity = memberIdentity;
        MetadataToken = metadataToken;
        MethodSemantics = methodSemantics;
        CompiledXmlIdentity = compiledXmlIdentity;
        ApiSurfaceCorrespondence = apiSurfaceCorrespondence;
    }

    public ApiAssemblyIdentity MetadataAssembly { get; }
    public MetadataTypeDefinitionName TypeIdentity { get; }
    public MemberAnchor? MemberIdentity { get; }
    public int? MetadataToken { get; }
    public bool IsMember => MemberIdentity is not null;
    public ApiMethodSemanticsKind? MethodSemantics { get; }
    public XmlDocMemberIdentity CompiledXmlIdentity { get; }
    public LibraryApiSurfaceCorrespondence ApiSurfaceCorrespondence { get; }
    public LibraryReference Library => ApiSurfaceCorrespondence.Library;
    public LibraryContentReference ApiContent =>
        ApiSurfaceCorrespondence.ApiContent;

    public static DocumentationSubjectReference ForType(
        LibraryApiSurfaceCorrespondence apiSurfaceCorrespondence,
        ApiType type)
    {
        ApiSurface surface = ValidateContext(
            apiSurfaceCorrespondence,
            type);
        if (!ApiMemberIdentity.TryGetXmlDocTypeIdentity(
                type,
                out XmlDocMemberIdentity compiledXmlIdentity))
        {
            throw new ArgumentException(
                "Metadata did not issue an exact compiler XML identity for the type.",
                nameof(type));
        }

        return new(
            surface.AssemblyIdentity!,
            type.DefinitionName!,
            memberIdentity: null,
            type.MetadataToken,
            methodSemantics: null,
            compiledXmlIdentity,
            apiSurfaceCorrespondence);
    }

    public static DocumentationSubjectReference ForMember(
        LibraryApiSurfaceCorrespondence apiSurfaceCorrespondence,
        ApiType declaringType,
        ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        ApiSurface surface = ValidateContext(
            apiSurfaceCorrespondence,
            declaringType);
        if (!declaringType.Members.Any(
                candidate => ReferenceEquals(candidate, member)))
        {
            throw new ArgumentException(
                "The member is not part of the supplied Metadata type.",
                nameof(member));
        }
        if (!ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                declaringType,
                member,
                out XmlDocMemberIdentity compiledXmlIdentity))
        {
            throw new ArgumentException(
                "Metadata did not issue an exact compiler XML identity for the member.",
                nameof(member));
        }

        return new(
            surface.AssemblyIdentity!,
            member.DeclaringTypeDefinitionName
                ?? declaringType.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(
                declaringType,
                member),
            member.MetadataToken,
            member.MethodSemantics,
            compiledXmlIdentity,
            apiSurfaceCorrespondence);
    }

    private static ApiSurface ValidateContext(
        LibraryApiSurfaceCorrespondence apiSurfaceCorrespondence,
        ApiType type)
    {
        ArgumentNullException.ThrowIfNull(apiSurfaceCorrespondence);
        ArgumentNullException.ThrowIfNull(type);
        ApiSurface surface = apiSurfaceCorrespondence.Surface;
        if (surface.AssemblyIdentity is not { } metadataAssembly)
        {
            throw new ArgumentException(
                "The Metadata surface requires an exact assembly identity.",
                nameof(surface));
        }
        if (!surface.Types.Any(
                candidate => ReferenceEquals(candidate, type)))
        {
            throw new ArgumentException(
                "The type is not part of the supplied Metadata surface.",
                nameof(type));
        }
        if (type.DefinitionName is null)
        {
            throw new ArgumentException(
                "The Metadata type requires an exact definition identity.",
                nameof(type));
        }

        return surface;
    }
}

public sealed class DocumentationImplementationSubjectReference
{
    public DocumentationImplementationSubjectReference(
        MetadataTypeDefinitionName typeIdentity,
        MemberAnchor? memberIdentity,
        int? metadataToken,
        ApiMethodSemanticsKind? methodSemantics,
        XmlDocMemberIdentity compiledXmlIdentity)
    {
        ArgumentNullException.ThrowIfNull(typeIdentity);
        ArgumentNullException.ThrowIfNull(compiledXmlIdentity);
        if (memberIdentity is null && metadataToken is not null)
        {
            throw new ArgumentException(
                "A type implementation subject cannot carry a member token.",
                nameof(metadataToken));
        }
        if (memberIdentity is not null
            && (metadataToken is not { } token
                || MetadataTokens.EntityHandle(token).Kind
                    != HandleKind.MethodDefinition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadataToken),
                "A member implementation subject requires one exact MethodDef token.");
        }

        TypeIdentity = typeIdentity;
        MemberIdentity = memberIdentity;
        MetadataToken = metadataToken;
        MethodSemantics = methodSemantics;
        CompiledXmlIdentity = compiledXmlIdentity;
    }

    public MetadataTypeDefinitionName TypeIdentity { get; }
    public MemberAnchor? MemberIdentity { get; }
    public int? MetadataToken { get; }
    public bool IsMember => MemberIdentity is not null;
    public ApiMethodSemanticsKind? MethodSemantics { get; }
    public XmlDocMemberIdentity CompiledXmlIdentity { get; }

    public static DocumentationImplementationSubjectReference FromApiSubject(
        DocumentationSubjectReference subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new(
            subject.TypeIdentity,
            subject.MemberIdentity,
            subject.IsMember ? subject.MetadataToken : null,
            subject.MethodSemantics,
            subject.CompiledXmlIdentity);
    }
}

public enum DocumentationDemand
{
    CompiledXml,
    AuthoredSourceDocumentation,
    CompiledXmlAndAuthoredSourceDocumentation,
}

public sealed class DocumentationHouseLimits
{
    public DocumentationHouseLimits(
        int maximumCompiledXmlContributions,
        int maximumCompiledXmlBytes,
        XmlDocumentationReadLimits xmlReadLimits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCompiledXmlContributions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCompiledXmlBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumCompiledXmlBytes,
            Array.MaxLength);
        ArgumentNullException.ThrowIfNull(xmlReadLimits);
        Validate(xmlReadLimits);

        MaximumCompiledXmlContributions =
            maximumCompiledXmlContributions;
        MaximumCompiledXmlBytes = maximumCompiledXmlBytes;
        XmlReadLimits = xmlReadLimits;
    }

    public int MaximumCompiledXmlContributions { get; }
    public int MaximumCompiledXmlBytes { get; }
    public XmlDocumentationReadLimits XmlReadLimits { get; }

    private static void Validate(XmlDocumentationReadLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxCharactersInDocument);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxMembers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxMemberIdCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxRetainedTextCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxParametersPerMember);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxExceptionsPerMember);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxSamplesPerMember);
    }
}

public sealed class DocumentationHouseOperationPlan
{
    private readonly CompiledXmlContribution[] _compiledXmlContributions;

    public DocumentationHouseOperationPlan(
        DocumentationHouseOperationPlanIdentity identity,
        DocumentationHousePolicyGeneration policyGeneration,
        DocumentationHouseLimits limits,
        DateTimeOffset deadline,
        IReadOnlyList<CompiledXmlContribution> compiledXmlContributions,
        DocumentationAuthoredSourceChannelPlan? authoredSource = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(compiledXmlContributions);
        if (deadline is { } value
            && (value == DateTimeOffset.MinValue
                || value == DateTimeOffset.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                "Documentation settlement requires a finite deadline.");
        }

        Identity = identity;
        PolicyGeneration = policyGeneration;
        Limits = limits;
        Deadline = deadline;
        SuppliedCompiledXmlContributionCount =
            compiledXmlContributions.Count;
        ExceedsCompiledXmlContributionLimit =
            compiledXmlContributions.Count
                > limits.MaximumCompiledXmlContributions;
        _compiledXmlContributions =
            compiledXmlContributions
                .Take(limits.MaximumCompiledXmlContributions)
                .ToArray();
        if (_compiledXmlContributions.Any(
                static contribution => contribution is null))
        {
            throw new ArgumentException(
                "Compiled XML contributions cannot contain null.",
                nameof(compiledXmlContributions));
        }

        CompiledXmlContributions =
            Array.AsReadOnly(_compiledXmlContributions);
        AuthoredSource = authoredSource;
    }

    public DocumentationHouseOperationPlanIdentity Identity { get; }
    public DocumentationHousePolicyGeneration PolicyGeneration { get; }
    public DocumentationHouseLimits Limits { get; }
    public DateTimeOffset Deadline { get; }
    public int SuppliedCompiledXmlContributionCount { get; }
    public bool ExceedsCompiledXmlContributionLimit { get; }
    public IReadOnlyList<CompiledXmlContribution> CompiledXmlContributions
    {
        get;
    }
    public DocumentationAuthoredSourceChannelPlan? AuthoredSource { get; }
}

/// <summary>
/// One pre-authorized source-neutral operation available to an authored
/// documentation channel.
/// </summary>
public sealed class DocumentationAuthoredSourceChannelPlan
{
    public DocumentationAuthoredSourceChannelPlan(
        DocumentationAuthoredSourceOperationBinding binding,
        DocumentationAuthoredSourceOperationLimits limits,
        IDocumentationAuthoredSourceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(operation);

        Binding = binding;
        Limits = limits;
        Operation = operation;
    }

    public DocumentationAuthoredSourceOperationBinding Binding { get; }
    public DocumentationAuthoredSourceOperationLimits Limits { get; }
    public IDocumentationAuthoredSourceOperation Operation { get; }
}

/// <summary>
/// One DocumentationHouse request. Live Library authority is transferred
/// separately; an optional cold authored operation is consumed only by
/// explicit authored demand.
/// </summary>
public sealed class DocumentationHouseRequest
{
    public DocumentationHouseRequest(
        DocumentationHouseRequestIdentity identity,
        DocumentationSubjectReference subject,
        DocumentationDemand demand,
        DocumentationHouseOperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(subject);
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));
        ArgumentNullException.ThrowIfNull(plan);
        if (demand == DocumentationDemand.CompiledXml
            && plan.AuthoredSource is not null)
        {
            throw new ArgumentException(
                "Compiled-only demand cannot retain an authored-source operation.",
                nameof(plan));
        }

        Identity = identity;
        Subject = subject;
        Demand = demand;
        Plan = plan;
    }

    public DocumentationHouseRequestIdentity Identity { get; }
    public DocumentationSubjectReference Subject { get; }
    public DocumentationDemand Demand { get; }
    public DocumentationHouseOperationPlan Plan { get; }
}

/// <summary>Resource-free evidence for an accepted operation plan.</summary>
public sealed class DocumentationHouseOperationPlanEvidence
{
    internal DocumentationHouseOperationPlanEvidence(
        DocumentationHouseOperationPlan plan)
    {
        Identity = plan.Identity;
        PolicyGeneration = plan.PolicyGeneration;
        Limits = plan.Limits;
        Deadline = plan.Deadline;
        SuppliedCompiledXmlContributionCount =
            plan.SuppliedCompiledXmlContributionCount;
        ExceedsCompiledXmlContributionLimit =
            plan.ExceedsCompiledXmlContributionLimit;
        CompiledXmlContributions = plan.CompiledXmlContributions;
        AuthoredSourceBinding = plan.AuthoredSource?.Binding;
        AuthoredSourceLimits = plan.AuthoredSource?.Limits;
    }

    public DocumentationHouseOperationPlanIdentity Identity { get; }
    public DocumentationHousePolicyGeneration PolicyGeneration { get; }
    public DocumentationHouseLimits Limits { get; }
    public DateTimeOffset Deadline { get; }
    public int SuppliedCompiledXmlContributionCount { get; }
    public bool ExceedsCompiledXmlContributionLimit { get; }
    public IReadOnlyList<CompiledXmlContribution> CompiledXmlContributions
    {
        get;
    }
    public DocumentationAuthoredSourceOperationBinding?
        AuthoredSourceBinding { get; }
    public DocumentationAuthoredSourceOperationLimits?
        AuthoredSourceLimits { get; }
}

/// <summary>Resource-free evidence for one DocumentationHouse request.</summary>
public sealed class DocumentationHouseRequestEvidence
{
    internal DocumentationHouseRequestEvidence(
        DocumentationHouseRequest request)
    {
        Identity = request.Identity;
        Subject = request.Subject;
        Demand = request.Demand;
        Plan = new(request.Plan);
    }

    public DocumentationHouseRequestIdentity Identity { get; }
    public DocumentationSubjectReference Subject { get; }
    public DocumentationDemand Demand { get; }
    public DocumentationHouseOperationPlanEvidence Plan { get; }
}

internal static class DocumentationHouseContractName
{
    internal static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                "DocumentationHouse identity names cannot exceed 256 characters.");
        }

        return name;
    }
}
