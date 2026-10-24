# Union compiler-support polyfills

Include `UnionAttribute.cs` and `IUnion.cs` as source in the consuming project. They are internal and guarded by `!NET11_0_OR_GREATER`, so .NET 11 uses the real framework types.

A C# 15-capable compiler is required even when the target is `netstandard2.0` or `net10.0`. These files do not backport runtime union JSON support. Do not import them into a consumer that already defines these types; select one polyfill provider.

The verifier compiles these exact files together with a standalone consumer on netstandard2.0, net10.0 and net11.0. The two executable targets check case conversion/matching, null default behavior, attribute metadata and whether the consumer or the framework supplies the support types. No unrelated polyfills or implicit usings are needed by that consumer.
