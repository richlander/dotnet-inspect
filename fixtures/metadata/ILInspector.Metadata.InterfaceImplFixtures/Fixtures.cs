extern alias contracts;

namespace ILInspector.Metadata.InterfaceImplFixtures
{
    public sealed class ExternalArgumentImplementation :
        contracts::ILInspector.Metadata.InterfaceImplContracts.IConstructed<
            contracts::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument>
    {
        contracts::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument
            contracts::ILInspector.Metadata.InterfaceImplContracts.IConstructed<
                contracts::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument>
                .Echo(
                    contracts::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument
                        value) => value;
    }

    public sealed class LocalArgumentImplementation :
        contracts::ILInspector.Metadata.InterfaceImplContracts.IConstructed<
            global::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument>
    {
        global::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument
            contracts::ILInspector.Metadata.InterfaceImplContracts.IConstructed<
                global::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument>
                .Echo(
                    global::ILInspector.Metadata.InterfaceImplContracts.Collision.Argument
                        value) => value;
    }

    public sealed class MultipleImplementation :
        contracts::ILInspector.Metadata.InterfaceImplContracts.IUnrelated,
        contracts::ILInspector.Metadata.InterfaceImplContracts.IConstructed<string>
    {
        string contracts::ILInspector.Metadata.InterfaceImplContracts
            .IConstructed<string>.Echo(string value) => value;
    }
}

namespace ILInspector.Metadata.InterfaceImplContracts.Collision
{
    public sealed class Argument;
}
