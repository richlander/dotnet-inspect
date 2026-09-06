using System.Collections.Immutable;
using InertText;

namespace ILInspector.Analysis;

/// <summary>
/// Validated exact text matched against decoded <c>ldstr</c> values.
/// </summary>
public sealed record StringLiteralUseOperand
{
    /// <summary>Largest accepted operand length, measured in UTF-16 code units.</summary>
    public const int MaximumLength = 1_024;

    StringLiteralUseOperand(string value)
    {
        RawValue = value;
        DisplayText = new InertString(TextPolicy.Field, value);
    }

    internal string RawValue { get; }

    public int CharacterCount => RawValue.Length;

    public InertString DisplayText { get; }

    public static StringLiteralUseOperand Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException(
                "The string-literal operand cannot be empty.",
                nameof(value));
        }
        if (value.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"The string-literal operand cannot exceed {MaximumLength} UTF-16 characters.");
        }

        return new StringLiteralUseOperand(value);
    }
}

/// <summary>Finite work and retention bounds for one assembly scan.</summary>
public sealed record StringLiteralUsePatternBudget
{
    public StringLiteralUsePatternBudget(
        int maximumMethods,
        int maximumMethodBodyBytes,
        long maximumMethodBodyBytesVisited,
        long maximumInstructions,
        long maximumDecodedUserStringCharacters,
        int maximumOccurrences)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMethods);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMethodBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMethodBodyBytesVisited);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumInstructions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDecodedUserStringCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOccurrences);

        MaximumMethods = maximumMethods;
        MaximumMethodBodyBytes = maximumMethodBodyBytes;
        MaximumMethodBodyBytesVisited = maximumMethodBodyBytesVisited;
        MaximumInstructions = maximumInstructions;
        MaximumDecodedUserStringCharacters = maximumDecodedUserStringCharacters;
        MaximumOccurrences = maximumOccurrences;
    }

    public int MaximumMethods { get; }

    public int MaximumMethodBodyBytes { get; }

    public long MaximumMethodBodyBytesVisited { get; }

    public long MaximumInstructions { get; }

    public long MaximumDecodedUserStringCharacters { get; }

    public int MaximumOccurrences { get; }

    public static StringLiteralUsePatternBudget Default { get; } = new(
        maximumMethods: 50_000,
        maximumMethodBodyBytes: 1_000_000,
        maximumMethodBodyBytesVisited: 16_000_000,
        maximumInstructions: 4_000_000,
        maximumDecodedUserStringCharacters: 4_000_000,
        maximumOccurrences: 10_000);
}

/// <summary>Resource-free physical address of one <c>ldstr</c> instruction.</summary>
public readonly record struct StringLiteralInstructionAddress
{
    public StringLiteralInstructionAddress(
        Guid moduleVersionId,
        int methodDefinitionToken,
        int ilOffset)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The module version id cannot be empty.",
                nameof(moduleVersionId));
        }
        ValidateMethodDefinitionToken(methodDefinitionToken);
        ArgumentOutOfRangeException.ThrowIfNegative(ilOffset);

        ModuleVersionId = moduleVersionId;
        MethodDefinitionToken = methodDefinitionToken;
        ILOffset = ilOffset;
    }

    public Guid ModuleVersionId { get; }

    public int MethodDefinitionToken { get; }

    public int ILOffset { get; }

    internal static void ValidateMethodDefinitionToken(int token)
    {
        if ((token & unchecked((int)0xFF000000)) != 0x06000000
            || (token & 0x00FFFFFF) == 0)
        {
            throw new ArgumentException(
                $"0x{token:X8} is not a non-nil MethodDef token.",
                nameof(token));
        }
    }
}

/// <summary>One decoded matching <c>ldstr</c> occurrence.</summary>
public sealed record StringLiteralUseOccurrence
{
    internal StringLiteralUseOccurrence(
        StringLiteralInstructionAddress address,
        int userStringToken,
        int literalCharacterCount,
        InertString literalText)
    {
        if ((userStringToken & unchecked((int)0xFF000000)) != 0x70000000
            || (userStringToken & 0x00FFFFFF) == 0)
        {
            throw new ArgumentException(
                $"0x{userStringToken:X8} is not a non-nil user-string token.",
                nameof(userStringToken));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(literalCharacterCount);

        Address = address;
        UserStringToken = userStringToken;
        LiteralCharacterCount = literalCharacterCount;
        LiteralText = literalText;
    }

    public StringLiteralInstructionAddress Address { get; }

    public int UserStringToken { get; }

    public int LiteralCharacterCount { get; }

    public InertString LiteralText { get; }
}

/// <summary>Completed charged work for one producer attempt.</summary>
public sealed record StringLiteralUsePatternReceipt
{
    internal StringLiteralUsePatternReceipt(
        int methodsVisited,
        int methodBodiesVisited,
        long methodBodyBytesVisited,
        long instructionsVisited,
        long userStringsDecoded,
        long userStringCharactersDecoded,
        int occurrencesRetained)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(methodsVisited);
        ArgumentOutOfRangeException.ThrowIfNegative(methodBodiesVisited);
        ArgumentOutOfRangeException.ThrowIfNegative(methodBodyBytesVisited);
        ArgumentOutOfRangeException.ThrowIfNegative(instructionsVisited);
        ArgumentOutOfRangeException.ThrowIfNegative(userStringsDecoded);
        ArgumentOutOfRangeException.ThrowIfNegative(userStringCharactersDecoded);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrencesRetained);

        MethodsVisited = methodsVisited;
        MethodBodiesVisited = methodBodiesVisited;
        MethodBodyBytesVisited = methodBodyBytesVisited;
        InstructionsVisited = instructionsVisited;
        UserStringsDecoded = userStringsDecoded;
        UserStringCharactersDecoded = userStringCharactersDecoded;
        OccurrencesRetained = occurrencesRetained;
    }

    public int MethodsVisited { get; }

    public int MethodBodiesVisited { get; }

    public long MethodBodyBytesVisited { get; }

    public long InstructionsVisited { get; }

    public long UserStringsDecoded { get; }

    public long UserStringCharactersDecoded { get; }

    public int OccurrencesRetained { get; }
}

public enum StringLiteralUseRejectionKind
{
    Incomplete,
    BoundedDecode,
    UnsupportedInput,
}

public enum StringLiteralUseFailureStage
{
    MethodEnumeration,
    MethodBody,
    InstructionDecode,
    UserString,
}

/// <summary>Resource-free location of a failed semantic operation.</summary>
public readonly record struct StringLiteralUseFailureSite
{
    public StringLiteralUseFailureSite(
        Guid moduleVersionId,
        int methodDefinitionToken,
        int? ilOffset = null)
    {
        StringLiteralInstructionAddress.ValidateMethodDefinitionToken(
            methodDefinitionToken);
        if (ilOffset is < 0)
            throw new ArgumentOutOfRangeException(nameof(ilOffset));

        ModuleVersionId = moduleVersionId;
        MethodDefinitionToken = methodDefinitionToken;
        ILOffset = ilOffset;
    }

    public Guid ModuleVersionId { get; }

    public int MethodDefinitionToken { get; }

    public int? ILOffset { get; }
}

public sealed record StringLiteralUseRejection(
    StringLiteralUseRejectionKind Kind,
    StringLiteralUseFailureStage Stage,
    StringLiteralUseFailureSite Site);

public enum StringLiteralUseLimitKind
{
    Methods,
    MethodBodyBytes,
    TotalMethodBodyBytes,
    Instructions,
    DecodedUserStringCharacters,
    Occurrences,
}

/// <summary>Closed result of one bounded semantic scan.</summary>
public abstract record StringLiteralUsePatternResult
{
    private protected StringLiteralUsePatternResult()
    {
    }

    public sealed record Match : StringLiteralUsePatternResult
    {
        internal Match(
            ImmutableArray<StringLiteralUseOccurrence> occurrences,
            StringLiteralUsePatternReceipt receipt)
        {
            if (occurrences.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A matching result requires at least one occurrence.",
                    nameof(occurrences));
            }
            ArgumentNullException.ThrowIfNull(receipt);
            if (receipt.OccurrencesRetained != occurrences.Length)
            {
                throw new ArgumentException(
                    "The receipt must account for every retained occurrence.",
                    nameof(receipt));
            }

            Occurrences = occurrences;
            Receipt = receipt;
        }

        public ImmutableArray<StringLiteralUseOccurrence> Occurrences { get; }

        public StringLiteralUsePatternReceipt Receipt { get; }
    }

    public sealed record NoMatch : StringLiteralUsePatternResult
    {
        internal NoMatch(StringLiteralUsePatternReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            if (receipt.OccurrencesRetained != 0)
            {
                throw new ArgumentException(
                    "A non-matching result cannot retain occurrences.",
                    nameof(receipt));
            }
            Receipt = receipt;
        }

        public StringLiteralUsePatternReceipt Receipt { get; }
    }

    public sealed record Rejected : StringLiteralUsePatternResult
    {
        internal Rejected(
            StringLiteralUseRejection rejection,
            StringLiteralUsePatternReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            ArgumentNullException.ThrowIfNull(receipt);

            Rejection = rejection;
            Receipt = receipt;
        }

        public StringLiteralUseRejection Rejection { get; }

        public StringLiteralUsePatternReceipt Receipt { get; }
    }

    public sealed record WorkLimitExceeded : StringLiteralUsePatternResult
    {
        internal WorkLimitExceeded(
            StringLiteralUseLimitKind limit,
            StringLiteralUsePatternReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);

            Limit = limit;
            Receipt = receipt;
        }

        public StringLiteralUseLimitKind Limit { get; }

        public StringLiteralUsePatternReceipt Receipt { get; }
    }
}
