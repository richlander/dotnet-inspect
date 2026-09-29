namespace CSharpText;

using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public enum IdentifierWordOracleEntryKind
{
    Atom,
    Compound,
}

public sealed record IdentifierWordOracleEntry(
    string Text,
    IdentifierWordOracleEntryKind Kind);

public sealed record IdentifierWordOracleReceipt(
    string GrammarVersion,
    string VocabularyVersion,
    string Digest,
    string SourceCoordinate,
    string ReviewSetVersion,
    int EntryCount);

public enum IdentifierWordOracleRejectionReason
{
    EmptyGrammarVersion,
    EmptyVocabularyVersion,
    EmptySourceCoordinate,
    EmptyReviewSetVersion,
    EmptyEntry,
    InvalidEntry,
    DuplicateEntry,
    DigestMismatch,
}

public abstract record IdentifierWordOracleConstruction
{
    private IdentifierWordOracleConstruction()
    {
    }

    public sealed record Created : IdentifierWordOracleConstruction
    {
        internal Created(IdentifierWordOracle oracle) => Oracle = oracle;

        public IdentifierWordOracle Oracle { get; }
    }

    public sealed record Rejected : IdentifierWordOracleConstruction
    {
        internal Rejected(
            IdentifierWordOracleRejectionReason reason,
            string? entry = null)
        {
            Reason = reason;
            Entry = entry;
        }

        public IdentifierWordOracleRejectionReason Reason { get; }

        public string? Entry { get; }
    }
}

public sealed class IdentifierWordOracle
{
    IdentifierWordOracle(
        IdentifierWordOracleReceipt receipt,
        ImmutableArray<IdentifierWordOracleEntry> entries)
    {
        Receipt = receipt;
        Entries = entries;
    }

    public IdentifierWordOracleReceipt Receipt { get; }

    public ImmutableArray<IdentifierWordOracleEntry> Entries { get; }

    public static IdentifierWordOracleConstruction Create(
        string grammarVersion,
        string vocabularyVersion,
        string expectedDigest,
        string sourceCoordinate,
        string reviewSetVersion,
        IEnumerable<IdentifierWordOracleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(grammarVersion);
        ArgumentNullException.ThrowIfNull(vocabularyVersion);
        ArgumentNullException.ThrowIfNull(expectedDigest);
        ArgumentNullException.ThrowIfNull(sourceCoordinate);
        ArgumentNullException.ThrowIfNull(reviewSetVersion);
        ArgumentNullException.ThrowIfNull(entries);

        if (grammarVersion.Length == 0)
            return new IdentifierWordOracleConstruction.Rejected(
                IdentifierWordOracleRejectionReason.EmptyGrammarVersion);
        if (vocabularyVersion.Length == 0)
            return new IdentifierWordOracleConstruction.Rejected(
                IdentifierWordOracleRejectionReason.EmptyVocabularyVersion);
        if (sourceCoordinate.Length == 0)
            return new IdentifierWordOracleConstruction.Rejected(
                IdentifierWordOracleRejectionReason.EmptySourceCoordinate);
        if (reviewSetVersion.Length == 0)
            return new IdentifierWordOracleConstruction.Rejected(
                IdentifierWordOracleRejectionReason.EmptyReviewSetVersion);

        var materialized = entries.ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in materialized)
        {
            if (entry.Text.Length == 0)
            {
                return new IdentifierWordOracleConstruction.Rejected(
                    IdentifierWordOracleRejectionReason.EmptyEntry);
            }
            if (!IsValidEntry(entry.Text)
                || entry.Kind == IdentifierWordOracleEntryKind.Atom
                    && entry.Text.Any(static ch => ch is >= '0' and <= '9'))
            {
                return new IdentifierWordOracleConstruction.Rejected(
                    IdentifierWordOracleRejectionReason.InvalidEntry,
                    entry.Text);
            }
            if (!seen.Add(entry.Text))
            {
                return new IdentifierWordOracleConstruction.Rejected(
                    IdentifierWordOracleRejectionReason.DuplicateEntry,
                    entry.Text);
            }
        }

        string digest = ComputeDigest(materialized);
        if (!string.Equals(expectedDigest, digest, StringComparison.Ordinal))
        {
            return new IdentifierWordOracleConstruction.Rejected(
                IdentifierWordOracleRejectionReason.DigestMismatch);
        }

        return new IdentifierWordOracleConstruction.Created(
            new IdentifierWordOracle(
                new IdentifierWordOracleReceipt(
                    grammarVersion,
                    vocabularyVersion,
                    digest,
                    sourceCoordinate,
                    reviewSetVersion,
                    materialized.Length),
                materialized));
    }

    public static string ComputeDigest(IEnumerable<IdentifierWordOracleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToImmutableArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReceiptDigest.AppendInt32(hash, materialized.Length);
        foreach (var entry in materialized)
        {
            hash.AppendData([(byte)entry.Kind]);
            ReceiptDigest.AppendString(hash, entry.Text);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static bool IsValidEntry(string text)
    {
        bool first = true;
        bool hasLetter = false;
        bool followsLetter = false;
        int offset = 0;
        while (offset < text.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                text.AsSpan(offset),
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
                return false;

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            bool letter = category is
                UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter;
            bool mark = IdentifierWordCharacterClasses.IsCombiningMark(category);
            bool digit = rune.IsAscii && Rune.IsDigit(rune);
            if ((!letter && !mark && !digit)
                || (first && !letter)
                || (mark && !followsLetter))
                return false;

            hasLetter |= letter;
            if (letter)
                followsLetter = true;
            else if (digit)
                followsLetter = false;
            first = false;
            offset += consumed;
        }
        return hasLetter;
    }
}

public sealed record IdentifierPopulationReceipt(
    string GrammarVersion,
    string Digest,
    int IdentifierCount);

public sealed record IdentifierNumberedFamilyEvidence(
    string Prefix,
    ImmutableArray<string> CanonicalOrdinals);

public enum IdentifierNumberedFamilyContextRejectionReason
{
    EmptyGrammarVersion,
    InvalidPopulationReceipt,
    EmptyPrefix,
    InvalidPrefix,
    TooFewDistinctOrdinals,
    InvalidOrdinal,
    DuplicateOrdinal,
    DuplicatePrefix,
}

public abstract record IdentifierNumberedFamilyContextConstruction
{
    private IdentifierNumberedFamilyContextConstruction()
    {
    }

    public sealed record Created : IdentifierNumberedFamilyContextConstruction
    {
        internal Created(IdentifierNumberedFamilyContext context) => Context = context;

        public IdentifierNumberedFamilyContext Context { get; }
    }

    public sealed record Rejected : IdentifierNumberedFamilyContextConstruction
    {
        internal Rejected(
            IdentifierNumberedFamilyContextRejectionReason reason,
            string? value = null)
        {
            Reason = reason;
            Value = value;
        }

        public IdentifierNumberedFamilyContextRejectionReason Reason { get; }

        public string? Value { get; }
    }
}

public sealed class IdentifierNumberedFamilyContext
{
    readonly HashSet<string> _prefixes;

    IdentifierNumberedFamilyContext(
        IdentifierPopulationReceipt receipt,
        ImmutableArray<IdentifierNumberedFamilyEvidence> families)
    {
        Receipt = receipt;
        Families = families;
        _prefixes = families
            .Select(static family => family.Prefix)
            .ToHashSet(StringComparer.Ordinal);
    }

    public IdentifierPopulationReceipt Receipt { get; }

    public ImmutableArray<IdentifierNumberedFamilyEvidence> Families { get; }

    public static IdentifierNumberedFamilyContextConstruction Build(
        IEnumerable<string> identifiers,
        string grammarVersion = IdentifierWordBreaker.GrammarVersion)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(grammarVersion);

        if (grammarVersion.Length == 0)
        {
            return new IdentifierNumberedFamilyContextConstruction.Rejected(
                IdentifierNumberedFamilyContextRejectionReason.EmptyGrammarVersion);
        }
        var population = identifiers.ToImmutableArray();
        var valuesByPrefix = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string identifier in population)
        {
            ArgumentNullException.ThrowIfNull(identifier);
            if (!TryGetCanonicalSuffix(identifier, out string prefix, out string ordinal))
                continue;
            if (!valuesByPrefix.TryGetValue(prefix, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                valuesByPrefix.Add(prefix, values);
            }
            values.Add(ordinal);
        }

        var families = valuesByPrefix
            .Where(static pair => pair.Value.Count >= 3)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new IdentifierNumberedFamilyEvidence(
                pair.Key,
                pair.Value.Order(StringComparer.Ordinal).ToImmutableArray()))
            .ToImmutableArray();

        var receipt = new IdentifierPopulationReceipt(
            grammarVersion,
            ComputePopulationDigest(population),
            population.Length);
        return Create(receipt, families);
    }

    public static IdentifierNumberedFamilyContextConstruction Create(
        IdentifierPopulationReceipt receipt,
        IEnumerable<IdentifierNumberedFamilyEvidence> families)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(families);

        if (receipt.GrammarVersion.Length == 0)
        {
            return new IdentifierNumberedFamilyContextConstruction.Rejected(
                IdentifierNumberedFamilyContextRejectionReason.EmptyGrammarVersion);
        }
        if (receipt.IdentifierCount < 0 || receipt.Digest.Length == 0)
        {
            return new IdentifierNumberedFamilyContextConstruction.Rejected(
                IdentifierNumberedFamilyContextRejectionReason.InvalidPopulationReceipt);
        }

        var materialized = families.ToImmutableArray();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in materialized)
        {
            if (family.Prefix.Length == 0)
            {
                return new IdentifierNumberedFamilyContextConstruction.Rejected(
                    IdentifierNumberedFamilyContextRejectionReason.EmptyPrefix);
            }
            if (!IsValidPrefix(family.Prefix))
            {
                return new IdentifierNumberedFamilyContextConstruction.Rejected(
                    IdentifierNumberedFamilyContextRejectionReason.InvalidPrefix,
                    family.Prefix);
            }
            if (!prefixes.Add(family.Prefix))
            {
                return new IdentifierNumberedFamilyContextConstruction.Rejected(
                    IdentifierNumberedFamilyContextRejectionReason.DuplicatePrefix,
                    family.Prefix);
            }

            var ordinals = new HashSet<string>(StringComparer.Ordinal);
            foreach (string ordinal in family.CanonicalOrdinals)
            {
                if (!IsCanonicalPositiveOrdinal(ordinal))
                {
                    return new IdentifierNumberedFamilyContextConstruction.Rejected(
                        IdentifierNumberedFamilyContextRejectionReason.InvalidOrdinal,
                        ordinal);
                }
                if (!ordinals.Add(ordinal))
                {
                    return new IdentifierNumberedFamilyContextConstruction.Rejected(
                        IdentifierNumberedFamilyContextRejectionReason.DuplicateOrdinal,
                        ordinal);
                }
            }
            if (ordinals.Count < 3)
            {
                return new IdentifierNumberedFamilyContextConstruction.Rejected(
                    IdentifierNumberedFamilyContextRejectionReason.TooFewDistinctOrdinals,
                    family.Prefix);
            }
        }

        ImmutableArray<IdentifierNumberedFamilyEvidence> canonical = materialized
            .OrderBy(static family => family.Prefix, StringComparer.Ordinal)
            .Select(static family => family with
            {
                CanonicalOrdinals = family.CanonicalOrdinals
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray(),
            })
            .ToImmutableArray();
        return new IdentifierNumberedFamilyContextConstruction.Created(
            new IdentifierNumberedFamilyContext(receipt, canonical));
    }

    public static string ComputePopulationDigest(IEnumerable<string> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var materialized = identifiers.ToImmutableArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReceiptDigest.AppendInt32(hash, materialized.Length);
        foreach (string identifier in materialized)
        {
            ArgumentNullException.ThrowIfNull(identifier);
            ReceiptDigest.AppendString(hash, identifier);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal bool ContainsPrefix(string prefix) => _prefixes.Contains(prefix);

    static bool TryGetCanonicalSuffix(
        string identifier,
        out string prefix,
        out string ordinal)
    {
        int start = identifier.Length;
        while (start > 0 && identifier[start - 1] is >= '0' and <= '9')
            start--;

        prefix = identifier[..start];
        ordinal = identifier[start..];
        return prefix.Length > 0
            && IsValidPrefix(prefix)
            && IsCanonicalPositiveOrdinal(ordinal);
    }

    static bool IsValidPrefix(string text)
    {
        if (text.Length == 0)
            return false;

        bool followsLetter = false;
        int offset = 0;
        while (offset < text.Length)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                text.AsSpan(offset),
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
                return false;

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            bool letter = category is
                UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter;
            bool mark = IdentifierWordCharacterClasses.IsCombiningMark(category);
            if (letter)
            {
                followsLetter = true;
            }
            else if (mark)
            {
                if (!followsLetter)
                    return false;
            }
            else if (rune.IsAscii && Rune.IsDigit(rune))
            {
                followsLetter = false;
            }
            else
            {
                return false;
            }
            offset += consumed;
        }
        return followsLetter;
    }

    internal static bool IsCanonicalPositiveOrdinal(string text)
    {
        if (text.Length == 0 || text[0] == '0')
            return false;
        foreach (char ch in text)
        {
            if (ch is < '0' or > '9')
                return false;
        }
        return true;
    }
}

internal static class IdentifierWordCharacterClasses
{
    public static bool IsCombiningMark(UnicodeCategory category)
        => category is
            UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}

internal static class ReceiptDigest
{
    public static void AppendString(IncrementalHash hash, string text)
    {
        AppendInt32(hash, text.Length);
        byte[] bytes = new byte[text.Length * sizeof(char)];
        for (int index = 0; index < text.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(index * sizeof(char), sizeof(char)),
                text[index]);
        }
        hash.AppendData(bytes);
    }

    public static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
