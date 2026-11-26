I first downloaded the exact NuGet package so the `lib/net10.0` assembly and XML docs would anchor the answers. The XML showed documentation and likely overloads; I then used a small .NET reflection program to enumerate signatures, return types, converter types, and `RequiresUnreferencedCode` attributes without relying on search terms in the docs.

For source locations, I used the commit recorded in the package metadata and read the matching source files. I stopped once the metadata enumeration, XML summary, and source definitions supplied the requested lists and line numbers. One scope choice worth noting: I treated “APIs” as public or protected members, not private or internal implementation methods.

