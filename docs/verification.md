# Verification evidence

The tests below implement the product PRD's G/R/S/T acceptance requirements. They are part of
`nami.slnx`; the repository gate runs them with native Microsoft.Testing.Platform and validates
its nonempty TRX reports. Each executable suite owns a `testconfig.json` with assertion diagnostics
and increasing output detail from Unit through Integration to EndToEnd.
MSTest dependencies point to the catalog root within each assembly; no cross-assembly dependency
is used. The full `tools/list` golden is committed under `tests/Nami.EndToEndTests/Fixtures/`.

## Current validation

Run `mise diagnostics` for current results. This document describes test responsibilities;
it is not a saved run or a claim about the current checkout. Verification output is stored
outside the repository, one current record per lane. Old reports are removed rather than
linked here. `./eng/inspect.sh --results current` and `./eng/test.sh --status current` provide
bounded details. A changed source/output fingerprint requires fresh validation.

## Requirement-to-test map

| Requirement | Evidence |
| --- | --- |
| G1 | `Registry_Check_PassesWithFuture`; `Registry_NestedCoreDependency_ResolvesOnce` |
| G2 | `Generate_TwiceSameRegistry_IsByteIdentical` |
| G3 | `Catalog_MatchesResolvedRegistry`; `Catalog_EmbeddedConstants_MatchRegistrySnapshot` |
| G4 | `NamiKeys_AppearOnlyInGeneratedFiles` |
| G5 | `DeprecatedKeys_HaveSuccessorOrDrop` |
| G6 | `EnumValues_MatchSchemaStability` |
| G7 | `GeneratedCode_AotAnalyzers_ReportNothing`; `NativeAOT conformance execution` |
| G8 | `PolicyNamingUnpersistedKey_FailsGeneration`; `TokenExemption_ForUnpersistedKey_FailsGeneration` |
| G9 | `PinChange_WithoutDiffEntry_FailsGate` |
| G10 | `RustCrate_Constants_EqualCatalogKeys` |
| R1 | `Ingest_ThreeTransports_SameRows` (traces/logs/metrics × JSON/protobuf/gRPC); `EveryTool_ThreeOtlpTransports_ReturnsTypedEvidence` (generated six-tool × three-transport cases) |
| R2 | `Watermark_BackdatedRecord_DoesNotRegress` |
| R3 | `LastSeen_FutureTimestamp_UsesReceiptTime` |
| R4 | `Ingest_DeprecatedKey_RewrittenToSuccessor`; `Discovery_RenamedAndDroppedKeys_SurvivesRestart`; `Ingest_DeprecatedTemplate_PreservesSuffixAndDiscovery` |
| R5 | `Ingest_DevelopmentKey_StoredWithStabilityMark` |
| R6 | `Sanitizer_Boundaries_HoldAtPlusMinusOne`; `Sanitizer_OutputUtf8Boundary_PreservesValidText`; `Sanitizer_InvalidUtf16_FailsClosed` |
| R7 | `Exception_LogEventAndSpanEvent_GroupTogether`; `ErrorsRecent_FiveHundredThousandSpans_UnderMaxBytes` |
| R8 | `Resource_MissingServiceName_CountedNotDropped` |
| R9 | `Read_OtherProject_ReturnsNothing`; `Http_KeyAndResourceConflict_Refused`; `Http_KeyRolesAndCrossProjectReads_AreEnforced` |
| R10 | `Store_HasNoDeletePathOutsideRetention`; `Retention_RemovesExpiredPreservesRecent` |
| S1 | `Union_SwitchWithoutDefault_IsExhaustive`; `Union_AddedCase_BreaksBuild` |
| S2 | `Serialize_EachCase_EmitsKindTopLevel`; `MetricPoint_UnionVariants_RoundTripWithKindTag` |
| S3 | `RoundTrip_EachCase_IsByteIdentical`; `AttributeMixedCases_RoundTrip_PreservesNestedValues` |
| S4 | `SerializerContext_CoversEveryContractType` |
| S5 | `Timestamp_Spellings_PreservedAndRelativeResolved`; `Since_FutureMalformedAndOverflow_Refused` |
| S6 | `OptionalValue_ThreeStates_ThreeSerializations` |
| S7 | `Integers_BeyondSafeRange_RefusedOrString`; `Integers_ExactValues_Accepted`; `IntegerInput_MathematicallyIntegral_Deserializes`; `ToolArguments_IntegralJsonNumbers_Accepted` |
| S8 | `ToolError_EachRefusalClass_HasOneShape`; `InvalidInputs_IdentifyRepairableField`; `Cursor_ExpiredSnapshot_NamesMissingResource` |
| S9 | `ContractRevision_DocChange_Unchanged` |
| T1 | `SourcesStatus_NoRecords_ReportsSilentWithDuration`; `SourcesStatus_AfterOneRequest_LastSeenWithinOneSecond` |
| T2 | `AnyTool_SameSnapshot_ByteIdentical (six tools)` |
| T3 | `AnyTool_AfterRestart_SameAnswer (six tools)`; `NativeHost_OtlpAndStdio_PreservesAllToolsAndSnapshotsAcrossRestart` |
| T4 | `ErrorsRecent_FiveHundredThousandSpans_UnderMaxBytes`; `Cursor_PagesCoverAllRowsWithoutDuplicates`; `Refusal_LargeUnknownName_RespectsByteBudget` |
| T5 | `Window_WithinWatermark_EmptyIsComplete`; `Window_BeyondWatermark_NeverComplete` |
| T6 | `Fields_EachTool_ProjectsDeclaredMembers`; `Fields_TraceProjection_OmitsUnrequestedData`; `Fields_Unknown_RefusedNamingAllowed` |
| T7 | `Exception_LogEventAndSpanEvent_GroupTogether (seeded stack trace absent from output)` |
| T8 | `LogsSearch_QuotesBackslashesNonAscii_MatchLiterally (including case mismatch)` |
| T9 | `Result_ThreeParts_OneSource (HTTP and stdio)` |
| T10 | `ToolsList_Snapshot_MatchesAnnotationsAndCache (full committed manifest golden)` |
| T11 | `ProfilesOrBaggage_ReturnsDataUnavailable` |
| T12 | `IncomingMeta_Traceparent_ParentsToolSpan`; `IncomingMeta_AbsentOrInvalid_StartsRoot` |
| T13 | `Since_Boundaries_HalfOpenAgainstWatermark` |

## Boundaries retained by the fixtures

- Native `DynamicData` derives the six tool names from the generated schema catalog and crosses
  them with the three OTLP transports; all eighteen combinations assert typed tool evidence.
- Transport tests send actual HTTP JSON, HTTP protobuf and gRPC requests for all three signals,
  stop the server and inspect persisted rows in the same real DuckDB file, including the original metric value.
- The large-result test ingests 500000 spans in bounded batches, verifies stored count and group
  occurrence counts, bounds UTF-8 output and follows every cursor without duplicates.
- Snapshot tests reopen real database files; conformance repeats a read across a native server
  restart. No mock database stands in for restart or consistency.
- OTLP JSON/protobuf uses the standard producer encoding. `kind` discriminates only Nami's
  projected attribute/metric contracts; persisted internal storage retains its separate representation.
- A failed contract-registry lookup fails the coverage test; it is not silently skipped.
- Unsafe strings are test data, never generated test names. Every configured TRX is parsed and
  non-passing or missing results prevent completion.

## Diagnostic entry-point evidence

| Requirement | Evidence |
| --- | --- |
| Successful commands cannot conceal warnings | `QuietOutput_SuccessfulBuildWarning_RemainsVisible`; `QuietOutput_UncodedToolError_RemainsVisible` |
| Original process failures remain failures | `QuietOutput_FailedCommand_PreservesExitAndError`; `InspectReport_NonzeroToolExit_IsPreserved` |
| Incomplete or stale inspection is rejected | `InspectReport_RoslynLoadFailure_RejectsSuccessfulReport`; `InspectReport_SourceGeneratorFailure_IsIncomplete`; `InspectWrapper_ForeignDirectory_DoesNotReusePreviousReport` |
| Missing NuGet imports/cache are explicit setup failures | `TestEntry_MissingNuGetImports_IsExplicitSetupFailure`; `TestEntry_IncompleteGlobalPackages_IsExplicitSetupFailure` |
| Zero-test and ignored-exit-code runs cannot pass | `TestEntry_EmptySelection_PreservesMinimumFailure`; `TestEntry_IgnoredMtpExitCodes_IsRefused` |
| A silent test remains identifiable | `TestStatus_DiagnosticEvents_NamesUnfinishedCase` |

Diagnostic methods assert subprocess status, structured failure details and report handling. Their subprocess exit codes, diagnostics, source locations,
fresh report identity and native restart state are asserted. This is a source/assertion review of
these changed methods, not a mutation-score or repository-wide coverage claim.

## Historical review scope

The final review used the code-testing, native MSTest, test-gap-analysis and assertion-quality
workflows. Source-to-assertion checks found and closed gaps in signal coverage, registration checks,
error byte limits, output projections, real snapshot reload, missing-service accounting, and
conditional serialization. No assertion-free MSTest cases or unconditional-true assertions remain.
