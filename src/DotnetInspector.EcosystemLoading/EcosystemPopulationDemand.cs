using DotnetInspector.SourceSelection;

namespace DotnetInspector.EcosystemLoading;

/// <summary>Closed demand for one explicit Ecosystem population load.</summary>
public abstract record EcosystemPopulationDemand
{
    private protected EcosystemPopulationDemand()
    {
    }

    public sealed record WholePopulation : EcosystemPopulationDemand
    {
        public static WholePopulation Instance { get; } = new();

        private WholePopulation()
        {
        }
    }

    public sealed record ExactLibrary : EcosystemPopulationDemand
    {
        public ExactLibrary(ExactLibrarySourceCoordinate coordinate)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            Coordinate = coordinate;
        }

        public ExactLibrarySourceCoordinate Coordinate { get; }
    }
}

/// <summary>One typed population-loading diagnostic.</summary>
public sealed record EcosystemPopulationLoadDiagnostic
{
    public EcosystemPopulationLoadDiagnostic(string code, string message)
    {
        Code = ValidateCode(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }

    static string ValidateCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code is not { Length: > 0 and <= 120 }
            || !char.IsAsciiLetterLower(code[0])
            || code[^1] is '.' or '-')
        {
            throw new ArgumentException(
                "A diagnostic code must be canonical lower-case dotted text.",
                nameof(code));
        }

        bool previousWasSeparator = false;
        foreach (char character in code)
        {
            if (char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character))
            {
                previousWasSeparator = false;
                continue;
            }

            if (character is not ('.' or '-') || previousWasSeparator)
            {
                throw new ArgumentException(
                    "A diagnostic code must be canonical lower-case dotted text.",
                    nameof(code));
            }

            previousWasSeparator = true;
        }

        return code;
    }
}

/// <summary>The terminal status retained by one loader receipt.</summary>
public enum EcosystemPopulationLoadSettlementKind
{
    Completed,
    Unavailable,
    Ambiguous,
    Incomplete,
    Rejected,
    Failed,
}

/// <summary>How one completed load establishes its population result.</summary>
public enum EcosystemPopulationCompletionKind
{
    Satisfied,
    NoMembers,
}

/// <summary>Owner-issued evidence for one completed population demand.</summary>
public sealed class EcosystemPopulationCompletionWitness
{
    internal EcosystemPopulationCompletionWitness(
        EcosystemPopulationLoadRequestIdentity request,
        EcosystemPopulationCompletionIdentity identity,
        EcosystemPopulationCompletionKind kind)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Request = request;
        Identity = identity;
        Kind = kind;
    }

    internal EcosystemPopulationLoadRequestIdentity Request { get; }
    public EcosystemPopulationCompletionIdentity Identity { get; }
    public EcosystemPopulationCompletionKind Kind { get; }
}
