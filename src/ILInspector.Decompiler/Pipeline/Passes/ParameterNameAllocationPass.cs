using System.Collections.Immutable;
using CSharpText;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Allocates final spellings for parameters whose metadata names were absent.
/// Exact binders reserve first across the lexical function tree; synthesized
/// names then resolve around them and around enclosing binders.
/// </summary>
public sealed class ParameterNameAllocationPass : IIrPass
{
    public string Name => "parameter-name-allocation";

    public void Run(IrFunction function, PassContext context)
        => AllocateScope(
            function.Signature.Parameters,
            function.Signature.GenericParameterNames,
            function.LocalNames,
            function,
            []);

    static void AllocateScope(
        ImmutableArray<Parameter> parameters,
        ImmutableArray<string> genericParameterNames,
        ImmutableArray<string?> localNames,
        IrNode scope,
        IEnumerable<string> enclosingNames)
    {
        var reserved = new HashSet<string>(enclosingNames, StringComparer.Ordinal);
        reserved.UnionWith(genericParameterNames);
        AddUsableLocalNames(reserved, localNames, scope);
        AddExactDescendantBinderNames(reserved, scope);

        string?[] exactNames = [
            .. parameters.Select(parameter =>
                parameter.NameIsSynthesized ? null : parameter.Name),
        ];
        string[] displayNames = CSharpParameterNames.Allocate(exactNames, reserved);
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].NameIsSynthesized)
                parameters[index].SetSynthesizedDisplayName(displayNames[index]);
        }

        var childEnclosingNames = new HashSet<string>(
            enclosingNames,
            StringComparer.Ordinal);
        childEnclosingNames.UnionWith(genericParameterNames);
        childEnclosingNames.UnionWith(
            parameters.Select(parameter => parameter.DisplayName));
        AddUsableLocalNames(childEnclosingNames, localNames, scope);
        foreach (var localFunction in scope
            .DescendantsOutsideNestedFunctions
            .OfType<LocalFunctionStatement>())
        {
            childEnclosingNames.Add(localFunction.Name);
        }

        foreach (var nested in scope.DescendantsOutsideNestedFunctions)
        {
            switch (nested)
            {
                case Lambda lambda:
                    AllocateScope(
                        lambda.Parameters,
                        [],
                        lambda.LocalNames,
                        lambda.Body,
                        childEnclosingNames);
                    break;
                case LocalFunctionStatement localFunction:
                    AllocateScope(
                        localFunction.Parameters,
                        [],
                        localFunction.LocalNames,
                        localFunction.Body,
                        childEnclosingNames);
                    break;
            }
        }
    }

    static void AddExactDescendantBinderNames(
        HashSet<string> reserved,
        IrNode scope)
    {
        foreach (var nested in scope.Descendants)
        {
            switch (nested)
            {
                case Lambda lambda:
                    AddExactParameterNames(reserved, lambda.Parameters);
                    AddUsableLocalNames(
                        reserved,
                        lambda.LocalNames,
                        lambda.Body);
                    break;
                case LocalFunctionStatement localFunction:
                    reserved.Add(localFunction.Name);
                    AddExactParameterNames(reserved, localFunction.Parameters);
                    AddUsableLocalNames(
                        reserved,
                        localFunction.LocalNames,
                        localFunction.Body);
                    break;
            }
        }
    }

    static void AddExactParameterNames(
        HashSet<string> reserved,
        ImmutableArray<Parameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (!parameter.NameIsSynthesized
                && CSharpNaming.IsUsableIdentifier(parameter.Name))
            {
                reserved.Add(parameter.Name);
            }
        }
    }

    static void AddUsableLocalNames(
        HashSet<string> reserved,
        ImmutableArray<string?> localNames,
        IrNode scope)
    {
        for (var index = 0; index < localNames.Length; index++)
        {
            string? name = localNames[index];
            if (name is not null
                && CSharpNaming.IsUsableIdentifier(name)
                && IrFunction.LocalSlotReferencesInScope(scope, index).Any())
            {
                reserved.Add(name);
            }
        }
    }
}
