using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspector.CommandLine;

internal static class CliArgumentOwnership
{
    internal readonly record struct ParsedArgument(Token[] Tokens, Token? AttachedOption = null);

    public static IReadOnlyList<OptionResult> GetOptionResults(ParseResult parseResult)
    {
        var results = new List<OptionResult>();
        for (SymbolResult? scope = parseResult.CommandResult; scope is not null; scope = scope.Parent)
        {
            if (scope is CommandResult command)
                results.AddRange(command.Children.OfType<OptionResult>());
        }

        return results;
    }

    public static ParsedArgument[] MapArguments(
        ParseResult parseResult,
        IReadOnlyList<string> arguments)
    {
        IReadOnlyList<Token> tokens = parseResult.Tokens;
        var result = new ParsedArgument[arguments.Count];
        int tokenIndex = 0;

        for (int argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            int start = tokenIndex;
            Token? attachedOption = null;
            if (tokenIndex < tokens.Count)
            {
                string argument = arguments[argumentIndex];
                Token token = tokens[tokenIndex];
                if (argument.Equals(token.Value, StringComparison.Ordinal))
                {
                    tokenIndex++;
                }
                else if (!TryConsumeAttachedArgument(argument, tokens, ref tokenIndex, out attachedOption))
                {
                    tokenIndex++;
                }
            }

            result[argumentIndex] = new(
                tokens.Skip(start).Take(tokenIndex - start).ToArray(),
                attachedOption);
        }

        return result;
    }

    private static bool TryConsumeAttachedArgument(
        string argument,
        IReadOnlyList<Token> tokens,
        ref int tokenIndex,
        out Token? attachedOption)
    {
        attachedOption = null;
        Token option = tokens[tokenIndex];
        if (option.Type != TokenType.Option)
            return false;

        if (TryGetDelimitedValue(argument, option.Value, out string? attachedValue))
        {
            attachedOption = option;
            tokenIndex++;
            ConsumeMatchingArgument(tokens, ref tokenIndex, attachedValue!);
            return true;
        }

        if (IsShortAlias(option.Value)
            && argument.Length > option.Value.Length
            && argument.StartsWith(option.Value, StringComparison.Ordinal)
            && tokenIndex + 1 < tokens.Count
            && tokens[tokenIndex + 1].Type == TokenType.Argument
            && tokens[tokenIndex + 1].Value.Equals(
                argument[option.Value.Length..], StringComparison.Ordinal))
        {
            attachedOption = option;
            tokenIndex += 2;
            return true;
        }

        return TryConsumeShortOptionExpansion(argument, tokens, ref tokenIndex, out attachedOption);
    }

    private static bool TryConsumeShortOptionExpansion(
        string argument,
        IReadOnlyList<Token> tokens,
        ref int tokenIndex,
        out Token? attachedOption)
    {
        attachedOption = null;
        if (!argument.StartsWith('-'))
            return false;

        // Follow the parser's expansion, rather than classifying bundled option arity.
        int position = 1;
        int next = tokenIndex;
        while (position < argument.Length
            && next < tokens.Count
            && tokens[next].Type == TokenType.Option
            && IsShortAlias(tokens[next].Value)
            && tokens[next].Value[1] == argument[position])
        {
            Token option = tokens[next];
            position++;
            next++;
            if (position < argument.Length && argument[position] is '=' or ':')
            {
                position++;
                if (position == argument.Length)
                {
                    attachedOption = option;
                    break;
                }
            }

            if (position < argument.Length
                && next < tokens.Count
                && tokens[next].Type == TokenType.Argument
                && tokens[next].Value.Equals(argument[position..], StringComparison.Ordinal))
            {
                attachedOption = option;
                next++;
                position = argument.Length;
            }
        }

        if (position != argument.Length)
            return false;

        tokenIndex = next;
        return true;
    }

    public static bool TryGetDelimitedValue(string argument, string alias, out string? value)
    {
        if (argument.Length > alias.Length
            && argument.StartsWith(alias, StringComparison.Ordinal)
            && argument[alias.Length] is '=' or ':')
        {
            value = argument[(alias.Length + 1)..];
            return true;
        }

        value = null;
        return false;
    }

    private static void ConsumeMatchingArgument(
        IReadOnlyList<Token> tokens,
        ref int tokenIndex,
        string value)
    {
        if (tokenIndex < tokens.Count
            && tokens[tokenIndex].Type == TokenType.Argument
            && tokens[tokenIndex].Value.Equals(value, StringComparison.Ordinal))
        {
            tokenIndex++;
        }
    }

    public static bool IsShortAlias(string alias) => alias is ['-', not '-'];
}
