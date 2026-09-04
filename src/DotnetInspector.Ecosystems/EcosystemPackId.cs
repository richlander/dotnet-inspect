using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Ecosystems;

/// <summary>A stable identity for one product-owned ecosystem pack.</summary>
public sealed record EcosystemPackId
{
    private const string Prefix = "ecosystem.";

    private EcosystemPackId(string value) => Value = value;

    /// <summary>Gets the canonical identity text.</summary>
    public string Value { get; }

    /// <summary>Creates a typed identity from canonical text.</summary>
    public static bool TryCreate(
        string? value,
        [NotNullWhen(true)] out EcosystemPackId? id)
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

        id = new EcosystemPackId(value);
        return true;
    }

    internal static EcosystemPackId Create(string value) =>
        TryCreate(value, out EcosystemPackId? id)
            ? id
            : throw new ArgumentException(
                $"'{value}' is not a canonical ecosystem-pack identity.",
                nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Canonical identities for shipped ecosystem packs.</summary>
public static class EcosystemPackIds
{
    public static EcosystemPackId Platform { get; } =
        EcosystemPackId.Create("ecosystem.platform");

    public static EcosystemPackId MicrosoftExtensions { get; } =
        EcosystemPackId.Create("ecosystem.microsoft-extensions");

    public static EcosystemPackId AspNetCore { get; } =
        EcosystemPackId.Create("ecosystem.aspnetcore");

    public static EcosystemPackId Aspire { get; } =
        EcosystemPackId.Create("ecosystem.aspire");
}

/// <summary>Stable scenario IDs for shipped product demos.</summary>
public static class ProductDemoIds
{
    public const string StjSerializer = "stj-serializer";
    public const string ExtensionsCallGraph = "extensions-callgraph";
    public const string StjSerializeCallGraph = "stj-serialize-callgraph";
    public const string ConfigBindCallGraph = "config-bind-callgraph";
    public const string OptionsAddCallGraph = "options-add-callgraph";
    public const string DiTryAddCallGraph = "di-tryadd-callgraph";
    public const string HttpAddHttpClientCallGraph = "http-addhttpclient-callgraph";
    public const string StjGetDecimalCallGraph = "stj-getdecimal-callgraph";
    public const string AspirePostgresCallGraph = "aspire-postgres-callgraph";
    public const string AspireRedisCallGraph = "aspire-redis-callgraph";
}
