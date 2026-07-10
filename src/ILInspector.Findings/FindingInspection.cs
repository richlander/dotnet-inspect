using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ILInspector.Findings;

/// <summary>
/// A whole-census inspection failure on the non-generic finding skeleton. This is not a
/// <c>Finding&lt;InspectionError&gt;</c>: there is no diffable domain occurrence, only a failure to
/// produce the requested census.
/// </summary>
public sealed record InspectionError(
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    string Reason) : IFinding
{
    public FindingSubject Subject { get; }
        = Subject ?? throw new ArgumentNullException(nameof(Subject));

    public FindingDescriptor Descriptor { get; }
        = Descriptor ?? throw new ArgumentNullException(nameof(Descriptor));

    public string Reason { get; }
        = Reason ?? throw new ArgumentNullException(nameof(Reason));

    public string? Detail => Reason;
}

/// <summary>
/// The explicit outcome of inspecting one subject for a stream of findings. This replaces a
/// <c>TryInspect(..., out findings)</c> contract when absence and failure are distinct states:
/// <see cref="Complete"/> may contain an empty census, <see cref="Absent"/> means the subject has
/// no applicable producer input, and <see cref="Failed"/> carries an <see cref="InspectionError"/>.
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
        public ImmutableArray<Finding<T>> Findings { get; }
            = Validate(Findings);

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

    /// <summary>The subject has no applicable input for this producer.</summary>
    public sealed record Absent(string? Detail = null);

    /// <summary>The producer could not complete the inspection.</summary>
    public sealed record Failed(InspectionError Error)
    {
        public InspectionError Error { get; }
            = Error ?? throw new ArgumentNullException(nameof(Error));
    }
}
