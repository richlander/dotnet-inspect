using ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// A physical method and, when evidence is instruction-specific, its IL offset.
/// </summary>
public sealed record ResearchEvidenceLocation
{
    ResearchEvidenceLocation(
        MethodIdentity method,
        int? ilOffset)
    {
        Method = method
            ?? throw new ArgumentNullException(nameof(method));
        if (ilOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ilOffset),
                "An instruction evidence offset must be non-negative.");
        }

        ILOffset = ilOffset;
    }

    public MethodIdentity Method { get; }
    public int? ILOffset { get; }
    public bool IsMethodOnly => ILOffset is null;

    public static ResearchEvidenceLocation ForMethod(
        MethodIdentity method) =>
        new(method, null);

    public static ResearchEvidenceLocation ForInstruction(
        MethodIdentity method,
        int ilOffset) =>
        new(method, ilOffset);

    public ResearchEvidenceLocationAdmission Admit(
        MethodIdentity expectedMethod)
    {
        ArgumentNullException.ThrowIfNull(expectedMethod);
        return Method == expectedMethod
            ? new ResearchEvidenceLocationAdmission.Admitted(this)
            : new ResearchEvidenceLocationAdmission.Rejected(
                expectedMethod,
                this);
    }
}

/// <summary>
/// Closed result of admitting one evidence location to a method projection.
/// </summary>
public abstract record ResearchEvidenceLocationAdmission
{
    private ResearchEvidenceLocationAdmission()
    {
    }

    public sealed record Admitted : ResearchEvidenceLocationAdmission
    {
        internal Admitted(ResearchEvidenceLocation location)
            => Location = location;

        public ResearchEvidenceLocation Location { get; }
    }

    public sealed record Rejected : ResearchEvidenceLocationAdmission
    {
        internal Rejected(
            MethodIdentity expectedMethod,
            ResearchEvidenceLocation location)
        {
            ExpectedMethod = expectedMethod;
            Location = location;
        }

        public MethodIdentity ExpectedMethod { get; }
        public ResearchEvidenceLocation Location { get; }
    }
}
