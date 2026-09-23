using System.Collections.Immutable;

using CSharpText;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

public enum SourceHouseSourceUnitScope
{
    ExactMember,
    PrimaryTypeDocument,
    AdditionalTypeDocument,
}

public enum SourceHouseMappingStrength
{
    ExactSequencePoints,
    CorrelatedTypeDocument,
    InferredTypeDocument,
}

public abstract class SourceHouseAuthoredMapping
{
    private protected SourceHouseAuthoredMapping(
        SourceHouseSourceUnitScope scope,
        SourceHouseMappingStrength strength,
        bool isPartial)
    {
        Scope = scope;
        Strength = strength;
        IsPartial = isPartial;
    }

    public SourceHouseSourceUnitScope Scope { get; }
    public SourceHouseMappingStrength Strength { get; }
    public bool IsPartial { get; }

    public sealed class Member : SourceHouseAuthoredMapping
    {
        public Member(
            MemberSourceObservation observation,
            SourceDocumentObservation document)
            : base(
                SourceHouseSourceUnitScope.ExactMember,
                SourceHouseMappingStrength.ExactSequencePoints,
                isPartial: false)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentNullException.ThrowIfNull(document);

            Observation = observation;
            Document = document;
        }

        public MemberSourceObservation Observation { get; }
        public SourceDocumentObservation Document { get; }
    }

    public sealed class Type : SourceHouseAuthoredMapping
    {
        public Type(
            SourceLinkResolver.TypeSourceInfo sourceMapping,
            SourceDocumentObservation document,
            SourceHouseMappingStrength mappingStrength,
            bool partial,
            IReadOnlyList<SourceHouseAdditionalTypeDocument>
                additionalDocuments)
            : base(
                DocumentScope(sourceMapping, document),
                mappingStrength,
                partial)
        {
            ArgumentNullException.ThrowIfNull(additionalDocuments);

            SourceMapping = sourceMapping;
            Document = document;
            AdditionalDocuments =
                ImmutableArray.CreateRange(additionalDocuments);
        }

        public SourceLinkResolver.TypeSourceInfo SourceMapping { get; }
        public SourceDocumentObservation Document { get; }
        public IReadOnlyList<SourceHouseAdditionalTypeDocument>
            AdditionalDocuments
        { get; }

        private static SourceHouseSourceUnitScope DocumentScope(
            SourceLinkResolver.TypeSourceInfo mapping,
            SourceDocumentObservation document)
        {
            ArgumentNullException.ThrowIfNull(mapping);
            ArgumentNullException.ThrowIfNull(document);
            return string.Equals(
                document.OriginalPath,
                TypeSourceDocumentSelection.SelectDefault(mapping)?.FilePath,
                StringComparison.Ordinal)
                    ? SourceHouseSourceUnitScope.PrimaryTypeDocument
                    : SourceHouseSourceUnitScope.AdditionalTypeDocument;
        }
    }
}

public sealed record SourceHouseAdditionalTypeDocument
{
    public SourceHouseAdditionalTypeDocument(
        string originalPath,
        string? resolvedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        OriginalPath = originalPath;
        ResolvedUrl = resolvedUrl;
    }

    public string OriginalPath { get; }
    public string? ResolvedUrl { get; }
}

public enum SourceHousePdbContributionKind
{
    SuppliedCompanion,
    Embedded,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

public enum SourceHouseNativeObservationStage
{
    Assembly,
    PortablePdb,
    SourceLink,
    Mapping,
    Disposal,
}

public sealed record SourceHouseNativeObservation
{
    public SourceHouseNativeObservation(
        SourceHouseNativeObservationStage stage,
        string detail)
    {
        if (!Enum.IsDefined(stage))
            throw new ArgumentOutOfRangeException(nameof(stage));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Stage = stage;
        DetailWasTruncated =
            detail.Length
                > SourceHouseContractText.MaximumDiagnosticCharacters;
        Detail = SourceHouseContractText.CaptureDiagnostic(detail);
    }

    public SourceHouseNativeObservationStage Stage { get; }
    public string Detail { get; }
    public bool DetailWasTruncated { get; }
}

public sealed record SourceHousePdbContribution
{
    public SourceHousePdbContribution(
        SourceHousePdbContributionKind kind,
        LibraryContentReference? content,
        long bytesObserved,
        SourceLinkMapAudit? sourceLinkMap,
        IReadOnlyList<SourceHouseNativeObservation> observations)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentOutOfRangeException.ThrowIfNegative(bytesObserved);
        ArgumentNullException.ThrowIfNull(observations);

        Kind = kind;
        Content = content;
        BytesObserved = bytesObserved;
        SourceLinkMap = sourceLinkMap;
        Observations = ImmutableArray.CreateRange(observations);
    }

    public SourceHousePdbContributionKind Kind { get; init; }
    public LibraryContentReference? Content { get; }
    public long BytesObserved { get; }
    public SourceLinkMapAudit? SourceLinkMap { get; }
    public IReadOnlyList<SourceHouseNativeObservation> Observations { get; }
}

public enum SourceHouseSourceAttemptKind
{
    Available,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

public sealed record SourceHouseSourceAttempt
{
    public SourceHouseSourceAttempt(
        SourceHouseCapabilityIdentity capability,
        SourceHouseCapabilityCategory category,
        SourceHouseSourceAttemptKind kind,
        long bytesObserved,
        SourceChecksumVerification? checksumVerification,
        SourceHouseCapabilityObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentOutOfRangeException.ThrowIfNegative(bytesObserved);

        Capability = capability;
        Category = category;
        Kind = kind;
        BytesObserved = bytesObserved;
        ChecksumVerification = checksumVerification;
        Observation = observation;
    }

    public SourceHouseCapabilityIdentity Capability { get; }
    public SourceHouseCapabilityCategory Category { get; }
    public SourceHouseSourceAttemptKind Kind { get; }
    public long BytesObserved { get; }
    public SourceChecksumVerification? ChecksumVerification { get; }
    public SourceHouseCapabilityObservation? Observation { get; }
}

public enum SourceHouseAuthoredAttemptKind
{
    Available,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

public sealed class SourceHouseAuthoredMemberDocument
{
    internal SourceHouseAuthoredMemberDocument(string text, MemberTextParts parts)
    {
        Text = text;
        Parts = parts;
    }

    public string Text { get; }
    public MemberTextParts Parts { get; }
}

public abstract class SourceHouseAuthoredAttempt
{
    private protected SourceHouseAuthoredAttempt(
        SourceHouseAuthoredAttemptKind kind,
        SourceHouseAuthoredMapping? mapping,
        IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(sourceAttempts);

        Kind = kind;
        Mapping = mapping;
        SourceAttempts = ImmutableArray.CreateRange(sourceAttempts);
    }

    public SourceHouseAuthoredAttemptKind Kind { get; }
    public SourceHouseAuthoredMapping? Mapping { get; }
    public IReadOnlyList<SourceHouseSourceAttempt> SourceAttempts { get; }

    public sealed class Available : SourceHouseAuthoredAttempt
    {
        internal Available(
            string text,
            SourceHouseAuthoredMapping mapping,
            SourceHouseSourceAttempt selected,
            IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts,
            SourceHouseAuthoredMemberDocument? memberDocument = null)
            : base(
                SourceHouseAuthoredAttemptKind.Available,
                mapping,
                sourceAttempts)
        {
            Text = text;
            Selected = selected;
            MemberDocument = memberDocument;
        }

        public string Text { get; }
        public SourceHouseSourceAttempt Selected { get; }
        public SourceHouseAuthoredMemberDocument? MemberDocument { get; }
    }

    public sealed class Unavailable : SourceHouseAuthoredAttempt
    {
        internal Unavailable(
            SourceHouseAuthoredMapping? mapping,
            IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts)
            : base(
                SourceHouseAuthoredAttemptKind.Unavailable,
                mapping,
                sourceAttempts)
        {
        }
    }

    public sealed class Rejected : SourceHouseAuthoredAttempt
    {
        internal Rejected(
            SourceHouseAuthoredMapping? mapping,
            IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts)
            : base(
                SourceHouseAuthoredAttemptKind.Rejected,
                mapping,
                sourceAttempts)
        {
        }
    }

    public sealed class Failed : SourceHouseAuthoredAttempt
    {
        internal Failed(
            SourceHouseAuthoredMapping? mapping,
            IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts)
            : base(
                SourceHouseAuthoredAttemptKind.Failed,
                mapping,
                sourceAttempts)
        {
        }
    }

    public sealed class Incomplete : SourceHouseAuthoredAttempt
    {
        internal Incomplete(
            SourceHouseAuthoredMapping? mapping,
            IReadOnlyList<SourceHouseSourceAttempt> sourceAttempts)
            : base(
                SourceHouseAuthoredAttemptKind.Incomplete,
                mapping,
                sourceAttempts)
        {
        }
    }
}

public enum SourceHouseRejectionKind
{
    LibraryReferenceMismatch,
    SelectedContentMismatch,
    SelectedContentRoleMismatch,
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    PortablePdbCompanionAmbiguous,
    PortablePdbCorrespondenceMismatch,
    TargetMismatch,
}

public sealed record SourceHouseRejection(SourceHouseRejectionKind Kind);

public enum SourceHouseFailureStage
{
    AssemblySnapshot,
    AssemblyInspection,
    PortablePdbSnapshot,
    PortablePdbInspection,
    SourceLinkInspection,
    TargetMapping,
    SourceCapability,
    SourceVerification,
    SourceSlicing,
    Decompilation,
    ResourceDisposal,
}

public sealed record SourceHouseFailure
{
    public SourceHouseFailure(
        SourceHouseFailureStage stage,
        string code,
        string? detail = null)
    {
        if (!Enum.IsDefined(stage))
            throw new ArgumentOutOfRangeException(nameof(stage));
        Stage = stage;
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

    public SourceHouseFailureStage Stage { get; }
    public string Code { get; }
    public string? Detail { get; }
    public bool DetailWasTruncated { get; }
}

public enum SourceHouseIncompleteBoundary
{
    Deadline,
    AssemblyBytes,
    PortablePdbBytes,
    TargetSurface,
    SourceLinkMapBytes,
    SourceLinkMappings,
    Documents,
    TargetMappings,
    CandidateAttempts,
    SourceBytes,
    SourceTextCharacters,
}

public sealed record SourceHouseWorkCharge(
    long AssemblyBytesObserved,
    long PortablePdbBytesObserved,
    int DocumentsObserved,
    int TargetMappingsObserved,
    int CandidateAttempts,
    long SourceBytesObserved,
    long SourceTextCharactersObserved);

public sealed record SourceHouseRequestEvidence
{
    public SourceHouseRequestEvidence(
        SourceHouseRequestIdentity identity,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseTarget target,
        SourceHouseOperationPlanIdentity operationPlan,
        SourceHousePolicyGeneration policyGeneration)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(policyGeneration);

        Identity = identity;
        Library = library;
        SelectedAssembly = selectedAssembly;
        Target = target;
        OperationPlan = operationPlan;
        PolicyGeneration = policyGeneration;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget Target { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseSourcePolicy SourcePolicy =>
        SourceHouseSourcePolicy.AuthoredOnly;
    public SourceHousePdbAcquisitionPolicy PdbAcquisitionPolicy =>
        SourceHousePdbAcquisitionPolicy.LibraryCompanionOrEmbeddedOnly;
}

public enum SourceHouseSourcePolicy
{
    AuthoredOnly,
    DecompiledOnly,
}

public enum SourceHousePdbAcquisitionPolicy
{
    LibraryCompanionOrEmbeddedOnly,
}

public enum SourceHouseLibraryLeaseConsumer
{
    SourceHouse,
}

public sealed record SourceHouseLibraryLeaseSettlement(
    SourceHouseLibraryLeaseConsumer Consumer);

public sealed class SourceHouseReceiptIdentity
{
    internal SourceHouseReceiptIdentity()
    {
    }

    public override string ToString() => nameof(SourceHouseReceiptIdentity);
}

public sealed class SourceHouseReceipt
{
    internal SourceHouseReceipt(
        SourceHouseRequestEvidence request,
        SourceHousePdbContribution pdbContribution,
        SourceHouseWorkCharge work,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Identity = new SourceHouseReceiptIdentity();
        Request = request;
        PdbContribution = pdbContribution;
        Work = work;
        LeaseSettlement = leaseSettlement;
    }

    public SourceHouseReceiptIdentity Identity { get; }
    public SourceHouseRequestEvidence Request { get; }
    public SourceHousePdbContribution PdbContribution { get; }
    public SourceHouseWorkCharge Work { get; }
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }
}

public abstract class SourceHouseOutcome
{
    private protected SourceHouseOutcome(
        SourceHouseRequestEvidence request,
        SourceHousePdbContribution pdbContribution,
        SourceHouseAuthoredAttempt authoredAttempt,
        SourceHouseWorkCharge work,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Request = request;
        PdbContribution = pdbContribution;
        AuthoredAttempt = authoredAttempt;
        Work = work;
        LeaseSettlement = leaseSettlement;
        Receipt = new SourceHouseReceipt(
            request,
            pdbContribution,
            work,
            leaseSettlement);
    }

    public SourceHouseRequestEvidence Request { get; }
    public SourceHousePdbContribution PdbContribution { get; }
    public SourceHouseAuthoredAttempt AuthoredAttempt { get; }
    public SourceHouseWorkCharge Work { get; }
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }
    public SourceHouseReceipt Receipt { get; }

    public sealed class Available : SourceHouseOutcome
    {
        internal Available(
            SourceHouseRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseAuthoredAttempt.Available authoredAttempt,
            SourceHouseWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                pdbContribution,
                authoredAttempt,
                work,
                leaseSettlement)
        {
            Source = authoredAttempt;
        }

        public SourceHouseAuthoredAttempt.Available Source { get; }
    }

    public sealed class Unavailable : SourceHouseOutcome
    {
        internal Unavailable(
            SourceHouseRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseAuthoredAttempt.Unavailable authoredAttempt,
            SourceHouseWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                pdbContribution,
                authoredAttempt,
                work,
                leaseSettlement)
        {
        }
    }

    public sealed class Rejected : SourceHouseOutcome
    {
        internal Rejected(
            SourceHouseRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseAuthoredAttempt.Rejected authoredAttempt,
            SourceHouseRejection rejection,
            SourceHouseWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                pdbContribution,
                authoredAttempt,
                work,
                leaseSettlement)
        {
            Rejection = rejection;
        }

        public SourceHouseRejection Rejection { get; }
    }

    public sealed class Failed : SourceHouseOutcome
    {
        internal Failed(
            SourceHouseRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseAuthoredAttempt.Failed authoredAttempt,
            SourceHouseFailure failure,
            SourceHouseWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                pdbContribution,
                authoredAttempt,
                work,
                leaseSettlement)
        {
            Failure = failure;
        }

        public SourceHouseFailure Failure { get; }
    }

    public sealed class Incomplete : SourceHouseOutcome
    {
        internal Incomplete(
            SourceHouseRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseAuthoredAttempt.Incomplete authoredAttempt,
            SourceHouseIncompleteBoundary boundary,
            SourceHouseWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                pdbContribution,
                authoredAttempt,
                work,
                leaseSettlement)
        {
            Boundary = boundary;
        }

        public SourceHouseIncompleteBoundary Boundary { get; }
    }
}
