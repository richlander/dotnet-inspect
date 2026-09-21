using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public enum CSharpDecompilationStatus
{
    Available,
    Absent,
    Failed,
    Incomplete,
}

public enum CSharpBodyProjectionKind
{
    MemberBody,
    AccessorBody,
    FieldInitializerProbe,
}

public sealed record CSharpBodyProjection(
    MetadataMethodAddress Address,
    DecompilerResult Projection,
    CSharpBodyProjectionKind Kind,
    bool ContributesToOutput)
{
    public SelectedPropertyAccessorSource? PropertySource { get; init; }
}

public sealed record CSharpDecompilationAttempt(
    CSharpDecompilationStatus Status,
    DecompilerResult Projection,
    ImmutableArray<string> Namespaces,
    ImmutableArray<CSharpBodyProjection> BodyProjections,
    bool PdbSupplied,
    DecompilerSymbolSource Symbols,
    int BodyProjectionsAttempted)
{
    /// <summary>
    /// The selected declaration without its optional containing context, indented
    /// for a type body. Comparison consumers use this fragment, not standalone source.
    /// </summary>
    public string? MemberDeclarationText { get; init; }

    public string? Text => Projection.Output;
    public DecompilationFidelity Fidelity => Projection.Fidelity;
    public bool IsAvailable => Status == CSharpDecompilationStatus.Available;
    public string DiagnosticSummary => string.Join(
        "; ",
        Projection.Diagnostics.Select(static diagnostic => diagnostic.ToString())
            .Concat(BodyProjections.SelectMany(static body =>
                body.Projection.Diagnostics.Select(diagnostic =>
                    $"{body.Kind} 0x{body.Address.Token:x8}: {diagnostic}"))));
}

/// <summary>
/// Produces detached C# source from one exact caller-selected assembly image.
/// Source and PDB acquisition policy remain caller-owned.
/// </summary>
public static class CSharpDecompilerService
{
    public const int DefaultMaxBodyProjections = 1024;

    public static CSharpDecompilationAttempt ProduceMember(
        ApiType type,
        ApiMember member,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ImmutableArray<byte>? pdbImage = null,
        PrinterOptions? printerOptions = null,
        int maxBodyProjections = DefaultMaxBodyProjections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return Produce(
            assembly,
            bindingPolicy,
            pdbImage,
            printerOptions,
            maxBodyProjections,
            cancellationToken,
            (openSource, tracker) =>
                MemberBodyProducer.ComposeServiceMember(
                    type,
                    member,
                    openSource,
                    printerOptions,
                    tracker));
    }

    public static CSharpDecompilationAttempt ProduceType(
        ApiType type,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ImmutableArray<byte>? pdbImage = null,
        PrinterOptions? printerOptions = null,
        int maxBodyProjections = DefaultMaxBodyProjections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Produce(
            assembly,
            bindingPolicy,
            pdbImage,
            printerOptions,
            maxBodyProjections,
            cancellationToken,
            (openSource, tracker) =>
                MemberBodyProducer.ComposeServiceType(
                    type,
                    openSource,
                    printerOptions,
                    tracker));
    }

    static CSharpDecompilationAttempt Produce(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ImmutableArray<byte>? pdbImage,
        PrinterOptions? printerOptions,
        int maxBodyProjections,
        CancellationToken cancellationToken,
        Func<
            Func<MetadataSource>,
            CSharpCompositionTracker,
            CSharpServiceCompositionResult> compose)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBodyProjections);
        cancellationToken.ThrowIfCancellationRequested();

        bool pdbSupplied = pdbImage.HasValue;
        if (pdbImage is { IsDefaultOrEmpty: true })
        {
            return Failure(
                pdbSupplied,
                "The supplied Portable PDB image is default or empty.");
        }

        var effectivePolicy =
            new CancellationCheckingBindingPolicy(
                bindingPolicy,
                cancellationToken);
        var tracker =
            new CSharpCompositionTracker(
                maxBodyProjections,
                cancellationToken);

        MetadataSource OpenSource()
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataSource source = pdbImage is { } supplied
                ? MetadataSource.OpenWithSuppliedPortablePdb(
                    assembly,
                    supplied,
                    effectivePolicy,
                    cancellationToken:
                        cancellationToken)
                : MetadataSource.OpenWithoutSymbols(
                    assembly,
                    effectivePolicy);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return source;
            }
            catch (OperationCanceledException)
            {
                source.Dispose();
                throw;
            }
        }

        try
        {
            CSharpServiceCompositionResult composed =
                compose(OpenSource, tracker);
            cancellationToken.ThrowIfCancellationRequested();
            return CompleteAttempt(
                composed,
                tracker,
                pdbSupplied,
                printerOptions);
        }
        catch (CSharpCompositionBudgetExceededException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CSharpDecompilationAttempt(
                CSharpDecompilationStatus.Incomplete,
                DecompilerResult.Failure(
                    DiagnosticIds.CompositionBudgetExceeded,
                    $"C# composition exhausted its {maxBodyProjections} body-projection budget."),
                [],
                tracker.Detach(),
                pdbSupplied,
                tracker.Symbols,
                tracker.Attempted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidDataException
                or IOException
                or ArgumentException
                or InvalidOperationException
                or NotSupportedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CSharpDecompilationAttempt(
                CSharpDecompilationStatus.Failed,
                DecompilerResult.Failure(
                    DiagnosticIds.ServiceInputFailure,
                    $"C# composition failed: {ex.GetType().Name}: {ex.Message}"),
                [],
                tracker.Detach(),
                pdbSupplied,
                tracker.Symbols,
                tracker.Attempted);
        }
    }

    static CSharpDecompilationAttempt CompleteAttempt(
        CSharpServiceCompositionResult composed,
        CSharpCompositionTracker tracker,
        bool pdbSupplied,
        PrinterOptions? printerOptions)
    {
        ImmutableArray<CSharpBodyProjection> bodies = tracker.Detach();
        bool contributingBodyFailed = bodies.Any(
            static body =>
                body.ContributesToOutput
                && body.Projection.Fidelity
                    == DecompilationFidelity.Failed);

        CSharpDecompilationStatus status = composed.Status switch
        {
            MemberBodyProductionStatus.Complete
                when contributingBodyFailed =>
                    CSharpDecompilationStatus.Failed,
            MemberBodyProductionStatus.Complete =>
                CSharpDecompilationStatus.Available,
            MemberBodyProductionStatus.Absent =>
                CSharpDecompilationStatus.Absent,
            _ => CSharpDecompilationStatus.Failed,
        };

        DecompilerResult projection;
        if (composed.Text is { } text)
        {
            DecompilationFidelity fidelity = bodies
                .Where(static body => body.ContributesToOutput)
                .Select(static body => body.Projection.Fidelity)
                .DefaultIfEmpty(DecompilationFidelity.Full)
                .Min();
            IReadOnlyList<DecompilerDiagnostic> diagnostics =
                status == CSharpDecompilationStatus.Failed
                    ? [
                        new(
                            DiagnosticIds.ServiceInputFailure,
                            "C# composition contains a failed contributing body projection."),
                    ]
                    : [];
            projection = new DecompilerResult(
                text,
                fidelity,
                diagnostics)
            {
                Metadata = new DecompilerResultMetadata(
                    DecompilerOptions.FromPrinterOptions(printerOptions),
                    []),
            };
        }
        else if (composed.Failure is { } failure
            && !bodies.Any(
                static body =>
                    body.Projection.Diagnostics.Count > 0))
        {
            projection = failure;
        }
        else
        {
            string detail = status == CSharpDecompilationStatus.Absent
                ? "The requested C# projection is absent."
                : "C# composition failed.";
            projection = DecompilerResult.Failure(
                DiagnosticIds.ServiceInputFailure,
                detail);
        }

        return new CSharpDecompilationAttempt(
            status,
            projection,
            composed.Namespaces,
            bodies,
            pdbSupplied,
            composed.Symbols,
            tracker.Attempted)
        {
            MemberDeclarationText = composed.MemberDeclarationText,
        };
    }

    static CSharpDecompilationAttempt Failure(
        bool pdbSupplied,
        string message)
        => new(
            CSharpDecompilationStatus.Failed,
            DecompilerResult.Failure(
                DiagnosticIds.ServiceInputFailure,
                message),
            [],
            [],
            pdbSupplied,
            DecompilerSymbolSource.None,
            0);

    sealed class CancellationCheckingBindingPolicy(
        IAssemblyBindingPolicy inner,
        CancellationToken cancellationToken)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyBindingSelectionSnapshot selection =
                inner.Select(request);
            cancellationToken.ThrowIfCancellationRequested();
            return selection;
        }
    }
}

internal sealed record CSharpServiceCompositionResult(
    MemberBodyProductionStatus Status,
    string? Text,
    ImmutableArray<string> Namespaces,
    DecompilerResult? Failure,
    DecompilerSymbolSource Symbols)
{
    internal string? MemberDeclarationText { get; init; }
}

internal sealed class CSharpCompositionBudgetExceededException : Exception
{
}

internal sealed class CSharpCompositionTracker(
    int maxBodyProjections,
    CancellationToken cancellationToken)
{
    readonly List<ProjectionEntry> _projections = [];
    int _attempted;

    internal int Attempted => _attempted;
    internal DecompilerSymbolSource Symbols { get; private set; }

    internal ProjectionTicket Begin(
        MetadataSource source,
        MethodDefinitionHandle handle,
        CSharpBodyProjectionKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (_attempted >= maxBodyProjections)
            throw new CSharpCompositionBudgetExceededException();

        _attempted++;
        return new ProjectionTicket(
            this,
            MetadataMethodAddress.Create(source.Reader, handle),
            kind);
    }

    internal void Complete(
        ProjectionTicket ticket,
        DecompilerResult projection,
        SelectedPropertyAccessorSource? propertySource = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        int index = _projections.Count;
        _projections.Add(
            new ProjectionEntry(
                ticket.Address,
                projection,
                ticket.Kind,
                ContributesToOutput: true,
                propertySource));
        ticket.Complete(index);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void ObserveSymbols(DecompilerSymbolSource symbols) =>
        Symbols = symbols;

    internal void MarkNonContributing(int index)
    {
        ProjectionEntry entry = _projections[index];
        _projections[index] = entry with
        {
            ContributesToOutput = false,
        };
    }

    internal ImmutableArray<CSharpBodyProjection> Detach()
        => [
            .. _projections.Select(
                static projection =>
                    new CSharpBodyProjection(
                        projection.Address,
                        projection.Projection,
                        projection.Kind,
                        projection.ContributesToOutput)
                    {
                        PropertySource =
                            projection.PropertySource,
                    }),
        ];

    sealed record ProjectionEntry(
        MetadataMethodAddress Address,
        DecompilerResult Projection,
        CSharpBodyProjectionKind Kind,
        bool ContributesToOutput,
        SelectedPropertyAccessorSource? PropertySource);

    internal sealed class ProjectionTicket(
        CSharpCompositionTracker owner,
        MetadataMethodAddress address,
        CSharpBodyProjectionKind kind)
    {
        int _index = -1;

        internal MetadataMethodAddress Address { get; } = address;
        internal CSharpBodyProjectionKind Kind { get; } = kind;

        internal void Complete(int index) => _index = index;

        internal void MarkNonContributing()
        {
            if (_index >= 0)
                owner.MarkNonContributing(_index);
        }
    }
}
