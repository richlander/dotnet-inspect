using System.Security.Cryptography;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace CiChangeDetection;

internal static class YamlContractAssertions
{
    internal static YamlMappingNode GetRequiredMapping(
        YamlMappingNode mapping,
        string key,
        string context) =>
        RequireMapping(
            GetRequiredNode(mapping, key, context),
            $"{context}.{key}");

    internal static YamlSequenceNode GetRequiredSequence(
        YamlMappingNode mapping,
        string key,
        string context) =>
        GetRequiredNode(mapping, key, context) is YamlSequenceNode sequence
            ? sequence
            : throw new InvalidOperationException(
                $"{context}.{key} must be a sequence.");

    internal static string GetRequiredScalar(
        YamlMappingNode mapping,
        string key,
        string context) =>
        RequireScalar(
            GetRequiredNode(mapping, key, context),
            $"{context}.{key}");

    internal static string? GetOptionalScalar(
        YamlMappingNode mapping,
        string key) =>
        TryGetNode(mapping, key, out YamlNode node)
            ? RequireScalar(node, key)
            : null;

    private static YamlNode GetRequiredNode(
        YamlMappingNode mapping,
        string key,
        string context) =>
        TryGetNode(mapping, key, out YamlNode node)
            ? node
            : throw new InvalidOperationException(
                $"Could not find {context}.{key}.");

    internal static bool TryGetNode(
        YamlMappingNode mapping,
        string key,
        out YamlNode value)
    {
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            if (keyNode is YamlScalarNode scalar && scalar.Value == key)
            {
                value = valueNode;
                return true;
            }
        }

        value = null!;
        return false;
    }

    internal static YamlMappingNode RequireMapping(
        YamlNode node,
        string context) =>
        node as YamlMappingNode
        ?? throw new InvalidOperationException(
            $"{context} must be a mapping.");

    internal static string RequireScalar(
        YamlNode node,
        string context) =>
        node is YamlScalarNode { Value: string value }
            ? value
            : throw new InvalidOperationException(
                $"{context} must be a scalar.");

    internal static void RequireScalarValue(
        YamlMappingNode mapping,
        string key,
        string expected,
        string context)
    {
        string actual = GetRequiredScalar(mapping, key, context);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{context}.{key} must be {expected}, got {actual}.");
        }
    }

    internal static void RequireExactScalarValues(
        YamlMappingNode mapping,
        IReadOnlyDictionary<string, string> expected,
        string context)
    {
        if (mapping.Children.Count != expected.Count)
        {
            throw new InvalidOperationException(
                $"{context} must declare exactly: " +
                $"{string.Join(", ", expected.Keys)}.");
        }

        foreach ((string key, string value) in expected)
        {
            RequireScalarValue(mapping, key, value, context);
        }
    }

    internal static void RequireExactKeys(
        YamlMappingNode mapping,
        IReadOnlyCollection<string> expected,
        string context)
    {
        var actual = mapping.Children.Keys
            .Select(key => ((YamlScalarNode)key).Value ?? "")
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw new InvalidOperationException(
                $"{context} must declare exactly: " +
                $"{string.Join(", ", expected)}.");
        }
    }

    internal static void RequireScalarSha256(
        YamlMappingNode mapping,
        string key,
        string expected,
        string context)
    {
        string actual = GetRequiredScalar(mapping, key, context);
        string hash = ComputeSha256(actual);
        if (hash != expected)
        {
            throw new InvalidOperationException(
                $"{context}.{key} SHA-256 must be {expected}, got {hash}.");
        }
    }

    internal static string ComputeSha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static void RequireSha256(string value, string context)
    {
        if (value.Length != 64 ||
            value.Any(character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'A' and <= 'F')))
        {
            throw new InvalidOperationException(
                $"{context} must be a 64-character uppercase hexadecimal " +
                "SHA-256 value.");
        }
    }

    internal static string ReplaceExactlyOnce(
        string source,
        string oldValue,
        string newValue,
        string context)
    {
        int index = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0 ||
            source.IndexOf(
                oldValue,
                index + oldValue.Length,
                StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(
                $"{context} must appear exactly once.");
        }

        return source[..index] +
            newValue +
            source[(index + oldValue.Length)..];
    }

    internal static void RequireAbsent(
        YamlMappingNode mapping,
        string key,
        string context)
    {
        if (TryGetNode(mapping, key, out _))
        {
            throw new InvalidOperationException(
                $"{context} must not declare {key}.");
        }
    }
}
