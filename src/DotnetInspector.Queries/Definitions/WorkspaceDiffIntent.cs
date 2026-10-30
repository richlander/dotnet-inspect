using NuGet.Versioning;
using ILInspector.Metadata;
using QuerySpace;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Inert exact Diff intent attached to the After occurrence and subject.</summary>
public sealed record WorkspaceDiffIntent(
    string Baseline,
    string Content,
    string Asset,
    string Medium,
    string? Body,
    string? PredicateOperator = null,
    string? PredicateValue = null)
{
    public const string QueryId = "workspace-diff/v1";

    public static PortableQueryDefinitionDescriptor<WorkspaceDiffIntent> Descriptor { get; } = new(
        QueryId,
        "exact-diff",
        PortableQueryDefinitionInputs.StateBound(
            [PortableSubjectRequestKind.Library, PortableSubjectRequestKind.Type, PortableSubjectRequestKind.Member],
            ["library.compare", "member.compare", "type.compare"],
            PortableQueryInputRequirement.Required,
            PortableQueryInputRequirement.Optional,
            PortableQueryInputRequirement.Forbidden),
        static (intent, attachment, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (attachment.StateCoordinate is not DefinitionMemberCoordinate.PackageCoordinate { Version: not null, Framework: not null })
                    throw new ArgumentException("An exact Gallery After coordinate and framework are required.");
                string expectedFacet = attachment.SubjectKind switch
                {
                    PortableSubjectRequestKind.Library => "library.compare",
                    PortableSubjectRequestKind.Type => "type.compare",
                    PortableSubjectRequestKind.Member => "member.compare",
                    _ => throw new ArgumentException("Unsupported Diff subject."),
                };
                if (attachment.FacetId != expectedFacet)
                    throw new ArgumentException("Diff facet must match its exact subject.");
                WorkspaceDiffIntent value = FromIntent(intent);
                if ((attachment.SubjectKind == PortableSubjectRequestKind.Member) != (value.Body is not null)
                    && value.Content == "member-body")
                    throw new ArgumentException("Member Body requires an exact body selector only at Member scope.");
                return new PortableQueryDefinitionResolution<WorkspaceDiffIntent>.Accepted(value);
            }
            catch (ArgumentException ex)
            {
                return new PortableQueryDefinitionResolution<WorkspaceDiffIntent>.Rejected(ex.Message);
            }
        });

    public PortableQueryIdentity ToIdentity()
    {
        var terms = new List<PortableQueryTerm>
        {
            new("baseline", PortableQueryOperator.Equal, Baseline),
            new("content", PortableQueryOperator.Equal, Content),
            new("asset", PortableQueryOperator.Equal, Asset),
            new("medium", PortableQueryOperator.Equal, Medium),
        };
        if (Body is not null) terms.Add(new("body", PortableQueryOperator.Equal, Body));
        if (PredicateOperator is not null) terms.Add(new("predicate-operator", PortableQueryOperator.Equal, PredicateOperator));
        if (PredicateValue is not null) terms.Add(new("predicate-value", PortableQueryOperator.Equal, PredicateValue));
        var intent = PortableQueryIntent.Create(terms, [], [], []);
        _ = FromIntent(intent);
        try
        {
            return PortableQueryIdentity.Create(QueryId, intent);
        }
        catch (PortableQueryPayloadException ex)
        {
            throw new ArgumentException(ex.Message, nameof(intent), ex);
        }
    }

    public static WorkspaceDiffIntent FromIntent(PortableQueryIntent intent)
    {
        if (intent.Bounds.Count != 0 || intent.Stages.Count != 0 || intent.Order.Count != 0)
            throw new ArgumentException("Exact Diff intent accepts only equality terms.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PortableQueryTerm term in intent.Terms)
        {
            if (term.Operator != PortableQueryOperator.Equal
                || term.Key is not ("baseline" or "content" or "asset" or "medium" or "body" or "predicate-operator" or "predicate-value")
                || !values.TryAdd(term.Key, term.Value))
                throw new ArgumentException("Exact Diff intent contains an unknown or repeated term.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) && value.Length > 0
            ? value : throw new ArgumentException($"Exact Diff intent requires '{key}'.");
        string baseline = Required("baseline");
        if (!NuGetVersion.TryParse(baseline, out var version) || version.ToNormalizedString() != baseline)
            throw new ArgumentException("Diff baseline must be a canonical exact package version.");
        string content = Required("content");
        if (content is not ("api" or "member-body" or "string-literals"))
            throw new ArgumentException("Unsupported Diff content.");
        string asset = Required("asset");
        if (!asset.StartsWith("compile:", StringComparison.Ordinal) || asset.Length <= 8)
            throw new ArgumentException("Diff requires an exact compile asset identity.");
        string medium = Required("medium");
        if (medium is not ("CSharp" or "Il") || (content != "member-body" && medium != "CSharp"))
            throw new ArgumentException("Unsupported Diff medium.");
        values.TryGetValue("body", out string? body);
        if (body is not null)
        {
            MemberTargetSelector selector = MemberTargetSelector.Parse(body);
            if (selector.NormalizedSelector != body || selector.DigestPrefix is not { Length: 10 })
                throw new ArgumentException("Diff body requires a canonical portable Member target selector with its complete anchor digest.");
        }
        values.TryGetValue("predicate-operator", out string? op);
        values.TryGetValue("predicate-value", out string? predicate);
        if (body is not null && (body.Length == 0 || content != "member-body"))
            throw new ArgumentException("Body selection belongs to Member Body content.");
        if (content == "string-literals" ? op is not ("contains" or "starts-with") || string.IsNullOrEmpty(predicate) || predicate.Length > 1024 : op is not null || predicate is not null)
            throw new ArgumentException("Invalid string literal predicate.");
        return new(baseline, content, asset, medium, body, op, predicate);
    }
}
