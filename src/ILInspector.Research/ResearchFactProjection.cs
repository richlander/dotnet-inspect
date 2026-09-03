using ILInspector.Decompiler.Annotations;
using ILInspector.Findings;

namespace ILInspector.Research;

sealed class ResearchFactProjection
{
    readonly FindingCensus<IAnnotation> _census;

    ResearchFactProjection(
        FindingCensus<IAnnotation> census,
        FindingCensusReceipt receipt,
        IReadOnlyList<ResearchFactAnnotation> annotations)
    {
        _census = census;
        Receipt = receipt;
        Annotations = annotations;
    }

    public FindingCensusReceipt Receipt { get; }
    public IReadOnlyList<ResearchFactAnnotation> Annotations { get; }

    public static ResearchFactProjection AdmitComplete(
        FindingCensus<IAnnotation> census,
        FindingCensusReceipt receipt,
        IEnumerable<FindingCensusEntry<IAnnotation>> entries)
    {
        ArgumentNullException.ThrowIfNull(census);
        ArgumentNullException.ThrowIfNull(entries);

        FindingCensusEntry<IAnnotation>[] retained = [.. entries];
        RequireValid(census.Validate(receipt, retained));
        return new ResearchFactProjection(
            census,
            receipt,
            [.. retained.Select(entry => new ResearchFactAnnotation(entry))]);
    }

    public ResearchFactProjection Where(
        Func<IAnnotation, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        FindingCensusEntry<IAnnotation>[] retained =
        [
            .. Annotations
                .Where(annotation => predicate(annotation))
                .Select(annotation => annotation.Entry),
        ];
        foreach (FindingCensusEntry<IAnnotation> entry in retained)
            RequireValid(_census.ValidateEntry(Receipt, entry));
        return new ResearchFactProjection(
            _census,
            Receipt,
            [.. retained.Select(entry => new ResearchFactAnnotation(entry))]);
    }

    internal static ResearchFactProjection AdmitSubset(
        FindingCensus<IAnnotation> census,
        FindingCensusReceipt receipt,
        IEnumerable<FindingCensusEntry<IAnnotation>> entries)
    {
        ArgumentNullException.ThrowIfNull(census);
        ArgumentNullException.ThrowIfNull(entries);

        FindingCensusEntry<IAnnotation>[] retained = [.. entries];
        foreach (FindingCensusEntry<IAnnotation> entry in retained)
            RequireValid(census.ValidateEntry(receipt, entry));

        FindingInstanceKey duplicate = retained
            .GroupBy(entry => entry.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key.Value)
            .FirstOrDefault();
        if (!duplicate.IsDefault)
        {
            throw new InvalidOperationException(
                $"Research Finding projection rejected DuplicateKey at key {duplicate}.");
        }
        return new ResearchFactProjection(
            census,
            receipt,
            [.. retained.Select(entry => new ResearchFactAnnotation(entry))]);
    }

    static void RequireValid(FindingCensusValidation validation)
    {
        FindingCensusValidationFailure? failure = validation switch
        {
            FindingCensusValidation.Valid => null,
            FindingCensusValidation.Invalid invalid => invalid.Failure,
        };
        if (failure is null)
            return;

        throw new InvalidOperationException(
            $"Research Finding projection rejected {failure.Kind}"
                + (failure.Key.IsDefault ? "" : $" at key {failure.Key}")
                + (failure.InputIndex is null
                    ? "."
                    : $" at input index {failure.InputIndex}."));
    }
}

sealed class ResearchFactAnnotation(
    FindingCensusEntry<IAnnotation> entry) : IAnnotation
{
    public FindingCensusEntry<IAnnotation> Entry { get; } =
        entry ?? throw new ArgumentNullException(nameof(entry));

    IAnnotation Annotation => Entry.Finding.Payload;

    public AnnotationDescriptor Descriptor => Annotation.Descriptor;
    public int SourceOffset => Annotation.SourceOffset;
    public AnnotationConditionality Conditionality => Annotation.Conditionality;
    public string? Detail => Annotation.Detail;
}
