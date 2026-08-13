using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record StructuralCloneCorpusDocument(
    int SchemaVersion,
    ImmutableArray<StructuralCloneCorpusCase> Cases);

public sealed record StructuralCloneCorpusCase(
    string Id,
    StructuralCloneCorpusMethod Left,
    StructuralCloneCorpusMethod Right,
    StructuralCloneDisposition ExpectedDisposition,
    StructuralCloneRelation? ExpectedRelation,
    string Difficulty,
    string Intent,
    string Actionability,
    ImmutableArray<string> Tags);

public sealed record StructuralCloneCorpusMethod(
    string Type,
    string Method);

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

public sealed record StructuralCloneCorpusReport(
    string Assembly,
    int Total,
    int Passed,
    ImmutableArray<StructuralCloneCorpusCaseResult> Cases)
{
    public bool Success => Total > 0 && Passed == Total;
}

public static class StructuralCloneCorpus
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
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
        if (!image.HasMetadata)
            throw new InvalidDataException(
                $"The clone corpus target is not a managed assembly: {assemblyPath}");

        MetadataReader reader = image.GetMetadataReader();
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

        ImmutableArray<StructuralCloneCorpusCaseResult> cases =
            results.ToImmutable();
        return new StructuralCloneCorpusReport(
            Path.GetFullPath(assemblyPath),
            cases.Length,
            cases.Count(static item => item.Passed),
            cases);
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
        return output.ToString();
    }

    static void Validate(StructuralCloneCorpusDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported structural clone corpus schema {document.SchemaVersion}; expected 1.");
        }
        if (document.Cases.IsDefaultOrEmpty)
            throw new InvalidDataException(
                "The structural clone relationship ledger has no cases.");

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusCase item in document.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidDataException(
                    $"Corpus case ids must be non-empty and unique: '{item.Id}'.");
            ValidateMethod(item.Id, "left", item.Left);
            ValidateMethod(item.Id, "right", item.Right);
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
    }

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
