using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// Runs the registered hidden-fact classifiers and returns their merged
/// annotations — the registry the "registry, not catalog" design calls for.
/// Adding a fact family is a matter of registering a classifier here; nothing
/// else changes. Classifiers are pure and order-independent, so the engine is
/// just a fan-out.
/// </summary>
/// <remarks>
/// Only the <see cref="AnnotationStage.Imported"/> stage is wired today, because
/// the one shipped classifier observes it. Lowered/Raised staging slots in the
/// same way (run the corresponding pass set, then the stage's classifiers)
/// when a classifier needs a higher altitude.
/// </remarks>
public sealed class AnnotationEngine
{
    readonly IReadOnlyList<IHiddenFactClassifier> _classifiers;

    public AnnotationEngine(params IHiddenFactClassifier[] classifiers)
        => _classifiers = classifiers;

    /// <summary>The shipped set of classifiers.</summary>
    public static AnnotationEngine Default { get; } = new(
        new AllocationClassifier(), new UnsafetyClassifier(), new LifetimeClassifier());

    /// <summary>
    /// Classifies the freshly imported tree — call before the raising passes
    /// run, so <see cref="AnnotationStage.Imported"/> classifiers see literal
    /// nodes. The annotations key to IL offsets, so they remain valid after the
    /// caller raises the same function.
    /// </summary>
    public IReadOnlyList<Annotation> ClassifyImported(IrFunction imported)
    {
        var annotations = new List<Annotation>();
        foreach (var classifier in _classifiers)
        {
            if (classifier.Stage != AnnotationStage.Imported)
                continue;
            annotations.AddRange(classifier.Classify(imported));
        }
        return annotations;
    }
}
