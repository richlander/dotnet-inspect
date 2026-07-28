using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins JSON wire names across every <see cref="JsonSerializerContext"/> in the product.
/// </summary>
/// <remarks>
/// <para>
/// Naming is declared per context, not per model, and several model types are serialized by more
/// than one context (the indented/compact pairs, and rows shared between a document context and a
/// JSONL context). Nothing in the compiler ties those declarations together, so a context added
/// without <c>PropertyNamingPolicy</c> would silently emit a second spelling of an existing shape.
/// These are the gate for that: <see cref="Contexts"/> is discovered by reflection rather than
/// listed, so a new context is covered the moment it is declared.
/// </para>
/// <para>
/// The output contract itself lives in <c>docs/design/output-shapes.md</c>.
/// </para>
/// </remarks>
public class JsonWireNameGateTests
{
    private static readonly Assembly[] ProductAssemblies =
    [
        typeof(DotnetInspector.JsonContext).Assembly,
        typeof(DotnetInspector.Services.DepsJsonParser).Assembly,
        typeof(ILInspector.Metadata.SourceDocumentPath).Assembly,
    ];

    /// <summary>Every concrete generated context in the product, discovered rather than listed.</summary>
    private static IReadOnlyList<JsonSerializerContext> Contexts { get; } = ProductAssemblies
        .Distinct()
        .SelectMany(static a => a.GetTypes())
        .Where(static t => typeof(JsonSerializerContext).IsAssignableFrom(t) && !t.IsAbstract)
        .Select(static t => t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static))
        .Where(static p => p is not null)
        .Select(static p => (JsonSerializerContext)p!.GetValue(null)!)
        .ToArray();

    [Fact]
    public void Gate_IsNotVacuous()
    {
        // If discovery breaks, every other test here passes over an empty set.
        Assert.True(Contexts.Count >= 20, $"Expected the product's generated contexts to be discovered; found {Contexts.Count}.");
        Assert.Contains(Contexts, static c => c is DotnetInspector.JsonContext);

        var shapes = Contexts.SelectMany(WireNamesByType).ToArray();
        Assert.True(shapes.Length >= 50, $"Expected a substantial serializable graph; found {shapes.Length} object shapes.");
    }

    /// <summary>
    /// A model type serialized by two contexts must present one spelling. This is the drift the
    /// per-context naming declaration cannot prevent on its own.
    /// </summary>
    [Fact]
    public void SharedTypes_HaveOneSpellingAcrossEveryContext()
    {
        var byType = new Dictionary<Type, (string Context, string[] Names)>();
        var divergences = new List<string>();

        foreach (var context in Contexts)
        {
            string contextName = context.GetType().Name;
            foreach (var (type, names) in WireNamesByType(context))
            {
                if (!byType.TryGetValue(type, out var seen))
                {
                    byType[type] = (contextName, names);
                    continue;
                }

                if (!seen.Names.SequenceEqual(names))
                {
                    divergences.Add(
                        $"{type.FullName}: {seen.Context} emits [{string.Join(", ", seen.Names)}] " +
                        $"but {contextName} emits [{string.Join(", ", names)}]");
                }
            }
        }

        Assert.True(divergences.Count == 0, string.Join(Environment.NewLine, divergences));
    }

    /// <summary>
    /// <c>TimelineJsonContext</c> declares no naming policy and its views carry no
    /// <see cref="JsonPropertyNameAttribute"/>, so <c>timeline --json</c> emits CLR PascalCase
    /// spelling while every other command emits snake_case. This gate found that; it is a
    /// pre-existing deviation from the output contract, not a sanctioned style. Correcting it
    /// changes a shipped wire format, so it is tracked separately rather than folded into the
    /// change that discovered it. <see cref="KnownPascalCaseDeviations_StillDeviate"/> fails if it
    /// is fixed, forcing this entry to be removed with the fix.
    /// </summary>
    private static readonly string[] KnownPascalCaseContexts = ["TimelineJsonContext"];

    /// <summary>
    /// Every wire name must match the shape its context declares. A context that omits
    /// <c>PropertyNamingPolicy</c> falls back to CLR PascalCase spelling and fails here.
    /// </summary>
    [Fact]
    public void EveryWireName_MatchesItsDeclaredNamingPolicy()
    {
        var violations = new List<string>();

        foreach (var context in Contexts)
        {
            var contextType = context.GetType();
            if (KnownPascalCaseContexts.Contains(contextType.Name))
            {
                continue;
            }

            var policy = contextType
                .GetCustomAttribute<JsonSourceGenerationOptionsAttribute>()?.PropertyNamingPolicy
                ?? JsonKnownNamingPolicy.Unspecified;

            foreach (var (type, names) in WireNamesByType(context))
            {
                foreach (string name in names)
                {
                    if (!MatchesPolicy(name, policy))
                    {
                        violations.Add($"{contextType.Name} declares {policy} but {type.FullName}.{name} does not match it.");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Pins the exemption above so it cannot outlive the deviation it describes.
    /// </summary>
    [Fact]
    public void KnownPascalCaseDeviations_StillDeviate()
    {
        foreach (string contextName in KnownPascalCaseContexts)
        {
            var context = Assert.Single(Contexts, c => c.GetType().Name == contextName);

            bool deviates = WireNamesByType(context)
                .SelectMany(static shape => shape.Names)
                .Any(static name => !MatchesPolicy(name, JsonKnownNamingPolicy.Unspecified));

            Assert.True(
                deviates,
                $"{contextName} now matches the snake_case contract. Remove it from {nameof(KnownPascalCaseContexts)}.");
        }
    }

    /// <summary>
    /// Contexts that emit a wire style other than the product's snake_case default are making a
    /// deliberate contract choice, so they are enumerated rather than inferred.
    /// </summary>
    [Fact]
    public void OnlyKnownContextsOptOutOfSnakeCase()
    {
        string[] expectedCamelCase = ["CorpusManifestJsonContext", "DiscoveryJsonContext"];

        var actual = Contexts
            .Select(static c => c.GetType())
            .Where(static t => t.GetCustomAttribute<JsonSourceGenerationOptionsAttribute>()?.PropertyNamingPolicy
                is JsonKnownNamingPolicy.CamelCase)
            .Select(static t => t.Name)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCamelCase.OrderBy(static n => n, StringComparer.Ordinal).ToArray(), actual);
    }

    private static bool MatchesPolicy(string name, JsonKnownNamingPolicy policy) => policy switch
    {
        JsonKnownNamingPolicy.CamelCase =>
            name.Length == 0 || !char.IsAsciiLetterUpper(name[0]),

        // SnakeCaseLower is the product's structured-output contract. A context that declares no
        // policy pins its names with [JsonPropertyName] instead, and those names are held to the
        // same contract -- otherwise the hand-written spellings would be the one unchecked path.
        _ => name.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'),
    };

    /// <summary>
    /// Walks everything a context can serialize, including nested and element types, and returns
    /// the wire names of each object shape. Naming attributes are not transitive, so the nested
    /// types are exactly where a spelling change would otherwise go unnoticed.
    /// </summary>
    private static List<(Type Type, string[] Names)> WireNamesByType(JsonSerializerContext context)
    {
        var results = new List<(Type, string[])>();
        var visited = new HashSet<Type>();

        foreach (var type in SerializableTypes(context.GetType()))
        {
            Walk(context, type, visited, results);
        }

        return results;
    }

    /// <summary>
    /// <see cref="JsonSerializableAttribute"/> does not surface its type as a property, so the
    /// registration is read from the constructor argument.
    /// </summary>
    private static IEnumerable<Type> SerializableTypes(Type contextType) => contextType
        .GetCustomAttributesData()
        .Where(static d => d.AttributeType == typeof(JsonSerializableAttribute))
        .Select(static d => d.ConstructorArguments.Count > 0 ? d.ConstructorArguments[0].Value as Type : null)
        .Where(static t => t is not null)
        .Select(static t => t!);

    private static void Walk(JsonSerializerContext context, Type type, HashSet<Type> visited, List<(Type, string[])> results)
    {
        if (!visited.Add(type))
        {
            return;
        }

        JsonTypeInfo? info;
        try
        {
            info = context.GetTypeInfo(type);
        }
        catch (InvalidOperationException)
        {
            // Not registered on this context; nothing to pin.
            return;
        }

        if (info is null)
        {
            return;
        }

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                results.Add((type, info.Properties.Select(static p => p.Name).ToArray()));
                foreach (var property in info.Properties)
                {
                    Walk(context, property.PropertyType, visited, results);
                }

                break;

            case JsonTypeInfoKind.Enumerable:
            case JsonTypeInfoKind.Dictionary:
                if (info.ElementType is { } element)
                {
                    Walk(context, element, visited, results);
                }

                break;
        }
    }
}
