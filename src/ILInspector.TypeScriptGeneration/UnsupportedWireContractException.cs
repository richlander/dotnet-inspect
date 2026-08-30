using CSharpText;

namespace ILInspector.TypeScriptGeneration;

internal sealed class UnsupportedWireContractException(string location, string reason)
    : Exception($"{CSharpIdentifier.ContainRenderedText(location)}: {reason}.");
