using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using NLinq;

return OwnershipIncidenceScorecardRunner.Run(args);

internal static class OwnershipIncidenceScorecardRunner
{
    // Rows issues every population member's slice (full demand). Head
    // issues only the first SparseMethods members (point-lookup demand, the
    // shape of most product consumers).
    static readonly ScorecardClosing[] Questions =
        [ScorecardClosing.Rows, ScorecardClosing.Head];

    const int SparseMethods = 6;

    public static int Run(string[] args)
    {
        if (!ScorecardCommandLine.TryParse(
                args,
                out ScorecardOptions? options,
                out string? error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        IReadOnlyList<ScorecardAsset<IncidenceAsset>> assets =
            LoadAssets(options!.Assets);
        var oracle =
            new ScorecardColumn<
                IncidenceAsset,
                IncidenceAnswer>(
                "NLinq",
                NLinqAnswer);
        ScorecardColumn<IncidenceAsset, IncidenceAnswer>[] columns =
        [
            new("LINQ", LinqAnswer),
            oracle,
            new("Product", ProductAnswer),
        ];
        foreach (ScorecardAsset<IncidenceAsset> asset in assets)
        {
            IncidenceAnswer incidence =
                oracle.Answer(
                    ScorecardClosing.Rows,
                    asset.Asset).Rows![0];
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"# asset: {asset.Name},"
                    + $" methods={incidence.MethodTokens.Length},"
                    + $" calls={incidence.InputCallCount},"
                    + $" groups={incidence.DirectCallsByMethod.Count},"
                    + $" runs={Runs(asset.Asset.DirectCalls)}"));
            if (incidence.IssuedCallCount
                != incidence.InputCallCount)
            {
                Console.Error.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{asset.Name}: incidence issued"
                        + $" {incidence.IssuedCallCount} of"
                        + $" {incidence.InputCallCount} direct calls."));
                return 1;
            }
        }
        ScorecardCheck check = Scorecard.Check(
            assets,
            oracle,
            columns,
            RowText,
            IncidenceAnswerComparer.Instance,
            Questions);
        foreach (ScorecardMismatch mismatch in check.Mismatches)
        {
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mismatch\t{mismatch.Asset}"
                    + $"\t{mismatch.Column}"
                    + $"\t{mismatch.Answer}"
                    + $"\toracle={mismatch.OracleAnswer}"));
        }
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# answers: {check.Compared} compared,"
                + $" {check.Mismatches.Count} mismatches"));
        if (!check.Agrees)
            return 1;
        if (options.Command == ScorecardCommand.Check)
            return 0;

        foreach (ScorecardAsset<IncidenceAsset> asset in assets)
        {
            foreach (ScorecardClosing closing in Questions)
            {
                foreach (var column in columns)
                {
                    _ = column.Answer(closing, asset.Asset);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    _ = column.Answer(closing, asset.Asset);
                    long bytes =
                        GC.GetAllocatedBytesForCurrentThread() - before;
                    Console.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"# alloc\t{asset.Name}\t{closing}"
                            + $"\t{column.Name}\t{bytes}"));
                }
            }
        }

        IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
            assets,
            columns,
            options.Timing,
            progress: Console.Error.WriteLine,
            closings: Questions);
        if (options.TsvPath is not null)
        {
            using var tsv =
                new StreamWriter(options.TsvPath, append: false);
            Scorecard.WriteTsv(cells, tsv);
        }

        Console.Write(
            Scorecard.Report(
                cells,
                oracle.Name,
                new ScorecardShape()));
        return 0;
    }

    static IReadOnlyList<ScorecardAsset<IncidenceAsset>>
        LoadAssets(IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names =
            ScorecardAssetNames.FromPaths(paths);
        var assets =
            new List<ScorecardAsset<IncidenceAsset>>(
                paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                paths[i],
                LibraryBodyAnalysisFeatures.MethodEvidence);
            assets.Add(
                new(
                    names[i],
                    new(
                        index,
                        index.Methods,
                        [.. index.Methods.Take(SparseMethods)],
                        index.DirectCalls)));
        }
        return assets;
    }

    static ScorecardAnswer<IncidenceAnswer> NLinqAnswer(
        ScorecardClosing closing,
        IncidenceAsset asset) =>
        Answer(
            closing,
            asset,
            asset.DirectCalls
                .AsNLinq()
                .ToImmutableLookup<
                    ImmutableArrayEnumerator<DirectCall>,
                    DirectCall,
                    int,
                    EvidenceMethodToken>(default));

    static ScorecardAnswer<IncidenceAnswer> LinqAnswer(
        ScorecardClosing closing,
        IncidenceAsset asset) =>
        Answer(
            closing,
            asset,
            asset.DirectCalls
                .GroupBy(static call =>
                    call.EvidenceMethod.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToImmutableArray()));

    static ScorecardAnswer<IncidenceAnswer> ProductAnswer(
        ScorecardClosing closing,
        IncidenceAsset asset)
    {
        asset.Index.ReleaseCallGraphCaches();
        return Answer(
            closing,
            asset,
            asset.Index.GetDirectCallsByEvidenceMethod());
    }

    static ScorecardAnswer<IncidenceAnswer> Answer(
        ScorecardClosing closing,
        IncidenceAsset asset,
        IReadOnlyDictionary<
            int,
            ImmutableArray<DirectCall>> directCallsByMethod)
    {
        ImmutableArray<MethodIdentity> methods = closing switch
        {
            ScorecardClosing.Rows => asset.Methods,
            ScorecardClosing.Head => asset.SparseMethods,
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
        return ScorecardAnswer<IncidenceAnswer>.OfRows(
            [
                IncidenceAnswer.Create(
                    methods,
                    asset.DirectCalls.Length,
                    directCallsByMethod),
            ]);
    }

    static int Runs(ImmutableArray<DirectCall> calls)
    {
        int runs = 0;
        int previous = 0;
        for (int i = 0; i < calls.Length; i++)
        {
            int token = calls[i].EvidenceMethod.MetadataToken;
            if (i == 0 || token != previous)
                runs++;
            previous = token;
        }
        return runs;
    }

    static string RowText(IncidenceAnswer answer)
    {
        var text = new StringBuilder();
        text.Append("methods=")
            .Append(answer.MethodTokens.Length)
            .Append(";calls=")
            .Append(answer.InputCallCount)
            .Append(";issued=")
            .Append(answer.IssuedCallCount);
        foreach (int token in answer.MethodTokens)
        {
            text.Append('|')
                .Append(
                    token.ToString(
                        "X8",
                        CultureInfo.InvariantCulture));
            if (!answer.DirectCallsByMethod.TryGetValue(
                    token,
                    out ImmutableArray<DirectCall> calls))
            {
                continue;
            }
            foreach (DirectCall call in calls)
            {
                text.Append('@')
                    .Append(
                        call.ILOffset.ToString(
                            "X",
                            CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(
                        call.OperandToken.ToString(
                            "X8",
                            CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(call.Kind);
            }
        }
        return text.ToString();
    }

    sealed record IncidenceAsset(
        LibraryBodyIndex Index,
        ImmutableArray<MethodIdentity> Methods,
        ImmutableArray<MethodIdentity> SparseMethods,
        ImmutableArray<DirectCall> DirectCalls);

    sealed class IncidenceAnswer
    {
        IncidenceAnswer(
            ImmutableArray<int> methodTokens,
            int inputCallCount,
            int issuedCallCount,
            IReadOnlyDictionary<
                int,
                ImmutableArray<DirectCall>> directCallsByMethod)
        {
            MethodTokens = methodTokens;
            InputCallCount = inputCallCount;
            IssuedCallCount = issuedCallCount;
            DirectCallsByMethod = directCallsByMethod;
        }

        public ImmutableArray<int> MethodTokens { get; }

        public int InputCallCount { get; }

        public int IssuedCallCount { get; }

        public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            DirectCallsByMethod { get; }

        public static IncidenceAnswer Create(
            ImmutableArray<MethodIdentity> methods,
            int inputCallCount,
            IReadOnlyDictionary<
                int,
                ImmutableArray<DirectCall>> directCallsByMethod)
        {
            ImmutableArray<int> methodTokens =
            [
                .. methods.Select(static method =>
                    method.MetadataToken),
            ];
            int issuedCallCount = 0;
            foreach (int token in methodTokens)
            {
                if (directCallsByMethod.TryGetValue(
                        token,
                        out ImmutableArray<DirectCall> directCalls))
                {
                    issuedCallCount += directCalls.Length;
                }
            }
            return new(
                methodTokens,
                inputCallCount,
                issuedCallCount,
                directCallsByMethod);
        }
    }

    sealed class IncidenceAnswerComparer :
        IEqualityComparer<IncidenceAnswer>
    {
        public static IncidenceAnswerComparer Instance { get; } =
            new();

        public bool Equals(
            IncidenceAnswer? left,
            IncidenceAnswer? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null
                || right is null
                || left.InputCallCount != right.InputCallCount
                || left.IssuedCallCount != right.IssuedCallCount
                || left.DirectCallsByMethod.Count
                    != right.DirectCallsByMethod.Count
                || !left.MethodTokens.AsSpan().SequenceEqual(
                    right.MethodTokens.AsSpan()))
            {
                return false;
            }

            foreach ((
                int token,
                ImmutableArray<DirectCall> leftCalls)
                in left.DirectCallsByMethod)
            {
                if (!right.DirectCallsByMethod.TryGetValue(
                        token,
                        out ImmutableArray<DirectCall> rightCalls)
                    || !leftCalls.AsSpan().SequenceEqual(
                        rightCalls.AsSpan()))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(IncidenceAnswer answer) =>
            HashCode.Combine(
                answer.MethodTokens.Length,
                answer.InputCallCount,
                answer.IssuedCallCount,
                answer.DirectCallsByMethod.Count);
    }

    readonly struct EvidenceMethodToken :
        IFunc<DirectCall, int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Invoke(DirectCall call) =>
            call.EvidenceMethod.MetadataToken;
    }
}
