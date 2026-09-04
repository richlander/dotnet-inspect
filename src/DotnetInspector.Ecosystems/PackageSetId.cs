using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Ecosystems;

/// <summary>A stable identity for one product-owned package set.</summary>
public sealed record PackageSetId
{
    private const string Prefix = "package-set.";

    private PackageSetId(string value) => Value = value;

    /// <summary>Gets the canonical identity text.</summary>
    public string Value { get; }

    /// <summary>Creates a typed identity from canonical text.</summary>
    public static bool TryCreate(
        string? value,
        [NotNullWhen(true)] out PackageSetId? id)
    {
        id = null;
        if (value is not { Length: > 0 and <= 80 }
            || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> name = value.AsSpan(Prefix.Length);
        if (name.IsEmpty || !char.IsAsciiLetterLower(name[0]))
            return false;

        bool previousWasHyphen = false;
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-'
                || index == name.Length - 1
                || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        id = new PackageSetId(value);
        return true;
    }

    internal static PackageSetId Create(string value) =>
        TryCreate(value, out PackageSetId? id)
            ? id
            : throw new ArgumentException(
                $"'{value}' is not a canonical package-set identity.",
                nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Canonical identities for shipped package sets.</summary>
public static class PackageSetIds
{
    /// <summary>The Microsoft.Extensions package set.</summary>
    public static PackageSetId MicrosoftExtensions { get; } =
        PackageSetId.Create("package-set.microsoft-extensions");

    /// <summary>The ASP.NET Core package set.</summary>
    public static PackageSetId AspNetCore { get; } =
        PackageSetId.Create("package-set.aspnetcore");
}
