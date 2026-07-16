namespace ILInspector.Text;

/// <summary>
/// Computes edit distance and normalized similarity for text matching.
/// </summary>
public static class StringDistance
{
    public static int EditDistance(string source, string target)
        => LevenshteinDistance.Compute(source, target);

    public static double Similarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target))
            return 1.0;

        int maxLength = Math.Max(source?.Length ?? 0, target?.Length ?? 0);
        return 1.0 - (double)EditDistance(source ?? "", target ?? "") / maxLength;
    }
}

static class LevenshteinDistance
{
    public static int Compute(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target))
            return source.Length;

        if (source.Length < target.Length)
            (source, target) = (target, source);

        var costs = new int[target.Length + 1];
        for (int i = 0; i <= target.Length; i++)
            costs[i] = i;

        for (int i = 1; i <= source.Length; i++)
        {
            int previous = costs[0];
            costs[0] = i;
            for (int j = 1; j <= target.Length; j++)
            {
                int current = costs[j];
                costs[j] = Math.Min(
                    Math.Min(costs[j - 1] + 1, costs[j] + 1),
                    previous + (source[i - 1] == target[j - 1] ? 0 : 1));
                previous = current;
            }
        }

        return costs[target.Length];
    }
}
