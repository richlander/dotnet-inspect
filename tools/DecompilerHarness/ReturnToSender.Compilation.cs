using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

static partial class ReturnToSender
{
    internal sealed class ReferencePreparationException(
        CompileReferenceFailure failure, string? detail = null)
        : InvalidOperationException(
            $"{failure.Kind}: {new InertText.InertString(InertText.TextPolicy.Field,
                detail ?? failure.BindingRequest?.Target.ToString() ?? failure.RequestedIdentity?.ToString() ?? "")}")
    {
        internal CompileReferenceFailure Failure { get; } = failure;
    }

    internal sealed class CompilationClosure(
        CompileReferenceContext context, PEReader sourceReader, MetadataSource bodySource)
    {
        bool _active = true;
        readonly MetadataReference[] _references = [.. context.CompilerReferences];
        internal CompileReferenceContext Resolver { get { EnsureActive(); return context; } }
        internal ResolvedAssemblyReference TargetAssembly => Resolver.Source;
        internal PEReader SourceReader { get { EnsureActive(); return sourceReader; } }
        internal MetadataSource BodySource { get { EnsureActive(); return bodySource; } }
        internal MetadataReference[] References { get { EnsureActive(); return _references; } }
        internal RoundTripCompilationProvenance WithReferenceProvenance(RoundTripCompilationProvenance provenance)
        {
            EnsureActive();
            return provenance with
            {
                References = [.. context.SelectedDescriptors.Select(descriptor =>
                    new RoundTripReferenceProvenance(
                        descriptor.SelectedOrdinal,
                        descriptor.Image.Location?.ToString() ?? $"reference:{descriptor.SelectedOrdinal}",
                        descriptor.Image.Location?.ToString(),
                        descriptor.Image.ContentDigest.HexValue,
                        descriptor.Image.ModuleVersionId,
                        descriptor.Properties.Aliases,
                        descriptor.Properties.EmbedInteropTypes))],
            };
        }
        internal void Close() => _active = false;
        internal void EnsureActive() => ObjectDisposedException.ThrowIf(!_active, this);
    }

    /// <summary>Initial compile-back and authored replay share this callback's frozen context.</summary>
    internal sealed class CompilationOperation(
        string assemblyPath, CompilationClosure closure,
        AssemblyDependencyDiscoveryResult.Captured discovery,
        CompileReferenceInventory inventory, CompileReferenceSet referenceSet)
    {
        internal CompilationClosure Closure => closure;
        internal AssemblyDependencyDiscoveryResult.Captured Discovery => discovery;
        internal CompileReferenceInventory Inventory => inventory;
        internal CompileReferenceSet ReferenceSet => referenceSet;

        internal IReadOnlyList<Result> CompileBackPropertyGetters(int maxTargets = int.MaxValue)
        {
            closure.EnsureActive();
            return ReturnToSender.CompileBackPropertyGetters(
                assemblyPath, maxTargets, applyCompileBackFloor: true, closure);
        }

        internal IReadOnlyList<Result> CompileBackTargets(
            IReadOnlyList<RequestedTarget> targets,
            RoundTripScope scope = RoundTripScope.Cluster,
            RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected,
            bool applyCompileBackFloor = true)
        {
            closure.EnsureActive();
            return ReturnToSender.CompileBackTargets(
                assemblyPath, targets, ReturnToSenderSourceIndex.TryCreate(assemblyPath),
                applyCompileBackFloor, scope, bodyPolicy, closure);
        }

        internal ScopePairResult CompileBackScopes(RequestedTarget target)
        {
            closure.EnsureActive();
            return ReturnToSender.CompileBackScopes(assemblyPath, target, closure);
        }
    }

    internal static T WithCompilation<T>(
        string assemblyPath,
        Func<CompilationOperation, T> consume,
        AssemblyDependencyResolutionOptions? options = null,
        IEnumerable<AssemblyReferenceIdentity>? additionalPlatformReferences = null,
        CancellationToken cancellationToken = default) =>
        WithCompilationAsync(
            assemblyPath, operation => ValueTask.FromResult(consume(operation)),
            options, additionalPlatformReferences, cancellationToken).AsTask().GetAwaiter().GetResult();

    internal static async ValueTask<T> WithCompilationAsync<T>(
        string assemblyPath,
        Func<CompilationOperation, ValueTask<T>> consume,
        AssemblyDependencyResolutionOptions? options = null,
        IEnumerable<AssemblyReferenceIdentity>? additionalPlatformReferences = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consume);
        var resolver = new AssemblyDependencyResolver(options ?? new(assemblyPath)
        {
            ExcludeTargetAssembly = true,
            SnapshotAssemblyImages = true,
            AllowPlatformAssemblyVersionRollForward = true,
        });
        var discoveryResult = resolver.CaptureDiscoveryInventory(cancellationToken);
        if (discoveryResult is not AssemblyDependencyDiscoveryResult.Captured discovery)
        {
            var failed = (AssemblyDependencyDiscoveryResult.Failed)discoveryResult;
            var failures = failed.DiscoveryFailures.Select(failure =>
                $"{failure.Tier}: {failure.Kind} ({failure.Location})")
                .Concat(failed.PartialEntries.Select(entry => entry.Acquisition switch
                {
                    AssemblyDependencyAcquisition.Unavailable unavailable =>
                        $"{entry.Dependency.Path}: {unavailable.Failure.Kind}",
                    AssemblyDependencyAcquisition.Rejected =>
                        $"{entry.Dependency.Path}: rejected metadata",
                    _ => null,
                }).OfType<string>());
            throw new ReferencePreparationException(
                new(CompileReferenceFailureKind.ReferenceDiscoveryFailed), string.Join("; ", failures));
        }
        ResolvedAssemblyReference source = resolver.AcquireTargetAssembly()
            ?? throw new ReferencePreparationException(new(CompileReferenceFailureKind.ReferenceImageInvalid));
        ResolvedAssemblyReference[] candidates = [.. discovery.Entries
            .Where(entry => !entry.IsTargetInput)
            .Select(entry => entry.Acquisition).OfType<AssemblyDependencyAcquisition.Acquired>()
            .Select(acquired => acquired.Assembly).DistinctBy(assembly => assembly.Registration)];
        var requests = new List<AssemblyBindingRequest>();
        foreach (ResolvedAssemblyReference assembly in candidates.Prepend(source)
            .Where(assembly => assembly.Provenance is not AssemblyResolutionProvenance.PlatformAsset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.Registration != source.Registration)
                IncludePlatformRequest(assembly.Identity, AssemblyBindingOrigin.Global());
            using var stream = assembly.OpenRead();
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                var identity = AssemblyReferenceIdentity.From(reader, handle);
                IncludePlatformRequest(identity, AssemblyBindingOrigin.FromAssembly(assembly));
            }
        }
        foreach (var identity in additionalPlatformReferences ?? [])
            requests.Add(new(AssemblyBindingTarget.Reference(identity),
                AssemblyBindingOrigin.FromAssembly(source), AssemblyResolutionScope.Platform));

        void IncludePlatformRequest(AssemblyReferenceIdentity identity, AssemblyBindingOrigin origin)
        {
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(identity), origin, AssemblyResolutionScope.Platform);
            if (resolver.Select(request).Selection is AssemblyBindingSelection.Selected
                { Assembly.Provenance: AssemblyResolutionProvenance.PlatformAsset } selected
                && CompileReferencePlatformPolicy.MatchesPlatformIdentity(identity, selected.Assembly.Identity))
                requests.Add(request);
        }

        await using var owner = new ArtifactSetSession();
        var policy = Require(await CompileReferencePlatformPolicy.PrepareInventoryAsync(
            owner, resolver, source, discovery, requests, cancellationToken).ConfigureAwait(false));
        if (await owner.SealAsync(cancellationToken).ConfigureAwait(false) is not ArtifactSetPublicationOutcome.Published)
            throw new ReferencePreparationException(new(CompileReferenceFailureKind.ReferenceContentUnavailable));
        using var lease = owner.IssueLease(owner.CreateQueryAuthorization());
        var inventory = Require(policy.Discover(lease, static _ => { }, cancellationToken));
        var platformIdentities = policy.Bindings.Select(binding =>
            ((AssemblyBindingSelection.Selected)binding.PlatformSelection.Selection).Assembly.Identity)
            .ToHashSet(AssemblyReferenceIdentity.EquivalentComparer);
        var exact = inventory.Candidates
            .Where(image => !platformIdentities.Contains(image.Identity))
            .Select(image => image.Identity).Distinct(AssemblyReferenceIdentity.EquivalentComparer)
            .Select(identity => new CompileReferenceRequest(identity));
        var set = Require(policy.Select(inventory, exact, cancellationToken));
        return Require(await set.UseAsync(async context =>
        {
            using var sourceReader = new PEReader(context.Source.OpenRead());
            using var metadata = new MetadataContext((IAssemblyBindingPolicy)context);
            using var bodySource = MetadataSource.Open(context.Source, null, (IAssemblyBindingPolicy)context, metadata);
            var closure = new CompilationClosure(context, sourceReader, bodySource);
            try
            {
                return await consume(new CompilationOperation(
                    assemblyPath, closure, discovery, inventory, set)).ConfigureAwait(false);
            }
            finally
            {
                closure.Close();
            }
        }, cancellationToken).ConfigureAwait(false));
    }

    static T Require<T>(CompileReferenceResult<T> result) => result switch
    {
        CompileReferenceResult<T>.Ready ready => ready.Value,
        CompileReferenceResult<T>.Rejected rejected => throw new ReferencePreparationException(rejected.Failure),
        _ => throw new InvalidOperationException("Unknown frozen reference outcome."),
    };

    internal static Result Detach(Result result) => result with { FinalRequest = null };

    internal static Result ReferenceFailureResult(string assemblyPath, ReferencePreparationException failure)
        => ContextFailResult(assemblyPath, failure.Message) with
        {
            ReferenceFailure = failure.Failure with { BindingRequest = null, PolicySelection = null },
        };

    static IReadOnlyList<Result> DetachedCompilation(
        string assemblyPath, Func<CompilationOperation, IReadOnlyList<Result>> consume)
    {
        try
        {
            return WithCompilation(assemblyPath, operation => consume(operation).Select(Detach).ToArray());
        }
        catch (ReferencePreparationException failure)
        {
            return [ReferenceFailureResult(assemblyPath, failure)];
        }
    }
}
