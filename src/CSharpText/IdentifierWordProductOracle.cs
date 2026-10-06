namespace CSharpText;

using System.Collections.Immutable;

public static class IdentifierWordProductOracle
{
    public const string VocabularyVersion = "netcoreapp-ref-10.0.10-v1";
    public const string SourceCoordinate = "Microsoft.NETCore.App.Ref/10.0.10";
    public const string ReviewSetVersion = "runtime-mixed-case-v1";
    public const string Digest =
        "30E1BDE1E9A6B4A1F63C3C143882225C7FCE1A3A2CA08D030D97FE343AC613EE";

    static readonly ImmutableArray<IdentifierWordOracleEntry> s_entries =
    [
        Atom("AES"),
        Atom("ASCII"),
        Atom("CF"),
        Atom("ECDSA"),
        Atom("GUID"),
        Atom("HMAC"),
        Atom("IL"),
        Atom("IO"),
        Atom("IP"),
        Atom("OS"),
        Atom("RSA"),
        Atom("SQL"),
        Atom("TLS"),
        Atom("URI"),
        Atom("URL"),
        Compound("Int16"),
        Compound("Int32"),
        Compound("Int64"),
        Compound("IPv4"),
        Compound("IPv6"),
        Compound("MD5"),
        Compound("PKCS1"),
        Compound("SHA1"),
        Compound("SHA256"),
        Compound("SHA384"),
        Compound("SHA512"),
        Compound("UInt16"),
        Compound("UInt32"),
        Compound("UInt64"),
        Compound("UTF8"),
    ];

    public static IdentifierWordOracle Instance { get; } = Create();

    public static ImmutableArray<IdentifierWordOracleEntry> Entries => s_entries;

    static IdentifierWordOracle Create()
    {
        return IdentifierWordOracle.Create(
            IdentifierWordBreaker.GrammarVersion,
            VocabularyVersion,
            Digest,
            SourceCoordinate,
            ReviewSetVersion,
            s_entries) is IdentifierWordOracleConstruction.Created created
                ? created.Oracle
                : throw new InvalidOperationException(
                    "The checked-in identifier word oracle is invalid.");
    }

    static IdentifierWordOracleEntry Atom(string text)
        => new(text, IdentifierWordOracleEntryKind.Atom);

    static IdentifierWordOracleEntry Compound(string text)
        => new(text, IdentifierWordOracleEntryKind.Compound);
}
