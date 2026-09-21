namespace QuerySpace.Operations;

public sealed class QueryOperationRegistry
{
    private readonly IReadOnlyDictionary<
        string,
        IQueryOperationRoute> _routesByIdentity;

    private QueryOperationRegistry(
        IReadOnlyList<IQueryOperationRoute> routes,
        IReadOnlyDictionary<
            string,
            IQueryOperationRoute> routesByIdentity)
    {
        Routes = routes;
        _routesByIdentity = routesByIdentity;
    }

    public IReadOnlyList<IQueryOperationRoute> Routes { get; }

    public static QueryOperationRegistry Create(
        IReadOnlyList<IQueryOperationRoute> routes)
    {
        IReadOnlyList<IQueryOperationRoute> copy =
            QueryOperationContract.Copy(
                routes,
                nameof(routes));
        var routesByIdentity =
            new Dictionary<string, IQueryOperationRoute>(
                StringComparer.Ordinal);
        foreach (IQueryOperationRoute route in copy)
        {
            if (!routesByIdentity.TryAdd(
                    route.Identity,
                    route))
            {
                throw new ArgumentException(
                    $"Query route identity '{route.Identity}' is duplicated.",
                    nameof(routes));
            }
        }

        return new(
            copy,
            routesByIdentity);
    }

    public bool TryGetRoute(
        string identity,
        out IQueryOperationRoute? route)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _routesByIdentity.TryGetValue(
            identity,
            out route);
    }
}
