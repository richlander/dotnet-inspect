using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ILInspector.Findings;

/// <summary>
/// Why a finding inspection has no applicable observation census.
/// </summary>
public enum FindingInspectionAbsenceKind
{
    /// <summary>The exact subject is proven not to exist at the evaluated address.</summary>
    SubjectAbsent,

    /// <summary>The subject exists, but this producer has no applicable input for it.</summary>
    NoApplicableInput,
}

/// <summary>
/// A whole-census inspection failure on the non-generic finding skeleton. This is not a
/// <c>Finding&lt;InspectionError&gt;</c>: there is no diffable domain occurrence, only a failure to
/// produce the requested census.
/// </summary>
public sealed record InspectionError(
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    string Reason)
{
    public FindingSubject Subject { get; }
        = Subject ?? throw new ArgumentNullException(nameof(Subject));

    public FindingDescriptor Descriptor { get; }
        = Descriptor ?? throw new ArgumentNullException(nameof(Descriptor));

    public string Reason { get; }
        = Reason ?? throw new ArgumentNullException(nameof(Reason));
}

/// <summary>
/// The explicit outcome of inspecting one subject for a stream of findings. This replaces a
/// <c>TryInspect(..., out findings)</c> contract when absence and failure are distinct states:
/// <see cref="Complete"/> may contain an empty census, <see cref="Absent"/> distinguishes subject
/// absence from inapplicable producer input, and <see cref="Failed"/> carries an
/// <see cref="InspectionError"/>.
/// </summary>
[Union]
public sealed record FindingInspection<T>
    where T : notnull
{
    public FindingInspection(Complete value) => Value = Guard(value);
    public FindingInspection(Absent value) => Value = Guard(value);
    public FindingInspection(Failed value) => Value = Guard(value);

    static object Guard(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    /// <summary>The active inspection case.</summary>
    public object? Value { get; }

    /// <summary>An inspection that completed. <see cref="Findings"/> may be empty.</summary>
    public sealed record Complete(
        ImmutableArray<Finding<T>> Findings)
    {
        ImmutableArray<Finding<T>> _findings = Validate(Findings);

        public ImmutableArray<Finding<T>> Findings
        {
            get => _findings;
            init => _findings = Validate(value);
        }

        public bool Equals(Complete? other)
            => other is not null
                && FindingValueEquality.SequenceEqual(Findings, other.Findings);

        public override int GetHashCode()
            => FindingValueEquality.SequenceHashCode(Findings);

        static ImmutableArray<Finding<T>> Validate(ImmutableArray<Finding<T>> findings)
        {
            if (findings.IsDefault)
                throw new ArgumentException("Findings must be initialized.", nameof(Findings));
            for (int i = 0; i < findings.Length; i++)
            {
                if (findings[i] is null)
                {
                    throw new ArgumentException(
                        $"Finding at index {i} must not be null.",
                        nameof(Findings));
                }
            }

            return findings;
        }
    }

    /// <summary>The typed reason this inspection has no applicable observation census.</summary>
    public sealed record Absent
    {
        public Absent(FindingInspectionAbsenceKind kind, string? detail = null)
        {
            Kind = kind switch
            {
                FindingInspectionAbsenceKind.SubjectAbsent => kind,
                FindingInspectionAbsenceKind.NoApplicableInput => kind,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            Detail = detail;
        }

        public FindingInspectionAbsenceKind Kind { get; }
        public string? Detail { get; }
    }

    /// <summary>The producer could not complete the inspection.</summary>
    public sealed record Failed(InspectionError Error)
    {
        public InspectionError Error { get; }
            = Error ?? throw new ArgumentNullException(nameof(Error));
    }
}
