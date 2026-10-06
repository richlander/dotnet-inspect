using DotnetInspector.Libraries;
using ILInspector.Decompiler;

namespace DotnetInspector.SourceHouse;

public sealed class SourceHouseBestAvailableRequest
{
    public SourceHouseBestAvailableRequest(
        SourceHouseRequestIdentity identity,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseTarget.MemberTarget target,
        SourceHouseOperationPlan authoredPlan,
        SourceHouseDecompilationPlan decompilationPlan)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(authoredPlan);
        ArgumentNullException.ThrowIfNull(decompilationPlan);
        if (!string.Equals(
                authoredPlan.Identity.Name,
                decompilationPlan.Identity.Name,
                StringComparison.Ordinal)
            || !string.Equals(
                authoredPlan.PolicyGeneration.Name,
                decompilationPlan.PolicyGeneration.Name,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Best-available producer plans must share one operation identity and policy generation.");
        }

        Identity = identity;
        Library = library;
        SelectedAssembly = selectedAssembly;
        Target = target;
        AuthoredPlan = authoredPlan;
        DecompilationPlan = decompilationPlan;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget.MemberTarget Target { get; }
    public SourceHouseOperationPlan AuthoredPlan { get; }
    public SourceHouseDecompilationPlan DecompilationPlan { get; }
}

public sealed record SourceHouseBestAvailableRequestEvidence
{
    internal SourceHouseBestAvailableRequestEvidence(
        SourceHouseBestAvailableRequest request)
    {
        Identity = request.Identity;
        Library = request.Library;
        SelectedAssembly = request.SelectedAssembly;
        Target = request.Target;
        OperationPlan = request.AuthoredPlan.Identity;
        PolicyGeneration = request.AuthoredPlan.PolicyGeneration;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget.MemberTarget Target { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseSourcePolicy SourcePolicy =>
        SourceHouseSourcePolicy.BestAvailable;
    public SourceHousePdbAcquisitionPolicy PdbAcquisitionPolicy =>
        SourceHousePdbAcquisitionPolicy.LibraryCompanionOrEmbeddedOnly;
}

public enum SourceHouseSelectedSource
{
    Authored,
    Decompiled,
}

public sealed class SourceHouseBestAvailableReceipt
{
    internal SourceHouseBestAvailableReceipt(
        SourceHouseBestAvailableRequestEvidence request,
        SourceHouseOutcome authoredOutcome,
        SourceHouseDecompilationOutcome? decompilationOutcome,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Identity = new SourceHouseReceiptIdentity();
        Request = request;
        AuthoredOutcome = authoredOutcome;
        DecompilationOutcome = decompilationOutcome;
        LeaseSettlement = leaseSettlement;
    }

    public SourceHouseReceiptIdentity Identity { get; }
    public SourceHouseBestAvailableRequestEvidence Request { get; }
    public SourceHouseOutcome AuthoredOutcome { get; }
    public SourceHouseDecompilationOutcome? DecompilationOutcome { get; }
    public SourceHousePdbContribution PdbContribution =>
        DecompilationOutcome?.PdbContribution
        ?? AuthoredOutcome.PdbContribution;
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }
}

public abstract class SourceHouseBestAvailableOutcome
{
    private protected SourceHouseBestAvailableOutcome(
        SourceHouseBestAvailableRequestEvidence request,
        SourceHouseOutcome authoredOutcome,
        SourceHouseDecompilationOutcome? decompilationOutcome,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Request = request;
        AuthoredOutcome = authoredOutcome;
        DecompilationOutcome = decompilationOutcome;
        LeaseSettlement = leaseSettlement;
        Receipt = new(
            request,
            authoredOutcome,
            decompilationOutcome,
            leaseSettlement);
    }

    public SourceHouseBestAvailableRequestEvidence Request { get; }
    public SourceHouseOutcome AuthoredOutcome { get; }
    public SourceHouseDecompilationOutcome? DecompilationOutcome { get; }
    public SourceHousePdbContribution PdbContribution =>
        DecompilationOutcome?.PdbContribution
        ?? AuthoredOutcome.PdbContribution;
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }
    public SourceHouseBestAvailableReceipt Receipt { get; }

    public sealed class Available : SourceHouseBestAvailableOutcome
    {
        internal Available(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authoredOutcome,
            SourceHouseDecompilationOutcome? decompilationOutcome,
            SourceHouseSelectedSource selected,
            string text,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                authoredOutcome,
                decompilationOutcome,
                leaseSettlement)
        {
            Selected = selected;
            Text = text
                ?? throw new ArgumentNullException(nameof(text));
        }

        public SourceHouseSelectedSource Selected { get; }
        public string Text { get; }
    }

    public sealed class Unavailable : SourceHouseBestAvailableOutcome
    {
        internal Unavailable(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authoredOutcome,
            SourceHouseDecompilationOutcome decompilationOutcome,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                authoredOutcome,
                decompilationOutcome,
                leaseSettlement)
        {
        }
    }

    public sealed class Rejected : SourceHouseBestAvailableOutcome
    {
        internal Rejected(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authoredOutcome,
            SourceHouseDecompilationOutcome decompilationOutcome,
            SourceHouseRejection rejection,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                authoredOutcome,
                decompilationOutcome,
                leaseSettlement)
        {
            Rejection = rejection;
        }

        public SourceHouseRejection Rejection { get; }
    }

    public sealed class Failed : SourceHouseBestAvailableOutcome
    {
        internal Failed(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authoredOutcome,
            SourceHouseDecompilationOutcome decompilationOutcome,
            SourceHouseFailure failure,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                authoredOutcome,
                decompilationOutcome,
                leaseSettlement)
        {
            Failure = failure;
        }

        public SourceHouseFailure Failure { get; }
    }

    public sealed class Incomplete : SourceHouseBestAvailableOutcome
    {
        internal Incomplete(
            SourceHouseBestAvailableRequestEvidence request,
            SourceHouseOutcome authoredOutcome,
            SourceHouseDecompilationOutcome decompilationOutcome,
            SourceHouseIncompleteBoundary boundary,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(
                request,
                authoredOutcome,
                decompilationOutcome,
                leaseSettlement)
        {
            Boundary = boundary;
        }

        public SourceHouseIncompleteBoundary Boundary { get; }
    }
}
