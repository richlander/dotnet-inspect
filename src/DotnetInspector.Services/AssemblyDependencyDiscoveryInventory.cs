using System.Collections.Immutable;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Services;

/// <summary>The acquisition evidence for one emitted discovery entry.</summary>
public abstract class AssemblyDependencyAcquisition
{
    private protected AssemblyDependencyAcquisition() { }

    public sealed class Acquired : AssemblyDependencyAcquisition
    {
        internal Acquired(ResolvedAssemblyReference assembly) => Assembly = assembly;
        public ResolvedAssemblyReference Assembly { get; }
    }

    public sealed class Descriptorless : AssemblyDependencyAcquisition
    {
        internal Descriptorless(AssemblyDescriptorSelectionResult.Descriptorless evidence) =>
            Evidence = evidence;
        public AssemblyDescriptorSelectionResult.Descriptorless Evidence { get; }
    }

    public sealed class Rejected : AssemblyDependencyAcquisition
    {
        internal Rejected(AssemblyDescriptorSelectionResult.Rejected evidence) =>
            Evidence = evidence;
        public AssemblyDescriptorSelectionResult.Rejected Evidence { get; }
    }

    public sealed class Unavailable : AssemblyDependencyAcquisition
    {
        internal Unavailable(CandidateOpenFailure failure) => Failure = failure;
        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// One discovery occurrence. Target membership is an input role, not correspondence.
/// </summary>
public sealed class AssemblyDependencyDiscoveryEntry
{
    internal AssemblyDependencyDiscoveryEntry(
        ResolvedAssemblyDependency dependency,
        AssemblyResolutionProvenance provenance,
        bool isTargetInput,
        AssemblyDependencyAcquisition acquisition)
    {
        Dependency = dependency;
        Provenance = provenance;
        IsTargetInput = isTargetInput;
        Acquisition = acquisition;
    }

    public ResolvedAssemblyDependency Dependency { get; }
    public AssemblyResolutionProvenance Provenance { get; }
    public bool IsTargetInput { get; }
    public AssemblyDependencyAcquisition Acquisition { get; }
}

public enum AssemblyDependencyDiscoveryFailureKind
{
    Unreadable,
    InvalidDocument,
}

/// <summary>A failure of an enabled discovery tier, distinct from a row acquisition.</summary>
public sealed class AssemblyDependencyDiscoveryFailure
{
    internal AssemblyDependencyDiscoveryFailure(
        AssemblyDependencyProvenance tier,
        string? location,
        AssemblyDependencyDiscoveryFailureKind kind)
    {
        Tier = tier;
        Location = location is null ? null : new InertString(TextPolicy.Field, location);
        Kind = kind;
    }

    public AssemblyDependencyProvenance Tier { get; }
    public InertString? Location { get; }
    public AssemblyDependencyDiscoveryFailureKind Kind { get; }
}

/// <summary>
/// Explicit discovery evidence under one Services policy version. Neither arm is a binding domain.
/// </summary>
public abstract class AssemblyDependencyDiscoveryResult
{
    private protected AssemblyDependencyDiscoveryResult(AssemblyBindingPolicyVersion version) =>
        Version = version;

    public AssemblyBindingPolicyVersion Version { get; }

    public sealed class Captured : AssemblyDependencyDiscoveryResult
    {
        internal Captured(
            AssemblyBindingPolicyVersion version,
            ImmutableArray<AssemblyDependencyDiscoveryEntry> entries) : base(version) =>
            Entries = entries;
        public ImmutableArray<AssemblyDependencyDiscoveryEntry> Entries { get; }
    }

    public sealed class Failed : AssemblyDependencyDiscoveryResult
    {
        internal Failed(
            AssemblyBindingPolicyVersion version,
            ImmutableArray<AssemblyDependencyDiscoveryEntry> partialEntries,
            ImmutableArray<AssemblyDependencyDiscoveryFailure> discoveryFailures) : base(version)
        {
            PartialEntries = partialEntries;
            DiscoveryFailures = discoveryFailures;
        }

        public ImmutableArray<AssemblyDependencyDiscoveryEntry> PartialEntries { get; }
        public ImmutableArray<AssemblyDependencyDiscoveryFailure> DiscoveryFailures { get; }
    }
}
