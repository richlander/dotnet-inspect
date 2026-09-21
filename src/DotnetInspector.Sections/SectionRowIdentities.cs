namespace DotnetInspector.Sections;

public abstract class SectionRowSchemaIdentity
{
    private protected SectionRowSchemaIdentity()
    {
    }
}

public sealed class SectionRowSchemaIdentity<TRow> :
    SectionRowSchemaIdentity
{
    private SectionRowSchemaIdentity()
    {
    }

    public static SectionRowSchemaIdentity<TRow> Create() =>
        new();
}

public sealed class RowIntentBindingIdentity
{
    internal RowIntentBindingIdentity()
    {
    }
}

public sealed class ShapingCohortIdentity
{
    internal ShapingCohortIdentity()
    {
    }
}
