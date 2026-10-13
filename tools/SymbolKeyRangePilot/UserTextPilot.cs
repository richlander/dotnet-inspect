using System.Diagnostics;
using System.Globalization;
using System.Text;

using InertText;

namespace SymbolKeyRangePilot;

internal sealed class UserSearchValue
{
    readonly byte[] _utf8;

    internal UserSearchValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        _utf8 = Encoding.UTF8.GetBytes(text);
    }

    internal string Text { get; }

    internal ReadOnlySpan<byte> Utf8 => _utf8;
}

internal readonly struct UserTextMatch(string text)
{
    readonly string? _text = text;

    internal string Text => _text ?? string.Empty;
}

internal enum UserTextScenario
{
    ScanAndPresent,
    MatchedNamePresentation,
}

internal enum UserTextStrategy
{
    DecodeAndContain,
    ReuseHostText,
}

internal readonly record struct UserTextCell(
    UserTextScenario Scenario,
    UserTextStrategy Strategy);

internal readonly record struct UserTextPilotSample(
    int Round,
    int Order,
    UserTextScenario Scenario,
    UserTextStrategy Strategy,
    double ElapsedMicroseconds,
    long AllocatedBytes,
    int Checksum);

internal sealed record UserTextPilotResult(
    string Asset,
    int Copies,
    int Keys,
    string Query,
    int QueryMatches,
    IReadOnlyList<UserTextPilotSample> Samples);

internal sealed class UserTextPayload
{
    readonly RangeKeyStore _store;
    readonly UserSearchValue _search;
    readonly OpaqueByteRange _matchedName;
    readonly UserTextMatch _match;
    readonly UserTextCell _cell;

    internal UserTextPayload(
        RangeKeyStore store,
        UserSearchValue search,
        UserTextCell cell)
    {
        _store = store;
        _search = search;
        _cell = cell;
        (_matchedName, _match) = FindFirstMatch(store, search);
    }

    internal int Consume() =>
        _cell.Scenario switch
        {
            UserTextScenario.ScanAndPresent =>
                ScanAndPresent(),
            UserTextScenario.MatchedNamePresentation =>
                PresentMatchedName(),
            _ => throw new ArgumentOutOfRangeException(),
        };

    int ScanAndPresent()
    {
        int hash = 17;
        int matches = 0;
        for (int index = 0; index < _store.Count; index++)
        {
            string? text = _cell.Strategy switch
            {
                UserTextStrategy.DecodeAndContain =>
                    PresentNormally(_store.NameReader(index), _search),
                UserTextStrategy.ReuseHostText =>
                    PresentMatchedUserText(
                        _store.NameReader(index),
                        _search),
                _ => throw new ArgumentOutOfRangeException(),
            };
            if (text is null)
                continue;
            matches++;
            hash = AddText(hash, text);
        }
        return HashCode.Combine(matches, hash);
    }

    int PresentMatchedName()
    {
        int hash = 17;
        for (int index = 0; index < _store.Count; index++)
        {
            string text = _cell.Strategy switch
            {
                UserTextStrategy.DecodeAndContain =>
                    new Utf8RangeReader(_matchedName.Bytes)
                        .DecodeAndContainForPilot()
                        .ToString(),
                UserTextStrategy.ReuseHostText =>
                    _match.Text,
                _ => throw new ArgumentOutOfRangeException(),
            };
            hash = AddText(hash, text);
        }
        return hash;
    }

    static string? PresentNormally(
        Utf8RangeReader reader,
        UserSearchValue search)
    {
        if (!reader.ValueTextEquals(search.Utf8))
            return null;
        return reader.DecodeAndContainForPilot().ToString();
    }

    static string? PresentMatchedUserText(
        Utf8RangeReader candidate,
        UserSearchValue search) =>
        Utf8RangeReader.TryMatchUserText(
            candidate,
            search,
            out UserTextMatch match)
            ? match.Text
            : null;

    static (
        OpaqueByteRange Range,
        UserTextMatch Match) FindFirstMatch(
            RangeKeyStore store,
            UserSearchValue search)
    {
        for (int index = 0; index < store.Count; index++)
        {
            if (Utf8RangeReader.TryMatchUserText(
                    store.NameReader(index),
                    search,
                    out UserTextMatch match))
            {
                return (store.NameRange(index), match);
            }
        }
        throw new InvalidOperationException(
            "The user text did not match an asset Name.");
    }

    static int AddText(int hash, string text) =>
        unchecked(
            (hash * 31)
            + StringComparer.Ordinal.GetHashCode(text));
}

internal static class UserTextPilot
{
    static readonly UserTextCell[] s_cells =
    [
        new(
            UserTextScenario.ScanAndPresent,
            UserTextStrategy.DecodeAndContain),
        new(
            UserTextScenario.ScanAndPresent,
            UserTextStrategy.ReuseHostText),
        new(
            UserTextScenario.MatchedNamePresentation,
            UserTextStrategy.DecodeAndContain),
        new(
            UserTextScenario.MatchedNamePresentation,
            UserTextStrategy.ReuseHostText),
    ];

    static int s_checksum;

    internal static void Validate(
        NameAsset asset,
        UserSearchValue search,
        int copies)
    {
        string contained =
            new InertString(TextPolicy.Field, search.Text).ToString();
        if (!StringComparer.Ordinal.Equals(contained, search.Text))
        {
            throw new InvalidOperationException(
                "The presentation pilot requires user text whose Field "
                + "presentation does not require visual containment.");
        }

        RangeKeyStore store = RangeKeyStore.Build(asset, copies);
        foreach (UserTextScenario scenario
            in Enum.GetValues<UserTextScenario>())
        {
            int baseline = new UserTextPayload(
                store,
                search,
                new(
                    scenario,
                    UserTextStrategy.DecodeAndContain))
                .Consume();
            int candidate = new UserTextPayload(
                store,
                search,
                new(
                    scenario,
                    UserTextStrategy.ReuseHostText))
                .Consume();
            if (candidate != baseline)
            {
                throw new InvalidOperationException(
                    $"{scenario} produced different presentation output.");
            }
        }
    }

    internal static UserTextPilotResult Measure(
        NameAsset asset,
        int rounds,
        int copies,
        UserSearchValue search,
        TextWriter samples)
    {
        RangeKeyStore store = RangeKeyStore.Build(asset, copies);
        foreach (UserTextCell cell in s_cells)
            Warm(store, search, cell);

        var collected = new List<UserTextPilotSample>(
            checked(rounds * s_cells.Length));
        for (int round = 0; round < rounds; round++)
        {
            for (int order = 0; order < s_cells.Length; order++)
            {
                UserTextCell cell =
                    s_cells[(round + order) % s_cells.Length];
                UserTextPilotSample sample = MeasureTime(
                    store,
                    search,
                    cell,
                    round + 1,
                    order + 1);
                collected.Add(sample);
                if (samples != TextWriter.Null)
                {
                    samples.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{asset.Name}\t{sample.Round}\t"
                            + $"{sample.Order}\t{sample.Scenario}\t"
                            + $"{sample.Strategy}\t{copies}\t"
                            + $"{store.Count}\t{store.EncodedBytes}\t"
                            + $"{sample.ElapsedMicroseconds:F3}\t"
                            + $"{sample.AllocatedBytes}\t0\t"
                            + $"{sample.Checksum}"));
                }
            }
            samples.Flush();
        }

        ValidateChecksums(asset.Name, collected);
        int queryMatches = CountMatches(store, search);
        return new(
            asset.Name,
            copies,
            store.Count,
            search.Text,
            queryMatches,
            collected);
    }

    internal static string Report(UserTextPilotResult result)
    {
        var writer = new StringWriter(
            CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"# USER-TEXT {result.Asset}: copies={result.Copies}, "
            + $"keys={result.Keys}, query={result.Query}, "
            + $"matches={result.QueryMatches}");
        writer.WriteLine(
            "ABS\tasset\tscenario\tstrategy\tmetric\tmedian\tp95");
        foreach (UserTextScenario scenario
            in Enum.GetValues<UserTextScenario>())
        {
            foreach (UserTextStrategy strategy
                in Enum.GetValues<UserTextStrategy>())
            {
                UserTextPilotSample[] samples =
                    [.. result.Samples.Where(
                        sample => sample.Scenario == scenario
                            && sample.Strategy == strategy)];
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "elapsed_us",
                    samples.Select(
                        sample => sample.ElapsedMicroseconds));
                WriteAbsolute(
                    writer,
                    result.Asset,
                    scenario,
                    strategy,
                    "allocated_b",
                    samples.Select(
                        sample => (double)sample.AllocatedBytes));
            }
            WriteSpc(
                writer,
                result,
                scenario,
                "elapsed_us",
                sample => sample.ElapsedMicroseconds);
            WriteSpc(
                writer,
                result,
                scenario,
                "allocated_b",
                sample => sample.AllocatedBytes);
        }
        return writer.ToString();
    }

    static void Warm(
        RangeKeyStore store,
        UserSearchValue search,
        UserTextCell cell)
    {
        var payload = new UserTextPayload(store, search, cell);
        s_checksum ^= payload.Consume();
        GC.KeepAlive(payload);
    }

    static UserTextPilotSample MeasureTime(
        RangeKeyStore store,
        UserSearchValue search,
        UserTextCell cell,
        int round,
        int order)
    {
        var payload = new UserTextPayload(store, search, cell);
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        int checksum = payload.Consume();
        double elapsed =
            Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;
        s_checksum ^= checksum;
        GC.KeepAlive(payload);
        return new(
            round,
            order,
            cell.Scenario,
            cell.Strategy,
            elapsed,
            allocated,
            checksum);
    }

    static int CountMatches(
        RangeKeyStore store,
        UserSearchValue search)
    {
        int matches = 0;
        for (int index = 0; index < store.Count; index++)
        {
            if (store.NameReader(index).ValueTextEquals(search.Utf8))
                matches++;
        }
        return matches;
    }

    static void ValidateChecksums(
        string asset,
        IReadOnlyList<UserTextPilotSample> samples)
    {
        foreach (UserTextScenario scenario
            in Enum.GetValues<UserTextScenario>())
        {
            foreach (int round in samples
                .Where(sample => sample.Scenario == scenario)
                .Select(sample => sample.Round)
                .Distinct())
            {
                int[] checksums =
                    [.. samples
                        .Where(sample =>
                            sample.Scenario == scenario
                            && sample.Round == round)
                        .Select(sample => sample.Checksum)
                        .Distinct()];
                if (checksums.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"{asset} {scenario} round {round} "
                        + "produced different answers.");
                }
            }
        }
    }

    static void WriteAbsolute(
        TextWriter writer,
        string asset,
        UserTextScenario scenario,
        UserTextStrategy strategy,
        string metric,
        IEnumerable<double> source)
    {
        double[] values = [.. source.Order()];
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"ABS\t{asset}\t{scenario}\t{strategy}\t{metric}\t"
                + $"{Percentile(values, 0.5):F3}\t"
                + $"{Percentile(values, 0.95):F3}"));
    }

    static void WriteSpc(
        TextWriter writer,
        UserTextPilotResult result,
        UserTextScenario scenario,
        string metric,
        Func<UserTextPilotSample, double> selector)
    {
        UserTextPilotSample[] baseline =
            [.. result.Samples
                .Where(sample =>
                    sample.Scenario == scenario
                    && sample.Strategy
                        == UserTextStrategy.DecodeAndContain)
                .OrderBy(sample => sample.Round)];
        UserTextPilotSample[] candidate =
            [.. result.Samples
                .Where(sample =>
                    sample.Scenario == scenario
                    && sample.Strategy
                        == UserTextStrategy.ReuseHostText)
                .OrderBy(sample => sample.Round)];
        var ratios = new double[baseline.Length];
        bool zeroCandidate =
            candidate.All(sample => selector(sample) == 0);
        bool positiveBaseline =
            baseline.All(sample => selector(sample) > 0);
        if (zeroCandidate && positiveBaseline)
        {
            writer.WriteLine(
                $"SPC\t{result.Asset}\t{scenario}\t"
                + "ReuseHostText\t"
                + $"{metric}\t0.0000\t0.0000\t0.0000\t0.0000\t"
                + "0\tzero-candidate");
            return;
        }
        for (int index = 0; index < ratios.Length; index++)
        {
            double denominator = selector(baseline[index]);
            double numerator = selector(candidate[index]);
            if (denominator <= 0 || numerator <= 0)
            {
                writer.WriteLine(
                    $"SPC\t{result.Asset}\t{scenario}\t"
                    + "ReuseHostText\t"
                    + $"{metric}\tNA\tNA\tNA\tNA\tNA\t"
                    + "non-positive-sample");
                return;
            }
            ratios[index] = numerator / denominator;
        }

        SpcSummary summary = SpcSummary.Calculate(ratios);
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SPC\t{result.Asset}\t{scenario}\t"
                + $"ReuseHostText\t{metric}\t"
                + $"{summary.MedianRatio:F4}\t"
                + $"{summary.CenterRatio:F4}\t"
                + $"{summary.LowerControlRatio:F4}\t"
                + $"{summary.UpperControlRatio:F4}\t"
                + $"{summary.OutsideControlLimits}\t"
                + $"{summary.Assessment}"));
    }

    static double Percentile(
        double[] ordered,
        double percentile)
    {
        if (ordered.Length == 0)
            throw new InvalidOperationException("No samples.");
        int index = Math.Clamp(
            (int)Math.Ceiling(
                ordered.Length * percentile)
                - 1,
            0,
            ordered.Length - 1);
        return ordered[index];
    }
}
