public sealed record @string(string Value);

public sealed record @byte(string Value);

public sealed record KeywordHolder(
    string Title,
    @string Inner,
    @string[] Many,
    IReadOnlyDictionary<string, @string> ByName,
    @byte[] ByteDtos);
