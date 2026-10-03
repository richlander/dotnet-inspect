using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformQueries;

/// <summary>
/// One Platform assembly-reference result bound to the exact owner-issued
/// request that produced its receipt.
/// </summary>
public sealed class ExternalAssemblyReferencePlatformResult
{
    public ExternalAssemblyReferencePlatformResult(
        PlatformHouseRequest request,
        PlatformHouseOutcome<AssemblyBindingDecision> outcome)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcome);
        if (request.Operation
            is not PlatformHouseOperation.ResolveAssemblyReference operation)
        {
            throw new ArgumentException(
                "The Platform result requires an assembly-reference binding request.",
                nameof(request));
        }
        if (!ReferenceEquals(outcome.Receipt.Request, request.Snapshot))
        {
            throw new ArgumentException(
                "The Platform outcome receipt must retain the exact request snapshot.",
                nameof(outcome));
        }

        Request = operation.Request;
        PlatformRequest = request;
        Outcome = outcome;
    }

    public AssemblyBindingRequest Request { get; }

    public PlatformHouseRequest PlatformRequest { get; }

    public PlatformHouseOutcome<AssemblyBindingDecision> Outcome { get; }
}

/// <summary>
/// Closed Package-then-Platform supplier result for one external AssemblyRef.
/// </summary>
public abstract record ExternalAssemblyReferenceSupplierOutcome
{
    private protected ExternalAssemblyReferenceSupplierOutcome(
        AssemblyBindingRequest request,
        PackageAssemblyReferenceSupplierOutcome packageStage)
    {
        Request = request;
        PackageStage = packageStage;
    }

    public AssemblyBindingRequest Request { get; }

    public PackageAssemblyReferenceSupplierOutcome PackageStage { get; }

    public sealed record PackageOwned :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal PackageOwned(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome.Selected package)
            : base(request, package) =>
            Package = package;

        public PackageAssemblyReferenceSupplierOutcome.Selected Package
        { get; }
    }

    public sealed record PlatformOwned :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal PlatformOwned(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome.Missing package,
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                platform,
            AssemblyBindingDecision.Resolved binding)
            : base(request, package)
        {
            Package = package;
            Platform = platform;
            Binding = binding;
        }

        public PackageAssemblyReferenceSupplierOutcome.Missing Package
        { get; }

        public PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            Platform
        { get; }

        public AssemblyBindingDecision.Resolved Binding { get; }
    }

    public sealed record NameOwnedNoMatch :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal NameOwnedNoMatch(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome.Missing package,
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                platform,
            AssemblyBindingDecision.Missing binding)
            : base(request, package)
        {
            Package = package;
            Platform = platform;
            Binding = binding;
        }

        public PackageAssemblyReferenceSupplierOutcome.Missing Package
        { get; }

        public PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            Platform
        { get; }

        public AssemblyBindingDecision.Missing Binding { get; }
    }

    public sealed record NoSupplier :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal NoSupplier(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome.Missing package,
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                platform,
            AssemblyBindingDecision.Missing binding)
            : base(request, package)
        {
            Package = package;
            Platform = platform;
            Binding = binding;
        }

        public PackageAssemblyReferenceSupplierOutcome.Missing Package
        { get; }

        public PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            Platform
        { get; }

        public AssemblyBindingDecision.Missing Binding { get; }
    }

    public sealed record Unavailable :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal Unavailable(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome packageStage,
            PlatformHouseOutcome<AssemblyBindingDecision>? platform)
            : base(request, packageStage) =>
            Platform = platform;

        public PlatformHouseOutcome<AssemblyBindingDecision>? Platform
        { get; }
    }

    public sealed record Ambiguous :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal Ambiguous(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome packageStage,
            PlatformHouseOutcome<AssemblyBindingDecision>? platform)
            : base(request, packageStage) =>
            Platform = platform;

        public PlatformHouseOutcome<AssemblyBindingDecision>? Platform
        { get; }
    }

    public sealed record Incomplete :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal Incomplete(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome packageStage,
            PlatformHouseOutcome<AssemblyBindingDecision>? platform)
            : base(request, packageStage) =>
            Platform = platform;

        public PlatformHouseOutcome<AssemblyBindingDecision>? Platform
        { get; }
    }

    public sealed record Failed :
        ExternalAssemblyReferenceSupplierOutcome
    {
        internal Failed(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome packageStage,
            PlatformHouseOutcome<AssemblyBindingDecision>? platform,
            string reason)
            : base(request, packageStage)
        {
            Platform = platform;
            Reason = reason;
        }

        public PlatformHouseOutcome<AssemblyBindingDecision>? Platform
        { get; }

        public string Reason { get; }
    }
}

/// <summary>
/// Composes the ordinary reachable-Package supplier stage with one deferred,
/// target-aware Platform binding operation.
/// </summary>
public static class ExternalAssemblyReferenceSupplierAssociation
{
    public static async ValueTask<
        ExternalAssemblyReferenceSupplierOutcome> ExecuteAsync(
            PackageAssemblyReferenceSupplierAssociation packageAssociation,
            AssemblyBindingRequest request,
            AssemblyBindingSelection referencingContextSelection,
            PackageHouse packageHouse,
            PackageSourceOperationLease packageSourceOperation,
            Func<
                AssemblyBindingRequest,
                CancellationToken,
                ValueTask<
                    ExternalAssemblyReferencePlatformResult>>
                resolvePlatform,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageAssociation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(
            referencingContextSelection);
        ArgumentNullException.ThrowIfNull(packageHouse);
        ArgumentNullException.ThrowIfNull(packageSourceOperation);
        ArgumentNullException.ThrowIfNull(resolvePlatform);
        cancellationToken.ThrowIfCancellationRequested();

        PackageAssemblyReferenceSupplierOutcome package =
            await packageAssociation.ResolveAsync(
                    request,
                    referencingContextSelection,
                    packageHouse,
                    packageSourceOperation)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        switch (package)
        {
            case PackageAssemblyReferenceSupplierOutcome.Selected selected:
                return new ExternalAssemblyReferenceSupplierOutcome
                    .PackageOwned(request, selected);
            case PackageAssemblyReferenceSupplierOutcome.Ambiguous:
                return new ExternalAssemblyReferenceSupplierOutcome
                    .Ambiguous(
                        request,
                        package,
                        platform: null);
            case PackageAssemblyReferenceSupplierOutcome.Unavailable:
                return new ExternalAssemblyReferenceSupplierOutcome
                    .Unavailable(
                        request,
                        package,
                        platform: null);
            case PackageAssemblyReferenceSupplierOutcome.Incomplete:
                return new ExternalAssemblyReferenceSupplierOutcome
                    .Incomplete(
                        request,
                        package,
                        platform: null);
            case PackageAssemblyReferenceSupplierOutcome.Failed:
                return new ExternalAssemblyReferenceSupplierOutcome
                    .Failed(
                        request,
                        package,
                        platform: null,
                        "The ordinary Package supplier stage failed.");
        }

        var missing =
            (PackageAssemblyReferenceSupplierOutcome.Missing)package;
        ExternalAssemblyReferencePlatformResult platform =
            await resolvePlatform(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return CompletePlatform(request, missing, platform);
    }

    public static ExternalAssemblyReferenceSupplierOutcome
        CompletePlatform(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierOutcome.Missing package,
            ExternalAssemblyReferencePlatformResult platform)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(platform);
        if (!ReferenceEquals(package.Request, request))
        {
            throw new ArgumentException(
                "The Package stage must retain the unchanged Metadata binding request.",
                nameof(package));
        }
        if (!ReferenceEquals(platform.Request, request))
        {
            throw new ArgumentException(
                "The Platform stage must retain the unchanged Metadata binding request.",
                nameof(platform));
        }

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            platform.Outcome;
        return outcome switch
        {
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            {
                Value: AssemblyBindingDecision.Resolved binding,
            } completed =>
                new ExternalAssemblyReferenceSupplierOutcome.PlatformOwned(
                    request,
                    package,
                    completed,
                    binding),
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            {
                Value: AssemblyBindingDecision.Missing binding,
            } completed =>
                CompleteMissing(
                    request,
                    package,
                    completed,
                    binding),
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            {
                Value: AssemblyBindingDecision.Unavailable,
            } =>
                new ExternalAssemblyReferenceSupplierOutcome.Unavailable(
                    request,
                    package,
                    outcome),
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            {
                Value: AssemblyBindingDecision.Ambiguous,
            } =>
                new ExternalAssemblyReferenceSupplierOutcome.Ambiguous(
                    request,
                    package,
                    outcome),
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed =>
                new ExternalAssemblyReferenceSupplierOutcome.Failed(
                    request,
                    package,
                    outcome,
                    "Platform binding returned a rejected or unsupported completed decision."),
            PlatformHouseOutcome<AssemblyBindingDecision>.Unavailable =>
                new ExternalAssemblyReferenceSupplierOutcome.Unavailable(
                    request,
                    package,
                    outcome),
            PlatformHouseOutcome<AssemblyBindingDecision>.Ambiguous =>
                new ExternalAssemblyReferenceSupplierOutcome.Ambiguous(
                    request,
                    package,
                    outcome),
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete =>
                new ExternalAssemblyReferenceSupplierOutcome.Incomplete(
                    request,
                    package,
                    outcome),
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed =>
                new ExternalAssemblyReferenceSupplierOutcome.Failed(
                    request,
                    package,
                    outcome,
                    "Platform binding failed."),
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected =>
                new ExternalAssemblyReferenceSupplierOutcome.Failed(
                    request,
                    package,
                    outcome,
                    "Platform binding rejected its request or owner evidence."),
            _ => throw new InvalidOperationException(
                "Unknown Platform assembly-reference outcome."),
        };
    }

    static ExternalAssemblyReferenceSupplierOutcome CompleteMissing(
        AssemblyBindingRequest request,
        PackageAssemblyReferenceSupplierOutcome.Missing package,
        PlatformHouseOutcome<AssemblyBindingDecision>.Completed platform,
        AssemblyBindingDecision.Missing binding)
    {
        if (binding.Disposition
                == AssemblyBindingMissDisposition.NameOwnedNoMatch
            || package.Disposition
                == AssemblyBindingMissDisposition.NameOwnedNoMatch)
        {
            return new ExternalAssemblyReferenceSupplierOutcome
                .NameOwnedNoMatch(
                    request,
                    package,
                    platform,
                    binding);
        }
        if (binding.Disposition
            == AssemblyBindingMissDisposition.NoNameOwner)
        {
            return new ExternalAssemblyReferenceSupplierOutcome.NoSupplier(
                request,
                package,
                platform,
                binding);
        }

        return new ExternalAssemblyReferenceSupplierOutcome.Failed(
            request,
            package,
            platform,
            "A completed Platform supplier stage must attest NoNameOwner or NameOwnedNoMatch.");
    }
}
