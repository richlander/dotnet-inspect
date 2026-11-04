using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal abstract class OutputItemBinding<TModel>
{
    private protected OutputItemBinding(string name, bool documentContainsItems)
    {
        Name = name;
        DocumentContainsItems = documentContainsItems;
    }

    public string Name { get; }
    public bool DocumentContainsItems { get; }

    public static OutputItemBinding<TModel> Create<TItem>(
        string name,
        Func<TModel, IReadOnlyList<TItem>> items,
        Func<TModel, IReadOnlyList<TItem>, TModel> rebind,
        bool documentContainsItems = true) =>
        new TypedBinding<TItem>(name, items, rebind, documentContainsItems);

    public abstract bool TrySelect(
        TModel model,
        RowSelectionIntent<string>? intent,
        out TModel selected);

    private sealed class TypedBinding<TItem>(
        string name,
        Func<TModel, IReadOnlyList<TItem>> items,
        Func<TModel, IReadOnlyList<TItem>, TModel> rebind,
        bool documentContainsItems)
        : OutputItemBinding<TModel>(name, documentContainsItems)
    {
        private readonly SectionRowSchemaIdentity<TItem> _schema =
            SectionRowSchemaIdentity<TItem>.Create();

        public override bool TrySelect(
            TModel model,
            RowSelectionIntent<string>? intent,
            out TModel selected)
        {
            selected = model;
            if (intent is not { Operations.Count: > 0 })
                return true;

            var declaration = new SectionRowSetDeclaration<string, TModel, TItem>(
                Name, _schema, items(model), rebind);
            var association = new SectionRowIntentAssociation<string>(
                [Name],
                [new SectionRowSchemaBinding<string, TItem>(
                    _schema,
                    sequences => RowsCohortExecutor.ApplyUnordered(sequences, intent))]);
            var result = SectionRowExecutor.ApplyRows(
                SectionRowExecutionRequest<string, TModel>.Create(
                    [declaration], association));
            if (!result.IsSuccess)
            {
                var failure = result.Failure!.Failure;
                CommandError.Write(
                    $"{Name} item selection stage {failure.StageNumber} requires item "
                    + $"{failure.RequiredPosition}, but only {failure.AvailableCount} items are available.");
                return false;
            }

            selected = result.Rebind(model);
            return true;
        }
    }
}
