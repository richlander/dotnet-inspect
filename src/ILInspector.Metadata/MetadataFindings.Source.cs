using ILInspector.Findings;

namespace ILInspector.Metadata;

public static partial class MetadataFindings
{
    public static readonly FindingDescriptor CompilationOptionDescriptor =
        new("metadata.compilation-option", "Compilation option");

    public static readonly FindingDescriptor CompilationReferenceDescriptor =
        new("metadata.compilation-reference", "Compilation reference");

    public static FindingInspection<CompilationOptionInfo> InspectCompilationOptions(
        PdbContext context,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (!context.HasPdb)
        {
            return new FindingInspection<CompilationOptionInfo>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            return InspectCompilationOptionsCore(
                context.GetCompilationOptions(),
                subject,
                nameof(context));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<CompilationOptionInfo>(
                subject,
                CompilationOptionDescriptor,
                "Could not inspect portable-PDB compilation options.",
                ex);
        }
    }

    public static FindingInspection<CompilationOptionInfo> InspectCompilationOptions(
        IEnumerable<CompilationOptionInfo> options,
        FindingSubject subject)
        => InspectCompilationOptionsCore(options, subject, nameof(options));

    public static FindingComparison<CompilationOptionInfo> CompareCompilationOptions(
        IEnumerable<CompilationOptionInfo> oldOptions,
        IEnumerable<CompilationOptionInfo> newOptions,
        FindingSubject subject)
        => CompareInventory(
            InspectCompilationOptionsCore(oldOptions, subject, nameof(oldOptions)),
            InspectCompilationOptionsCore(newOptions, subject, nameof(newOptions)));

    public static FindingComparison<CompilationOptionInfo> CompareCompilationOptions(
        PdbContext oldContext,
        PdbContext newContext,
        FindingSubject subject)
        => FindingComparison.Compare(
            InspectCompilationOptions(oldContext, subject),
            InspectCompilationOptions(newContext, subject),
            IdentitySetOptions)
            .TransformPairs(pairs =>
                PromoteChangedPayloads<CompilationOptionInfo>(pairs, null));

    public static FindingInspection<CompilationReferenceInfo> InspectCompilationReferences(
        PdbContext context,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (!context.HasPdb)
        {
            return new FindingInspection<CompilationReferenceInfo>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            return InspectCompilationReferencesCore(
                context.GetCompilationReferences(),
                subject,
                nameof(context));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<CompilationReferenceInfo>(
                subject,
                CompilationReferenceDescriptor,
                "Could not inspect portable-PDB compilation references.",
                ex);
        }
    }

    public static FindingInspection<CompilationReferenceInfo> InspectCompilationReferences(
        IEnumerable<CompilationReferenceInfo> references,
        FindingSubject subject)
        => InspectCompilationReferencesCore(references, subject, nameof(references));

    public static FindingComparison<CompilationReferenceInfo> CompareCompilationReferences(
        IEnumerable<CompilationReferenceInfo> oldReferences,
        IEnumerable<CompilationReferenceInfo> newReferences,
        FindingSubject subject)
        => CompareInventory(
            InspectCompilationReferencesCore(
                oldReferences,
                subject,
                nameof(oldReferences)),
            InspectCompilationReferencesCore(
                newReferences,
                subject,
                nameof(newReferences)));

    public static FindingComparison<CompilationReferenceInfo> CompareCompilationReferences(
        PdbContext oldContext,
        PdbContext newContext,
        FindingSubject subject)
        => FindingComparison.Compare(
            InspectCompilationReferences(oldContext, subject),
            InspectCompilationReferences(newContext, subject),
            IdentitySetOptions)
            .TransformPairs(pairs =>
                PromoteChangedPayloads<CompilationReferenceInfo>(pairs, null));

    static FindingInspection<CompilationOptionInfo> InspectCompilationOptionsCore(
        IEnumerable<CompilationOptionInfo> options,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            options,
            subject,
            CompilationOptionDescriptor,
            static option => option.Name,
            static option => JoinSortKey(option.Name, option.Value),
            parameterName);

    static FindingInspection<CompilationReferenceInfo> InspectCompilationReferencesCore(
        IEnumerable<CompilationReferenceInfo> references,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            references,
            subject,
            CompilationReferenceDescriptor,
            static reference => NormalizeReferenceName(reference.Name),
            static reference => JoinSortKey(
                NormalizeReferenceName(reference.Name),
                reference.Aliases,
                reference.ModuleVersionId.ToString("D")),
            parameterName);

    static string NormalizeReferenceName(string name)
        => name.Replace('\\', '/');

    static bool IsPdbInspectionFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException;

    static FindingInspection<T> Failed<T>(
        FindingSubject subject,
        FindingDescriptor descriptor,
        string reason,
        Exception exception)
        where T : notnull
        => new FindingInspection<T>.Failed(
            new InspectionError(subject, descriptor, $"{reason} {exception.Message}"));
}
