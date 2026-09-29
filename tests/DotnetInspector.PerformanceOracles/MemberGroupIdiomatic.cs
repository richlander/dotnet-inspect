using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using NLinq;

namespace DotnetInspector.PerformanceOracles;

/// <summary>
/// The idiomatic NLinq column: the exact-overload population composed from
/// NLinq operators over the model's method handles rather than the hand-fused
/// fold. It uses the same Selection.Classify and Analysis.Project and returns
/// the same outcome shape so the answer check compares it to the fold.
/// </summary>
internal static class MemberGroupIdiomatic
{
    internal static MetadataMethodGroupInspectionOutcome Execute(
        MetadataMethodGroupInspection.Analysis model,
        MetadataMethodGroupInspection.Selection selection,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        int maximumMembers,
        int maximumRetainedTextCharacters)
    {
        // Count pipeline. Take(max + 1) is the member bound: it stops one
        // past the bound, so the measured value equals the fold's.
        int count = new MethodHandles(model.Methods)
            .Where<MethodHandles, MethodDefinitionHandle, IsMatch>(new(selection))
            .Take<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>(maximumMembers + 1)
            .Count<TakeEnumerator<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>, MethodDefinitionHandle>();
        if (count > maximumMembers)
        {
            return new MetadataMethodGroupInspectionOutcome.Incomplete(
                MetadataMethodGroupInspectionBound.Members, maximumMembers, count);
        }

        // Group existence is a different predicate than the match: a group
        // whose every member is filtered out is an empty Read, not NotFound.
        if (count == 0
            && !new MethodHandles(model.Methods)
                .Any<MethodHandles, MethodDefinitionHandle, IsInGroup>(new(selection)))
        {
            return new MetadataMethodGroupInspectionOutcome.MemberGroupNotFound();
        }

        if (startOrdinal > count || (startOrdinal == count && count != 0))
            return Read(model, count, [], null, continuationOutOfRange: true);
        if (!materializeRows)
            return Read(model, count, [], null);

        // Rows pipeline: Where, Skip before Select, Take, Select(project).
        List<MetadataMethodGroupRow> window;
        try
        {
            window = new MethodHandles(model.Methods)
                .Where<MethodHandles, MethodDefinitionHandle, IsMatch>(new(selection))
                .Skip<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>(startOrdinal)
                .Take<SkipEnumerator<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>, MethodDefinitionHandle>(maximumRows)
                .Select<TakeEnumerator<SkipEnumerator<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>, MethodDefinitionHandle>, MethodDefinitionHandle, MetadataMethodGroupRow, ProjectRow>(new(selection))
                .ToList<Map<MethodDefinitionHandle, MetadataMethodGroupRow, TakeEnumerator<SkipEnumerator<Filter<MethodDefinitionHandle, MethodHandles, IsMatch>, MethodDefinitionHandle>, MethodDefinitionHandle>, ProjectRow>, MetadataMethodGroupRow>();
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Read(model, count, [], null, rowsFailed: true);
        }

        // Retained-text budget over the projected window.
        long retained = 0;
        foreach (MetadataMethodGroupRow row in window)
        {
            retained = checked(retained
                + row.DisplaySignature.Length + row.CanonicalSignature.Length
                + row.Fingerprint.Length + row.Accessibility.Length);
            if (retained > maximumRetainedTextCharacters)
                return Read(model, count, [], null, incompleteRetainedTextCharacters: retained);
        }

        int next = checked(startOrdinal + window.Count);
        return Read(model, count, [.. window], next < count ? next : null);
    }

    private static MetadataMethodGroupInspectionOutcome.Read Read(
        MetadataMethodGroupInspection.Analysis model,
        int count,
        ImmutableArray<MetadataMethodGroupRow> rows,
        int? nextOrdinal,
        bool continuationOutOfRange = false,
        long? incompleteRetainedTextCharacters = null,
        bool rowsFailed = false) =>
        new(model.DeclaringType, MetadataTokens.GetToken(model.TypeHandle), count, rows,
            nextOrdinal, continuationOutOfRange, incompleteRetainedTextCharacters, rowsFailed);

    private readonly struct IsMatch(MetadataMethodGroupInspection.Selection selection)
        : IFunc<MethodDefinitionHandle, bool>
    {
        public bool Invoke(MethodDefinitionHandle handle) =>
            selection.Classify(handle, selection.Model.GetMethod(handle))
                is MetadataMethodGroupInspection.CandidateKind.Selected;
    }

    private readonly struct IsInGroup(MetadataMethodGroupInspection.Selection selection)
        : IFunc<MethodDefinitionHandle, bool>
    {
        public bool Invoke(MethodDefinitionHandle handle) =>
            selection.Classify(handle, selection.Model.GetMethod(handle))
                is not MetadataMethodGroupInspection.CandidateKind.OutsideGroup;
    }

    private readonly struct ProjectRow(MetadataMethodGroupInspection.Selection selection)
        : IFunc<MethodDefinitionHandle, MetadataMethodGroupRow>
    {
        public MetadataMethodGroupRow Invoke(MethodDefinitionHandle handle) =>
            selection.Model.Project(handle, selection.ReceiverFor(selection.Model.GetMethod(handle)));
    }

    internal struct MethodHandles : NLinq.IEnumerator<MethodHandles, MethodDefinitionHandle>
    {
        private MethodDefinitionHandleCollection.Enumerator _handles;

        internal MethodHandles(MethodDefinitionHandleCollection handles) =>
            _handles = handles.GetEnumerator();

        public MethodDefinitionHandle TryGetNext(out bool hasMore)
        {
            hasMore = _handles.MoveNext();
            return hasMore ? _handles.Current : default;
        }
    }
}
