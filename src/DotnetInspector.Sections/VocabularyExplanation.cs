using System.Collections.Immutable;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections;

/// <summary>
/// The host-neutral request surface for product vocabulary explanation:
/// resolves a <c>vocabularies</c> path against a host's composed snapshot and
/// explains it under <see cref="ResourceExplanationRequest.ForHost"/>. Inspect
/// Web requests through it; the CLI issues the same request through its own
/// <c>explain</c> dispatch, so equal snapshots yield equal Document Content.
/// </summary>
public sealed class VocabularyExplanation
{
    private readonly ResourceExplanationCatalog _catalog;

    public VocabularyExplanation(VocabularySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _catalog = ResourceExplanationCatalog.CreateVocabularies(snapshot);
    }

    /// <summary>
    /// Explains <paramref name="path"/> to <paramref name="depth"/>. A
    /// non-canonical path, a path outside <c>vocabularies</c>, an unknown path,
    /// or a negative depth is a typed rejection, never an empty Document.
    /// </summary>
    public VocabularyExplanationResult Explain(string path, int depth)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (depth < 0)
        {
            return new VocabularyExplanationResult.Rejected(
                VocabularyExplanationRejection.InvalidDepth,
                path,
                "Explanation depth must be zero or greater.",
                []);
        }

        switch (_catalog.Resolve(path))
        {
            case ResourcePathResolution.Invalid invalid:
                return new VocabularyExplanationResult.Rejected(
                    VocabularyExplanationRejection.InvalidPath,
                    path,
                    invalid.Reason,
                    []);

            case ResourcePathResolution.Unknown unknown:
                string root = path.Split('/')[0];
                return root == ResourceExplanationCatalog
                        .VocabulariesCollectionSegment
                    ? new VocabularyExplanationResult.Rejected(
                        VocabularyExplanationRejection.Unknown,
                        path,
                        $"Resource path '{path}' was not found.",
                        unknown.Suggestions)
                    : new VocabularyExplanationResult.Rejected(
                        VocabularyExplanationRejection.OutsideVocabularies,
                        path,
                        $"Resource path '{path}' is not under '"
                        + ResourceExplanationCatalog
                            .VocabulariesCollectionSegment
                        + "'.",
                        []);

            case ResourcePathResolution.Resolved resolved:
                return new VocabularyExplanationResult.Explained(
                    _catalog.Explain(
                        resolved,
                        ResourceExplanationRequest.ForHost(depth)));

            default:
                throw new InvalidOperationException(
                    "Unknown resource-path resolution.");
        }
    }
}

/// <summary>The outcome of one vocabulary explanation request.</summary>
public abstract record VocabularyExplanationResult
{
    private VocabularyExplanationResult()
    {
    }

    public sealed record Explained(
        InspectionEnvelope<ResourceExplanationDocument> Inspection) :
        VocabularyExplanationResult;

    public sealed record Rejected(
        VocabularyExplanationRejection Reason,
        string RequestedPath,
        string Message,
        ImmutableArray<ResourcePath> Suggestions) :
        VocabularyExplanationResult;
}

/// <summary>Why a vocabulary explanation request has no Document.</summary>
public enum VocabularyExplanationRejection
{
    InvalidPath,
    OutsideVocabularies,
    Unknown,
    InvalidDepth,
}
