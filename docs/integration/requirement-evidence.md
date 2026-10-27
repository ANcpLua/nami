# I1–I18 und D1–D6: Implementierung und Nachweis

Stand: 27. Oktober 2026. Lesender Quell-/Assertion-Abgleich auf `a3f4e74`, danach ausschließlich
Dokumentationsänderungen. Maßstab sind der [Request-/Response-Review](../prd/nami-request-response-review.md)
und die [I-/D-Anforderungen im PRD](../prd/nami.md#i-instrumentierung-und-beobachtungsnachweis).
Die [Checkliste](../checklist.md) fasst diesen Stand zusammen. Eigener Collector,
Compile-Time-Anwendungsinstrumentierung und Beobachtungslebenszeit bleiben verbindlicher Umfang.

## So ist der Status zu lesen

- **Belegt** bezeichnet ausschließlich den ausdrücklich genannten Teilumfang mit vorhandener
  Implementierung und passender Assertion im letzten ausgeführten Gate.
- **Teilbelegt** bezeichnet eine Anforderung mit nutzbarer Implementierung/Evidenz, deren voller
  Umfang noch nicht nachgewiesen ist. Die letzte Spalte nennt den fehlenden Abschluss.
- **Offen** bezeichnet eine noch fehlende Produktfähigkeit oder zugesagte Policy. Zugehörige
  Registry-/Testinfrastruktur kann trotzdem bereits bestehen.

Testnamen im PRD benennen das gewünschte Verhalten. Ein anders benannter vorhandener Test zählt,
wenn seine tatsächlichen Assertions dieses Verhalten prüfen. Ein gleicher Name, Serializer-Erfolg,
Flush-Erfolg oder bloßes Vorkommen eines Symbols genügt nicht. Diese Matrix ist keine Messung von
Zeilen-/Branch-Coverage und keine Aussage, dass alle übrigen Dateien ungetestet seien.

Der optionale statische Quellen-/Test-Zuordner aus `dotnet-test:find-untested-sources` konnte
wegen des fehlenden Python-Pakets `tree-sitter-language-pack` nicht starten. Es wurde keine
automatische Paarungsliste erzeugt und keine Ersatzliste ungetesteter Dateien behauptet.
Die folgenden Zuordnungen beruhen auf den konkret gelesenen Implementierungen und Assertions.

## Ausgeführte Basis und Aktualitätsgrenze

Der vollständige Release-Codegate auf `00523cc` lief mit **239 Unit, 465 Integration und
40 EndToEnd: 744 bestanden, keine Fehler oder übersprungenen Fälle**. Registry und reproduzierbare
Contract-Generierung bestanden; erzeugte Dateien waren aktuell, Wire-Schemas unverändert.
Die TypeSpec-Generierung führt zusätzlich die Node-Tests aus
[contracts/package.json](../../contracts/package.json) aus; diese sind nicht Teil der 744 MTP-Fälle.
Native Publish meldete weiterhin DuckDB IL2104/IL3053 und lokale Apple-Toolchain-Diagnostik.

`mise diagnostics` meldete beim vorliegenden Abgleich **STALE** für Build, Tests, Contracts und
die drei Inspektionsspuren. Die gespeicherten Testzahlen bleiben sichtbar; sie sind kein frischer
Pass für den späteren Dokumentationsstand. Für diese Änderung wurden keine Laufzeittests erneut
ausgeführt. Änderungen an Produktcode/Emittern erfordern passende neue Prüfungen und den Codegate.

## Instrumentierung und Datenweg

| ID / Status des vollen Umfangs | Tatsächlicher Implementierungsstand | Vorhandene konkrete Evidenz | Noch erforderlicher Abschluss |
| --- | --- | --- | --- |
| **I1 — Offen** | [Registry-Generierung][registry] und [SemConv-Katalog][semconv] besitzen gepinnte Definitionen. Ein Nami-Produktgenerator für Marker → semantischen Vertrag → Anwendungsaufrufstelle fehlt. Der vorhandene [Roslyn-Generator][duckgen] erzeugt DuckDB-Code. | [RegistryGateTests][registry-tests]: `Catalog_MatchesResolvedRegistry`, `Registry_NestedCoreDependency_ResolvesOnce` prüfen Katalog/Abhängigkeiten. | Eindeutige Markerauflösung einschließlich Definitionsart/Signal, Registry-Pin und Build-Diagnosen für unbekannte/widersprüchliche Auswahl. Katalogtests ersetzen diese Auflösung nicht. |
| **I2 — Offen** | Weaver-Stabilität und Nachfolger sind im Katalog vorhanden; der Collector darf Development-Daten aufnehmen. Eine Stable-only-Auswahl für Anwendungsinstrumentierung ist nicht implementiert. | [RegistryGateTests][registry-tests]: `EnumValues_MatchSchemaStability`, `DeprecatedKeys_HaveSuccessorOrDrop`; [IngestStoreTests][ingest-tests]: `Ingest_DevelopmentKey_StoredWithStabilityMark`. | Stable-Regel für Modell, referenzierte Attribute und eingeschränkte Enum-Werte; offene Werte offen lassen. Collector-Akzeptanz ist keine Instrumentierungsfreigabe. |
| **I3 — Offen** | Das [Contract-Manifest][manifest] ordnet generierte Datenmodelle Verbrauchern zu. Es ist kein Manifest instrumentierbarer Aufrufstellen. | [ContractIntegrationTests][contract-integration]: `SerializerContext_CoversEveryContractType` prüft CLR-/JSON-Metadatenabdeckung der Wire-Schemas. | Vollständiges Instrumentierungsmanifest mit ausgeführten Proben oder konkreten Ausschlüssen pro Ziel/Aufrufform. 99 Contract-Wurzeln und 193 Migrationseinträge belegen diesen Umfang nicht. |
| **I4 — Offen** | [EngineProtocol.Execute][engine-cs] instrumentiert den eigenen Tool-Handler. Es existiert noch kein generierter Wrapper für deklarierte Anwendungscalls. | [HostTransportTests][host-tests] belegen reale Tool-Aufrufe; SDK-/Compiler-Fixtures sind vorhandene Hilfen, kein Interceptor-Verhaltensnachweis. | Receiver-/Argumentauswertung und Reihenfolge, genau eine Ausführung, Ergebnis, Seiteneffekte, Exception und Cancellation des ursprünglichen Aufrufs. |
| **I5 — Offen** | Der eigene Engine-Handler wartet `InvokeAsync` ab; [TelemetryCapture][capture] wartet im Harness auf eine definierte Evidenzbedingung. | [CaptureLifecycleTests][capture-tests]: `TelemetryCapture_EvidenceMissingAtTimeout_Fails`, `TelemetryCapture_MultipleWaiters_AreNotifiedByOneRealExport`. Das prüft den Harness. | Generierte Instrumentierung bis zum wirklichen Task-/ValueTask- und unterstützten Enumerationsabschluss, einschließlich Fehler, Abbruch, frühem Dispose und genau einmaligem Ende. |
| **I6 — Teilbelegt** | [EngineProtocol][engine-cs] übernimmt Trace-Kontext und setzt `Activity.Current` im `finally` zurück. | [HostTransportTests][host-tests]: `IncomingMeta_Traceparent_ParentsToolSpan`, `IncomingMeta_AbsentOrInvalid_StartsRoot`. [CaptureLifecycleTests][capture-tests] prüfen getrennte Harness-Roots bei Parallelität. | Verschachtelte/parallele Produktoperationen mit Erfolg, Fehler und Abbruch sowie vorher/nachher geprüftem Ambient-Kontext; danach derselbe Nachweis für Anwendungswrapper. |
| **I7 — Teilbelegt** | [Eigeninstrumentierung][self] erzeugt Tool-Spans, Dauer-Histogramm und Fehlerereignisse/-Logs. OTLP-Konverter und [TypeSpec-Metrikmodelle][metric-models] unterscheiden Signal-/Payloadformen. | [HostTransportTests][host-tests]: `Ingest_ThreeTransports_SameRows`; [SdkSignalTests][sdk-tests]: `OfficialSdk_TraceNamedLogAndPartitionedCounter_AreExportedToTheReceiver` prüft reale SDK-Span-/Log-/Counter-Form, Resource und Scope am Testreceiver. | Alle deklarierten Anwendungsmodelle mit Signalziel, Span-Kind/Instrument, Einheiten, Attributtypen, Resource/Scope und expliziten Ausschlüssen. Testreceiver-Evidenz ist kein vollständiger Produktpfad. |
| **I8 — Teilbelegt** | [TypeSpec/Emitter][contracts], generierte C#/TS-Formen und Codecs führen Namen, Präsenz und Zahlenrepräsentation durch. | [EncodedPresenceTests][presence], [EncodedContainerTests][containers] und [WireCodecTests][wire] prüfen Int64-Endpunkte, null/abwesend und ungültige Darstellungen. Der [TS-Codec-Test][ts-codec] prüft denselben Wertebereich, optionale nullable Werte und Map-Schlüssel. [GeneratedModelTests][generated-tests] prüfen rekursive `type`-/`kind`-Schlüssel und erhaltene Provenienz. | Gemeinsamer datengetriebener Fixture-Korpus über den gesamten vereinbarten C#/TS-Verbraucherumfang, einschließlich benötigter unsigned Grenzen und aller Varianten. Separate gezielte Tests sind bereits belegt, vollständige Manifestabdeckung noch nicht. |
| **I9 — Teilbelegt** | Generierte C#-Union-Writer und geschlossene Hierarchien behandeln Default-/unbekannte Fälle ausdrücklich; TS validiert Tool-Schemas in [server.ts][server] und [engine.ts][engine-ts]. | [UnionCompilerTests][union-tests]: `Build_AddedWireCase_RejectsTheActualGeneratedWriter`, `Build_AddedHierarchyLeaf_RejectsTheActualConsumer` sowie `Publish_ActualUnionWriters_RoundTripsEveryCaseUnderNativeAot`: 15 Wire-/8 Storage-Roundtrips, 7 Default- und 12 ungültige Tag-Fälle; Reflection ausgeschaltet. | Derselbe Negativkorpus durch beide Sprachgrenzen und alle zugesagten Modellwurzeln. Der NativeAOT-Test enumeriert fünf Wire-Union-Typen plus StoredAttributeValue, nicht sämtliche künftigen Instrumentierungsmodelle. |
| **I10 — Teilbelegt** | [PublishNative][build] liefert native Engine und gebündelten TS-Einstieg; [McpHarness][harness] startet beide. | [NativeConformanceTests][native-tests]: `NativeHost_OtlpAndStdio_PreservesAllToolsAndSnapshotsAcrossRestart`; [HostTransportTests][host-tests] fragen eine wirklich erzeugte Eigeninstrumentierungs-Span ab. [CaptureFixtureTests][aot-capture] publizieren zusätzlich einen echten NativeAOT-Capture-Consumer. | Eine tatsächlich durch Nami generierte Anwendungsinstrumentierung durch Export → eigenen Collector → DuckDB → MCP; vollständige benötigte JSON-Fälle und saubere Laufzeitumgebung ohne installiertes .NET/Profiler. Der Capture-Consumer ist Testinfrastruktur. |
| **I11 — Offen** | [Snapshot-Speicher][snapshots] und authentifizierte Projekte existieren. Eine über Folgeaufrufe referenzierte Beobachtung mit eigenem Lebenszyklus fehlt. Die Metrik-`ObservationId` bezeichnet eine Messbeobachtung, keine Beobachtungssitzung. | [NativeConformanceTests][native-tests] prüfen Snapshot-Replay nach Neustart; [IngestStoreTests][ingest-tests]: `Read_OtherProject_ReturnsNothing`. | Autorisierte Beobachtungskennung, Zuordnung bei Folgeaufrufen, parallelen Clients/Projekten und Reconnect. Snapshot- oder Request-ID erfüllt das nicht. |
| **I12 — Offen** | Retention, Snapshot-Ablauf und Prozess-/Testdeadlines sind implementiert; kein Produktmanager für XML-konfigurierte Beobachtungslebenszeit. | [IngestStoreTests][ingest-tests]: `Snapshot_Expired_Refused`, `Retention_RemovesExpiredPreservesRecent` prüfen ausdrücklich andere Uhren/Regeln. | Beide PRD-Nachweise: absolute/Inaktivitätsgrenzen direkt vor/auf/nach der Grenze, Erneuerung, idempotentes Ende **und** Weiterlauf über 15 Minuten bei entsprechender Konfiguration. `since: -15m` ist nur ein Abfragefenster. |
| **I13 — Teilbelegt** | [EngineProtocol][engine-cs] nutzt `Stopwatch` und zeichnet Sekunden auf; [IngestStoreTests][ingest-tests] behandeln Receipt-Zeit und Wasserlinie. | `LastSeen_FutureTimestamp_UsesReceiptTime`, `Watermark_BackdatedRecord_DoesNotRegress`; [HostTransportTests][host-tests]: `SourcesStatus_AfterOneRequest_LastSeenWithinOneSecond`. Letzterer prüft `last_seen - before <= 1s`, nicht die gesamte Query-Latenz. | Kontrollierte Operation, monotone Dauer bei Wanduhränderung und gemessene Annahme-bis-Abfrage-Verzögerung ≤ 1s unter dokumentierter Last; Erzeugung/Export/Annahme/Sichtbarkeit getrennt erfassen. |
| **I14 — Offen** | [NamiSelfTelemetry][self] registriert eigene Quellen und OTel-Exporter. Eine ausdrückliche vollständige Recording-Policy für den deklarierten Anwendungsumfang ist nicht vorhanden; dort wird kein Sampler explizit gesetzt. | [SdkSignalTests][sdk-tests] belegen einzelne SDK-Signale; der Traceparent-Test in [HostTransportTests][host-tests] verwendet das Sampling-Flag `01`. | Quellen vor dem ersten Aufruf aktiv; bekannte Operationenzahl vollständig erfasst, auch mit eingehendem `00`-Parent. Die aktuelle Stichprobe belegt weder diese Policy noch verlustfreie Zustellung. |
| **I15 — Teilbelegt** | [OtlpConverter.Metrics][metric-converter], generierte Modelle und [MetricStorageMapper][metric-storage] erhalten Metrikrollen, Payloads und Identitäten. | [GeneratedModelTests][generated-tests]: `ToStored_GaugeAndSum_SharePayloadWithoutConflatingKinds`; [MetricIdentityTests][metric-identity] prüfen exakte Messwertgleichheit. [MetricWireTests][metric-wire] prüfen Sum/Histogram/ExponentialHistogram/Summary am Testreceiver; [SdkSignalTests][sdk-tests] prüfen einen exportierten Counterwert 7. | Bekannte Messfolgen mit erwarteten Counts, Summen, Buckets/Gauges, Temporality und Dimensionen durch den Produktpfad. Einzelner Counterwert oder Payload-Roundtrip beweist noch keine vollständige Aggregationssemantik. |
| **I16 — Teilbelegt** | Tool-Eigeninstrumentierung, Export und projektgebundene Abfrage sind verbunden; [server.ts][server] unterscheidet Toolfehler und Engine-/Transportausfall. | [HostTransportTests][host-tests]: `IncomingMeta_Traceparent_ParentsToolSpan` fragt die durch den echten Tool-Aufruf erzeugte Span anhand der Trace-ID ab und prüft ihren Parent. `EveryTool_ThreeOtlpTransports_ReturnsTypedEvidence` prüft ingestierte Fixtures. | Für generierte Anwendungscalls Ausführung, Exportannahme und Query mit eindeutiger Operation verbinden; fachliche, Protokoll- und Exportfehler getrennt provozieren und nachweisen. |
| **I17 — Teilbelegt** | Konverter zählen Ablehnungen; [OtlpExportAck][ack] bildet sie auf OTLP Partial Success ab. [StorageSchemaTests][storage-tests] prüfen Log-Retransmission und gespeicherte Reihenfolge. | `LogSequence_RestartAndRetransmission_PreserveOrderAndStoredSequence`, `Upsert_RepeatedLog_PreservesEarliestReceipt`; [TelemetryPolicyTests][policy-tests]: `ExportsCleanly_ReceiptOutcome_PreservesRejectionsAndWarnings`, `ExportsCleanly_NoReceipt_RejectsMissingExportEvidence` prüfen die Harness-Auswertung. | Retry-fähige/permanente Exportfehler, Drops, verlorene Antworten und Duplikate im Produktpfad zählen; Partial Success darf nicht zu blindem Gesamtbatch-Retry führen. Receiver-/Policytests beweisen keine Produkt-Exporter-Retrypolitik. |
| **I18 — Teilbelegt** | [Eigenexport][self] nutzt eigene Quellen/Meter und Loopback; [DuckDbStore.DisposeAsync][store] beendet Worker, versucht CHECKPOINT und meldet Shutdown-Fehler. [Engine.close][engine-ts] hat Eskalationsfristen. | Die realen Eigeninstrumentierungs-/Neustarttests in [HostTransportTests][host-tests] und [NativeConformanceTests][native-tests] funktionieren. [ChildProcessTests][child-tests] prüfen Deadline/Abbruch/Descendant-Cleanup des Harness. | **Zwei separate Beweise fehlen:** endliche erwartete Selbsttelemetrie nach definiertem Eingang ohne Rückkopplung; Shutdown mit noch angenommenen Daten, korrekter Exporter-/Ingest-Reihenfolge und verarbeitetem oder gezähltem Rest. Harness-Cleanup oder gespeicherter Snapshot allein genügt nicht. |

## Private Engine und Agentenauslieferung

| ID / Status des vollen Umfangs | Tatsächlicher Implementierungsstand | Vorhandene konkrete Evidenz | Noch erforderlicher Abschluss |
| --- | --- | --- | --- |
| **D1 — Teilbelegt** | [server.ts][server] registriert den generierten Sechs-Tool-Katalog, Schemas, Annotationen und Ergebnisrahmen. | [HostTransportTests][host-tests]: `ToolsList_Snapshot_MatchesAnnotationsAndCache`, `Result_ThreeParts_OneSource`; [NativeConformanceTests][native-tests] prüfen alle Tools lokal. [ToolContractTests][tool-tests] und [ToolEdgeTests][edge-tests] prüfen Freshness, Bytebudget, Projektionen und Fehler. | Tatsächliche Claude.ai-/OpenAI-Integration einschließlich unterstützter Oberfläche, Deployment/Auth und Host-Parität. Lokale SDK-/mcpdo-Prüfungen sind keine Host-Abnahme. |
| **D2 — Teilbelegt** | [EngineProtocol.ProcessAsync][engine-cs] prüft Revision vor `Execute`; [Engine][engine-ts] prüft Ready/Antwortrevision und Ergebnis-Schemas. [Wire-Revision][revision] wird aus vereinbarten Wurzeln erzeugt. | [ContractIntegrationTests][contract-integration]: `ContractRevision_DocChange_Unchanged` prüft Dokument-/Internal-Abgrenzung und tatsächliche Schemaänderung. [HostTransportTests][host-tests]: `Health_ReturnsGeneratedSchemaAndRevision` prüft die richtige Revision. | Absichtlich inkompatibler TS-/Engine-Peer: Fehler vor fachlichem Dispatch und belegte Nichtausführung, auch für falsches Ergebnisschema. Hash-/Health-Tests testen diese Abweisung nicht. |
| **D3 — Teilbelegt** | Beide [TS][engine-ts]/[C#][engine-cs]-Reader verarbeiten komplette JSONL-Frames; Requests besitzen IDs, Pending-/Frame-Limits, Workerbegrenzung und Write-/Drain-Behandlung aus [engine.tsp][engine-model]. | [HostTransportTests][host-tests] und [NativeConformanceTests][native-tests] führen reale normale Calls aus. | Gezielte Fragmentierung/zusammengefasste Reads, konkurrierende/duplizierte IDs, Grenzgrößen und langsamer Peer mit belegter Speicher-/Backpressure-Grenze. Große OTLP-Eingaben oder begrenzte Toolantworten testen nicht automatisch diese private Pipe. |
| **D4 — Teilbelegt** | [Engine.request/fail/close][engine-ts] verwalten Abort, wartende Promises und Prozessende; [EngineProtocol][engine-cs] verknüpft Cancel mit PendingCall und wartet Aufgaben beim Ende ab. | [HostTransportTests][host-tests]: `Startup_EngineCannotStart_ReportsExitAndDiagnosticsWithoutWaitingForHealthTimeout`; reguläre Harness-Entsorgung in E2E. [ChildProcessTests][child-tests] belegen separat den Testprozess-Runner. | Produktcalls im wartenden/laufenden Zustand abbrechen, Engine während eines Calls beenden, jede Pending-Antwort definiert abschließen und verwaiste Prozesse ausschließen. Fehlstart ist kein Mid-call-Absturztest. |
| **D5 — Teilbelegt** | [EngineProtocol][engine-cs] löst Read-Credentials in Projekte auf; [server.ts][server] reicht Credential und Trace-Metadaten getrennt von Toolargumenten weiter. | [HostTransportTests][host-tests]: `Http_KeyRolesAndCrossProjectReads_AreEnforced`, `Http_KeyAndResourceConflict_Refused`, `IncomingMeta_Traceparent_ParentsToolSpan`, `IncomingMeta_AbsentOrInvalid_StartsRoot`. | Gleichzeitige Projekte, Reconnects und manipulierte Zustandskennungen mit Nachweis, dass Daten und Ambient-Kontext nicht überlaufen. Beobachtungskennungen hängen zusätzlich an I11. |
| **D6 — Teilbelegt** | [Build.PublishNative][build] kopiert das TS-Bundle neben den nativen Publish; der [Dockerfile][docker] enthält Bun, Engine und native Abhängigkeiten. Der Supervisor trennt private Pipes und stderr. | [NativeConformanceTests][native-tests] starten den veröffentlichten Stack und prüfen echte MCP-Antworten/Neustart. Der lokale Harness verwendet ein vorhandenes Bun. | Das vollständige Distributiv in einer sauberen Laufzeitumgebung ohne installiertes .NET starten; native Bibliotheken, deklarierte Bun-Bereitstellung und Diagnosen bei Erfolg/Fehler prüfen. Ein vorhandener Dockerfile beweist keinen ausgeführten Container-Release. |

## Bereits belegte Teilumfänge und nächste Arbeit

Belegt sind unter anderem die gezielten C#-/TS-Codec-Fälle, die genannten NativeAOT-Union-Fälle,
sechs lokale Tools, OTLP über drei Transporte, Credential-Rollen, Snapshot-Replay und die getrennten
Harness-Policies. Diese Arbeit bleibt erhalten. Die vollständigen I/D-Anforderungen werden erst
geschlossen, wenn auch ihr jeweils verbleibender Umfang belegt ist.

Die nächsten Produktarbeiten sind der Instrumentierungsmanifest-/Generatorpfad I1–I7 und die
Beobachtungslebenszeit I11/I12. Der erste generierte echte Anwendungscall verbindet dann I10/I16.
Daneben lassen sich bestehende Stream-/Contract-Funktionen gezielt für I8/I9 und D2–D5 absichern;
für I13–I18 fehlen die beschriebenen Zeit-, Recording-, Aggregations- und Fehlerexperimente.
D1 und der vollständige D6-Abschluss benötigen reale Auslieferungsevidenz.

### Build-/Solution-Grenze

Nachtrag zur Build-Integration vom 27. Oktober: [nami.slnx](../../nami.slnx) umfasst elf .NET-Projekte
einschließlich Build-Orchestrator und nun auch [nami.mcp.esproj](../../src/nami.mcp/nami.mcp.esproj).
Das gepinnte JavaScript-SDK führt den bestehenden [Bun-Build](../../src/nami.mcp/package.json) aus;
der projektbezogene Restore verwendet den eingefrorenen Bun-Lockfile. [Build.Compile][build]
baut diese Solution, ohne MCP zuvor ein zweites Mal aufzurufen. Der separate `Mcp`-Einstieg
delegiert an dieselbe `.esproj`. Das ergänzt die ursprüngliche lesende Bestandsaufnahme;
Riders aktueller Indexzustand bleibt von der CLI-/Solution-Integration zu unterscheiden.

[registry]: ../../registry/generate.sh
[semconv]: ../../internal/nami.semconv
[duckgen]: ../../internal/nami.collector.storage.generators/DuckDbInsertGenerator.cs
[registry-tests]: ../../tests/Nami.IntegrationTests/RegistryGateTests.cs
[ingest-tests]: ../../tests/Nami.IntegrationTests/IngestStoreTests.cs
[manifest]: ../../contracts/generated/model-manifest.json
[contract-integration]: ../../tests/Nami.IntegrationTests/ContractIntegrationTests.cs
[engine-cs]: ../../src/nami/Hosting/EngineProtocol.cs
[engine-ts]: ../../src/nami.mcp/src/engine.ts
[host-tests]: ../../tests/Nami.EndToEndTests/HostTransportTests.cs
[capture]: ../../tests/Nami.Tests/TelemetryCapture.cs
[capture-tests]: ../../tests/Nami.IntegrationTests/Receiver/CaptureLifecycleTests.cs
[self]: ../../src/nami/Telemetry/NamiSelfTelemetry.cs
[metric-models]: ../../contracts/models/ingestion-metrics.tsp
[sdk-tests]: ../../tests/Nami.IntegrationTests/Receiver/SdkSignalTests.cs
[contracts]: ../../contracts
[presence]: ../../tests/Nami.UnitTests/EncodedPresenceTests.cs
[containers]: ../../tests/Nami.UnitTests/EncodedContainerTests.cs
[wire]: ../../tests/Nami.UnitTests/WireCodecTests.cs
[ts-codec]: ../../contracts/scripts/lib/property-semantics.test.mjs
[generated-tests]: ../../tests/Nami.IntegrationTests/GeneratedModelTests.cs
[server]: ../../src/nami.mcp/src/server.ts
[union-tests]: ../../tests/Nami.IntegrationTests/UnionCompilerTests.cs
[build]: ../../eng/build/Build.cs
[harness]: ../../tests/Nami.Tests/Infrastructure/McpHarness.cs
[native-tests]: ../../tests/Nami.EndToEndTests/NativeConformanceTests.cs
[aot-capture]: ../../tests/Nami.IntegrationTests/Projects/CaptureFixtureTests.cs
[snapshots]: ../../src/nami.collector.storage/DuckDbStore.Snapshots.cs
[metric-converter]: ../../src/nami.collector.ingestion/OtlpConverter.Metrics.cs
[metric-storage]: ../../src/nami.collector.storage/MetricStorageMapper.cs
[metric-identity]: ../../tests/Nami.IntegrationTests/MetricIdentityTests.cs
[metric-wire]: ../../tests/Nami.IntegrationTests/Receiver/MetricWireTests.cs
[ack]: ../../src/nami/Grpc/OtlpExportAck.cs
[storage-tests]: ../../tests/Nami.IntegrationTests/StorageSchemaTests.cs
[policy-tests]: ../../tests/Nami.UnitTests/Policies/TelemetryPolicyTests.cs
[store]: ../../src/nami.collector.storage/DuckDbStore.cs
[child-tests]: ../../tests/Nami.IntegrationTests/Probes/ChildProcessTests.cs
[tool-tests]: ../../tests/Nami.IntegrationTests/ToolContractTests.cs
[edge-tests]: ../../tests/Nami.IntegrationTests/ToolEdgeTests.cs
[revision]: ../../contracts/scripts/emit-contract-revision.mjs
[engine-model]: ../../contracts/models/engine.tsp
[docker]: ../../src/nami/Dockerfile
