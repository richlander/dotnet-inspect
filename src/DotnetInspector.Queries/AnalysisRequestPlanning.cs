using System.Collections.Immutable;

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

/// <summary>An owner-issued report-target role.</summary>
public sealed class AnalysisTargetRoleDescriptor(string name) : AnalysisRequestDefinition(name);

/// <summary>An owner-issued evidence capability supplied by a universe provider.</summary>
public sealed class AnalysisUniverseCapabilityDescriptor(string name)
    : AnalysisRequestDefinition(name);

/// <summary>
/// One descriptor-issued requirement for an evidence-universe capability.
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
/// One role accepted by an analysis for a report surface and question mode.
/// </summary>
public sealed class AnalysisTargetRoleDeclaration
{
    public AnalysisTargetRoleDeclaration(
        AnalysisReportSurfaceKind surfaceKind,
        AnalysisQuestionMode mode,
        AnalysisTargetRoleDescriptor role,
        AnalysisTargetFunction function)
    {
        ArgumentNullException.ThrowIfNull(role);
        SurfaceKind = surfaceKind;
        Mode = mode;
        Role = role;
        Function = function;
    }

    public AnalysisReportSurfaceKind SurfaceKind { get; }

    public AnalysisQuestionMode Mode { get; }

    public AnalysisTargetRoleDescriptor Role { get; }

    public AnalysisTargetFunction Function { get; }
}

/// <summary>
/// One producer-owned analysis declaration. Definition instances are identities.
/// </summary>
public class AnalysisDescriptor : AnalysisRequestDefinition
{
    public AnalysisDescriptor(
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
        SupportedModes = FreezeValues(supportedModes, nameof(supportedModes), requireNonEmpty: true);
        TargetRoles = FreezeDefinitions(targetRoles, nameof(targetRoles), requireNonEmpty: true);
        SupportedProjections = FreezeDefinitions(
            supportedProjections,
            nameof(supportedProjections),
            requireNonEmpty: true);
        UniverseRequirements = FreezeDefinitions(
            universeRequirements,
            nameof(universeRequirements));
        StructuralPrerequisites = FreezeDefinitions(
            structuralPrerequisites,
            nameof(structuralPrerequisites));
        PreflightRequirements = FreezeDefinitions(
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

    private static ImmutableArray<T> FreezeValues<T>(
        IReadOnlyList<T> values,
        string parameterName,
        bool requireNonEmpty)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        ImmutableArray<T> frozen = [.. values];
        if (requireNonEmpty && frozen.IsEmpty)
            throw new ArgumentException("At least one value is required.", parameterName);
        if (frozen.Length != frozen.Distinct().Count())
            throw new ArgumentException("Duplicate values are not permitted.", parameterName);
        return frozen;
    }

    private static ImmutableArray<T> FreezeDefinitions<T>(
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

/// <summary>One typed report identity bound to an owner-issued role.</summary>
public sealed record AnalysisReportTarget<TIdentity>
{
    public AnalysisReportTarget(AnalysisTargetRoleDescriptor role, TIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(identity);
        Role = role;
        Identity = identity;
    }

    public AnalysisTargetRoleDescriptor Role { get; }

    public TIdentity Identity { get; }
}

/// <summary>A typed report surface whose identities remain owner-issued.</summary>
public sealed class AnalysisReportSurface<TIdentity>
{
    public AnalysisReportSurface(
        AnalysisReportSurfaceKind kind,
        IReadOnlyList<AnalysisReportTarget<TIdentity>> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Targets = [.. targets];
        if (Targets.IsEmpty)
            throw new ArgumentException("A report surface requires at least one target.", nameof(targets));
        if (Targets.Any(static target => target is null))
            throw new ArgumentException("Null report targets are not permitted.", nameof(targets));
        Kind = kind;
    }

    public AnalysisReportSurfaceKind Kind { get; }

    public ImmutableArray<AnalysisReportTarget<TIdentity>> Targets { get; }
}

/// <summary>
/// A finite or unbounded owner-issued universe description with opaque owner state.
/// </summary>
public sealed class AnalysisUniverseDescription<TBoundary, TProviderState>
{
    public AnalysisUniverseDescription(
        AnalysisUniverseBoundKind boundKind,
        TBoundary requestedBoundary,
        TBoundary realizedBoundary,
        IReadOnlyList<AnalysisUniverseCapabilityDescriptor> capabilities,
        TProviderState providerState)
    {
        ArgumentNullException.ThrowIfNull(requestedBoundary);
        ArgumentNullException.ThrowIfNull(realizedBoundary);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(providerState);

        Capabilities = [.. capabilities];
        if (Capabilities.Any(static capability => capability is null))
            throw new ArgumentException("Null capabilities are not permitted.", nameof(capabilities));
        if (Capabilities.Distinct(ReferenceEqualityComparer.Instance).Count() != Capabilities.Length)
            throw new ArgumentException("Duplicate capabilities are not permitted.", nameof(capabilities));

        BoundKind = boundKind;
        RequestedBoundary = requestedBoundary;
        RealizedBoundary = realizedBoundary;
        ProviderState = providerState;
    }

    public AnalysisUniverseBoundKind BoundKind { get; }

    public TBoundary RequestedBoundary { get; }

    public TBoundary RealizedBoundary { get; }

    public ImmutableArray<AnalysisUniverseCapabilityDescriptor> Capabilities { get; }

    public TProviderState ProviderState { get; }
}

/// <summary>One five-field analysis request.</summary>
public sealed class AnalysisRequest<TTargetIdentity, TUniverseBoundary, TUniverseState>
{
    public AnalysisRequest(
        AnalysisDescriptor analysis,
        AnalysisReportSurface<TTargetIdentity> reportSurface,
        AnalysisUniverseDescription<TUniverseBoundary, TUniverseState>? universe,
        AnalysisQuestionMode mode,
        AnalysisProjectionDescriptor projection)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(reportSurface);
        ArgumentNullException.ThrowIfNull(projection);
        Analysis = analysis;
        ReportSurface = reportSurface;
        Universe = universe;
        Mode = mode;
        Projection = projection;
    }

    public AnalysisDescriptor Analysis { get; }

    public AnalysisReportSurface<TTargetIdentity> ReportSurface { get; }

    public AnalysisUniverseDescription<TUniverseBoundary, TUniverseState>? Universe { get; }

    public AnalysisQuestionMode Mode { get; }

    public AnalysisProjectionDescriptor Projection { get; }
}

/// <summary>A request plan retaining exact owner-issued inputs and declarations.</summary>
public sealed class AnalysisValidatedPlan<TTargetIdentity, TUniverseBoundary, TUniverseState>
{
    internal AnalysisValidatedPlan(
        AnalysisDescriptor analysis,
        AnalysisReportSurface<TTargetIdentity> reportSurface,
        AnalysisUniverseDescription<TUniverseBoundary, TUniverseState> universe,
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

    public AnalysisDescriptor Analysis { get; }

    public AnalysisReportSurface<TTargetIdentity> ReportSurface { get; }

    public AnalysisUniverseDescription<TUniverseBoundary, TUniverseState> Universe { get; }

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

    public sealed record UnsupportedMode(AnalysisQuestionMode Mode) : AnalysisRequestRejection
    {
        public override string Guidance => $"Analysis does not support mode '{Mode}'.";
    }

    public sealed record UnsupportedSurface(AnalysisReportSurfaceKind SurfaceKind)
        : AnalysisRequestRejection
    {
        public override string Guidance =>
            $"Analysis does not support report surface '{SurfaceKind}'.";
    }

    public sealed record UnsupportedTargetRole(AnalysisTargetRoleDescriptor Role)
        : AnalysisRequestRejection
    {
        public override string Guidance => $"Analysis does not support target role '{Role.Name}'.";
    }

    public sealed record InvalidMode(
        AnalysisQuestionMode Mode,
        AnalysisModeViolation Violation) : AnalysisRequestRejection
    {
        public override string Guidance => Violation switch
        {
            AnalysisModeViolation.TargetedMissingPrivilegedAnchor =>
                "Targeted mode requires at least one privileged anchor.",
            AnalysisModeViolation.CensusContainsPrivilegedAnchor =>
                "Census mode cannot contain a privileged anchor.",
            _ => throw new InvalidOperationException($"Unknown mode violation '{Violation}'."),
        };
    }

    public sealed record MissingUniverse : AnalysisRequestRejection
    {
        public override string Guidance => "Supply an owner-issued finite universe.";
    }

    public sealed record UnboundedUniverse : AnalysisRequestRejection
    {
        public override string Guidance => "Supply a universe with an explicit finite bound.";
    }

    public sealed record UnsatisfiedUniverse(
        ImmutableArray<AnalysisUniverseRequirementDescriptor> Requirements)
        : AnalysisRequestRejection
    {
        public override string Guidance =>
            $"Universe does not satisfy: {string.Join(", ", Requirements.Select(r => r.Name))}.";
    }

    public sealed record MissingStructuralPrerequisites(
        ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> Prerequisites)
        : AnalysisRequestRejection
    {
        public override string Guidance =>
            $"Missing structural prerequisites: {string.Join(", ", Prerequisites.Select(p => p.Name))}.";
    }

    public sealed record UnsupportedProjection(AnalysisProjectionDescriptor Projection)
        : AnalysisRequestRejection
    {
        public override string Guidance =>
            $"Analysis does not support projection '{Projection.Name}'.";
    }
}

/// <summary>A validated plan or one typed pre-execution rejection.</summary>
public abstract record AnalysisRequestPlanningResult<
    TTargetIdentity,
    TUniverseBoundary,
    TUniverseState>
{
    private AnalysisRequestPlanningResult()
    {
    }

    public sealed record Validated(
        AnalysisValidatedPlan<TTargetIdentity, TUniverseBoundary, TUniverseState> Plan)
        : AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState>;

    public sealed record Rejected(AnalysisRequestRejection Rejection)
        : AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState>;
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
        Descriptors = FreezeReferenceSet(descriptors, nameof(descriptors), requireNonEmpty: true);
        ImmutableArray<AnalysisStructuralPrerequisiteDescriptor> prerequisites =
            FreezeReferenceSet(
                availableStructuralPrerequisites,
                nameof(availableStructuralPrerequisites),
                requireNonEmpty: false);
        _descriptorSet = new(Descriptors, ReferenceEqualityComparer.Instance);
        _availableStructuralPrerequisites =
            new(prerequisites, ReferenceEqualityComparer.Instance);
    }

    /// <summary>Configured structural capabilities in declaration order.</summary>
    public ImmutableArray<AnalysisDescriptor> Descriptors { get; }

    /// <summary>Validates one complete request without executing a producer or query.</summary>
    public AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState> Plan<
        TTargetIdentity,
        TUniverseBoundary,
        TUniverseState>(
        AnalysisRequest<TTargetIdentity, TUniverseBoundary, TUniverseState> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_descriptorSet.Contains(request.Analysis))
        {
            throw new ArgumentException(
                "The analysis descriptor is not part of this structural capability catalog.",
                nameof(request));
        }

        AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState> Reject(
            AnalysisRequestRejection rejection)
            => new AnalysisRequestPlanningResult<
                TTargetIdentity,
                TUniverseBoundary,
                TUniverseState>.Rejected(rejection);

        AnalysisDescriptor analysis = request.Analysis;
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

        if (request.Mode == AnalysisQuestionMode.Census)
        {
            if (functions.Contains(AnalysisTargetFunction.PrivilegedAnchor))
            {
                return Reject(
                    new AnalysisRequestRejection.InvalidMode(
                        request.Mode,
                        AnalysisModeViolation.CensusContainsPrivilegedAnchor));
            }
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

        return new AnalysisRequestPlanningResult<
            TTargetIdentity,
            TUniverseBoundary,
            TUniverseState>.Validated(
                new AnalysisValidatedPlan<
                    TTargetIdentity,
                    TUniverseBoundary,
                    TUniverseState>(
                        analysis,
                        request.ReportSurface,
                        request.Universe,
                        request.Mode,
                        request.Projection));
    }

    private static ImmutableArray<T> FreezeReferenceSet<T>(
        IReadOnlyList<T> values,
        string parameterName,
        bool requireNonEmpty)
        where T : class
    {
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
