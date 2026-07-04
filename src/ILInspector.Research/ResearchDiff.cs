using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research;

public enum ResearchDiffEvidenceKind
{
    MetadataApi,
    IlBody,
    BodySignal
}

public sealed record ResearchDiffRow(
    string ChangeId,
    ResearchDiffEvidenceKind EvidenceKind,
    string Message,
    ApiChange? ApiChange = null,
    IlDiffRow? IlRow = null,
    BodySignalDiffRow? BodySignalRow = null);

public sealed record ResearchDiffResult(ImmutableArray<ResearchDiffRow> Rows)
{
    public bool IsEmpty => Rows.IsEmpty;
}

/// <summary>
/// Product-level diff projector. It preserves producer-owned lower-layer rows as
/// typed evidence and adds stable product change IDs without parsing messages.
/// </summary>
public static class ResearchDiff
{
    public static ResearchDiffResult FromApiDiff(ApiDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var rows = ImmutableArray.CreateBuilder<ResearchDiffRow>();
        foreach (var typeDiff in diff.TypeDiffs)
        {
            foreach (var change in typeDiff.Changes)
            {
                rows.Add(new ResearchDiffRow(
                    $"api.{Kebab(change.Kind.ToString())}",
                    ResearchDiffEvidenceKind.MetadataApi,
                    change.Message,
                    ApiChange: change));
            }
        }

        return new ResearchDiffResult(rows.ToImmutable());
    }

    public static ResearchDiffResult FromIlBodyDiff(IlBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.Failure is { Length: > 0 } failure)
        {
            return new ResearchDiffResult([
                new ResearchDiffRow("il.diff.failed", ResearchDiffEvidenceKind.IlBody, failure)
            ]);
        }

        return new ResearchDiffResult([
            .. diff.Rows.Select(row => new ResearchDiffRow(
                $"il.operation.{ChangeIdSuffix(row.Kind)}",
                ResearchDiffEvidenceKind.IlBody,
                row.Message,
                IlRow: row))
        ]);
    }

    public static ResearchDiffResult FromBodySignalDiff(BodySignalDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return new ResearchDiffResult([
            .. diff.Rows.Select(row => new ResearchDiffRow(
                $"unsafe.{Kebab(row.Signal)}.{Kebab(row.Kind.ToString())}",
                ResearchDiffEvidenceKind.BodySignal,
                $"{row.Kind} {row.Signal}: {row.Operation}",
                BodySignalRow: row))
        ]);
    }

    public static ResearchDiffResult Combine(params ResearchDiffResult[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new ResearchDiffResult([.. results.SelectMany(result => result.Rows)]);
    }

    static string Kebab(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c))
            {
                if (builder.Length > 0 && builder[^1] != '-')
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    static string ChangeIdSuffix(IlDiffKind kind)
        => kind switch
        {
            IlDiffKind.Add => "added",
            IlDiffKind.Remove => "removed",
            IlDiffKind.Context => "context",
            _ => Kebab(kind.ToString()),
        };
}
