using System.Collections.Immutable;
using System.Diagnostics;

namespace DotnetInspector.Queries;

/// <summary>The domain reported by an analysis request.</summary>
public enum AnalysisReportSurfaceKind
{
    Member,
    Type,
    Library,
    Root,
    Workspace,
}

/// <summary>Whether an analysis is anchored or enumerates a finite universe.</summary>
public enum AnalysisQuestionMode
{
    Targeted,
    Census,
}

/// <summary>The function of one target role in one analysis mode.</summary>
public enum AnalysisTargetFunction
{
    PrivilegedAnchor,
    ReportDomain,
}

/// <summary>Whether an owner-issued universe description has a finite boundary.</summary>
public enum AnalysisUniverseBoundKind
{
    Finite,
    Unbounded,
}

/// <summary>A violated Targeted or Census invariant.</summary>
public enum AnalysisModeViolation
{
    TargetedMissingPrivilegedAnchor,
    CensusContainsPrivilegedAnchor,
}

/// <summary>A violated report-surface cardinality invariant.</summary>
public enum AnalysisReportSurfaceCardinalityViolation
{
    MissingTarget,
    WorkspaceRequiresSingleTarget,
}

/// <summary>
/// Reference-identity base for owner-issued analysis request declarations.
/// </summary>
public abstract class AnalysisRequestDefinition
{
    protected AnalysisRequestDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>A stable diagnostic name that is never used for identity or lookup.</summary>
    public string Name { get; }
}

/// <summary>Planner-readable base for an owner-issued typed report-target role.</summary>
public abstract class AnalysisTargetRoleDescriptor : AnalysisRequestDefinition
{
    protected AnalysisTargetRoleDescriptor(string name)
        : base(name)
    {
    }
}

/// <summary>An owner-issued report-target role bound to one identity currency.</summary>
public sealed class AnalysisTargetRoleDescriptor<TIdentity>(string name)
    : AnalysisTargetRoleDescriptor(name);

/// <summary>An owner-issued evidence capability supplied by a universe provider.</summary>
public sealed class AnalysisUniverseCapabilityDescriptor(string name)
    : AnalysisRequestDefinition(name);

/// <summary>
/// One descriptor-issued requirement for an evidence-universe capability.
/// Owners may specialize it with typed concept or producer-policy scope.
/// </summary>
public class AnalysisUniverseRequirementDescriptor : AnalysisRequestDefinition
{
    public AnalysisUniverseRequirementDescriptor(
        string name,
        AnalysisUniverseCapabilityDescriptor capability)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(capability);
        Capability = capability;
    }

    public AnalysisUniverseCapabilityDescriptor Capability { get; }
}

/// <summary>A producer or query prerequisite checked without executing it.</summary>
public sealed class AnalysisStructuralPrerequisiteDescriptor(string name)
    : AnalysisRequestDefinition(name);

/// <summary>A requirement retained for later host authorization or cost enforcement.</summary>
public sealed class AnalysisPreflightRequirementDescriptor(string name)
    : AnalysisRequestDefinition(name);

/// <summary>An owner-issued result projection supported by an analysis.</summary>
public sealed class AnalysisProjectionDescriptor(string name) : AnalysisRequestDefinition(name);

/// <summary>
/// Planner-readable base for one typed role accepted by an analysis.
/// </summary>
public abstract class AnalysisTargetRoleDeclaration
{
    protected AnalysisTargetRoleDeclaration(
        AnalysisReportSurfaceKind surfaceKind,
        AnalysisQuestionMode mode,
        AnalysisTargetFunction function)
    {
        AnalysisRequestGuard.EnumDefined(surfaceKind, nameof(surfaceKind));
        AnalysisRequestGuard.EnumDefined(mode, nameof(mode));
        AnalysisRequestGuard.EnumDefined(function, nameof(function));
        SurfaceKind = surfaceKind;
        Mode = mode;
        Function = function;
    }

    public AnalysisReportSurfaceKind SurfaceKind { get; }

    public AnalysisQuestionMode Mode { get; }

    public abstract AnalysisTargetRoleDescriptor Role { get; }

    public AnalysisTargetFunction Function { get; }
}

/// <summary>
/// One role and identity currency accepted for a report surface and question mode.
/// </summary>
public sealed class AnalysisTargetRoleDeclaration<TIdentity>
    : AnalysisTargetRoleDeclaration
{
    public AnalysisTargetRoleDeclaration(
        AnalysisReportSurfaceKind surfaceKind,
        AnalysisQuestionMode mode,
        AnalysisTargetRoleDescriptor<TIdentity> role,
        AnalysisTargetFunction function)
        : base(surfaceKind, mode, function)
    {
        ArgumentNullException.ThrowIfNull(role);
        Role = role;
    }

    public override AnalysisTargetRoleDescriptor<TIdentity> Role { get; }
}

/// <summary>
/// Planner-readable base for one producer-owned analysis declaration.
/// Concrete descriptor instances and subtypes remain owner-issued identities.
/// </summary>
public abstract class AnalysisDescriptor : AnalysisRequestDefinition
{
    protected AnalysisDescriptor(
        string name,
        string revision,
        IReadOnlyList<AnalysisQuestionMode> supportedModes,
        IReadOnlyList<AnalysisTargetRoleDeclaration> targetRoles,
        IReadOnlyList<AnalysisProjectionDescriptor> supportedProjections,
        IReadOnlyList<AnalysisUniverseRequirementDescriptor>? universeRequirements = null,
        IReadOnlyList<AnalysisStructuralPrerequisiteDescriptor>? structuralPrerequisites = null,
        IReadOnlyList<AnalysisPreflightRequirementDescriptor>? preflightRequirements = null)
        : base(name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        Revision = revision;
        SupportedModes = AnalysisRequestGuard.FreezeEnums(
            supportedModes,
            nameof(supportedModes),
            requireNonEmpty: true);
        TargetRoles = AnalysisRequestGuard.FreezeDefinitions(
            targetRoles,
            nameof(targetRoles),
            requireNonEmpty: true);
        SupportedProjections = AnalysisRequestGuard.FreezeDefinitions(
            supportedProjections,
            nameof(supportedProjections),
            requireNonEmpty: true);
        UniverseRequirements = AnalysisRequestGuard.FreezeDefinitions(
            universeRequirements,
            nameof(universeRequirements));
        StructuralPrerequisites = AnalysisRequestGuard.FreezeDefinitions(
            structuralPrerequisites,
            nameof(structuralPrerequisites));
        PreflightRequirements = AnalysisRequestGuard.FreezeDefinitions(
            preflightRequirements,
            nameof(preflightRequirements));

        var declarations = new HashSet<(
            AnalysisReportSurfaceKind SurfaceKind,
            AnalysisQuestionMode Mode,
            AnalysisTargetRoleDescriptor Role)>();
        foreach (AnalysisTargetRoleDeclaration declaration in TargetRoles)
        {
            if (!SupportedModes.Contains(declaration.Mode))
            {
                throw new ArgumentException(
                    $"Target role '{declaration.Role.Name}' uses unsupported mode '{declaration.Mode}'.",
                    nameof(targetRoles));
            }

            if (!declarations.Add((
                declaration.SurfaceKind,
                declaration.Mode,
                declaration.Role)))
            {
                throw new ArgumentException(
                    $"Target role '{declaration.Role.Name}' is declared more than once for "
                    + $"surface '{declaration.SurfaceKind}' and mode '{declaration.Mode}'.",
                    nameof(targetRoles));
            }
        }

        foreach (AnalysisQuestionMode mode in SupportedModes)
        {
            if (!TargetRoles.Any(declaration => declaration.Mode == mode))
            {
                throw new ArgumentException(
                    $"Supported mode '{mode}' has no target-role declarations.",
                    nameof(targetRoles));
            }
        }
    }

    public string Revision { get; }

    public ImmutableArray<AnalysisQuestionMode> SupportedModes { get; }

    public ImmutableArray<AnalysisTargetRoleDeclaration> TargetRoles { get; }

    public ImmutableArray<AnalysisProjectionDescriptor> SupportedProjections { get; }

    public ImmutableArray<AnalysisUniverseRequirementDescriptor> UniverseRequirements { get; }

    public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> StructuralPrerequisites { get; }

    public ImmutableArray<AnalysisPreflightRequirementDescriptor> PreflightRequirements { get; }
}

/// <summary>One typed report identity bound to a role accepting that currency.</summary>
public sealed record AnalysisReportTarget<TIdentity>
{
    public AnalysisReportTarget(
        AnalysisTargetRoleDescriptor<TIdentity> role,
        TIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(identity);
        Role = role;
        Identity = identity;
    }

    public AnalysisTargetRoleDescriptor<TIdentity> Role { get; }

    public TIdentity Identity { get; }
}

/// <summary>A typed report surface whose identities remain owner-issued.</summary>
public sealed class AnalysisReportSurface<TIdentity>
{
    public AnalysisReportSurface(
        AnalysisReportSurfaceKind kind,
        IReadOnlyList<AnalysisReportTarget<TIdentity>> targets)
    {
        AnalysisRequestGuard.EnumDefined(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(targets);
        Targets = [.. targets];
        if (Targets.Any(static target => target is null))
            throw new ArgumentException("Null report targets are not permitted.", nameof(targets));
        Kind = kind;
    }

    public AnalysisReportSurfaceKind Kind { get; }

    public ImmutableArray<AnalysisReportTarget<TIdentity>> Targets { get; }
}

/// <summary>
/// Planner-readable universe declarations. Concrete owners retain all other typed state.
/// </summary>
public abstract class AnalysisUniverseDescription
{
    protected AnalysisUniverseDescription(
        AnalysisUniverseBoundKind boundKind,
        IReadOnlyList<AnalysisUniverseCapabilityDescriptor> capabilities)
    {
        AnalysisRequestGuard.EnumDefined(boundKind, nameof(boundKind));
        BoundKind = boundKind;
        Capabilities = AnalysisRequestGuard.FreezeDefinitions(
            capabilities,
            nameof(capabilities));
    }

    public AnalysisUniverseBoundKind BoundKind { get; }

    public ImmutableArray<AnalysisUniverseCapabilityDescriptor> Capabilities { get; }
}

/// <summary>One five-field analysis request retaining exact owner types.</summary>
public sealed class AnalysisRequest<TAnalysis, TTargetIdentity, TUniverse>
    where TAnalysis : AnalysisDescriptor
    where TUniverse : AnalysisUniverseDescription
{
    public AnalysisRequest(
        TAnalysis analysis,
        AnalysisReportSurface<TTargetIdentity> reportSurface,
        TUniverse? universe,
        AnalysisQuestionMode mode,
        AnalysisProjectionDescriptor projection)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(reportSurface);
        AnalysisRequestGuard.EnumDefined(mode, nameof(mode));
        ArgumentNullException.ThrowIfNull(projection);
        Analysis = analysis;
        ReportSurface = reportSurface;
        Universe = universe;
        Mode = mode;
        Projection = projection;
    }

    public TAnalysis Analysis { get; }

    public AnalysisReportSurface<TTargetIdentity> ReportSurface { get; }

    public TUniverse? Universe { get; }

    public AnalysisQuestionMode Mode { get; }

    public AnalysisProjectionDescriptor Projection { get; }
}

/// <summary>A request plan retaining exact owner-issued inputs and declarations.</summary>
public sealed class AnalysisValidatedPlan<TAnalysis, TTargetIdentity, TUniverse>
    where TAnalysis : AnalysisDescriptor
    where TUniverse : AnalysisUniverseDescription
{
    internal AnalysisValidatedPlan(
        TAnalysis analysis,
        AnalysisReportSurface<TTargetIdentity> reportSurface,
        TUniverse universe,
        AnalysisQuestionMode mode,
        AnalysisProjectionDescriptor projection)
    {
        Analysis = analysis;
        ReportSurface = reportSurface;
        Universe = universe;
        Mode = mode;
        Projection = projection;
        UniverseRequirements = analysis.UniverseRequirements;
        StructuralPrerequisites = analysis.StructuralPrerequisites;
        PreflightRequirements = analysis.PreflightRequirements;
    }

    public TAnalysis Analysis { get; }

    public AnalysisReportSurface<TTargetIdentity> ReportSurface { get; }

    public TUniverse Universe { get; }

    public AnalysisQuestionMode Mode { get; }

    public AnalysisProjectionDescriptor Projection { get; }

    public ImmutableArray<AnalysisUniverseRequirementDescriptor> UniverseRequirements { get; }

    public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> StructuralPrerequisites { get; }

    public ImmutableArray<AnalysisPreflightRequirementDescriptor> PreflightRequirements { get; }
}

/// <summary>A typed pre-execution request rejection.</summary>
public abstract record AnalysisRequestRejection
{
    private AnalysisRequestRejection()
    {
    }

    public abstract string Guidance { get; }

    public sealed record UnconfiguredAnalysis : AnalysisRequestRejection
    {
        internal UnconfiguredAnalysis(AnalysisDescriptor analysis)
        {
            ArgumentNullException.ThrowIfNull(analysis);
            Analysis = analysis;
        }

        public AnalysisDescriptor Analysis { get; }

        public override string Guidance =>
            $"Analysis '{Analysis.Name}' is not configured in this capability catalog.";
    }

    public sealed record InvalidReportSurface : AnalysisRequestRejection
    {
        internal InvalidReportSurface(
            AnalysisReportSurfaceKind surfaceKind,
            AnalysisReportSurfaceCardinalityViolation violation)
        {
            AnalysisRequestGuard.EnumDefined(surfaceKind, nameof(surfaceKind));
            AnalysisRequestGuard.EnumDefined(violation, nameof(violation));
            SurfaceKind = surfaceKind;
            Violation = violation;
        }

        public AnalysisReportSurfaceKind SurfaceKind { get; }

        public AnalysisReportSurfaceCardinalityViolation Violation { get; }

        public override string Guidance => Violation switch
        {
            AnalysisReportSurfaceCardinalityViolation.MissingTarget =>
                $"Report surface '{SurfaceKind}' requires at least one target.",
            AnalysisReportSurfaceCardinalityViolation.WorkspaceRequiresSingleTarget =>
                "A Workspace report surface requires exactly one workspace or operation target.",
            _ => throw new UnreachableException(),
        };
    }

    public sealed record UnsupportedMode : AnalysisRequestRejection
    {
        internal UnsupportedMode(AnalysisQuestionMode mode)
        {
            AnalysisRequestGuard.EnumDefined(mode, nameof(mode));
            Mode = mode;
        }

        public AnalysisQuestionMode Mode { get; }

        public override string Guidance => $"Analysis does not support mode '{Mode}'.";
    }

    public sealed record UnsupportedSurface : AnalysisRequestRejection
    {
        internal UnsupportedSurface(AnalysisReportSurfaceKind surfaceKind)
        {
            AnalysisRequestGuard.EnumDefined(surfaceKind, nameof(surfaceKind));
            SurfaceKind = surfaceKind;
        }

        public AnalysisReportSurfaceKind SurfaceKind { get; }

        public override string Guidance =>
            $"Analysis does not support report surface '{SurfaceKind}'.";
    }

    public sealed record UnsupportedTargetRole : AnalysisRequestRejection
    {
        internal UnsupportedTargetRole(AnalysisTargetRoleDescriptor role)
        {
            ArgumentNullException.ThrowIfNull(role);
            Role = role;
        }

        public AnalysisTargetRoleDescriptor Role { get; }

        public override string Guidance =>
            $"Analysis does not support target role '{Role.Name}'.";
    }

    public sealed record InvalidMode : AnalysisRequestRejection
    {
        internal InvalidMode(
            AnalysisQuestionMode mode,
            AnalysisModeViolation violation)
        {
            AnalysisRequestGuard.EnumDefined(mode, nameof(mode));
            AnalysisRequestGuard.EnumDefined(violation, nameof(violation));
            bool compatible = (mode, violation) switch
            {
                (AnalysisQuestionMode.Targeted,
                    AnalysisModeViolation.TargetedMissingPrivilegedAnchor) => true,
                (AnalysisQuestionMode.Census,
                    AnalysisModeViolation.CensusContainsPrivilegedAnchor) => true,
                _ => false,
            };
            if (!compatible)
            {
                throw new ArgumentException(
                    $"Mode '{mode}' is incompatible with violation '{violation}'.",
                    nameof(violation));
            }

            Mode = mode;
            Violation = violation;
        }

        public AnalysisQuestionMode Mode { get; }

        public AnalysisModeViolation Violation { get; }

        public override string Guidance => Violation switch
        {
            AnalysisModeViolation.TargetedMissingPrivilegedAnchor =>
                "Targeted mode requires at least one privileged anchor.",
            AnalysisModeViolation.CensusContainsPrivilegedAnchor =>
                "Census mode cannot contain a privileged anchor.",
            _ => throw new UnreachableException(),
        };
    }

    public sealed record MissingUniverse : AnalysisRequestRejection
    {
        internal MissingUniverse()
        {
        }

        public override string Guidance => "Supply an owner-issued finite universe.";
    }

    public sealed record UnboundedUniverse : AnalysisRequestRejection
    {
        internal UnboundedUniverse()
        {
        }

        public override string Guidance => "Supply a universe with an explicit finite bound.";
    }

    public sealed record UnsatisfiedUniverse : AnalysisRequestRejection
    {
        internal UnsatisfiedUniverse(
            ImmutableArray<AnalysisUniverseRequirementDescriptor> requirements)
        {
            if (requirements.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "At least one unsatisfied requirement is required.",
                    nameof(requirements));
            }

            Requirements = requirements;
        }

        public ImmutableArray<AnalysisUniverseRequirementDescriptor> Requirements { get; }

        public override string Guidance =>
            $"Universe does not satisfy: {string.Join(", ", Requirements.Select(r => r.Name))}.";
    }

    public sealed record MissingStructuralPrerequisites : AnalysisRequestRejection
    {
        internal MissingStructuralPrerequisites(
            ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> prerequisites)
        {
            if (prerequisites.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "At least one missing prerequisite is required.",
                    nameof(prerequisites));
            }

            Prerequisites = prerequisites;
        }

        public ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> Prerequisites { get; }

        public override string Guidance =>
            $"Missing structural prerequisites: {string.Join(", ", Prerequisites.Select(p => p.Name))}.";
    }

    public sealed record UnsupportedProjection : AnalysisRequestRejection
    {
        internal UnsupportedProjection(AnalysisProjectionDescriptor projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            Projection = projection;
        }

        public AnalysisProjectionDescriptor Projection { get; }

        public override string Guidance =>
            $"Analysis does not support projection '{Projection.Name}'.";
    }
}

/// <summary>A validated plan or one typed pre-execution rejection.</summary>
public abstract record AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse>
    where TAnalysis : AnalysisDescriptor
    where TUniverse : AnalysisUniverseDescription
{
    private AnalysisRequestPlanningResult()
    {
    }

    public sealed record Validated
        : AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse>
    {
        internal Validated(AnalysisValidatedPlan<TAnalysis, TTargetIdentity, TUniverse> plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            Plan = plan;
        }

        public AnalysisValidatedPlan<TAnalysis, TTargetIdentity, TUniverse> Plan { get; }
    }

    public sealed record Rejected
        : AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse>
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
/// Immutable structural capability catalog and pre-execution request planner.
/// </summary>
public sealed class AnalysisRequestPlanner
{
    private readonly HashSet<AnalysisDescriptor> _descriptorSet;
    private readonly HashSet<AnalysisStructuralPrerequisiteDescriptor>
        _availableStructuralPrerequisites;

    public AnalysisRequestPlanner(
        IReadOnlyList<AnalysisDescriptor> descriptors,
        IReadOnlyList<AnalysisStructuralPrerequisiteDescriptor> availableStructuralPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(availableStructuralPrerequisites);
        Descriptors = AnalysisRequestGuard.FreezeDefinitions(
            descriptors,
            nameof(descriptors),
            requireNonEmpty: true);
        ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> prerequisites =
            AnalysisRequestGuard.FreezeDefinitions(
                availableStructuralPrerequisites,
                nameof(availableStructuralPrerequisites));
        _descriptorSet = new(Descriptors, ReferenceEqualityComparer.Instance);
        _availableStructuralPrerequisites =
            new(prerequisites, ReferenceEqualityComparer.Instance);
    }

    /// <summary>Configured structural capabilities in declaration order.</summary>
    public ImmutableArray<AnalysisDescriptor> Descriptors { get; }

    /// <summary>Validates one complete request without executing a producer or query.</summary>
    public AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse> Plan<
        TAnalysis,
        TTargetIdentity,
        TUniverse>(
        AnalysisRequest<TAnalysis, TTargetIdentity, TUniverse> request)
        where TAnalysis : AnalysisDescriptor
        where TUniverse : AnalysisUniverseDescription
    {
        ArgumentNullException.ThrowIfNull(request);

        AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse> Reject(
            AnalysisRequestRejection rejection)
            => new AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse>
                .Rejected(rejection);

        TAnalysis analysis = request.Analysis;
        if (!_descriptorSet.Contains(analysis))
            return Reject(new AnalysisRequestRejection.UnconfiguredAnalysis(analysis));

        if (request.ReportSurface.Targets.IsEmpty)
        {
            return Reject(
                new AnalysisRequestRejection.InvalidReportSurface(
                    request.ReportSurface.Kind,
                    AnalysisReportSurfaceCardinalityViolation.MissingTarget));
        }

        if (request.ReportSurface.Kind == AnalysisReportSurfaceKind.Workspace
            && request.ReportSurface.Targets.Length != 1)
        {
            return Reject(
                new AnalysisRequestRejection.InvalidReportSurface(
                    request.ReportSurface.Kind,
                    AnalysisReportSurfaceCardinalityViolation.WorkspaceRequiresSingleTarget));
        }

        if (!analysis.SupportedModes.Contains(request.Mode))
            return Reject(new AnalysisRequestRejection.UnsupportedMode(request.Mode));

        if (!analysis.TargetRoles.Any(
            declaration => declaration.SurfaceKind == request.ReportSurface.Kind))
        {
            return Reject(
                new AnalysisRequestRejection.UnsupportedSurface(request.ReportSurface.Kind));
        }

        var functions = ImmutableArray.CreateBuilder<AnalysisTargetFunction>(
            request.ReportSurface.Targets.Length);
        foreach (AnalysisReportTarget<TTargetIdentity> target in request.ReportSurface.Targets)
        {
            AnalysisTargetRoleDeclaration? declaration = analysis.TargetRoles.FirstOrDefault(
                candidate =>
                    candidate.SurfaceKind == request.ReportSurface.Kind
                    && candidate.Mode == request.Mode
                    && ReferenceEquals(candidate.Role, target.Role));
            if (declaration is null)
                return Reject(new AnalysisRequestRejection.UnsupportedTargetRole(target.Role));
            functions.Add(declaration.Function);
        }

        if (request.Mode == AnalysisQuestionMode.Targeted
            && !functions.Contains(AnalysisTargetFunction.PrivilegedAnchor))
        {
            return Reject(
                new AnalysisRequestRejection.InvalidMode(
                    request.Mode,
                    AnalysisModeViolation.TargetedMissingPrivilegedAnchor));
        }

        if (request.Mode == AnalysisQuestionMode.Census
            && functions.Contains(AnalysisTargetFunction.PrivilegedAnchor))
        {
            return Reject(
                new AnalysisRequestRejection.InvalidMode(
                    request.Mode,
                    AnalysisModeViolation.CensusContainsPrivilegedAnchor));
        }

        if (request.Universe is null)
            return Reject(new AnalysisRequestRejection.MissingUniverse());
        if (request.Universe.BoundKind != AnalysisUniverseBoundKind.Finite)
            return Reject(new AnalysisRequestRejection.UnboundedUniverse());

        var capabilities = new HashSet<AnalysisUniverseCapabilityDescriptor>(
            request.Universe.Capabilities,
            ReferenceEqualityComparer.Instance);
        ImmutableArray<AnalysisUniverseRequirementDescriptor> unsatisfied =
        [
            .. analysis.UniverseRequirements.Where(
                requirement => !capabilities.Contains(requirement.Capability)),
        ];
        if (!unsatisfied.IsEmpty)
            return Reject(new AnalysisRequestRejection.UnsatisfiedUniverse(unsatisfied));

        ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> missingPrerequisites =
        [
            .. analysis.StructuralPrerequisites.Where(
                prerequisite => !_availableStructuralPrerequisites.Contains(prerequisite)),
        ];
        if (!missingPrerequisites.IsEmpty)
        {
            return Reject(
                new AnalysisRequestRejection.MissingStructuralPrerequisites(
                    missingPrerequisites));
        }

        if (!analysis.SupportedProjections.Contains(
            request.Projection,
            ReferenceEqualityComparer.Instance))
        {
            return Reject(
                new AnalysisRequestRejection.UnsupportedProjection(request.Projection));
        }

        return new AnalysisRequestPlanningResult<TAnalysis, TTargetIdentity, TUniverse>
            .Validated(
                new AnalysisValidatedPlan<TAnalysis, TTargetIdentity, TUniverse>(
                    analysis,
                    request.ReportSurface,
                    request.Universe,
                    request.Mode,
                    request.Projection));
    }
}

internal static class AnalysisRequestGuard
{
    public static void EnumDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Undefined enum value.");
    }

    public static ImmutableArray<TEnum> FreezeEnums<TEnum>(
        IReadOnlyList<TEnum> values,
        string parameterName,
        bool requireNonEmpty)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        ImmutableArray<TEnum> frozen = [.. values];
        if (requireNonEmpty && frozen.IsEmpty)
            throw new ArgumentException("At least one value is required.", parameterName);
        foreach (TEnum value in frozen)
            EnumDefined(value, parameterName);
        if (frozen.Length != frozen.Distinct().Count())
            throw new ArgumentException("Duplicate values are not permitted.", parameterName);
        return frozen;
    }

    public static ImmutableArray<T> FreezeDefinitions<T>(
        IReadOnlyList<T>? values,
        string parameterName,
        bool requireNonEmpty = false)
        where T : class
    {
        if (values is null)
        {
            if (requireNonEmpty)
                throw new ArgumentNullException(parameterName);
            return [];
        }

        ImmutableArray<T> frozen = [.. values];
        if (requireNonEmpty && frozen.IsEmpty)
            throw new ArgumentException("At least one declaration is required.", parameterName);
        if (frozen.Any(static value => value is null))
            throw new ArgumentException("Null declarations are not permitted.", parameterName);
        if (frozen.Distinct(ReferenceEqualityComparer.Instance).Count() != frozen.Length)
            throw new ArgumentException("Duplicate declarations are not permitted.", parameterName);
        return frozen;
    }
}
