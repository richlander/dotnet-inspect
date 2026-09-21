using DotnetInspector.Libraries;
using ILInspector.Decompiler;

namespace DotnetInspector.SourceHouse;

public sealed record SourceHouseDecompilationRequestEvidence
{
    internal SourceHouseDecompilationRequestEvidence(
        SourceHouseDecompilationRequest request)
    {
        Identity = request.Identity;
        Library = request.Library;
        SelectedAssembly = request.SelectedAssembly;
        Target = request.Target;
        OperationPlan = request.Plan.Identity;
        PolicyGeneration = request.Plan.PolicyGeneration;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget Target { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseSourcePolicy SourcePolicy =>
        SourceHouseSourcePolicy.DecompiledOnly;
    public SourceHousePdbAcquisitionPolicy PdbAcquisitionPolicy =>
        SourceHousePdbAcquisitionPolicy.LibraryCompanionOrEmbeddedOnly;
}

public sealed record SourceHouseDecompilationWorkCharge(
    long AssemblyBytesObserved,
    long PortablePdbBytesObserved,
    int BodyProjectionsAttempted);

public abstract class SourceHouseDecompilationOutcome
{
    private protected SourceHouseDecompilationOutcome(
        SourceHouseDecompilationRequestEvidence request,
        SourceHousePdbContribution pdbContribution,
        SourceHouseDecompilationWorkCharge work,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Request = request;
        PdbContribution = pdbContribution;
        Work = work;
        LeaseSettlement = leaseSettlement;
    }

    public SourceHouseDecompilationRequestEvidence Request { get; }
    public SourceHousePdbContribution PdbContribution { get; }
    public SourceHouseDecompilationWorkCharge Work { get; }
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }

    public sealed class Completed : SourceHouseDecompilationOutcome
    {
        internal Completed(
            SourceHouseDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            CSharpDecompilationAttempt attempt,
            SourceHouseDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Attempt = attempt;
        }

        public CSharpDecompilationAttempt Attempt { get; }
    }

    public sealed class Rejected : SourceHouseDecompilationOutcome
    {
        internal Rejected(
            SourceHouseDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseRejection rejection,
            SourceHouseDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Rejection = rejection;
        }

        public SourceHouseRejection Rejection { get; }
    }

    public sealed class Failed : SourceHouseDecompilationOutcome
    {
        internal Failed(
            SourceHouseDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseFailure failure,
            SourceHouseDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Failure = failure;
        }

        public SourceHouseFailure Failure { get; }
    }

    public sealed class Incomplete : SourceHouseDecompilationOutcome
    {
        internal Incomplete(
            SourceHouseDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseIncompleteBoundary boundary,
            SourceHouseDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Boundary = boundary;
        }

        public SourceHouseIncompleteBoundary Boundary { get; }
    }
}
