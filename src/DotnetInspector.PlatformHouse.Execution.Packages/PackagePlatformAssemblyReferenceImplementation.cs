using System.Diagnostics;
using DotnetInspector.Packages;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Closed result of continuing one package-backed Platform binding into exact
/// implementation realization.
/// </summary>
public abstract class PackagePlatformAssemblyReferenceImplementationResult
{
    private protected PackagePlatformAssemblyReferenceImplementationResult(
        PlatformHouseOutcome<AssemblyBindingDecision> binding,
        PlatformHouseConsumedWork? consumedWork)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        ConsumedWork = consumedWork;
    }

    public PlatformHouseOutcome<AssemblyBindingDecision> Binding { get; }
    public PlatformHouseConsumedWork? ConsumedWork { get; }

    public sealed class NotContinued :
        PackagePlatformAssemblyReferenceImplementationResult
    {
        internal NotContinued(
            PlatformHouseOutcome<AssemblyBindingDecision> binding)
            : base(binding, binding.Receipt.ConsumedWork)
        {
        }
    }

    public sealed class SourceTerminal :
        PackagePlatformAssemblyReferenceImplementationResult
    {
        internal SourceTerminal(
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed binding,
            PlatformHouseRequest implementationRequest,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.NotSucceeded source,
            TimeSpan elapsed)
            : base(
                binding,
                source.SourceWork is null
                    ? null
                    : Add(
                        binding.Receipt.ConsumedWork,
                        InvokedSourceWork(
                            source.SourceWork,
                            elapsed)))
        {
            ImplementationRequest = implementationRequest;
            Source = source;
        }

        public PlatformHouseRequest ImplementationRequest { get; }
        public PackagePlatformHouseResult<
            PackageImplementationRealization>.NotSucceeded Source { get; }
    }

    public sealed class WorkIncomplete :
        PackagePlatformAssemblyReferenceImplementationResult
    {
        internal WorkIncomplete(
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed binding,
            PlatformHouseRequest implementationRequest)
            : base(binding, binding.Receipt.ConsumedWork) =>
            ImplementationRequest = implementationRequest;

        public PlatformHouseRequest ImplementationRequest { get; }
    }

    public sealed class MaterializationTerminal :
        PackagePlatformAssemblyReferenceImplementationResult
    {
        internal MaterializationTerminal(
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed binding,
            PlatformHouseRequest implementationRequest,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded source,
            PackagePlatformLibraryMaterializationResult.Terminal
                materialization)
            : base(
                binding,
                Add(
                    binding.Receipt.ConsumedWork,
                    materialization.TerminalRealization.Outcome
                        .Receipt.ConsumedWork))
        {
            ImplementationRequest = implementationRequest;
            Source = source;
            Materialization = materialization;
        }

        public PlatformHouseRequest ImplementationRequest { get; }
        public PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded Source { get; }
        public PackagePlatformLibraryMaterializationResult.Terminal
            Materialization { get; }
    }

    public sealed class Completed :
        PackagePlatformAssemblyReferenceImplementationResult
    {
        internal Completed(
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed binding,
            PlatformHouseRequest implementationRequest,
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded source,
            PackagePlatformLibraryMaterializationResult.Completed
                implementation)
            : base(
                binding,
                Add(
                    binding.Receipt.ConsumedWork,
                    implementation.Library.Receipt.HouseReceipt
                        .ConsumedWork))
        {
            ImplementationRequest = implementationRequest;
            Source = source;
            Implementation = implementation;
        }

        public PlatformHouseRequest ImplementationRequest { get; }
        public PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded Source { get; }
        public PackagePlatformLibraryMaterializationResult.Completed
            Implementation { get; }
    }

    private static PlatformHouseConsumedWork Add(
        PlatformHouseConsumedWork left,
        PlatformHouseConsumedWork right) =>
        new(
            checked(left.SourceOperations + right.SourceOperations),
            checked(left.TargetCandidates + right.TargetCandidates),
            checked(left.Assemblies + right.Assemblies),
            checked(left.XmlDocuments + right.XmlDocuments),
            checked(left.PortablePdbs + right.PortablePdbs),
            checked(left.SourceDocuments + right.SourceDocuments),
            checked(left.Bytes + right.Bytes),
            checked(left.ForwardingHops + right.ForwardingHops),
            checked(left.TargetComparisons + right.TargetComparisons),
            left.Elapsed + right.Elapsed);

    private static PlatformHouseConsumedWork InvokedSourceWork(
        PlatformHouseConsumedWork observed,
        TimeSpan elapsed) =>
        new(
            checked(observed.SourceOperations + 1),
            observed.TargetCandidates,
            observed.Assemblies,
            observed.XmlDocuments,
            observed.PortablePdbs,
            observed.SourceDocuments,
            observed.Bytes,
            observed.ForwardingHops,
            observed.TargetComparisons,
            elapsed + observed.Elapsed);
}

/// <summary>
/// Continues a canonical package-backed Platform binding through the existing
/// exact ranged implementation path.
/// </summary>
public static class PackagePlatformAssemblyReferenceImplementation
{
    public static async ValueTask<
        PackagePlatformAssemblyReferenceImplementationResult> ContinueAsync(
            PlatformHouseRequest bindingRequest,
            PlatformHouseOutcome<AssemblyBindingDecision> binding,
            PlatformHouseRequestIdentity implementationRequestIdentity,
            PackagePlatformHouseAdapter adapter,
            string runtimeIdentifier,
            Func<
                PlatformHouseRequest,
                PlatformHouseWorkBudget,
                PackageSourceOperationLease> issueOperation)
    {
        ArgumentNullException.ThrowIfNull(bindingRequest);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(
            implementationRequestIdentity);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentNullException.ThrowIfNull(issueOperation);

        if (!ReferenceEquals(
                binding.Receipt.Request,
                bindingRequest.Snapshot))
        {
            throw new ArgumentException(
                "The binding outcome must belong to the supplied request.",
                nameof(binding));
        }

        if (binding
                is not PlatformHouseOutcome<
                    AssemblyBindingDecision>.Completed completed
            || completed.Value
                is not AssemblyBindingDecision.Resolved resolved)
        {
            return new PackagePlatformAssemblyReferenceImplementationResult
                .NotContinued(binding);
        }

        PlatformHouseWorkBudget remaining = Remaining(
            bindingRequest.Work,
            binding.Receipt.ConsumedWork);
        var implementationRequest = new PlatformHouseRequest(
            implementationRequestIdentity,
            bindingRequest.Target,
            bindingRequest.Origin,
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(
                        resolved.Candidate.Identity)),
                PlatformViewDemand.Implementation),
            bindingRequest.Sources,
            remaining,
            bindingRequest.CancellationToken);

        bindingRequest.CancellationToken.ThrowIfCancellationRequested();
        if (!CanIssueImplementation(remaining))
        {
            return new PackagePlatformAssemblyReferenceImplementationResult
                .WorkIncomplete(
                    completed,
                    implementationRequest);
        }

        long started = Stopwatch.GetTimestamp();
        PackagePlatformHouseResult<PackageImplementationRealization>
            implementation =
                await adapter.RealizeImplementationAsync(
                        implementationRequest,
                        runtimeIdentifier,
                        issueOperation(
                            implementationRequest,
                            remaining))
                    .ConfigureAwait(false);
        if (implementation
            is PackagePlatformHouseResult<
                PackageImplementationRealization>.NotSucceeded terminal)
        {
            return new PackagePlatformAssemblyReferenceImplementationResult
                .SourceTerminal(
                    completed,
                    implementationRequest,
                    terminal,
                    Stopwatch.GetElapsedTime(started));
        }

        var source = (PackagePlatformHouseResult<
            PackageImplementationRealization>.Succeeded)implementation;
        PlatformHouseConsumedWork implementationWork = Work(
            source.Value,
            Stopwatch.GetElapsedTime(started));
        PackagePlatformLibraryMaterializationResult materialization =
            await PackagePlatformLibraryMaterializer
                .MaterializeImplementationAsync(
                    implementationRequest,
                    source,
                    implementationWork)
                .ConfigureAwait(false);
        return materialization switch
        {
            PackagePlatformLibraryMaterializationResult.Completed success =>
                new PackagePlatformAssemblyReferenceImplementationResult
                    .Completed(
                        completed,
                        implementationRequest,
                        source,
                        success),
            PackagePlatformLibraryMaterializationResult.Terminal
                materializationTerminal =>
                new PackagePlatformAssemblyReferenceImplementationResult
                    .MaterializationTerminal(
                        completed,
                        implementationRequest,
                        source,
                        materializationTerminal),
            _ => throw new InvalidOperationException(
                "Unknown package-backed implementation materialization result."),
        };
    }

    private static bool CanIssueImplementation(
        PlatformHouseWorkBudget remaining) =>
        remaining.MaxSourceOperations > 0
        && remaining.MaxAssemblies > 0
        && remaining.MaxBytes > 0
        && remaining.MaxDuration > TimeSpan.Zero;

    private static PlatformHouseWorkBudget Remaining(
        PlatformHouseWorkBudget maximum,
        PlatformHouseConsumedWork consumed) =>
        new(
            Math.Max(
                0,
                maximum.MaxSourceOperations
                    - consumed.SourceOperations),
            Math.Max(
                0,
                maximum.MaxTargetCandidates
                    - consumed.TargetCandidates),
            Math.Max(0, maximum.MaxAssemblies - consumed.Assemblies),
            Math.Max(
                0,
                maximum.MaxXmlDocuments - consumed.XmlDocuments),
            Math.Max(
                0,
                maximum.MaxPortablePdbs - consumed.PortablePdbs),
            Math.Max(
                0,
                maximum.MaxSourceDocuments - consumed.SourceDocuments),
            Math.Max(0, maximum.MaxBytes - consumed.Bytes),
            Math.Max(
                0,
                maximum.MaxForwardingHops
                    - consumed.ForwardingHops),
            consumed.Elapsed >= maximum.MaxDuration
                ? TimeSpan.Zero
                : maximum.MaxDuration - consumed.Elapsed);

    private static PlatformHouseConsumedWork Work(
        PackageImplementationRealization implementation,
        TimeSpan elapsed) =>
        new(
            implementation.Frameworks.Length,
            targetCandidates: 0,
            implementation.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            implementation.ConsumedBytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed);
}
