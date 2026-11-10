using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using InertText;
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
        var owners = new UncertainOwners();
        if (HasPresentedApiDifference(a, before, b, after, owners))
            return FindingDiffAnyState.Changed;

        // Settle with the complete API comparison over only what the
        // uncertain facts can change: the Type row and the Members that own
        // them, by census key, or every Member of either side when the Type
        // itself owns one.
        HashSet<SymbolKey<FindingDiffAnyPart>> keys = owners.MemberKeys(a, unitA, b, unitB);
        if (work.ApiSettler is { } settler)
            return Settle(settler, a, unitA, b, unitB, keys);
        ApiSurface oldSurface;
        ApiSurface newSurface;
        long start = Stopwatch.GetTimestamp();
        try
        {
            work.ApiSurfacesBuilt++;
            oldSurface = FindingDiffAnyApiShapes.Extract(
                a, unitA.Owner, FindingDiffAnyApiShapes.Select(a, unitA.Owner, keys));
            work.ApiSurfacesBuilt++;
            newSurface = FindingDiffAnyApiShapes.Extract(
                b, unitB.Owner, FindingDiffAnyApiShapes.Select(b, unitB.Owner, keys));
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

    // The settler receives the keys expanded, so they name the same Members
    // in any interner, sorted so that equal selections are equal lists; an
    // empty selection asks for the Type's own entry alone.
    static FindingDiffAnyState Settle(
        FindingDiffAnyApiSettler settler, Side a, Unit unitA, Side b, Unit unitB,
        HashSet<SymbolKey<FindingDiffAnyPart>> keys)
    {
        OpaqueByteRange[]? expanded = null;
        if (keys.Count != 0)
        {
            expanded = new OpaqueByteRange[keys.Count];
            int i = 0;
            foreach (SymbolKey<FindingDiffAnyPart> key in keys)
                expanded[i++] = a.Interner.Expand(key, a.Transient).Detach().Range;
            Array.Sort(expanded);
        }
        FindingDiffAnyApiShapeProducer before = FindingDiffAnyApiShapeProducer.Borrow(a);
        FindingDiffAnyApiShapeProducer after = FindingDiffAnyApiShapeProducer.Borrow(b);
        try
        {
            return settler(before, unitA.Owner, after, unitB.Owner, expanded) switch
            {
                FindingExistsDecision.Unchanged => FindingDiffAnyState.Unchanged,
                FindingExistsDecision.Changed => FindingDiffAnyState.Changed,
                _ => FindingDiffAnyState.Indeterminate,
            };
        }
        finally
        {
            // The producers are valid for this call only.
            before.Dispose();
            after.Dispose();
        }
    }

    // The censuses are sorted by serialized key. Compare the kind and first
    // subject part before the payload: a row present on one side differs from
    // two versions of the same row whose flags alone changed. No key detaches.
    // The owners of the uncertain differences. A fact's subject is its first
    // part after the kind: a Member, or a parameter or generic parameter of
    // one, is owned by that Member's row, named by its census key; any other
    // subject, such as the Type itself, makes the Type the owner.
    sealed class UncertainOwners
    {
        readonly HashSet<SymbolKey<FindingDiffAnyPart>> _rows = [];

        public bool TypeWide { get; private set; }

        public void Add(StagedSymbolKey<FindingDiffAnyPart> fact)
        {
            if (TypeWide)
                return;
            var parts = fact.Parts;
            parts.Read();
            parts.Read();
            AddSubject(parts);
        }

        /// <summary>
        /// The census keys of the Members that own the uncertain rows, with
        /// accessors raised into their property or event, on either side; or
        /// every Member of either side when the Type owns a difference. A row
        /// no declared Member owns, such as generated code, is outside the API
        /// surface and selects nothing.
        /// </summary>
        public HashSet<SymbolKey<FindingDiffAnyPart>> MemberKeys(Side a, Unit unitA, Side b, Unit unitB)
        {
            var keys = new HashSet<SymbolKey<FindingDiffAnyPart>>();
            AddMemberKeys(a, unitA.Owner, keys);
            AddMemberKeys(b, unitB.Owner, keys);
            return keys;
        }

        void AddMemberKeys(Side side, TypeDefinitionHandle type, HashSet<SymbolKey<FindingDiffAnyPart>> keys)
        {
            if (TypeWide)
            {
                foreach ((_, SymbolKey<FindingDiffAnyPart> key) in FindingDiffAnyApiShapes.DeclaredMembers(side, type))
                    keys.Add(key);
                return;
            }
            if (_rows.Count == 0)
                return;
            foreach ((EntityHandle row, EntityHandle declaration) in ApiSurfaceExtractor.DeclaredMemberOwners(side.Md, type))
            {
                if (_rows.Contains(side.MemberKey(type, row)))
                    keys.Add(side.MemberKey(type, declaration));
            }
        }

        void AddSubject(SymbolKeyPartReader<FindingDiffAnyPart> part)
        {
            if (part.Kind != SymbolKeyPartKind.Key)
            {
                TypeWide = true;
                return;
            }
            var key = part.NestedParts;
            key.Read();
            if (key.Marker is FindingDiffAnyPart.Parameter or FindingDiffAnyPart.GenericParameter)
            {
                key.Read();
                AddSubject(key);
                return;
            }
            if (key.Marker is not (FindingDiffAnyPart.Method or FindingDiffAnyPart.Field
                or FindingDiffAnyPart.Property or FindingDiffAnyPart.Event))
            {
                TypeWide = true;
                return;
            }
            _rows.Add(new SymbolKey<FindingDiffAnyPart>(OpaqueByteRange.Copy(part.CopyPayload())));
        }
    }

    static bool HasPresentedApiDifference(
        Side a, List<SymbolKeySlot> before, Side b, List<SymbolKeySlot> after, UncertainOwners owners)
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
                owners.Add(x);
                i++;
                j++;
            }
            else if (order < 0)
            {
                if (PresentedPresence(x))
                    return true;
                owners.Add(x);
                i++;
            }
            else
            {
                if (PresentedPresence(y))
                    return true;
                owners.Add(y);
                j++;
            }
        }
        for (; i < before.Count; i++)
        {
            var x = a.Transient.Resolve(before[i]);
            if (PresentedPresence(x))
                return true;
            owners.Add(x);
        }
        for (; j < after.Count; j++)
        {
            var y = b.Transient.Resolve(after[j]);
            if (PresentedPresence(y))
                return true;
            owners.Add(y);
        }
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
