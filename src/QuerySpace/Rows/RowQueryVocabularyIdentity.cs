namespace QuerySpace.Rows;

public sealed class RowQueryVocabularyIdentity
{
    private RowQueryVocabularyIdentity()
    {
    }

    public static RowQueryVocabularyIdentity Create() => new();
}

public sealed class RowQueryKeyIdentity
{
    private RowQueryKeyIdentity()
    {
    }

    public static RowQueryKeyIdentity Create() => new();
}

public sealed class RowQueryNamedOrderIdentity
{
    private RowQueryNamedOrderIdentity()
    {
    }

    public static RowQueryNamedOrderIdentity Create() => new();
}

public sealed class ResolvedRowQueryOrderIdentity
{
    internal ResolvedRowQueryOrderIdentity()
    {
    }
}
