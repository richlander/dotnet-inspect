using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler;

/// <summary>
/// Adapts decompiled C# method bodies onto the domain-free finding substrate. It reuses
/// <see cref="CSharpBodyDiff"/> to render and canonicalize a method into ordered C# line
/// occurrences, runs the shared <see cref="FindingMatcher"/>, and folds the alignment into
/// <see cref="PairFinding{T}"/> transitions. Semantic C# operations remain a separate projection;
/// this producer reports the raw rendered-line census.
/// </summary>
public static class CSharpFindings
{
    /// <summary>The finding descriptor for a single decompiled C# line occurrence.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("csharp.line", "C# line");

    /// <summary>The descriptor for a failure to inspect a decompiled C# line census.</summary>
    public static readonly FindingDescriptor InspectionDescriptor = new(
        "csharp.inspect",
        "C# inspection");

    /// <summary>
    /// Inspects a single decompiled method body and returns an explicit body, absence, or failure
    /// outcome. A body may contain zero rendered lines.
    /// </summary>
    public static FindingInspection<CSharpCanonicalLine> Inspect(
        MetadataSource source,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        return CSharpBodyDiff.Canonicalize(source, method) switch
        {
            CSharpCanonicalization.Body body =>
                new FindingInspection<CSharpCanonicalLine>.Complete(
                    ProjectAtoms(body.Lines, subject)),
            CSharpCanonicalization.Absent absent =>
                new FindingInspection<CSharpCanonicalLine>.Absent(absent.Detail),
            CSharpCanonicalization.Failed failed =>
                new FindingInspection<CSharpCanonicalLine>.Failed(
                    CreateInspectionError(subject, failed.Reason)),
        };
    }

    public static FindingComparison<CSharpCanonicalLine> Compare(
        MetadataSource oldSource,
        MethodDefinitionHandle oldMethod,
        MetadataSource newSource,
        MethodDefinitionHandle newMethod,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        var oldInspection = Inspect(oldSource, oldMethod, subject);
        var newInspection = Inspect(newSource, newMethod, subject);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    internal static FindingComparison<CSharpCanonicalLine> CompareCanonicalized(
        ImmutableArray<CSharpCanonicalLine> oldLines,
        ImmutableArray<CSharpCanonicalLine> newLines,
        FindingSubject subject,
        int acceptanceThreshold = 100)
        => CompareInspections(
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(oldLines, subject)),
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(newLines, subject)),
            acceptanceThreshold);

    static FindingComparison<CSharpCanonicalLine> CompareInspections(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection,
        int acceptanceThreshold)
        => FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold);

    static InspectionError CreateInspectionError(FindingSubject subject, string reason)
        => new(subject, InspectionDescriptor, reason);

    static ImmutableArray<Finding<CSharpCanonicalLine>> ProjectAtoms(
        ImmutableArray<CSharpCanonicalLine> lines,
        FindingSubject subject)
    {
        var builder = ImmutableArray.CreateBuilder<Finding<CSharpCanonicalLine>>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            builder.Add(new Finding<CSharpCanonicalLine>(
                subject,
                LineDescriptor,
                new FindingKey(lines[i].IdentityKey),
                i,
                lines[i]));
        }

        return builder.MoveToImmutable();
    }
}
