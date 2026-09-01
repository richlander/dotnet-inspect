using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

public enum CatalogMethodDefinitionCorrespondenceSide
{
    Source,
    Target,
}

public enum CatalogMethodDefinitionCorrespondenceLimit
{
    TargetMethods,
    SameNameCandidates,
}

public abstract class CatalogMethodDefinitionCorrespondenceFailure
{
    private protected CatalogMethodDefinitionCorrespondenceFailure(
        CatalogMethodDefinitionCorrespondenceSide side,
        int? methodToken)
    {
        Side = side;
        MethodToken = methodToken;
    }

    public CatalogMethodDefinitionCorrespondenceSide Side { get; }
    public int? MethodToken { get; }

    public sealed class ImageOwnerMismatch
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal ImageOwnerMismatch(
            CatalogMethodDefinitionCorrespondenceSide side,
            int? methodToken,
            ResolvedAssemblyReference assembly)
            : base(side, methodToken) =>
            Assembly = assembly;

        public ResolvedAssemblyReference Assembly { get; }
    }

    public sealed class GenerationMismatch
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal GenerationMismatch(
            CatalogMethodDefinitionCorrespondenceSide side,
            int methodToken,
            ResolvedAssemblyReference assembly,
            Guid expectedModuleVersionId,
            Guid actualModuleVersionId)
            : base(side, methodToken)
        {
            Assembly = assembly;
            ExpectedModuleVersionId = expectedModuleVersionId;
            ActualModuleVersionId = actualModuleVersionId;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public Guid ExpectedModuleVersionId { get; }
        public Guid ActualModuleVersionId { get; }
    }

    public sealed class InvalidMethodToken
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal InvalidMethodToken(
            CatalogMethodDefinitionCorrespondenceSide side,
            int methodToken)
            : base(side, methodToken)
        {
        }
    }

    public sealed class ResourceLimitExceeded
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal ResourceLimitExceeded(
            CatalogMethodDefinitionCorrespondenceLimit limit,
            int maximum)
            : base(
                CatalogMethodDefinitionCorrespondenceSide.Target,
                methodToken: null)
        {
            Limit = limit;
            Maximum = maximum;
        }

        public CatalogMethodDefinitionCorrespondenceLimit Limit { get; }
        public int Maximum { get; }
    }

    public sealed class ResolutionContextMismatch
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal ResolutionContextMismatch(
            CatalogMethodDefinitionCorrespondenceSide side,
            int methodToken,
            ResolvedAssemblyReference expectedAssembly,
            Guid expectedModuleVersionId,
            ResolvedAssemblyReference? actualAssembly,
            Guid? actualModuleVersionId)
            : base(side, methodToken)
        {
            ExpectedAssembly = expectedAssembly;
            ExpectedModuleVersionId = expectedModuleVersionId;
            ActualAssembly = actualAssembly;
            ActualModuleVersionId = actualModuleVersionId;
        }

        public ResolvedAssemblyReference ExpectedAssembly { get; }
        public Guid ExpectedModuleVersionId { get; }
        public ResolvedAssemblyReference? ActualAssembly { get; }
        public Guid? ActualModuleVersionId { get; }
    }

    public sealed class IncompleteProjection
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal IncompleteProjection(
            CatalogMethodDefinitionCorrespondenceSide side,
            int methodToken,
            ImmutableArray<MemberCorrespondenceFailure> failures)
            : base(side, methodToken) =>
            Failures = failures;

        public ImmutableArray<MemberCorrespondenceFailure> Failures { get; }
    }

    public sealed class IndeterminateProjection
        : CatalogMethodDefinitionCorrespondenceFailure
    {
        internal IndeterminateProjection(
            CatalogMethodDefinitionCorrespondenceSide side,
            int methodToken,
            ImmutableArray<MemberCorrespondenceEvidence> evidence)
            : base(side, methodToken) =>
            Evidence = evidence;

        public ImmutableArray<MemberCorrespondenceEvidence> Evidence { get; }
    }
}

public abstract class CatalogMethodDefinitionCorrespondenceOutcome
{
    private protected CatalogMethodDefinitionCorrespondenceOutcome()
    {
    }

    public sealed class Exact : CatalogMethodDefinitionCorrespondenceOutcome
    {
        internal Exact(
            ResolvedAssemblyReference assembly,
            MetadataMethodAddress method)
        {
            Assembly = assembly;
            Method = method;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public MetadataMethodAddress Method { get; }
    }

    public sealed class Missing : CatalogMethodDefinitionCorrespondenceOutcome
    {
        internal Missing()
        {
        }
    }

    public sealed class Ambiguous
        : CatalogMethodDefinitionCorrespondenceOutcome
    {
        internal Ambiguous(
            ImmutableArray<MetadataMethodAddress> candidates) =>
            Candidates = candidates;

        public ImmutableArray<MetadataMethodAddress> Candidates { get; }
    }

    public sealed class Unavailable
        : CatalogMethodDefinitionCorrespondenceOutcome
    {
        internal Unavailable(
            ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>
                failures) =>
            Failures = failures;

        public ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>
            Failures { get; }
    }
}

/// <summary>
/// Demand-scoped correspondence from one source MethodDef to one MethodDef in
/// a separately selected target acquisition.
/// </summary>
/// <remarks>
/// <c>ReorderedMethodDefs_SelectExactTargetInsteadOfReusingSourceRid</c>
/// gates the acquisition-local token boundary. Missing, ambiguous, unresolved,
/// and generation-mismatched cases are gated by the correspondingly named
/// tests in <c>CatalogMethodDefinitionCorrespondencePlanTests</c>.
/// </remarks>
public sealed class CatalogMethodDefinitionCorrespondencePlan
{
    readonly ResolvedAssemblyReference _sourceAssembly;
    readonly ResolvedAssemblyReference _targetAssembly;
    readonly MethodIdentity _source;
    readonly CatalogMemberCorrespondencePlan _sourcePlan;
    readonly Guid _targetModuleVersionId;
    readonly ImmutableArray<TargetCandidate> _targets;
    readonly ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>
        _ownershipFailures;

    CatalogMethodDefinitionCorrespondencePlan(
        ResolvedAssemblyReference sourceAssembly,
        ResolvedAssemblyReference targetAssembly,
        MethodIdentity source,
        Guid targetModuleVersionId,
        CatalogMemberCorrespondencePlan sourcePlan,
        ImmutableArray<TargetCandidate> targets,
        ImmutableArray<TypeResolutionRequest> requests,
        ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>
            ownershipFailures)
    {
        _sourceAssembly = sourceAssembly;
        _targetAssembly = targetAssembly;
        _source = source;
        _targetModuleVersionId = targetModuleVersionId;
        _sourcePlan = sourcePlan;
        _targets = targets;
        Requests = requests;
        _ownershipFailures = ownershipFailures;
    }

    public ImmutableArray<TypeResolutionRequest> Requests { get; }

    public static CatalogMethodDefinitionCorrespondencePlan Create(
        ResolvedAssemblyReference sourceAssembly,
        AssemblyImageSnapshot sourceImage,
        MethodIdentity source,
        ResolvedAssemblyReference targetAssembly,
        AssemblyImageSnapshot targetImage,
        IEnumerable<MethodIdentity> targetMethods)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetAssembly);
        ArgumentNullException.ThrowIfNull(targetImage);
        ArgumentNullException.ThrowIfNull(targetMethods);

        var failures = ImmutableArray.CreateBuilder<
            CatalogMethodDefinitionCorrespondenceFailure>();
        int sourceMethodDefinitionCount =
            GetMethodDefinitionCount(sourceImage);
        int targetMethodDefinitionCount =
            GetMethodDefinitionCount(targetImage);
        ValidateImageOwner(
            CatalogMethodDefinitionCorrespondenceSide.Source,
            sourceAssembly,
            sourceImage,
            source.MetadataToken,
            failures);
        ValidateOwnership(
            CatalogMethodDefinitionCorrespondenceSide.Source,
            sourceAssembly,
            sourceImage.ModuleVersionId,
            sourceMethodDefinitionCount,
            source,
            failures);
        ValidateImageOwner(
            CatalogMethodDefinitionCorrespondenceSide.Target,
            targetAssembly,
            targetImage,
            methodToken: null,
            failures);

        CatalogMemberCorrespondencePlan sourcePlan =
            CatalogMemberCorrespondencePlan.Create(
                sourceAssembly,
                source);
        var targets = ImmutableArray.CreateBuilder<TargetCandidate>();
        int targetMethodCount = 0;
        foreach (MethodIdentity target in targetMethods)
        {
            ArgumentNullException.ThrowIfNull(target);
            targetMethodCount++;
            if (targetMethodCount
                > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
            {
                failures.Add(
                    new CatalogMethodDefinitionCorrespondenceFailure
                        .ResourceLimitExceeded(
                            CatalogMethodDefinitionCorrespondenceLimit
                                .TargetMethods,
                            MetadataSafetyPolicy
                                .MaxCorrespondenceMethodRows));
                break;
            }
            if (!string.Equals(
                    source.Name,
                    target.Name,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (targets.Count
                == MetadataSafetyPolicy.MaxCorrespondenceCandidates)
            {
                failures.Add(
                    new CatalogMethodDefinitionCorrespondenceFailure
                        .ResourceLimitExceeded(
                            CatalogMethodDefinitionCorrespondenceLimit
                                .SameNameCandidates,
                            MetadataSafetyPolicy
                                .MaxCorrespondenceCandidates));
                break;
            }

            ValidateOwnership(
                CatalogMethodDefinitionCorrespondenceSide.Target,
                targetAssembly,
                targetImage.ModuleVersionId,
                targetMethodDefinitionCount,
                target,
                failures);
            targets.Add(
                new TargetCandidate(
                    target,
                    CatalogMemberCorrespondencePlan.Create(
                        targetAssembly,
                        target)));
        }

        ImmutableArray<TargetCandidate> targetSnapshot =
            targets.ToImmutable();
        ImmutableArray<TypeResolutionRequest> requests =
        [
            .. sourcePlan.Requests
                .Concat(targetSnapshot.SelectMany(
                    static target => target.Plan.Requests))
                .Distinct(TypeResolutionRequestComparer.Instance),
        ];
        return new(
            sourceAssembly,
            targetAssembly,
            source,
            targetImage.ModuleVersionId,
            sourcePlan,
            targetSnapshot,
            requests,
            failures.ToImmutable());
    }

    public CatalogMethodDefinitionCorrespondenceOutcome Project(
        TypeResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_ownershipFailures.IsEmpty)
        {
            return new CatalogMethodDefinitionCorrespondenceOutcome.Unavailable(
                _ownershipFailures);
        }

        var failures = ImmutableArray.CreateBuilder<
            CatalogMethodDefinitionCorrespondenceFailure>();
        CatalogMemberJoinProjection? sourceProjection =
            ExactProjection(
                _sourcePlan,
                CatalogMethodDefinitionCorrespondenceSide.Source,
                _source.MetadataToken,
                context,
                failures);
        if (sourceProjection
                is not CatalogMemberJoinProjection.Issued issuedSource)
        {
            return new CatalogMethodDefinitionCorrespondenceOutcome.Unavailable(
                failures.ToImmutable());
        }
        ValidateResolutionContext(
            _sourcePlan,
            context,
            CatalogMethodDefinitionCorrespondenceSide.Source,
            _source.MetadataToken,
            _sourceAssembly,
            _source.ModuleVersionId,
            failures);
        if (failures.Count > 0)
        {
            return new CatalogMethodDefinitionCorrespondenceOutcome.Unavailable(
                failures.ToImmutable());
        }

        var matches = ImmutableArray.CreateBuilder<MetadataMethodAddress>();
        TypeResolutionRequest? sourceDeclaringTypeRequest =
            _sourcePlan.DeclaringTypeResolutionRequest;
        foreach (TargetCandidate target in _targets)
        {
            CatalogMemberJoinProjection? targetProjection =
                ExactProjection(
                    target.Plan,
                    CatalogMethodDefinitionCorrespondenceSide.Target,
                    target.Member.MetadataToken,
                    context,
                    failures);
            if (targetProjection
                    is CatalogMemberJoinProjection.Issued issuedTarget)
            {
                ValidateResolutionContext(
                    target.Plan,
                    context,
                    CatalogMethodDefinitionCorrespondenceSide.Target,
                    target.Member.MetadataToken,
                    _targetAssembly,
                    _targetModuleVersionId,
                    failures);
            }
            if (targetProjection
                    is CatalogMemberJoinProjection.Issued issuedTargetExact
                && _sourcePlan.CorrespondsToEstablished(
                    target.Plan,
                    issuedSource,
                    issuedTargetExact,
                    (sourceRequest, targetRequest) =>
                        IsSelectedRootTypeCorrespondence(
                            context,
                            sourceRequest,
                            targetRequest,
                            sourceDeclaringTypeRequest,
                            target.Plan
                                .DeclaringTypeResolutionRequest)))
            {
                matches.Add(Address(target.Member));
            }
        }

        if (failures.Count > 0)
        {
            return new CatalogMethodDefinitionCorrespondenceOutcome.Unavailable(
                failures.ToImmutable());
        }

        return matches.Count switch
        {
            0 => new CatalogMethodDefinitionCorrespondenceOutcome.Missing(),
            1 => new CatalogMethodDefinitionCorrespondenceOutcome.Exact(
                _targetAssembly,
                matches[0]),
            _ => new CatalogMethodDefinitionCorrespondenceOutcome.Ambiguous(
                matches.ToImmutable()),
        };
    }

    bool IsSelectedRootTypeCorrespondence(
        TypeResolutionContext context,
        TypeResolutionRequest sourceRequest,
        TypeResolutionRequest targetRequest,
        TypeResolutionRequest? sourceDeclaringTypeRequest,
        TypeResolutionRequest? targetDeclaringTypeRequest)
    {
        if (sourceDeclaringTypeRequest is null
            || targetDeclaringTypeRequest is null
            || !TypeResolutionRequestComparer.Instance.Equals(
                sourceRequest,
                sourceDeclaringTypeRequest)
            || !TypeResolutionRequestComparer.Instance.Equals(
                targetRequest,
                targetDeclaringTypeRequest)
            || sourceRequest.Type != targetRequest.Type
            || context.Resolve(sourceRequest)
                is not TypeResolutionOutcome.Resolved source
            || context.Resolve(targetRequest)
                is not TypeResolutionOutcome.Resolved target
            || (source.Definition.Kind
                    != MetadataTypeDefinitionKind.Unknown
                && target.Definition.Kind
                    != MetadataTypeDefinitionKind.Unknown
                && source.Definition.Kind != target.Definition.Kind))
        {
            return false;
        }

        return ReferenceEquals(
                source.Definition.Assembly.Assembly.Registration,
                _sourceAssembly.Registration)
            && ReferenceEquals(
                target.Definition.Assembly.Assembly.Registration,
                _targetAssembly.Registration);
    }

    static CatalogMemberJoinProjection? ExactProjection(
        CatalogMemberCorrespondencePlan plan,
        CatalogMethodDefinitionCorrespondenceSide side,
        int methodToken,
        TypeResolutionContext context,
        ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>.Builder
            failures)
    {
        CatalogMemberJoinProjection projection = plan.Project(context);
        switch (projection)
        {
            case CatalogMemberJoinProjection.Incomplete incomplete:
                failures.Add(
                    new CatalogMethodDefinitionCorrespondenceFailure
                        .IncompleteProjection(
                            side,
                            methodToken,
                            incomplete.Failures));
                return null;
            case CatalogMemberJoinProjection.Issued issued
                when issued.Key.Kind
                    != CatalogMemberCorrespondenceKind.Exact:
                failures.Add(
                    new CatalogMethodDefinitionCorrespondenceFailure
                        .IndeterminateProjection(
                            side,
                            methodToken,
                            issued.Evidence));
                return null;
            default:
                return projection;
        }
    }

    static void ValidateResolutionContext(
        CatalogMemberCorrespondencePlan plan,
        TypeResolutionContext context,
        CatalogMethodDefinitionCorrespondenceSide side,
        int methodToken,
        ResolvedAssemblyReference expectedAssembly,
        Guid expectedModuleVersionId,
        ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>.Builder
            failures)
    {
        TypeResolutionOutcome.Resolved? resolution =
            plan.DeclaringTypeResolution(context);
        ResolvedAssemblyReference? actualAssembly =
            resolution?.Definition.Assembly.Assembly;
        Guid? actualModuleVersionId =
            resolution?.Definition.Address.ModuleVersionId;
        if (actualAssembly is not null
            && ReferenceEquals(
                actualAssembly.Registration,
                expectedAssembly.Registration)
            && actualModuleVersionId == expectedModuleVersionId)
        {
            return;
        }

        failures.Add(
            new CatalogMethodDefinitionCorrespondenceFailure
                .ResolutionContextMismatch(
                    side,
                    methodToken,
                    expectedAssembly,
                    expectedModuleVersionId,
                    actualAssembly,
                    actualModuleVersionId));
    }

    static void ValidateOwnership(
        CatalogMethodDefinitionCorrespondenceSide side,
        ResolvedAssemblyReference assembly,
        Guid expectedModuleVersionId,
        int methodDefinitionCount,
        MethodIdentity method,
        ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>.Builder
            failures)
    {
        if (expectedModuleVersionId != method.ModuleVersionId)
        {
            failures.Add(
                new CatalogMethodDefinitionCorrespondenceFailure
                    .GenerationMismatch(
                        side,
                        method.MetadataToken,
                        assembly,
                        expectedModuleVersionId,
                        method.ModuleVersionId));
        }

        int rowNumber = method.MetadataToken & 0x00FFFFFF;
        if (!IsMethodDefinitionToken(method.MetadataToken)
            || rowNumber > methodDefinitionCount)
        {
            failures.Add(
                new CatalogMethodDefinitionCorrespondenceFailure
                    .InvalidMethodToken(
                        side,
                        method.MetadataToken));
        }
    }

    static void ValidateImageOwner(
        CatalogMethodDefinitionCorrespondenceSide side,
        ResolvedAssemblyReference assembly,
        AssemblyImageSnapshot image,
        int? methodToken,
        ImmutableArray<CatalogMethodDefinitionCorrespondenceFailure>.Builder
            failures)
    {
        if (!ReferenceEquals(
                assembly.Registration,
                image.Registration)
            || !assembly.Identity.IsEquivalentTo(image.Identity))
        {
            failures.Add(
                new CatalogMethodDefinitionCorrespondenceFailure
                    .ImageOwnerMismatch(
                        side,
                        methodToken,
                        assembly));
        }
    }

    static bool IsMethodDefinitionToken(int token) =>
        (token & unchecked((int)0xFF000000)) == 0x06000000
        && (token & 0x00FFFFFF) != 0;

    static int GetMethodDefinitionCount(AssemblyImageSnapshot image)
    {
        using var peReader = new PEReader(image.Content);
        return MetadataFormatAdmission.GetMetadataReader(peReader)
            .GetTableRowCount(TableIndex.MethodDef);
    }

    static MetadataMethodAddress Address(MethodIdentity method) =>
        new(
            method.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(
                method.MetadataToken & 0x00FFFFFF));

    readonly record struct TargetCandidate(
        MethodIdentity Member,
        CatalogMemberCorrespondencePlan Plan);
}
