using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record StructuralCloneCorpusDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] ImmutableArray<StructuralCloneCorpusCase> Cases,
    [property: JsonRequired] StructuralCloneCorpusDiscovery Discovery);

public sealed record StructuralCloneCorpusDiscovery(
    [property: JsonRequired]
    ImmutableArray<StructuralCloneCorpusMethod> Population);

public sealed record StructuralCloneCorpusCase(
    [property: JsonRequired] string Id,
    [property: JsonRequired] StructuralCloneCorpusMethod Left,
    [property: JsonRequired] StructuralCloneCorpusMethod Right,
    [property: JsonRequired] StructuralCloneDisposition ExpectedDisposition,
    [property: JsonRequired] StructuralCloneRelation? ExpectedRelation,
    [property: JsonRequired] string Difficulty,
    [property: JsonRequired] string Intent,
    [property: JsonRequired] string Actionability,
    [property: JsonRequired] ImmutableArray<string> Tags);

public sealed record StructuralCloneCorpusMethod(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method);

public sealed record StructuralCloneCorpusCaseResult(
    string Id,
    StructuralCloneCorpusMethod Left,
    StructuralCloneCorpusMethod Right,
    StructuralCloneDisposition ExpectedDisposition,
    StructuralCloneRelation? ExpectedRelation,
    StructuralCloneDisposition ActualDisposition,
    StructuralCloneRelation? ActualRelation,
    StructuralCloneCorrespondenceKind? Correspondence,
    ImmutableArray<StructuralCloneBlocker> Blockers,
    StructuralCloneVerificationReceipt Receipt,
    bool Passed);

public sealed record StructuralCloneCorpusCluster(
    ImmutableArray<StructuralCloneCorpusMethod> Members);

public sealed record StructuralCloneCorpusDiscoveryResult(
    StructuralCloneDiscoveryDisposition Disposition,
    ImmutableArray<StructuralCloneCorpusCluster> ExpectedClusters,
    ImmutableArray<StructuralCloneCorpusCluster> ActualClusters,
    ImmutableArray<StructuralCloneSuppressedBucket> SuppressedBuckets,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneDiscoveryReceipt Receipt,
    bool Passed);

public sealed record StructuralCloneCorpusReport(
    string Assembly,
    int Total,
    int Passed,
    ImmutableArray<StructuralCloneCorpusCaseResult> Cases,
    StructuralCloneCorpusDiscoveryResult Discovery)
{
    public bool Success =>
        Total > 0
        && Passed == Total
        && Discovery.Passed;
}

public static class StructuralCloneCorpus
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false),
        },
    };

    static readonly ImmutableHashSet<string> s_difficulties =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "banal",
            "challenging");
    static readonly ImmutableHashSet<string> s_intents =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "authored-duplicate",
            "control-flow-contrast",
            "semantic-hazard",
            "unsupported-boundary");
    static readonly ImmutableHashSet<string> s_actionabilities =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "actionable",
            "diagnostic",
            "none");

    public static StructuralCloneCorpusDocument Load(string json)
    {
        StructuralCloneCorpusDocument document =
            JsonSerializer.Deserialize<StructuralCloneCorpusDocument>(
                json,
                s_json)
            ?? throw new InvalidDataException(
                "The structural clone relationship ledger is empty.");
        Validate(document);
        return document;
    }

    public static StructuralCloneCorpusReport Run(
        string assemblyPath,
        StructuralCloneCorpusDocument corpus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(corpus);
        Validate(corpus);

        using PEReader image =
            new(File.OpenRead(Path.GetFullPath(assemblyPath)));
        MetadataReader reader;
        try
        {
            if (!image.HasMetadata)
            {
                throw new InvalidDataException(
                    $"The clone corpus target is not a managed assembly: "
                        + assemblyPath);
            }
            reader = image.GetMetadataReader();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            throw new InvalidDataException(
                $"The clone corpus target has invalid managed metadata: "
                    + assemblyPath,
                ex);
        }
        ImmutableArray<StructuralCloneCorpusCaseResult>.Builder results =
            ImmutableArray.CreateBuilder<StructuralCloneCorpusCaseResult>(
                corpus.Cases.Length);
        foreach (StructuralCloneCorpusCase item in corpus.Cases)
        {
            MethodDefinitionHandle left = Resolve(reader, item.Left);
            MethodDefinitionHandle right = Resolve(reader, item.Right);
            StructuralCloneComparison comparison =
                StructuralCloneAnalysis.Compare(image, left, right);
            bool passed =
                comparison.Disposition == item.ExpectedDisposition
                && comparison.Relation == item.ExpectedRelation;
            results.Add(
                new StructuralCloneCorpusCaseResult(
                    item.Id,
                    item.Left,
                    item.Right,
                    item.ExpectedDisposition,
                    item.ExpectedRelation,
                    comparison.Disposition,
                    comparison.Relation,
                    comparison.Correspondence?.Kind,
                    comparison.Blockers,
                    comparison.Receipt,
                    passed));
        }

        var methodsByHandle =
            new Dictionary<MethodDefinitionHandle, StructuralCloneCorpusMethod>();
        ImmutableArray<MethodDefinitionHandle>.Builder population =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>(
                corpus.Discovery.Population.Length);
        foreach (StructuralCloneCorpusMethod method
            in corpus.Discovery.Population)
        {
            MethodDefinitionHandle handle = Resolve(reader, method);
            methodsByHandle.Add(handle, method);
            population.Add(handle);
        }
        StructuralCloneDiscoveryResult discovery =
            StructuralCloneAnalysis.Discover(
                image,
                population.ToImmutable());
        ImmutableArray<StructuralCloneCorpusCluster> expectedClusters =
            ExpectedClusters(corpus);
        ImmutableArray<StructuralCloneCorpusCluster> actualClusters =
        [
            .. discovery.Clusters
                .Select(cluster =>
                    new StructuralCloneCorpusCluster(
                        [
                            .. cluster.Members
                                .Select(member =>
                                    methodsByHandle[member.Handle])
                                .OrderBy(MethodKey, StringComparer.Ordinal),
                        ]))
                .OrderBy(ClusterKey, StringComparer.Ordinal),
        ];
        bool discoveryPassed =
            discovery.Disposition
                == StructuralCloneDiscoveryDisposition.Completed
            && discovery.SuppressedBuckets.IsEmpty
            && expectedClusters
                .Select(ClusterKey)
                .SequenceEqual(
                    actualClusters.Select(ClusterKey),
                    StringComparer.Ordinal);

        ImmutableArray<StructuralCloneCorpusCaseResult> cases =
            results.ToImmutable();
        return new StructuralCloneCorpusReport(
            Path.GetFullPath(assemblyPath),
            cases.Length,
            cases.Count(static item => item.Passed),
            cases,
            new StructuralCloneCorpusDiscoveryResult(
                discovery.Disposition,
                expectedClusters,
                actualClusters,
                discovery.SuppressedBuckets,
                discovery.Blockers,
                discovery.Receipt,
                discoveryPassed));
    }

    public static string ToJson(StructuralCloneCorpusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string Format(StructuralCloneCorpusReport report)
    {
        StringBuilder output = new();
        output.AppendLine(
            $"Structural clone relationship corpus: {report.Passed}/{report.Total} passed");
        foreach (StructuralCloneCorpusCaseResult item in report.Cases)
        {
            output.Append(item.Passed ? "PASS " : "FAIL ");
            output.Append(item.Id);
            output.Append(": expected ");
            AppendOutcome(
                output,
                item.ExpectedDisposition,
                item.ExpectedRelation);
            output.Append(", actual ");
            AppendOutcome(
                output,
                item.ActualDisposition,
                item.ActualRelation);
            if (item.Correspondence is { } correspondence)
            {
                output.Append(" (");
                output.Append(correspondence);
                output.Append(" correspondence)");
            }
            output.AppendLine();
        }
        output.Append(report.Discovery.Passed ? "PASS " : "FAIL ");
        output.Append("closed-world exact discovery: expected ");
        output.Append(report.Discovery.ExpectedClusters.Length);
        output.Append(" clusters, actual ");
        output.Append(report.Discovery.ActualClusters.Length);
        output.Append(" clusters, disposition ");
        output.AppendLine(report.Discovery.Disposition.ToString());
        foreach (StructuralCloneCorpusCluster cluster
            in report.Discovery.ActualClusters)
        {
            output.Append("  ");
            output.AppendLine(string.Join(
                " = ",
                cluster.Members.Select(static member =>
                    $"{member.Type}::{member.Method}")));
        }
        return output.ToString();
    }

    static void Validate(StructuralCloneCorpusDocument document)
    {
        if (document.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                $"Unsupported structural clone corpus schema {document.SchemaVersion}; expected 2.");
        }
        if (document.Cases.IsDefaultOrEmpty)
            throw new InvalidDataException(
                "The structural clone relationship ledger has no cases.");
        if (document.Discovery is null
            || document.Discovery.Population.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The structural clone relationship ledger has no closed-world discovery population.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> caseMethods = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusCase item in document.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidDataException(
                    $"Corpus case ids must be non-empty and unique: '{item.Id}'.");
            ValidateMethod(item.Id, "left", item.Left);
            ValidateMethod(item.Id, "right", item.Right);
            caseMethods.Add(MethodKey(item.Left));
            caseMethods.Add(MethodKey(item.Right));
            if (item.ExpectedDisposition == StructuralCloneDisposition.Completed
                ^ item.ExpectedRelation is not null)
            {
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' must separate disposition from relation: completed requires a relation and other dispositions forbid one.");
            }
            if (!s_difficulties.Contains(item.Difficulty))
                throw InvalidAxis(item.Id, "difficulty", item.Difficulty);
            if (!s_intents.Contains(item.Intent))
                throw InvalidAxis(item.Id, "intent", item.Intent);
            if (!s_actionabilities.Contains(item.Actionability))
                throw InvalidAxis(
                    item.Id,
                    "actionability",
                    item.Actionability);
            if (item.Tags.IsDefault)
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' must declare a tags array.");
        }

        HashSet<string> population = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusMethod method
            in document.Discovery.Population)
        {
            ValidateMethod("discovery", "population", method);
            if (!population.Add(MethodKey(method)))
            {
                throw new InvalidDataException(
                    $"Discovery population method '{method.Type}::{method.Method}' is duplicated.");
            }
        }
        if (!caseMethods.SetEquals(population))
        {
            throw new InvalidDataException(
                "The closed-world discovery population must equal the distinct methods declared by relationship cases.");
        }
    }

    static ImmutableArray<StructuralCloneCorpusCluster> ExpectedClusters(
        StructuralCloneCorpusDocument corpus)
    {
        Dictionary<string, string> parent =
            corpus.Discovery.Population.ToDictionary(
                MethodKey,
                MethodKey,
                StringComparer.Ordinal);

        foreach (StructuralCloneCorpusCase item in corpus.Cases)
        {
            if (item.ExpectedDisposition
                    == StructuralCloneDisposition.Completed
                && item.ExpectedRelation == StructuralCloneRelation.Exact)
            {
                Union(
                    parent,
                    MethodKey(item.Left),
                    MethodKey(item.Right));
            }
        }

        return
        [
            .. corpus.Discovery.Population
                .GroupBy(
                    method => Find(parent, MethodKey(method)),
                    StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(group =>
                    new StructuralCloneCorpusCluster(
                        [
                            .. group.OrderBy(
                                MethodKey,
                                StringComparer.Ordinal),
                        ]))
                .OrderBy(ClusterKey, StringComparer.Ordinal),
        ];
    }

    static void Union(
        Dictionary<string, string> parent,
        string left,
        string right)
    {
        string leftRoot = Find(parent, left);
        string rightRoot = Find(parent, right);
        if (!StringComparer.Ordinal.Equals(leftRoot, rightRoot))
            parent[rightRoot] = leftRoot;
    }

    static string Find(
        Dictionary<string, string> parent,
        string member)
    {
        string root = member;
        while (!StringComparer.Ordinal.Equals(root, parent[root]))
            root = parent[root];
        while (!StringComparer.Ordinal.Equals(member, root))
        {
            string next = parent[member];
            parent[member] = root;
            member = next;
        }
        return root;
    }

    static string ClusterKey(StructuralCloneCorpusCluster cluster)
        => string.Join("\n", cluster.Members.Select(MethodKey));

    static string MethodKey(StructuralCloneCorpusMethod method)
        => $"{method.Type}\0{method.Method}";

    static MethodDefinitionHandle Resolve(
        MetadataReader reader,
        StructuralCloneCorpusMethod method)
    {
        TypeDefinitionHandle typeHandle = default;
        foreach (TypeDefinitionHandle candidate in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(candidate);
            string candidateName =
                string.IsNullOrEmpty(reader.GetString(type.Namespace))
                    ? reader.GetString(type.Name)
                    : $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
            if (StringComparer.Ordinal.Equals(candidateName, method.Type))
            {
                if (!typeHandle.IsNil)
                    throw Ambiguous(method, "type");
                typeHandle = candidate;
            }
        }
        if (typeHandle.IsNil)
            throw Missing(method, "type");

        MethodDefinitionHandle result = default;
        foreach (MethodDefinitionHandle candidate
            in reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            if (!reader.StringComparer.Equals(
                reader.GetMethodDefinition(candidate).Name,
                method.Method))
            {
                continue;
            }
            if (!result.IsNil)
                throw Ambiguous(method, "method");
            result = candidate;
        }
        return !result.IsNil ? result : throw Missing(method, "method");
    }

    static void ValidateMethod(
        string id,
        string side,
        StructuralCloneCorpusMethod method)
    {
        if (method is null
            || string.IsNullOrWhiteSpace(method.Type)
            || string.IsNullOrWhiteSpace(method.Method))
        {
            throw new InvalidDataException(
                $"Corpus case '{id}' has an incomplete {side} method identity.");
        }
    }

    static InvalidDataException InvalidAxis(
        string id,
        string axis,
        string value)
        => new(
            $"Corpus case '{id}' has invalid {axis} value '{value}'.");

    static InvalidDataException Missing(
        StructuralCloneCorpusMethod method,
        string part)
        => new(
            $"Could not resolve {part} for '{method.Type}::{method.Method}'.");

    static InvalidDataException Ambiguous(
        StructuralCloneCorpusMethod method,
        string part)
        => new(
            $"Ambiguous {part} for '{method.Type}::{method.Method}'.");

    static void AppendOutcome(
        StringBuilder output,
        StructuralCloneDisposition disposition,
        StructuralCloneRelation? relation)
    {
        output.Append(disposition);
        if (relation is { } value)
        {
            output.Append('/');
            output.Append(value);
        }
    }
}
