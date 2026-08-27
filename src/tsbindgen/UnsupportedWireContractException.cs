using CSharpText;

namespace tsbindgen;

internal sealed class UnsupportedWireContractException(string location, string reason)
    : Exception($"{CSharpIdentifier.ContainRenderedText(location)}: {reason}.");
