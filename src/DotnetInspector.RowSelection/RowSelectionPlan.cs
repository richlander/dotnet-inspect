namespace DotnetInspector.RowSelection;

public sealed class RowSelectionPlan<TOrder>
    where TOrder : notnull
{
    private RowSelectionPlan(
        IReadOnlyList<RowSelectionStage<TOrder>> stages)
    {
        Stages = stages;
    }

    public static RowSelectionPlan<TOrder> Empty { get; } =
        new(RowSelectionSnapshot.Empty<RowSelectionStage<TOrder>>());

    public IReadOnlyList<RowSelectionStage<TOrder>> Stages { get; }

    public static RowSelectionPlan<TOrder> Create(
        IReadOnlyList<RowSelectionStage<TOrder>> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);

        var copy = new RowSelectionStage<TOrder>[stages.Count];
        for (int index = 0; index < stages.Count; index++)
        {
            copy[index] = stages[index]
                ?? throw new ArgumentNullException(
                    nameof(stages),
                    $"Stage {index + 1} is null.");
        }

        return copy.Length == 0
            ? Empty
            : new(RowSelectionSnapshot.Own(copy));
    }

    public RowSelectionPlan<TOrder> Append(
        RowSelectionStage<TOrder> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var copy =
            new RowSelectionStage<TOrder>[Stages.Count + 1];
        for (int index = 0; index < Stages.Count; index++)
            copy[index] = Stages[index];
        copy[^1] = stage;
        return new(RowSelectionSnapshot.Own(copy));
    }
}
