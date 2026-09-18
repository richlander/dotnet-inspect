using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

/// <summary>
/// Opaque application-lifetime metadata for one statically rooted loader.
/// </summary>
public abstract class EcosystemPopulationLoaderBinding
{
    private protected EcosystemPopulationLoaderBinding(
        EcosystemPopulationLoaderId id) =>
        Id = id ?? throw new ArgumentNullException(nameof(id));

    public EcosystemPopulationLoaderId Id { get; }

    public EcosystemPopulationLoaderCorrespondence CreateCorrespondence(
        WorkspaceEcosystemRegistrationDeclaration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return CreateCorrespondenceCore(registration);
    }

    public static EcosystemPopulationLoaderBinding<TInputs> Create<TInputs>(
        EcosystemPopulationLoaderId id,
        Func<
            EcosystemPopulationLoadRequest<TInputs>,
            ValueTask<EcosystemPopulationLoaderReply>> load)
        where TInputs : class, IEcosystemPopulationLoadInputs =>
        EcosystemPopulationLoaderBinding<TInputs>.Create(id, load);

    private protected abstract EcosystemPopulationLoaderCorrespondence
        CreateCorrespondenceCore(
            WorkspaceEcosystemRegistrationDeclaration registration);
}

/// <summary>
/// Typed executable binding selected through its non-generic metadata base.
/// </summary>
public sealed class EcosystemPopulationLoaderBinding<TInputs> :
    EcosystemPopulationLoaderBinding
    where TInputs : class, IEcosystemPopulationLoadInputs
{
    readonly Func<
        EcosystemPopulationLoadRequest<TInputs>,
        ValueTask<EcosystemPopulationLoaderReply>> _load;

    EcosystemPopulationLoaderBinding(
        EcosystemPopulationLoaderId id,
        Func<
            EcosystemPopulationLoadRequest<TInputs>,
            ValueTask<EcosystemPopulationLoaderReply>> load)
        : base(id) =>
        _load = load;

    internal static EcosystemPopulationLoaderBinding<TInputs> Create(
        EcosystemPopulationLoaderId id,
        Func<
            EcosystemPopulationLoadRequest<TInputs>,
            ValueTask<EcosystemPopulationLoaderReply>> load)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(load);
        if (load.Target is not null || load.GetInvocationList().Length != 1)
        {
            throw new ArgumentException(
                "An Ecosystem population loader must be exactly one target-free static method group.",
                nameof(load));
        }

        return new EcosystemPopulationLoaderBinding<TInputs>(id, load);
    }

    internal ValueTask<EcosystemPopulationLoaderReply> LoadAsync(
        EcosystemPopulationLoadRequest<TInputs> request) =>
        _load(request);

    private protected override EcosystemPopulationLoaderCorrespondence
        CreateCorrespondenceCore(
            WorkspaceEcosystemRegistrationDeclaration registration) =>
        new EcosystemPopulationLoaderCorrespondence<TInputs>(
            registration,
            this);
}
