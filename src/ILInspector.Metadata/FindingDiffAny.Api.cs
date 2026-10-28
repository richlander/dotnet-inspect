using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using Inspector.Findings;

namespace ILInspector.Metadata;

public static partial class FindingDiffAny
{
    internal static FindingDiffAnyState ApiState(
        Side a, Unit unitA, List<SymbolKeySlot> before,
        Side b, Unit unitB, List<SymbolKeySlot> after, Work work)
    {
        if (CensusesEqual(a, before, b, after))
            return FindingDiffAnyState.Unchanged;
        if (HasPresentedApiDifference(a, before, b, after))
            return FindingDiffAnyState.Changed;

        ApiSurface oldSurface;
        ApiSurface newSurface;
        long start = Stopwatch.GetTimestamp();
        try
        {
            work.ApiSurfacesBuilt++;
            oldSurface = ApiSurfaceExtractor.ExtractDeclarations(
                a.Pe, ApiSurfaceExtractionScope.ApiAxis, type => type == unitA.Owner);
            work.ApiSurfacesBuilt++;
            newSurface = ApiSurfaceExtractor.ExtractDeclarations(
                b.Pe, ApiSurfaceExtractionScope.ApiAxis, type => type == unitB.Owner);
        }
        finally
        {
            work.ApiSurfaceTicks += Stopwatch.GetTimestamp() - start;
        }
        ApiFindingComparison comparison = MetadataFindings.CompareApi(
            oldSurface, newSurface, new FindingSubject("api", "Selected Type API"),
            new ApiDiffOptions(ApiDiffScope.All));
        if (comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
            || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete
            || comparison.ApiDiff.InspectionFailures.Count != 0)
        {
            return FindingDiffAnyState.Indeterminate;
        }
        return comparison.ApiDiff.IsEmpty ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed;
    }

    // The censuses are sorted by serialized key. Compare the kind and first
    // subject part before the payload: a row present on one side differs from
    // two versions of the same row whose flags alone changed. No key detaches.
    static bool HasPresentedApiDifference(Side a, List<SymbolKeySlot> before, Side b, List<SymbolKeySlot> after)
    {
        int i = 0, j = 0;
        while (i < before.Count && j < after.Count)
        {
            StagedSymbolKey<FindingDiffAnyPart> x = a.Transient.Resolve(before[i]);
            StagedSymbolKey<FindingDiffAnyPart> y = b.Transient.Resolve(after[j]);
            if (x.Equals(y))
            {
                i++;
                j++;
                continue;
            }
            var px = x.Parts;
            var py = y.Parts;
            px.Read();
            py.Read();
            if (x.Kind is FindingDiffAnyPart.Visible or FindingDiffAnyPart.Hidden
                || y.Kind is FindingDiffAnyPart.Visible or FindingDiffAnyPart.Hidden)
                return true;
            px.Read();
            py.Read();
            int order = px.ComparePrefix(py);
            if (order == 0)
            {
                if (PresentedPair(x.Kind, ref px, ref py))
                    return true;
                i++;
                j++;
            }
            else if (order < 0)
            {
                if (PresentedPresence(x))
                    return true;
                i++;
            }
            else
            {
                if (PresentedPresence(y))
                    return true;
                j++;
            }
        }
        for (; i < before.Count; i++)
            if (PresentedPresence(a.Transient.Resolve(before[i])))
                return true;
        for (; j < after.Count; j++)
            if (PresentedPresence(b.Transient.Resolve(after[j])))
                return true;
        return false;
    }

    static bool PresentedPresence(StagedSymbolKey<FindingDiffAnyPart> fact)
    {
        if (fact.Kind is FindingDiffAnyPart.InterfaceFact or FindingDiffAnyPart.Constraint
            or FindingDiffAnyPart.Visible or FindingDiffAnyPart.Hidden)
            return true;
        if (fact.Kind is not (FindingDiffAnyPart.MethodFact or FindingDiffAnyPart.FieldFact
            or FindingDiffAnyPart.PropertyFact or FindingDiffAnyPart.EventFact
            or FindingDiffAnyPart.GenericParameterFact))
            return false;
        var parts = fact.Parts;
        parts.Read();
        parts.Read();
        return OrdinarySubject(parts) && !HasModifier(parts.NestedParts, 0);
    }

    static bool PresentedPair(FindingDiffAnyPart kind,
        ref SymbolKeyPartReader<FindingDiffAnyPart> x,
        ref SymbolKeyPartReader<FindingDiffAnyPart> y)
    {
        if (kind is FindingDiffAnyPart.InterfaceFact or FindingDiffAnyPart.Constraint)
            return true;
        if (!OrdinarySubject(x) || !OrdinarySubject(y))
            return false;
        switch (kind)
        {
            case FindingDiffAnyPart.MethodFact:
                x.Read();
                y.Read();
                const MethodAttributes methods = MethodAttributes.MemberAccessMask
                    | MethodAttributes.Static | MethodAttributes.Abstract
                    | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
                return ((x.Int ^ y.Int) & (int)methods) != 0;
            case FindingDiffAnyPart.TypeFact:
                x.Read();
                y.Read();
                const TypeAttributes types = TypeAttributes.VisibilityMask | TypeAttributes.Interface
                    | TypeAttributes.Abstract | TypeAttributes.Sealed;
                if (((x.Int ^ y.Int) & (int)types) != 0)
                    return true;
                x.Read();
                y.Read();
                return !x.SamePart(y);
            case FindingDiffAnyPart.ParameterFact:
                var parameter = x.NestedParts;
                parameter.Read();
                parameter.Read();
                parameter.Read();
                if (parameter.Int == 0)
                    return false;
                x.Read();
                y.Read();
                if (!x.SamePart(y))
                    return true;
                x.Read();
                y.Read();
                if (((x.Int ^ y.Int) & (int)ParameterAttributes.Out) != 0)
                    return true;
                // Defaults are uncertain: a changed enum value can still
                // present the same symbolic default (E.A) on both sides.
                return false;
            case FindingDiffAnyPart.GenericParameterFact:
                x.Read();
                y.Read();
                if (!x.SamePart(y))
                    return true;
                x.Read();
                y.Read();
                return ((x.Int ^ y.Int) & (int)GenericParameterAttributes.SpecialConstraintMask) != 0;
            default:
                return false;
        }
    }

    static bool OrdinarySubject(SymbolKeyPartReader<FindingDiffAnyPart> part)
    {
        if (part.Kind == SymbolKeyPartKind.Ref)
            return true;
        if (part.Kind != SymbolKeyPartKind.Key)
            return false;
        var key = part.NestedParts;
        key.Read();
        if (key.Marker is FindingDiffAnyPart.Parameter or FindingDiffAnyPart.GenericParameter)
        {
            key.Read();
            return OrdinarySubject(key);
        }
        if (key.Marker is not (FindingDiffAnyPart.Method or FindingDiffAnyPart.Field
            or FindingDiffAnyPart.Property or FindingDiffAnyPart.Event))
            return true;
        key.Read();
        key.Read();
        return !key.PayloadStartsWith((byte)'<');
    }

    static bool HasModifier(SymbolKeyPartReader<FindingDiffAnyPart> parts, int depth)
    {
        if (depth > SignatureBlobGuard.DefaultMaxDepth)
            return true;
        while (parts.Read())
        {
            if (parts.Kind == SymbolKeyPartKind.Marker
                && parts.Marker is FindingDiffAnyPart.RequiredModifier or FindingDiffAnyPart.OptionalModifier)
                return true;
            if (parts.Kind == SymbolKeyPartKind.Key && HasModifier(parts.NestedParts, depth + 1))
                return true;
        }
        return false;
    }
}
