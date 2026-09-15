using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>Which platform assembly views an operation requires.</summary>
public enum PlatformViewDemand
{
    Reference,
    Implementation,
    ReferenceAndImplementation,
}

/// <summary>One exact Metadata assembly identity requested from the platform.</summary>
public abstract class PlatformLibraryDemand
{
    private protected PlatformLibraryDemand()
    {
    }

    public sealed class Assembly : PlatformLibraryDemand
    {
        public Assembly(AssemblyReferenceIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public AssemblyReferenceIdentity Identity { get; }
    }

    public sealed class PlatformLibrary : PlatformLibraryDemand
    {
        public PlatformLibrary(PlatformLibraryIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            Identity = identity;
        }

        public PlatformLibraryIdentity Identity { get; }
    }
}

/// <summary>
/// Source-issued identity for one platform library. The diagnostic name is not
/// identity and is never interpreted as an assembly or package coordinate.
/// </summary>
public sealed class PlatformLibraryIdentity
{
    internal PlatformLibraryIdentity(
        PlatformLibraryIdentityAuthority authority,
        long ordinal,
        string name)
    {
        Authority = authority;
        Ordinal = ordinal;
        Name = name;
    }

    internal PlatformLibraryIdentityAuthority Authority { get; }
    public long Ordinal { get; }
    public string Name { get; }
}

/// <summary>Owner authority that issues platform-library identities.</summary>
public sealed class PlatformLibraryIdentityAuthority
{
    long nextOrdinal;

    private PlatformLibraryIdentityAuthority(string name) => Name = name;

    public string Name { get; }

    public static PlatformLibraryIdentityAuthority Create(string name) =>
        new(PlatformHouseIdentityName.Validate(name));

    public PlatformLibraryIdentity Issue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        long ordinal = Interlocked.Increment(ref nextOrdinal);
        return new(this, ordinal, name);
    }
}

/// <summary>One-library or complete-population realization demand.</summary>
public abstract class PlatformPopulationDemand
{
    private protected PlatformPopulationDemand()
    {
    }

    public sealed class Library : PlatformPopulationDemand
    {
        public Library(PlatformLibraryDemand value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public PlatformLibraryDemand Value { get; }
    }

    public sealed class CompletePopulation : PlatformPopulationDemand
    {
        public CompletePopulation()
        {
        }
    }
}

/// <summary>Which independent platform documentation channels are requested.</summary>
public enum PlatformDocumentationDemand
{
    CompiledXml,
    SourceDerived,
    CompiledXmlAndSourceDerived,
}
