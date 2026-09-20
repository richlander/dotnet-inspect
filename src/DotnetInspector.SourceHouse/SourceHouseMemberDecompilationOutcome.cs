using DotnetInspector.Libraries;
using ILInspector.Decompiler;

namespace DotnetInspector.SourceHouse;

public sealed record SourceHouseMemberDecompilationRequestEvidence
{
    internal SourceHouseMemberDecompilationRequestEvidence(
        SourceHouseMemberDecompilationRequest request)
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
    public SourceHouseTarget.MemberTarget Target { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseSourcePolicy SourcePolicy =>
        SourceHouseSourcePolicy.DecompiledOnly;
    public SourceHousePdbAcquisitionPolicy PdbAcquisitionPolicy =>
        SourceHousePdbAcquisitionPolicy.LibraryCompanionOrEmbeddedOnly;
}

public sealed record SourceHouseMemberDecompilationWorkCharge(
    long AssemblyBytesObserved,
    long PortablePdbBytesObserved,
    int BodyProjectionsAttempted);

public abstract class SourceHouseMemberDecompilationOutcome
{
    private protected SourceHouseMemberDecompilationOutcome(
        SourceHouseMemberDecompilationRequestEvidence request,
        SourceHousePdbContribution pdbContribution,
        SourceHouseMemberDecompilationWorkCharge work,
        SourceHouseLibraryLeaseSettlement leaseSettlement)
    {
        Request = request;
        PdbContribution = pdbContribution;
        Work = work;
        LeaseSettlement = leaseSettlement;
    }

    public SourceHouseMemberDecompilationRequestEvidence Request { get; }
    public SourceHousePdbContribution PdbContribution { get; }
    public SourceHouseMemberDecompilationWorkCharge Work { get; }
    public SourceHouseLibraryLeaseSettlement LeaseSettlement { get; }

    public sealed class Completed : SourceHouseMemberDecompilationOutcome
    {
        internal Completed(
            SourceHouseMemberDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            CSharpDecompilationAttempt attempt,
            SourceHouseMemberDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Attempt = attempt;
        }

        public CSharpDecompilationAttempt Attempt { get; }
    }

    public sealed class Rejected : SourceHouseMemberDecompilationOutcome
    {
        internal Rejected(
            SourceHouseMemberDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseRejection rejection,
            SourceHouseMemberDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Rejection = rejection;
        }

        public SourceHouseRejection Rejection { get; }
    }

    public sealed class Failed : SourceHouseMemberDecompilationOutcome
    {
        internal Failed(
            SourceHouseMemberDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseFailure failure,
            SourceHouseMemberDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Failure = failure;
        }

        public SourceHouseFailure Failure { get; }
    }

    public sealed class Incomplete : SourceHouseMemberDecompilationOutcome
    {
        internal Incomplete(
            SourceHouseMemberDecompilationRequestEvidence request,
            SourceHousePdbContribution pdbContribution,
            SourceHouseIncompleteBoundary boundary,
            SourceHouseMemberDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Boundary = boundary;
        }

        public SourceHouseIncompleteBoundary Boundary { get; }
    }
}
