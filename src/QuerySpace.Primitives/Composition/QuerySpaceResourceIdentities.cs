namespace QuerySpace.Composition;

/// <summary>
/// Opaque identity for one resource domain whose owner issues resource and
/// source-binding identities.
/// </summary>
public sealed class QuerySpaceResourceDomainIdentity
{
    QuerySpaceResourceDomainIdentity()
    {
    }

    /// <summary>Creates one owner-issued resource domain identity.</summary>
    public static QuerySpaceResourceDomainIdentity Create() => new();
}

/// <summary>Opaque owner-issued identity for one resource.</summary>
public sealed class QuerySpaceResourceIdentity
{
    QuerySpaceResourceIdentity(QuerySpaceResourceDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one resource identity in the supplied owner domain.</summary>
    public static QuerySpaceResourceIdentity Create(
        QuerySpaceResourceDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal QuerySpaceResourceDomainIdentity Domain { get; }
}

/// <summary>
/// Opaque owner-issued identity for one source binding within a resource
/// domain.
/// </summary>
public sealed class QuerySpaceSourceBindingIdentity
{
    QuerySpaceSourceBindingIdentity(QuerySpaceResourceDomainIdentity domain)
    {
        Domain = domain;
    }

    /// <summary>Creates one source-binding identity in the supplied owner domain.</summary>
    public static QuerySpaceSourceBindingIdentity Create(
        QuerySpaceResourceDomainIdentity domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new(domain);
    }

    internal QuerySpaceResourceDomainIdentity Domain { get; }
}

/// <summary>Opaque caller-issued identity for one request association.</summary>
public sealed class QuerySpaceRequestAssociationIdentity
{
    QuerySpaceRequestAssociationIdentity()
    {
    }

    /// <summary>Creates one caller-issued association identity.</summary>
    public static QuerySpaceRequestAssociationIdentity Create() => new();
}
