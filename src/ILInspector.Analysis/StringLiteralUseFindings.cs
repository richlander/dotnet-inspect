using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.Analysis;

/// <summary>
/// Finding projection and exact comparison for decoded string-literal uses.
/// </summary>
public static class StringLiteralUseFindings
{
    private const string IdentityPrefix = "utf16-v1:";

    public static FindingDescriptor Descriptor { get; } =
        new("analysis.string-literal-use", "String literal use");

    public static FindingInspection<StringLiteralUseOccurrence> Inspect(
        StringLiteralUsePatternResult result,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(subject);

        return result switch
        {
            StringLiteralUsePatternResult.Match match =>
                new FindingInspection<StringLiteralUseOccurrence>.Complete(
                    Project(match.Occurrences, subject)),
            StringLiteralUsePatternResult.NoMatch =>
                new FindingInspection<StringLiteralUseOccurrence>.Complete([]),
            StringLiteralUsePatternResult.Rejected rejected =>
                Failed(subject, RejectionReason(rejected)),
            StringLiteralUsePatternResult.WorkLimitExceeded limited =>
                Failed(subject, LimitReason(limited)),
            _ => throw new InvalidOperationException(
                "Unknown string-literal-use result."),
        };
    }

    public static FindingComparison<StringLiteralUseOccurrence> Compare(
        StringLiteralUsePatternResult oldResult,
        StringLiteralUsePatternResult newResult,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldResult);
        ArgumentNullException.ThrowIfNull(newResult);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
            Inspect(oldResult, subject),
            Inspect(newResult, subject));
    }

    internal static string CreateIdentityKey(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        return string.Create(
            checked(IdentityPrefix.Length + literal.Length * 4),
            literal,
            static (destination, value) =>
            {
                IdentityPrefix.AsSpan().CopyTo(destination);
                int offset = IdentityPrefix.Length;
                foreach (char codeUnit in value)
                {
                    destination[offset++] = Hex(codeUnit >> 12);
                    destination[offset++] = Hex(codeUnit >> 8);
                    destination[offset++] = Hex(codeUnit >> 4);
                    destination[offset++] = Hex(codeUnit);
                }
            });
    }

    private static ImmutableArray<Finding<StringLiteralUseOccurrence>> Project(
        ImmutableArray<StringLiteralUseOccurrence> occurrences,
        FindingSubject subject)
    {
        var findings =
            ImmutableArray.CreateBuilder<Finding<StringLiteralUseOccurrence>>(
                occurrences.Length);
        for (int i = 0; i < occurrences.Length; i++)
        {
            StringLiteralUseOccurrence occurrence = occurrences[i];
            findings.Add(new Finding<StringLiteralUseOccurrence>(
                subject,
                Descriptor,
                new FindingKey(occurrence.LiteralIdentityKey),
                occurrence,
                Ordinal: i,
                Detail: occurrence.LiteralText.ToString()));
        }

        return findings.MoveToImmutable();
    }

    private static FindingInspection<StringLiteralUseOccurrence> Failed(
        FindingSubject subject,
        string reason) =>
        new FindingInspection<StringLiteralUseOccurrence>.Failed(
            new InspectionError(subject, Descriptor, reason));

    private static string RejectionReason(
        StringLiteralUsePatternResult.Rejected rejected)
    {
        StringLiteralUseFailureSite site = rejected.Rejection.Site;
        string location =
            $"{site.ModuleVersionId:D}/0x{site.MethodDefinitionToken:X8}"
            + (site.ILOffset is { } offset ? $"/IL_{offset:X4}" : "");
        return $"String-literal inspection was rejected "
            + $"({rejected.Rejection.Kind}/{rejected.Rejection.Stage}) "
            + $"at {location}; {Receipt(rejected.Receipt)}.";
    }

    private static string LimitReason(
        StringLiteralUsePatternResult.WorkLimitExceeded limited) =>
        $"String-literal inspection exceeded the {limited.Limit} work limit; "
        + $"{Receipt(limited.Receipt)}.";

    private static string Receipt(StringLiteralUsePatternReceipt receipt) =>
        $"visited {receipt.MethodsVisited} methods, "
        + $"{receipt.MethodBodiesVisited} bodies, "
        + $"{receipt.MethodBodyBytesVisited} body bytes, "
        + $"{receipt.InstructionsVisited} instructions, "
        + $"{receipt.UserStringsDecoded} user strings, "
        + $"{receipt.UserStringCharactersDecoded} user-string characters, "
        + $"and retained {receipt.OccurrencesRetained} occurrences";

    private static char Hex(int value)
    {
        int nibble = value & 0xF;
        return (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
    }
}
