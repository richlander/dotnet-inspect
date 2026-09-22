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
        Product = request.Product;
    }

    public SourceHouseRequestIdentity Identity { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseTarget Target { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseDecompilationProduct Product { get; }
    public SourceHouseSourcePolicy SourcePolicy =>
        SourceHouseSourcePolicy.DecompiledOnly;
    public SourceHousePdbAcquisitionPolicy PdbAcquisitionPolicy =>
        SourceHousePdbAcquisitionPolicy.LibraryCompanionOrEmbeddedOnly;
}

public sealed record SourceHouseDecompilationWorkCharge(
    long AssemblyBytesObserved,
    long PortablePdbBytesObserved,
    int BodyProjectionsAttempted);

public abstract record SourceHouseDecompilationContent
{
    private SourceHouseDecompilationContent()
    {
    }

    public sealed record SourceText : SourceHouseDecompilationContent
    {
        public SourceText(CSharpDecompilationAttempt attempt)
        {
            Attempt = attempt
                ?? throw new ArgumentNullException(nameof(attempt));
        }

        public CSharpDecompilationAttempt Attempt { get; }
    }

    public sealed record StructuredTypeDocument
        : SourceHouseDecompilationContent
    {
        public StructuredTypeDocument(
            CSharpTypeDocumentOutcome outcome)
        {
            Outcome = outcome
                ?? throw new ArgumentNullException(nameof(outcome));
        }

        public CSharpTypeDocumentOutcome Outcome { get; }
    }
}

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
            SourceHouseDecompilationContent content,
            SourceHouseDecompilationWorkCharge work,
            SourceHouseLibraryLeaseSettlement leaseSettlement)
            : base(request, pdbContribution, work, leaseSettlement)
        {
            Content = content
                ?? throw new ArgumentNullException(nameof(content));
            bool matchesProduct =
                (request.Product, content) switch
                {
                    (
                        SourceHouseDecompilationProduct.SourceText,
                        SourceHouseDecompilationContent.SourceText) =>
                        true,
                    (
                        SourceHouseDecompilationProduct
                            .StructuredTypeDocument,
                        SourceHouseDecompilationContent
                            .StructuredTypeDocument) =>
                        true,
                    _ => false,
                };
            if (!matchesProduct)
            {
                throw new ArgumentException(
                    "Completed decompilation content must match the requested product.",
                    nameof(content));
            }
            int expectedBodyProjections = content switch
            {
                SourceHouseDecompilationContent.SourceText source =>
                    source.Attempt.BodyProjectionsAttempted,
                SourceHouseDecompilationContent.StructuredTypeDocument
                    document =>
                    document.Outcome.BodyProjectionsAttempted,
                _ => throw new InvalidOperationException(
                    "Unknown completed decompilation content."),
            };
            if (work.BodyProjectionsAttempted
                != expectedBodyProjections)
            {
                throw new ArgumentException(
                    "Completed decompilation work must match the producer work charge.",
                    nameof(work));
            }
        }

        public SourceHouseDecompilationContent Content { get; }

        public CSharpDecompilationAttempt Attempt =>
            Content
                is SourceHouseDecompilationContent.SourceText source
            ? source.Attempt
            : throw new InvalidOperationException(
                "The completed decompilation contains a structured Type document.");

        public CSharpTypeDocumentOutcome TypeDocument =>
            Content
                is SourceHouseDecompilationContent.StructuredTypeDocument
                    document
            ? document.Outcome
            : throw new InvalidOperationException(
                "The completed decompilation contains source text.");
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
