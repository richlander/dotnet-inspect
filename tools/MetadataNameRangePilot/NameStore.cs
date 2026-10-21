using InertText;
using InertText.Encoding;

namespace MetadataNameRangePilot;

internal enum NameStrategy
{
    EagerInert,
    PerNameOpaqueMeasured,
    PopulationOpaqueMeasured,
    PopulationOpaqueBytes,
    PopulationSlotMeasured,
    PopulationSlotBytes,
}

internal readonly record struct DisplayedName(
    NameRole Role,
    InertString Value);

internal abstract class NameStore
{
    internal abstract int Count { get; }

    internal abstract long SourceBytes { get; }

    internal abstract long Budget { get; }

    internal abstract int CountMatches(
        ReadOnlySpan<byte> query,
        InertString containedQuery);

    internal abstract DisplayedName[] SelectAndProject(
        ReadOnlySpan<byte> query,
        InertString containedQuery,
        int expectedMatches);

    internal abstract DisplayedName[] ProjectAll();

    internal static NameStore Build(
        NameAsset asset,
        NameStrategy strategy,
        int copies) =>
        strategy switch
        {
            NameStrategy.EagerInert =>
                EagerNameStore.Build(asset, copies),
            NameStrategy.PerNameOpaqueMeasured =>
                PerNameOpaqueStore.Build(asset, copies),
            NameStrategy.PopulationOpaqueMeasured =>
                PopulationOpaqueStore.Build(
                    asset,
                    copies,
                    measureContainedLength: true),
            NameStrategy.PopulationOpaqueBytes =>
                PopulationOpaqueStore.Build(
                    asset,
                    copies,
                    measureContainedLength: false),
            NameStrategy.PopulationSlotMeasured =>
                PopulationSlotStore.Build(
                    asset,
                    copies,
                    measureContainedLength: true),
            NameStrategy.PopulationSlotBytes =>
                PopulationSlotStore.Build(
                    asset,
                    copies,
                    measureContainedLength: false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy)),
        };
}

internal readonly record struct EagerName(
    NameRole Role,
    InertString Value);

internal sealed class EagerNameStore(
    EagerName[] names,
    long sourceBytes,
    long budget) : NameStore
{
    readonly EagerName[] _names = names;

    internal override int Count => _names.Length;

    internal override long SourceBytes => sourceBytes;

    internal override long Budget => budget;

    internal static EagerNameStore Build(
        NameAsset asset,
        int copies)
    {
        var names = new EagerName[
            checked(asset.NameCount * copies)];
        long budget = 0;
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                string text = asset.ReadString(entry.Handle);

                // The declaration baseline measures the contained length, then constructs the
                // retained value. Constructing twice models those two ordinary-name scans while
                // retaining the original string in both passes.
                InertString measured = new(
                    TextPolicy.Field,
                    text);
                budget = checked(budget + measured.Length);
                names[index++] = new(
                    entry.Role,
                    new InertString(
                        TextPolicy.Field,
                        text));
            }
        }
        return new(
            names,
            checked((long)asset.Utf8Bytes * copies),
            budget);
    }

    internal override int CountMatches(
        ReadOnlySpan<byte> query,
        InertString containedQuery)
    {
        int matches = 0;
        foreach (EagerName name in _names)
        {
            if (name.Value == containedQuery)
                matches++;
        }
        return matches;
    }

    internal override DisplayedName[] SelectAndProject(
        ReadOnlySpan<byte> query,
        InertString containedQuery,
        int expectedMatches)
    {
        var result = new List<DisplayedName>(expectedMatches);
        foreach (EagerName name in _names)
        {
            if (name.Value == containedQuery)
                result.Add(new(name.Role, name.Value));
        }
        return [.. result];
    }

    internal override DisplayedName[] ProjectAll()
    {
        var result = new DisplayedName[_names.Length];
        for (int index = 0; index < result.Length; index++)
        {
            EagerName name = _names[index];
            result[index] = new(name.Role, name.Value);
        }
        return result;
    }
}

internal readonly record struct OpaqueName(
    NameRole Role,
    OpaqueByteRange Range)
{
    internal bool Matches(ReadOnlySpan<byte> query) =>
        Utf8RangeClassification.Read(Range)
            .ValueTextEquals(query);

    internal InertString Project() =>
        new InertUtf8String(
            TextPolicy.Field,
            Utf8RangeClassification.Read(Range))
        .ToInertString();

    internal int MeasureContainedLength() =>
        Utf8VisualEncoder.MeasureInertStringLength(
            TextPolicy.Field,
            Utf8RangeClassification.Read(Range));
}

internal abstract class OpaqueNameStore(
    OpaqueName[] names,
    long sourceBytes,
    long budget) : NameStore
{
    readonly OpaqueName[] _names = names;

    internal override int Count => _names.Length;

    internal override long SourceBytes => sourceBytes;

    internal override long Budget => budget;

    internal override int CountMatches(
        ReadOnlySpan<byte> query,
        InertString containedQuery)
    {
        int matches = 0;
        foreach (OpaqueName name in _names)
        {
            if (name.Matches(query))
                matches++;
        }
        return matches;
    }

    internal override DisplayedName[] SelectAndProject(
        ReadOnlySpan<byte> query,
        InertString containedQuery,
        int expectedMatches)
    {
        var result = new List<DisplayedName>(expectedMatches);
        foreach (OpaqueName name in _names)
        {
            if (name.Matches(query))
                result.Add(new(name.Role, name.Project()));
        }
        return [.. result];
    }

    internal override DisplayedName[] ProjectAll()
    {
        var result = new DisplayedName[_names.Length];
        for (int index = 0; index < result.Length; index++)
        {
            OpaqueName name = _names[index];
            result[index] = new(
                name.Role,
                name.Project());
        }
        return result;
    }
}

internal sealed class PerNameOpaqueStore(
    OpaqueName[] names,
    long sourceBytes,
    long budget) : OpaqueNameStore(
        names,
        sourceBytes,
        budget)
{
    internal static PerNameOpaqueStore Build(
        NameAsset asset,
        int copies)
    {
        var names = new OpaqueName[
            checked(asset.NameCount * copies)];
        long budget = 0;
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                var name = new OpaqueName(
                    entry.Role,
                    asset.CopyRange(entry.Handle));
                budget = checked(
                    budget + name.MeasureContainedLength());
                names[index++] = name;
            }
        }
        return new(
            names,
            checked((long)asset.Utf8Bytes * copies),
            budget);
    }
}

internal sealed class PopulationOpaqueStore(
    OpaqueName[] names,
    long sourceBytes,
    long budget) : OpaqueNameStore(
        names,
        sourceBytes,
        budget)
{
    internal static PopulationOpaqueStore Build(
        NameAsset asset,
        int copies,
        bool measureContainedLength)
    {
        int count = checked(asset.NameCount * copies);
        int byteCount = checked(asset.Utf8Bytes * copies);
        var roles = new NameRole[count];
        var slots = new OpaqueByteRangeSlot[count];
        var builder = new OpaqueByteRangeBuilder(byteCount);
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                roles[index] = entry.Role;
                slots[index] = asset.Append(
                    builder,
                    entry.Handle);
                index++;
            }
        }

        OpaqueByteRange population = builder.Freeze();
        var names = new OpaqueName[count];
        long budget = 0;
        for (index = 0; index < names.Length; index++)
        {
            var name = new OpaqueName(
                roles[index],
                population.Slice(slots[index]));
            budget = checked(
                budget
                + (measureContainedLength
                    ? name.MeasureContainedLength()
                    : name.Range.Length));
            names[index] = name;
        }
        return new(
            names,
            byteCount,
            budget);
    }
}

internal sealed class PopulationSlotStore(
    NameRole[] roles,
    OpaqueByteRange population,
    OpaqueByteRangeSlot[] slots,
    long sourceBytes,
    long budget) : NameStore
{
    readonly NameRole[] _roles = roles;
    readonly OpaqueByteRange _population = population;
    readonly OpaqueByteRangeSlot[] _slots = slots;

    internal override int Count => _slots.Length;

    internal override long SourceBytes => sourceBytes;

    internal override long Budget => budget;

    internal static PopulationSlotStore Build(
        NameAsset asset,
        int copies,
        bool measureContainedLength)
    {
        int count = checked(asset.NameCount * copies);
        int byteCount = checked(asset.Utf8Bytes * copies);
        var roles = new NameRole[count];
        var slots = new OpaqueByteRangeSlot[count];
        var builder = new OpaqueByteRangeBuilder(byteCount);
        int index = 0;
        for (int copy = 0; copy < copies; copy++)
        {
            foreach (NameEntry entry in asset.Entries)
            {
                roles[index] = entry.Role;
                slots[index] = asset.Append(
                    builder,
                    entry.Handle);
                index++;
            }
        }

        OpaqueByteRange population = builder.Freeze();
        long budget = 0;
        foreach (OpaqueByteRangeSlot slot in slots)
        {
            OpaqueByteRange range = population.Slice(slot);
            budget = checked(
                budget
                + (measureContainedLength
                    ? Utf8VisualEncoder.MeasureInertStringLength(
                        TextPolicy.Field,
                        Utf8RangeClassification.Read(range))
                    : range.Length));
        }
        return new(
            roles,
            population,
            slots,
            byteCount,
            budget);
    }

    internal override int CountMatches(
        ReadOnlySpan<byte> query,
        InertString containedQuery)
    {
        int matches = 0;
        foreach (OpaqueByteRangeSlot slot in _slots)
        {
            if (Utf8RangeClassification
                .Read(_population.Slice(slot))
                .ValueTextEquals(query))
            {
                matches++;
            }
        }
        return matches;
    }

    internal override DisplayedName[] SelectAndProject(
        ReadOnlySpan<byte> query,
        InertString containedQuery,
        int expectedMatches)
    {
        var result = new List<DisplayedName>(expectedMatches);
        for (int index = 0; index < _slots.Length; index++)
        {
            OpaqueByteRange range =
                _population.Slice(_slots[index]);
            Utf8RangeReader reader =
                Utf8RangeClassification.Read(range);
            if (reader.ValueTextEquals(query))
            {
                result.Add(new(
                    _roles[index],
                    new InertUtf8String(
                        TextPolicy.Field,
                        reader)
                    .ToInertString()));
            }
        }
        return [.. result];
    }

    internal override DisplayedName[] ProjectAll()
    {
        var result = new DisplayedName[_slots.Length];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = new(
                _roles[index],
                new InertUtf8String(
                    TextPolicy.Field,
                    Utf8RangeClassification.Read(
                        _population.Slice(_slots[index])))
                .ToInertString());
        }
        return result;
    }
}
