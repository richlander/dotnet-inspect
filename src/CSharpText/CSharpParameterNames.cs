namespace CSharpText;

/// <summary>
/// Allocates deterministic parameter names while preserving every non-empty
/// artifact identity exactly.
/// </summary>
public static class CSharpParameterNames
{
    /// <summary>
    /// Replaces absent or empty names with <c>arg{ordinal}</c>, reserving all
    /// surviving artifact names before choosing collision-free fallbacks.
    /// </summary>
    public static string[] Allocate(IReadOnlyList<string?> artifactNames)
    {
        ArgumentNullException.ThrowIfNull(artifactNames);

        var result = new string[artifactNames.Count];
        var hasMissingName = false;
        for (var index = 0; index < result.Length; index++)
        {
            if (artifactNames[index] is { Length: > 0 } name)
                result[index] = name;
            else
                hasMissingName = true;
        }
        if (!hasMissingName)
            return result;

        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? name in result)
        {
            if (name is not null)
                reserved.Add(name);
        }
        for (var index = 0; index < result.Length; index++)
        {
            if (result[index] is not null)
                continue;

            string baseName = $"arg{index}";
            string candidate = baseName;
            for (var suffix = 1; !reserved.Add(candidate); suffix++)
                candidate = $"{baseName}_{suffix}";
            result[index] = candidate;
        }

        return result;
    }
}
