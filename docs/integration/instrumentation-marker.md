# Instrumentation marker and generator path

Requirements I1 to I7 rest on one compiler fact, which the interceptor probe checks on every gate run.

## Compiler facts

| Fact | Value | Where it comes from |
| --- | --- | --- |
| Roslyn the generator is built against | `Microsoft.CodeAnalysis.CSharp` 5.9.0, `netstandard2.0` | `RoslynVersion` in `Version.props` |
| Compiler that loads the generator and binds its interceptors | 5.12.0-1.26529.118, in .NET SDK 11.0.100 | `global.json`; `sdk/11.0.100/Roslyn/bincore` |
| Interceptable location | version 1: an opaque `data` string from `GetInterceptableLocation` | `InterceptableLocation.Version` |
| Consumer setting | `InterceptorsNamespaces` names the namespace of the generated interceptors | MSBuild passes it to the compiler as a feature |

A generator built against the pinned package emits `[InterceptsLocation(version, data)]`, and the newer compiler
of the SDK binds it: the intercepted call runs the generated method, under the JIT and in a NativeAOT publish.
Without the namespace in `InterceptorsNamespaces` the compiler reports error CS9137; the call is never left
silently unintercepted.

[InterceptorProbeTests](../../tests/Nami.IntegrationTests/Compilation/InterceptorProbeTests.cs) holds both
checks. `InterceptorProbe_PinnedRoslynGenerator_IsBoundInProcess` also compares the Roslyn version the probe
generator references with the pin, so a probe that drifts to the SDK's compiler assemblies fails.
`InterceptorProbe_SdkCompiler_BindsAndRunsUnderNativeAot` publishes a consumer with the SDK and runs the native
binary. The [probe generator](../../tests/Nami.IntegrationTests/Fixtures/InterceptorProbe/InterceptorProbeGenerator.cs)
is a test fixture, not the product generator. After a change of the SDK or of `RoslynVersion`, these two tests
are the evidence that the path still holds.

## What the probe does not show

The probe intercepts one static call. Marker resolution, the stable-only policy, the wrappers and the manifest
(I1 to I7) are not implemented; [requirement evidence](requirement-evidence.md) lists what each still needs.
