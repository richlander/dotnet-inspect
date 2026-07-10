using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpTypeBodyPolicy
{
    Skeleton,
    Full,
    Mixed,
    Stubs
}

public sealed record CSharpTypePrintRequest
{
    public CSharpTypePrintRequest(
        ApiType type,
        CSharpTypeBodyPolicy bodyPolicy = CSharpTypeBodyPolicy.Skeleton,
        IReadOnlyList<ApiMember>? members = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(bodyPolicy))
            throw new ArgumentOutOfRangeException(nameof(bodyPolicy));
        if (members?.Any(member => member is null) == true)
            throw new ArgumentException("Type print members cannot contain null entries.", nameof(members));

        Type = type;
        BodyPolicy = bodyPolicy;
        Members = members?.ToArray();
    }

    public ApiType Type { get; }

    public CSharpTypeBodyPolicy BodyPolicy { get; }

    public IReadOnlyList<ApiMember>? Members { get; }
}

public sealed record CSharpTypePrintOptions
{
    public bool IncludeCustomAttributes { get; init; }
}

public sealed record CSharpTypeSourceUnit(string? Namespace, string Source);

public sealed record CSharpTypePrintDiagnostic(string TypeName, string Message);

public sealed record CSharpTypePrintResult(
    ImmutableArray<CSharpTypeSourceUnit> Units,
    ImmutableArray<CSharpTypePrintDiagnostic> Diagnostics);
