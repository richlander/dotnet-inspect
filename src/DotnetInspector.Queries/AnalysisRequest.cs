using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>A stable owner-issued identifier for one analysis declaration.</summary>
public sealed record AnalysisDeclarationId
{
    public AnalysisDeclarationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>The domain an analysis result describes.</summary>
public enum AnalysisReportSurfaceKind
{
    Member,
    Type,
    Library,
    Root,
    Workspace,
}

/// <summary>Whether a request has privileged anchors or reports a census.</summary>
public enum AnalysisQuestionMode
{
    Targeted,
    Census,
}

/// <summary>The function one target role serves in a request.</summary>
public enum AnalysisTargetFunction
{
    ReportDomain,
    PrivilegedAnchor,
}

/// <summary>Owner-issued report-surface identity.</summary>
public interface IAnalysisReportSurfaceIdentity
{
}

/// <summary>Owner-issued target identity.</summary>
public interface IAnalysisTargetIdentity
{
}

/// <summary>Owner-issued finite-universe identity.</summary>
public interface IAnalysisUniverseIdentity
{
}

/// <summary>Owner-issued requested or realized universe boundary.</summary>
public interface IAnalysisUniverseBoundary
{
}

/// <summary>Owner-issued universe completeness evidence.</summary>
public interface IAnalysisUniverseCompleteness
{
}

/// <summary>Owner-issued universe failure input.</summary>
public interface IAnalysisUniverseFailure
{
}

/// <summary>
/// Owner-specific identity affected by one universe requirement, such as an
/// Integration concept descriptor.
/// </summary>
public interface IAnalysisRequirementAffectedIdentity
{
}

/// <summary>One owner-issued target role accepted by an analysis descriptor.</summary>
public sealed class AnalysisTargetRoleDescriptor
{
    public AnalysisTargetRoleDescriptor(
        AnalysisDeclarationId id,
        AnalysisTargetFunction function,
        int minimumCount,
        int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(function))
            throw new ArgumentOutOfRangeException(nameof(function));
        ArgumentOutOfRangeException.ThrowIfNegative(minimumCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, minimumCount);
        Id = id;
        Function = function;
        MinimumCount = minimumCount;
        MaximumCount = maximumCount;
    }

    public AnalysisDeclarationId Id { get; }
    public AnalysisTargetFunction Function { get; }
    public int MinimumCount { get; }
    public int MaximumCount { get; }
}

/// <summary>One target bound to an owner-issued role.</summary>
public sealed class AnalysisTargetBinding
{
    public AnalysisTargetBinding(
        AnalysisTargetRoleDescriptor role,
        IAnalysisTargetIdentity target)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(target);
        Role = role;
        Target = target;
    }

    public AnalysisTargetRoleDescriptor Role { get; }
    public IAnalysisTargetIdentity Target { get; }
}

/// <summary>One report surface and its typed target-role bindings.</summary>
public sealed class AnalysisReportSurface
{
    public AnalysisReportSurface(
        AnalysisReportSurfaceKind kind,
        IAnalysisReportSurfaceIdentity identity,
        IEnumerable<AnalysisTargetBinding> targets)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(targets);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        Identity = identity;
        Targets = CopyNonNull(targets, nameof(targets));
    }

    public AnalysisReportSurfaceKind Kind { get; }
    public IAnalysisReportSurfaceIdentity Identity { get; }
    public ImmutableArray<AnalysisTargetBinding> Targets { get; }

    static ImmutableArray<T> CopyNonNull<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ImmutableArray<T> result = [.. values];
        if (result.Any(value => value is null))
            throw new ArgumentException("The collection cannot contain null.", parameterName);
        return result;
    }
}

/// <summary>One evidence capability a universe provider may declare.</summary>
public sealed class AnalysisUniverseCapabilityDescriptor
{
    public AnalysisUniverseCapabilityDescriptor(
        AnalysisDeclarationId id,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Id = id;
        Summary = summary;
    }

    public AnalysisDeclarationId Id { get; }
    public string Summary { get; }
}

/// <summary>One descriptor-issued universe requirement.</summary>
public sealed class AnalysisUniverseRequirementDescriptor
{
    public AnalysisUniverseRequirementDescriptor(
        AnalysisDeclarationId id,
        AnalysisUniverseCapabilityDescriptor capability,
        IEnumerable<AnalysisQuestionMode> modes,
        IEnumerable<IAnalysisRequirementAffectedIdentity>? affected = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(modes);
        Id = id;
        Capability = capability;
        Modes = CopyDistinct(modes, nameof(modes));
        if (Modes.IsEmpty)
            throw new ArgumentException("At least one question mode is required.", nameof(modes));
        if (Modes.Any(mode => !Enum.IsDefined(mode)))
            throw new ArgumentException("Every question mode must be defined.", nameof(modes));
        Affected = affected is null
            ? []
            : CopyNonNull(affected, nameof(affected));
    }

    public AnalysisDeclarationId Id { get; }
    public AnalysisUniverseCapabilityDescriptor Capability { get; }
    public ImmutableArray<AnalysisQuestionMode> Modes { get; }
    public ImmutableArray<IAnalysisRequirementAffectedIdentity> Affected { get; }

    static ImmutableArray<T> CopyDistinct<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : struct, Enum
    {
        ImmutableArray<T> result = [.. values];
        if (result.Distinct().Count() != result.Length)
            throw new ArgumentException("The collection cannot contain duplicates.", parameterName);
        return result;
    }

    static ImmutableArray<T> CopyNonNull<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ImmutableArray<T> result = [.. values];
        if (result.Any(value => value is null))
            throw new ArgumentException("The collection cannot contain null.", parameterName);
        return result;
    }
}

/// <summary>One producer or query registration required before execution.</summary>
public abstract class AnalysisStructuralPrerequisiteDescriptor
{
    private protected AnalysisStructuralPrerequisiteDescriptor(
        AnalysisDeclarationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public AnalysisDeclarationId Id { get; }
}

/// <summary>An exact typed query registration required before execution.</summary>
public sealed class AnalysisQueryPrerequisiteDescriptor
    : AnalysisStructuralPrerequisiteDescriptor
{
    public AnalysisQueryPrerequisiteDescriptor(
        AnalysisDeclarationId id,
        InspectionQueryDefinition query)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query;
    }

    public InspectionQueryDefinition Query { get; }
}

/// <summary>
/// One owner-issued non-query producer registration required before execution.
/// </summary>
public sealed class AnalysisProducerPrerequisiteDescriptor
    : AnalysisStructuralPrerequisiteDescriptor
{
    public AnalysisProducerPrerequisiteDescriptor(AnalysisDeclarationId id)
        : base(id)
    {
    }
}

/// <summary>
/// One host-preflight requirement retained by the planner without enforcing
/// host policy.
/// </summary>
public sealed class AnalysisHostRequirementDescriptor
{
    public AnalysisHostRequirementDescriptor(
        AnalysisDeclarationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public AnalysisDeclarationId Id { get; }
}

/// <summary>One owner-issued result projection supported by an analysis.</summary>
public sealed class AnalysisProjectionDescriptor
{
    public AnalysisProjectionDescriptor(AnalysisDeclarationId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    public AnalysisDeclarationId Id { get; }
}

/// <summary>One supported report-surface and question-mode combination.</summary>
public sealed class AnalysisReportSurfaceSupport
{
    public AnalysisReportSurfaceSupport(
        AnalysisReportSurfaceKind kind,
        AnalysisQuestionMode mode,
        IEnumerable<AnalysisTargetRoleDescriptor> targetRoles)
    {
        ArgumentNullException.ThrowIfNull(targetRoles);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Kind = kind;
        Mode = mode;
        TargetRoles = CopyUniqueRoles(targetRoles, nameof(targetRoles));
    }

    public AnalysisReportSurfaceKind Kind { get; }
    public AnalysisQuestionMode Mode { get; }
    public ImmutableArray<AnalysisTargetRoleDescriptor> TargetRoles { get; }

    static ImmutableArray<AnalysisTargetRoleDescriptor> CopyUniqueRoles(
        IEnumerable<AnalysisTargetRoleDescriptor> values,
        string parameterName)
    {
        ImmutableArray<AnalysisTargetRoleDescriptor> result = [.. values];
        if (result.Any(value => value is null))
            throw new ArgumentException("The collection cannot contain null.", parameterName);
        if (result.Select(role => role.Id).Distinct().Count() != result.Length)
        {
            throw new ArgumentException(
                "Declaration identifiers must be unique.",
                parameterName);
        }
        return result;
    }
}

/// <summary>One projection supported for specific question modes.</summary>
public sealed class AnalysisProjectionSupport
{
    public AnalysisProjectionSupport(
        AnalysisProjectionDescriptor projection,
        IEnumerable<AnalysisQuestionMode> modes)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(modes);
        Projection = projection;
        Modes = [.. modes];
        if (Modes.IsEmpty || Modes.Distinct().Count() != Modes.Length)
        {
            throw new ArgumentException(
                "Projection modes must be non-empty and unique.",
                nameof(modes));
        }
        if (Modes.Any(mode => !Enum.IsDefined(mode)))
            throw new ArgumentException("Every question mode must be defined.", nameof(modes));
    }

    public AnalysisProjectionDescriptor Projection { get; }
    public ImmutableArray<AnalysisQuestionMode> Modes { get; }
}

/// <summary>
/// One owner-issued analysis descriptor and its closed pre-execution
/// declarations.
/// </summary>
public sealed class AnalysisDescriptor
{
    public AnalysisDescriptor(
        AnalysisDeclarationId id,
        int revision,
        InspectionCost cost,
        IEnumerable<AnalysisQuestionMode> modes,
        IEnumerable<AnalysisReportSurfaceSupport> reportSurfaces,
        IEnumerable<AnalysisUniverseRequirementDescriptor> universeRequirements,
        IEnumerable<AnalysisStructuralPrerequisiteDescriptor> structuralPrerequisites,
        IEnumerable<AnalysisHostRequirementDescriptor> hostRequirements,
        IEnumerable<AnalysisProjectionSupport> projections)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (!Enum.IsDefined(cost))
            throw new ArgumentOutOfRangeException(nameof(cost));
        ArgumentNullException.ThrowIfNull(modes);
        Id = id;
        Revision = revision;
        Cost = cost;
        Modes = CopyDistinctModes(modes, nameof(modes));
        if (Modes.IsEmpty)
            throw new ArgumentException("At least one question mode is required.", nameof(modes));
        if (Modes.Any(mode => !Enum.IsDefined(mode)))
            throw new ArgumentException("Every question mode must be defined.", nameof(modes));
        ReportSurfaces = CopyNonNull(reportSurfaces, nameof(reportSurfaces));
        UniverseRequirements = CopyUniqueDeclarations(
            universeRequirements,
            requirement => requirement.Id,
            nameof(universeRequirements));
        StructuralPrerequisites = CopyUniqueDeclarations(
            structuralPrerequisites,
            prerequisite => prerequisite.Id,
            nameof(structuralPrerequisites));
        HostRequirements = CopyUniqueDeclarations(
            hostRequirements,
            requirement => requirement.Id,
            nameof(hostRequirements));
        Projections = CopyNonNull(projections, nameof(projections));
        if (ReportSurfaces.IsEmpty)
            throw new ArgumentException("At least one report surface is required.", nameof(reportSurfaces));
        if (Projections.IsEmpty)
            throw new ArgumentException("At least one projection is required.", nameof(projections));

        ValidateSurfaceDeclarations();
        ValidateProjectionDeclarations();
        ValidateRequirementModes();
        ValidateRequirementCapabilities();
        ValidateModeCoherence();
    }

    public AnalysisDeclarationId Id { get; }
    public int Revision { get; }
    public InspectionCost Cost { get; }
    public ImmutableArray<AnalysisQuestionMode> Modes { get; }
    public ImmutableArray<AnalysisReportSurfaceSupport> ReportSurfaces { get; }
    public ImmutableArray<AnalysisUniverseRequirementDescriptor> UniverseRequirements { get; }
    public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> StructuralPrerequisites { get; }
    public ImmutableArray<AnalysisHostRequirementDescriptor> HostRequirements { get; }
    public ImmutableArray<AnalysisProjectionSupport> Projections { get; }

    void ValidateSurfaceDeclarations()
    {
        if (ReportSurfaces.Any(surface => !Modes.Contains(surface.Mode)))
        {
            throw new ArgumentException(
                "Every report surface mode must be supported by the analysis.",
                nameof(ReportSurfaces));
        }

        if (ReportSurfaces
            .GroupBy(surface => (surface.Kind, surface.Mode))
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException(
                "A report surface kind and mode may be declared only once.",
                nameof(ReportSurfaces));
        }
    }

    void ValidateProjectionDeclarations()
    {
        if (Projections
            .Select(projection => projection.Projection.Id)
            .Distinct()
            .Count() != Projections.Length)
        {
            throw new ArgumentException(
                "Projection identifiers must be unique.",
                nameof(Projections));
        }

        if (Projections.Any(projection =>
                projection.Modes.Any(mode => !Modes.Contains(mode))))
        {
            throw new ArgumentException(
                "Every projection mode must be supported by the analysis.",
                nameof(Projections));
        }
    }

    void ValidateRequirementModes()
    {
        if (UniverseRequirements.Any(requirement =>
                requirement.Modes.Any(mode => !Modes.Contains(mode))))
        {
            throw new ArgumentException(
                "Every universe-requirement mode must be supported by the analysis.",
                nameof(UniverseRequirements));
        }
    }

    void ValidateRequirementCapabilities()
    {
        foreach (IGrouping<
            AnalysisDeclarationId,
            AnalysisUniverseRequirementDescriptor> group in
            UniverseRequirements.GroupBy(requirement => requirement.Capability.Id))
        {
            AnalysisUniverseRequirementDescriptor[] requirements = [.. group];
            for (int left = 0; left < requirements.Length; left++)
            {
                for (int right = left + 1; right < requirements.Length; right++)
                {
                    if (!ReferenceEquals(
                            requirements[left].Capability,
                            requirements[right].Capability))
                    {
                        throw new ArgumentException(
                            "Requirements with one capability identifier must share its exact descriptor.",
                            nameof(UniverseRequirements));
                    }
                }
            }
        }
    }

    void ValidateModeCoherence()
    {
        foreach (AnalysisQuestionMode mode in Modes)
        {
            if (!Projections.Any(projection => projection.Modes.Contains(mode)))
            {
                throw new ArgumentException(
                    "Every supported mode must have a supported projection.",
                    nameof(Projections));
            }

            ImmutableArray<AnalysisReportSurfaceSupport> modeSurfaces =
            [
                .. ReportSurfaces.Where(surface => surface.Mode == mode),
            ];
            if (modeSurfaces.IsEmpty
                || modeSurfaces.Any(surface => !IsViable(surface, mode)))
            {
                throw new ArgumentException(
                    "Every supported report surface must be satisfiable.",
                    nameof(ReportSurfaces));
            }
        }
    }

    static bool IsViable(
        AnalysisReportSurfaceSupport surface,
        AnalysisQuestionMode mode) =>
        mode switch
        {
            AnalysisQuestionMode.Targeted => surface.TargetRoles.Any(role =>
                role.Function == AnalysisTargetFunction.PrivilegedAnchor
                && role.MaximumCount > 0),
            AnalysisQuestionMode.Census =>
                surface.TargetRoles.Any(role =>
                    role.Function == AnalysisTargetFunction.ReportDomain
                    && role.MaximumCount > 0)
                && !surface.TargetRoles.Any(role =>
                    role.Function == AnalysisTargetFunction.PrivilegedAnchor
                    && role.MinimumCount > 0),
            _ => false,
        };

    static ImmutableArray<AnalysisQuestionMode> CopyDistinctModes(
        IEnumerable<AnalysisQuestionMode> values,
        string parameterName)
    {
        ImmutableArray<AnalysisQuestionMode> result = [.. values];
        if (result.Distinct().Count() != result.Length)
            throw new ArgumentException("The collection cannot contain duplicates.", parameterName);
        return result;
    }

    static ImmutableArray<T> CopyNonNull<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        ImmutableArray<T> result = [.. values];
        if (result.Any(value => value is null))
            throw new ArgumentException("The collection cannot contain null.", parameterName);
        return result;
    }

    static ImmutableArray<T> CopyUniqueDeclarations<T>(
        IEnumerable<T> values,
        Func<T, AnalysisDeclarationId> getId,
        string parameterName)
        where T : class
    {
        ImmutableArray<T> result = CopyNonNull(values, parameterName);
        if (result.Select(getId).Distinct().Count() != result.Length)
        {
            throw new ArgumentException(
                "Declaration identifiers must be unique.",
                parameterName);
        }
        return result;
    }
}

/// <summary>
/// One finite universe description issued by the owner that constructed it.
/// </summary>
public sealed class AnalysisUniverseDescription
{
    public AnalysisUniverseDescription(
        IAnalysisUniverseIdentity identity,
        IAnalysisUniverseBoundary requestedBoundary,
        IAnalysisUniverseBoundary realizedBoundary,
        bool isFinite,
        IEnumerable<AnalysisUniverseCapabilityDescriptor> capabilities,
        IAnalysisUniverseCompleteness completeness,
        IEnumerable<IAnalysisUniverseFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(requestedBoundary);
        ArgumentNullException.ThrowIfNull(realizedBoundary);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(completeness);
        Identity = identity;
        RequestedBoundary = requestedBoundary;
        RealizedBoundary = realizedBoundary;
        IsFinite = isFinite;
        Capabilities = CopyUniqueDeclarations(capabilities, nameof(capabilities));
        Completeness = completeness;
        Failures = failures is null ? [] : CopyNonNull(failures, nameof(failures));
    }

    public IAnalysisUniverseIdentity Identity { get; }
    public IAnalysisUniverseBoundary RequestedBoundary { get; }
    public IAnalysisUniverseBoundary RealizedBoundary { get; }
    public bool IsFinite { get; }
    public ImmutableArray<AnalysisUniverseCapabilityDescriptor> Capabilities { get; }
    public IAnalysisUniverseCompleteness Completeness { get; }
    public ImmutableArray<IAnalysisUniverseFailure> Failures { get; }

    static ImmutableArray<AnalysisUniverseCapabilityDescriptor>
        CopyUniqueDeclarations(
            IEnumerable<AnalysisUniverseCapabilityDescriptor> values,
            string parameterName)
    {
        ImmutableArray<AnalysisUniverseCapabilityDescriptor> result =
            CopyNonNull(values, parameterName);
        if (result.Select(value => value.Id).Distinct().Count() != result.Length)
        {
            throw new ArgumentException(
                "Capability identifiers must be unique.",
                parameterName);
        }
        return result;
    }

    static ImmutableArray<T> CopyNonNull<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ImmutableArray<T> result = [.. values];
        if (result.Any(value => value is null))
            throw new ArgumentException("The collection cannot contain null.", parameterName);
        return result;
    }
}

/// <summary>The complete five-field request validated before producer work.</summary>
public sealed record AnalysisRequest(
    AnalysisDescriptor? Analysis,
    AnalysisReportSurface? ReportSurface,
    AnalysisUniverseDescription? Universe,
    AnalysisQuestionMode Mode,
    AnalysisProjectionDescriptor? Projection);

/// <summary>Why an analysis request could not become an executable plan.</summary>
public enum AnalysisRequestRejectionReason
{
    InvalidRequest,
    UnsupportedMode,
    UnsupportedSurface,
    UnsupportedTargetRole,
    InvalidMode,
    MissingUniverse,
    UnboundedUniverse,
    UnsatisfiedUniverse,
    MissingStructuralPrerequisite,
    UnsupportedProjection,
}

/// <summary>Typed pre-execution rejection using only owner-issued identities.</summary>
public sealed class AnalysisRequestRejection
{
    internal AnalysisRequestRejection(
        AnalysisRequestRejectionReason reason,
        IEnumerable<AnalysisTargetRoleDescriptor>? targetRoles = null,
        IEnumerable<AnalysisUniverseRequirementDescriptor>? universeRequirements = null,
        IEnumerable<AnalysisStructuralPrerequisiteDescriptor>? prerequisites = null)
    {
        Reason = reason;
        TargetRoles = targetRoles is null ? [] : [.. targetRoles];
        UniverseRequirements = universeRequirements is null
            ? []
            : [.. universeRequirements];
        StructuralPrerequisites = prerequisites is null
            ? []
            : [.. prerequisites];
    }

    public AnalysisRequestRejectionReason Reason { get; }
    public ImmutableArray<AnalysisTargetRoleDescriptor> TargetRoles { get; }
    public ImmutableArray<AnalysisUniverseRequirementDescriptor> UniverseRequirements { get; }
    public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> StructuralPrerequisites { get; }

    public string Guidance => Reason switch
    {
        AnalysisRequestRejectionReason.InvalidRequest =>
            "Select one configured analysis and provide its complete request.",
        AnalysisRequestRejectionReason.UnsupportedMode =>
            "Select a question mode supported by the analysis descriptor.",
        AnalysisRequestRejectionReason.UnsupportedSurface =>
            "Select a report surface supported for this analysis and mode.",
        AnalysisRequestRejectionReason.UnsupportedTargetRole =>
            "Bind targets only to owner-issued roles and satisfy each role's "
            + "declared minimum and maximum counts.",
        AnalysisRequestRejectionReason.InvalidMode =>
            "Targeted requests require a privileged anchor; "
            + "Census requests require a report-domain target and forbid privileged anchors.",
        AnalysisRequestRejectionReason.MissingUniverse =>
            "Supply one owner-issued finite analysis universe.",
        AnalysisRequestRejectionReason.UnboundedUniverse =>
            "Bound the analysis universe before producer execution.",
        AnalysisRequestRejectionReason.UnsatisfiedUniverse =>
            "Supply every universe capability required by the analysis descriptor.",
        AnalysisRequestRejectionReason.MissingStructuralPrerequisite =>
            "Register every producer and query prerequisite before execution.",
        AnalysisRequestRejectionReason.UnsupportedProjection =>
            "Select a result projection supported for this analysis and mode.",
        _ => "The analysis request is invalid.",
    };
}

/// <summary>Result of host-neutral request capability validation.</summary>
public abstract class AnalysisRequestPlanResult
{
    private AnalysisRequestPlanResult()
    {
    }

    public sealed class Accepted : AnalysisRequestPlanResult
    {
        internal Accepted(AnalysisRequestPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            Plan = plan;
        }

        public AnalysisRequestPlan Plan { get; }
    }

    public sealed class Rejected : AnalysisRequestPlanResult
    {
        internal Rejected(AnalysisRequestRejection rejection)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public AnalysisRequestRejection Rejection { get; }
    }
}

/// <summary>
/// One validated request retaining the exact owner-issued inputs and
/// descriptor requirements.
/// </summary>
public sealed class AnalysisRequestPlan
{
    internal AnalysisRequestPlan(
        AnalysisRequest request,
        AnalysisDescriptor analysis,
        AnalysisReportSurface reportSurface,
        AnalysisUniverseDescription universe,
        AnalysisProjectionDescriptor projection,
        ImmutableArray<AnalysisUniverseRequirementDescriptor> universeRequirements,
        InspectionCost cost)
    {
        Request = request;
        Analysis = analysis;
        ReportSurface = reportSurface;
        Universe = universe;
        Mode = request.Mode;
        Projection = projection;
        Cost = cost;
        UniverseRequirements = universeRequirements;
        StructuralPrerequisites = analysis.StructuralPrerequisites;
        HostRequirements = analysis.HostRequirements;
    }

    public AnalysisRequest Request { get; }
    public AnalysisDescriptor Analysis { get; }
    public AnalysisReportSurface ReportSurface { get; }
    public AnalysisUniverseDescription Universe { get; }
    public AnalysisQuestionMode Mode { get; }
    public AnalysisProjectionDescriptor Projection { get; }
    public InspectionCost Cost { get; }
    public ImmutableArray<AnalysisUniverseRequirementDescriptor> UniverseRequirements { get; }
    public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> StructuralPrerequisites { get; }
    public ImmutableArray<AnalysisHostRequirementDescriptor> HostRequirements { get; }
    public IAnalysisUniverseCompleteness UniverseCompleteness =>
        Universe.Completeness;
    public ImmutableArray<IAnalysisUniverseFailure> UniverseFailures =>
        Universe.Failures;
}

/// <summary>Structural prerequisite availability for one planning host.</summary>
public sealed class AnalysisPlanningEnvironment
{
    readonly IInspectionQueryCatalog? _queryCatalog;
    readonly ImmutableHashSet<AnalysisProducerPrerequisiteDescriptor>
        _availableProducerPrerequisites;

    public AnalysisPlanningEnvironment(
        IInspectionQueryCatalog? queryCatalog = null,
        IEnumerable<AnalysisProducerPrerequisiteDescriptor>?
            availableProducerPrerequisites = null)
    {
        _queryCatalog = queryCatalog;
        if (availableProducerPrerequisites is null)
        {
            _availableProducerPrerequisites =
                ImmutableHashSet.Create<AnalysisProducerPrerequisiteDescriptor>(
                    ReferenceEqualityComparer.Instance);
            return;
        }

        ImmutableArray<AnalysisProducerPrerequisiteDescriptor> prerequisites =
            [.. availableProducerPrerequisites];
        if (prerequisites.Any(prerequisite => prerequisite is null))
        {
            throw new ArgumentException(
                "The environment cannot contain null.",
                nameof(availableProducerPrerequisites));
        }
        _availableProducerPrerequisites = ImmutableHashSet.CreateRange<
            AnalysisProducerPrerequisiteDescriptor>(
            ReferenceEqualityComparer.Instance,
            prerequisites);
    }

    internal bool IsAvailable(
        AnalysisStructuralPrerequisiteDescriptor prerequisite) =>
        prerequisite switch
        {
            AnalysisQueryPrerequisiteDescriptor query =>
                _queryCatalog?.Contains(query.Query) is true,
            AnalysisProducerPrerequisiteDescriptor producer =>
                _availableProducerPrerequisites.Contains(producer),
            _ => false,
        };

    internal InspectionCost CostOf(
        AnalysisDescriptor analysis,
        IEnumerable<AnalysisStructuralPrerequisiteDescriptor> prerequisites)
    {
        InspectionCost cost = analysis.Cost;
        foreach (AnalysisQueryPrerequisiteDescriptor query in
            prerequisites.OfType<AnalysisQueryPrerequisiteDescriptor>())
        {
            InspectionCost queryCost = _queryCatalog!.CostOf(query.Query);
            if (queryCost > cost)
                cost = queryCost;
        }
        return cost;
    }
}

/// <summary>
/// Configured structural capabilities and host-neutral request planning.
/// </summary>
public sealed class AnalysisCapabilityCatalog
{
    readonly ImmutableHashSet<AnalysisDescriptor> _configured;

    public AnalysisCapabilityCatalog(IEnumerable<AnalysisDescriptor> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        Analyses = [.. analyses];
        if (Analyses.Any(analysis => analysis is null))
            throw new ArgumentException("The catalog cannot contain null.", nameof(analyses));
        if (Analyses.Select(analysis => analysis.Id).Distinct().Count() != Analyses.Length)
            throw new ArgumentException("Analysis identifiers must be unique.", nameof(analyses));
        _configured = ImmutableHashSet.CreateRange<AnalysisDescriptor>(
            ReferenceEqualityComparer.Instance,
            Analyses);
    }

    /// <summary>
    /// Configured descriptors in product order. Reading this collection never
    /// resolves content or executes producers.
    /// </summary>
    public ImmutableArray<AnalysisDescriptor> Analyses { get; }

    public AnalysisRequestPlanResult Plan(
        AnalysisRequest request,
        AnalysisPlanningEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environment);

        if (request.Analysis is null
            || request.ReportSurface is null
            || request.Projection is null
            || !_configured.Contains(request.Analysis)
            || !Enum.IsDefined(request.Mode))
        {
            return Reject(AnalysisRequestRejectionReason.InvalidRequest);
        }

        AnalysisDescriptor analysis = request.Analysis;
        AnalysisReportSurface reportSurface = request.ReportSurface;
        AnalysisProjectionDescriptor projection = request.Projection;

        if (!analysis.Modes.Contains(request.Mode))
            return Reject(AnalysisRequestRejectionReason.UnsupportedMode);

        AnalysisReportSurfaceSupport? surface = analysis.ReportSurfaces
            .FirstOrDefault(candidate =>
                candidate.Kind == reportSurface.Kind
                && candidate.Mode == request.Mode);
        if (surface is null)
            return Reject(AnalysisRequestRejectionReason.UnsupportedSurface);

        ImmutableArray<AnalysisTargetRoleDescriptor> invalidRoles =
        [
            .. reportSurface.Targets
                .Select(target => target.Role)
                .Where(role => !surface.TargetRoles.Contains(
                    role,
                    ReferenceEqualityComparer.Instance))
                .Distinct<AnalysisTargetRoleDescriptor>(
                    ReferenceEqualityComparer.Instance),
        ];
        if (!invalidRoles.IsEmpty)
        {
            return Reject(
                AnalysisRequestRejectionReason.UnsupportedTargetRole,
                targetRoles: invalidRoles);
        }

        bool hasAnchor = reportSurface.Targets.Any(target =>
            target.Role.Function == AnalysisTargetFunction.PrivilegedAnchor);
        bool hasReportDomain = reportSurface.Targets.Any(target =>
            target.Role.Function == AnalysisTargetFunction.ReportDomain);
        ImmutableArray<AnalysisTargetRoleDescriptor> invalidModeRoles =
            request.Mode switch
            {
                AnalysisQuestionMode.Targeted when !hasAnchor =>
                [
                    .. surface.TargetRoles.Where(role =>
                        role.Function == AnalysisTargetFunction.PrivilegedAnchor),
                ],
                AnalysisQuestionMode.Census when hasAnchor || !hasReportDomain =>
                [
                    .. reportSurface.Targets
                        .Where(target =>
                            target.Role.Function
                                == AnalysisTargetFunction.PrivilegedAnchor)
                        .Select(target => target.Role)
                        .Concat(!hasReportDomain
                            ? surface.TargetRoles.Where(role =>
                                role.Function == AnalysisTargetFunction.ReportDomain)
                            : [])
                        .Distinct<AnalysisTargetRoleDescriptor>(
                            ReferenceEqualityComparer.Instance),
                ],
                _ => [],
            };
        if (!invalidModeRoles.IsEmpty)
        {
            return Reject(
                AnalysisRequestRejectionReason.InvalidMode,
                targetRoles: invalidModeRoles);
        }

        ImmutableArray<AnalysisTargetRoleDescriptor> countMismatch =
        [
            .. surface.TargetRoles.Where(role =>
            {
                int count = reportSurface.Targets.Count(target =>
                    ReferenceEquals(target.Role, role));
                return count < role.MinimumCount || count > role.MaximumCount;
            }),
        ];
        if (!countMismatch.IsEmpty)
        {
            return Reject(
                AnalysisRequestRejectionReason.UnsupportedTargetRole,
                targetRoles: countMismatch);
        }

        if (request.Universe is null)
            return Reject(AnalysisRequestRejectionReason.MissingUniverse);
        if (!request.Universe.IsFinite)
            return Reject(AnalysisRequestRejectionReason.UnboundedUniverse);

        ImmutableArray<AnalysisUniverseRequirementDescriptor>
            requiredUniverseCapabilities =
        [
            .. analysis.UniverseRequirements.Where(requirement =>
                requirement.Modes.Contains(request.Mode)),
        ];
        ImmutableArray<AnalysisUniverseRequirementDescriptor> unmetRequirements =
        [
            .. requiredUniverseCapabilities.Where(requirement =>
                !request.Universe.Capabilities.Contains(
                    requirement.Capability,
                    ReferenceEqualityComparer.Instance)),
        ];
        if (!unmetRequirements.IsEmpty)
        {
            return Reject(
                AnalysisRequestRejectionReason.UnsatisfiedUniverse,
                universeRequirements: unmetRequirements);
        }

        ImmutableArray<AnalysisStructuralPrerequisiteDescriptor>
            missingPrerequisites =
        [
            .. analysis.StructuralPrerequisites.Where(
                prerequisite => !environment.IsAvailable(prerequisite)),
        ];
        if (!missingPrerequisites.IsEmpty)
        {
            return Reject(
                AnalysisRequestRejectionReason.MissingStructuralPrerequisite,
                prerequisites: missingPrerequisites);
        }

        AnalysisProjectionSupport? supportedProjection = analysis.Projections
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Projection, projection)
                && candidate.Modes.Contains(request.Mode));
        if (supportedProjection is null)
            return Reject(AnalysisRequestRejectionReason.UnsupportedProjection);

        InspectionCost cost = environment.CostOf(
            analysis,
            analysis.StructuralPrerequisites);
        return new AnalysisRequestPlanResult.Accepted(
            new AnalysisRequestPlan(
                request,
                analysis,
                reportSurface,
                request.Universe,
                projection,
                requiredUniverseCapabilities,
                cost));
    }

    static AnalysisRequestPlanResult.Rejected Reject(
        AnalysisRequestRejectionReason reason,
        IEnumerable<AnalysisTargetRoleDescriptor>? targetRoles = null,
        IEnumerable<AnalysisUniverseRequirementDescriptor>? universeRequirements = null,
        IEnumerable<AnalysisStructuralPrerequisiteDescriptor>? prerequisites = null) =>
        new(
            new AnalysisRequestRejection(
                reason,
                targetRoles,
                universeRequirements,
                prerequisites));
}
