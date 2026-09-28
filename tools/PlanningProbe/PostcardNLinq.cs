using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis.Planning.Experiments;
using NLinq;

// The postcard's NLinq column: each closing as an NLinq pipeline over the
// struct source MethodRows (every method, type by type). The predicate is
// written per row, as an NLinq author would, without hoisting the type test.
//
// NLinq at 229e243 has Where, Select, Any, Count, Fold, and ToList, but no
// Take, Skip, or TakeLast. Those are composed here in NLinq style, each placed
// where a pipeline author would put it: Take pulls N from Where.Select;
// Skip pulls and discards from Where before Select; TakeLast is a ring fold
// over Where, and Select projects the kept N.
static class PostcardNLinq
{
    public static PostcardAnswer Run(string closing, PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        switch (closing)
        {
            case "exists":
                return new(new MethodRows(reader).Any<MethodRows, MethodRow, IsDenseRow>(default), null, null, false);
            case "count":
                return new(
                    null,
                    new MethodRows(reader)
                        .Where<MethodRows, MethodRow, IsDenseRow>(default)
                        .CountFold<Filter<MethodRow, MethodRows, IsDenseRow>, MethodRow>(),
                    null,
                    false);
            case "rows":
                return new(
                    null,
                    null,
                    new MethodRows(reader)
                        .Where<MethodRows, MethodRow, IsDenseRow>(default)
                        .Select<Filter<MethodRow, MethodRows, IsDenseRow>, MethodRow, MethodTextRow, ToTextRow>(default)
                        .ToList<Map<MethodRow, MethodTextRow, Filter<MethodRow, MethodRows, IsDenseRow>, ToTextRow>, MethodTextRow>(),
                    false);
            case "head":
            {
                // Where.Select.Take(N)
                var rows = new MethodRows(reader)
                    .Where<MethodRows, MethodRow, IsDenseRow>(default)
                    .Select<Filter<MethodRow, MethodRows, IsDenseRow>, MethodRow, MethodTextRow, ToTextRow>(default);
                var list = new List<MethodTextRow>(Postcard.N);
                while (list.Count < Postcard.N)
                {
                    MethodTextRow row = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        break;
                    list.Add(row);
                }

                return new(null, null, list, false);
            }
            case "window":
            {
                // Where.Skip(A - 1).Take(B - A + 1).Select, strict.
                var selected = new MethodRows(reader).Where<MethodRows, MethodRow, IsDenseRow>(default);
                for (int i = 0; i < Postcard.WindowFirst - 1; i++)
                {
                    selected.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return new(null, null, null, true);
                }

                var rows = selected.Select<Filter<MethodRow, MethodRows, IsDenseRow>, MethodRow, MethodTextRow, ToTextRow>(default);
                int take = Postcard.WindowLast - Postcard.WindowFirst + 1;
                var list = new List<MethodTextRow>(take);
                while (list.Count < take)
                {
                    MethodTextRow row = rows.TryGetNext(out bool hasMore);
                    if (!hasMore)
                        return new(null, null, null, true);
                    list.Add(row);
                }

                return new(null, null, list, false);
            }
            case "tail":
            {
                // Where.TakeLast(N).Select: a ring fold keeps the last N rows' units.
                LastN last = new MethodRows(reader)
                    .Where<MethodRows, MethodRow, IsDenseRow>(default)
                    .Fold<Filter<MethodRow, MethodRows, IsDenseRow>, MethodRow, LastN, KeepLast>(new LastN(Postcard.N), default);
                int count = Math.Min(last.Seen, Postcard.N);
                var list = new List<MethodTextRow>(count);
                for (int i = last.Seen - count; i < last.Seen; i++)
                {
                    MethodRow row = last.Ring[i % Postcard.N];
                    list.Add(MethodText.Row(row.Reader, row.Type, row.Method));
                }

                return new(null, null, list, false);
            }
            default:
                throw new ArgumentException(closing);
        }
    }

    struct IsDenseRow : IFunc<MethodRow, bool>
    {
        public bool Invoke(MethodRow row) =>
            ClassifiedScope.IsScopedType(row.Reader, row.Type)
            && ClassifiedScope.IsScopedMethod(row.Reader, row.Method);
    }

    struct ToTextRow : IFunc<MethodRow, MethodTextRow>
    {
        public MethodTextRow Invoke(MethodRow row) => MethodText.Row(row.Reader, row.Type, row.Method);
    }

    sealed class LastN(int n)
    {
        public MethodRow[] Ring { get; } = new MethodRow[n];
        public int Seen;
    }

    struct KeepLast : IFunc<LastN, MethodRow, LastN>
    {
        public LastN Invoke(LastN last, MethodRow row)
        {
            last.Ring[last.Seen++ % last.Ring.Length] = row;
            return last;
        }
    }
}

static class PostcardProbe
{
    public static PostcardAnswer Exec(string variant, string path, PEReader peReader)
    {
        string[] parts = variant.Split(':');
        return parts[2] switch
        {
            "old" => Postcard.Old(parts[1], peReader),
            "mat" or "before" => Postcard.Before(parts[1], peReader),
            "linq" => Postcard.Linq(parts[1], peReader),
            "nlinq" => PostcardNLinq.Run(parts[1], peReader),
            "after" => Postcard.After(parts[1], path, peReader),
            _ => throw new ArgumentException(variant),
        };
    }

    public static string Describe(PostcardAnswer answer)
    {
        if (answer.Failed)
            return "fail";
        if (answer.Exists is bool exists)
            return exists ? "true" : "false";
        if (answer.Count is int count)
            return $"c={count}";
        // FNV-1a over the row text: stable across processes, unlike string.GetHashCode.
        uint hash = 2166136261;
        foreach (MethodTextRow row in answer.Rows!)
        {
            foreach (string part in (string[])[row.Name, "|", row.DeclaringType, "|", row.Signature, "\n"])
            {
                foreach (char c in part)
                    hash = unchecked((hash ^ c) * 16777619);
            }
        }

        return $"n={answer.Rows!.Count};h={hash:X8}";
    }
}
