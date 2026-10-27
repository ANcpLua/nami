# Nami: Übergabe und Aufräumregister

> **Stand: 27. Oktober 2026 · Quelltext- und Dateivergleich, kein neuer Testlauf**  
> Arbeitsort für Nami: `/Users/alexandernachtmann/RiderProjects/nami`.  
> Zweck: vorhandene Arbeit finden, alte Regeln nicht wieder aktivieren und dieselbe Migration nicht zweimal ausführen.

## Vor einer erneuten Migration lesen

Die Übernahme des Materialisierungs-Prototyps ist bereits im [Test-Harness-Handover](test-harness.md) und im [Dateiinventar](test-harness-inventory.csv) dokumentiert. **Die 257 Inventareinträge sind keine offenen Importaufträge.** Der [Request-/Response-Review](../prd/nami-request-response-review.md) enthält den verbindlich korrigierten Produktumfang; ältere Stationslisten sind keine neuen Importaufträge.

Eine vorhandene Datei beweist nicht, dass ihre komplette Implementierung optimal oder jeder frühere Test aktuell bestanden ist. Sie zeigt aber den Ort, an dem Verbesserungen ansetzen. Vor einem neuen Import: bestehenden Eigentümer finden, konkrete fehlende Fähigkeit benennen, Verhalten vergleichen und nur diese Lücke bearbeiten. Keine zweite Fixture-Infrastruktur neben die vorhandene stellen.

## 1. Welcher Arbeitsbereich wofür zuständig ist

| Ort | Jetzt beobachteter Stand | Verwendung und Aufräumentscheidung |
| --- | --- | --- |
| `~/RiderProjects/nami` | Implementierung der Konsolidierung in `02181a0`; aktive Branch `codex/playground-consolidation-20261027`. | Produktimplementierung, vier Testprojekte und gemeinsame Build-Dateien. |
| Downloads-Prototyp und seine alte Sicherung | Laut ausdrücklicher Nutzerbestätigung gelöscht; dokumentiert in [skill-plugin-dedup.md](skill-plugin-dedup.md). | Keine aktive Abhängigkeit und kein neuer Importauftrag. |
| `~/RiderProjects/qyl` | Separates Produkt mit eigenständigen verschachtelten Repositories. | **Bleibt erhalten.** Die lesende Instrumentierungsberatung ist kein Migrationsauftrag. |
| `~/RiderProjects/nami.playground` | Kandidaten geprüft; sinnvolle Fähigkeiten in vorhandene Nami-Eigentümer übernommen. | **Am 27. Oktober 2026 auf ausdrücklichen Nutzerauftrag gelöscht.** Geprüfte Sicherung bleibt unter `artifacts/migration-checkpoints/`; kein Produkt-, Build- oder Test-Verweis. |
| `~/RiderProjects/nami-plugin` | Portable Skill-Distribution; Eigentum in [skill-plugin-dedup.md](skill-plugin-dedup.md). | Separate Plugin-Arbeit; nicht zusammen mit Prototypen entfernen. |
| `~/RiderProjects/nami-architecture-review` | Entscheidungsarbeit. | Keine Produktlaufzeit und keine zweite Implementierung. |

Die Dateiprüfung und Migration ersetzen keinen aktuellen vollständigen Testlauf. Ergebnisse besitzen
ihren eigenen Quellstand; die früheren Testzahlen bleiben historische Evidenz.

## 2. Diese Bausteine sind in Nami vorhanden

| Früheres Ergebnis | Heutiger Eigentümer in Nami | Konsequenz |
| --- | --- | --- |
| Projektbeschreibung im Speicher | [`FixtureProjectDefinition.cs`](../../tests/Nami.Tests/FixtureProjectDefinition.cs), [`FixtureTarget.cs`](../../tests/Nami.Tests/FixtureTarget.cs) | Nicht erneut aus dem Prototyp kopieren. |
| Materialisierung und SDK-Ausführung | [`FixtureProject.cs`](../../tests/Nami.Tests/FixtureProject.cs) | Bestehenden Ablauf verbessern, falls ein konkreter Mangel nachgewiesen wird. |
| Attributbasierte Fixtures | [`ProjectFixture.cs`](../../tests/Nami.Tests/ProjectFixture.cs) | Bestehende Invocation-Lebenszeit beachten; keinen konkurrierenden Fixture-Manager einführen. |
| Gemeinsam genutzte Fixtures | [`SharedFixture.cs`](../../tests/Nami.Tests/SharedFixture.cs) | Bereits vorhanden; nicht aus dem Playground neu erfinden. |
| Temporärer Paketfeed | [`PackageFeedFixture.cs`](../../tests/Nami.Tests/PackageFeedFixture.cs) | Paketidentität, Besitz und Cleanup am vorhandenen Typ bearbeiten. |
| Compiler und virtuelle Eingaben | [`InProcessCompiler.cs`](../../tests/Nami.Tests/Compilation/InProcessCompiler.cs), [`InMemoryAdditionalText.cs`](../../tests/Nami.Tests/Compilation/InMemoryAdditionalText.cs), [`InMemoryAnalyzerOptions.cs`](../../tests/Nami.Tests/Compilation/InMemoryAnalyzerOptions.cs), [`DiagnosticReport.cs`](../../tests/Nami.Tests/Compilation/DiagnosticReport.cs) | Vorhandene reine Compiler-Oberfläche; kein neuer Compiler-Harness nötig. |
| PE-Metadatenprüfung | [`PeImage.cs`](../../tests/Nami.Tests/Metadata/PeImage.cs) | Bestehende byte-/streambasierte Prüfung verwenden. |
| Diagnosebelege | [`FixtureEvidence.cs`](../../tests/Nami.Tests/FixtureEvidence.cs) | Evidenz vor Cleanup ist bereits Teil der Infrastruktur. |
| Prozessbesitz und Capture | [`ChildProcess.cs`](../../tests/Nami.Tests/ChildProcess.cs), [`OwnedProcess.cs`](../../tests/Nami.Tests/OwnedProcess.cs), `tests/Nami.Tests/Fixtures/ProcessHost`, `tests/Nami.Tests/Capture` | Keine separaten primären ProcessHost-/Capture-Projekte wiederherstellen. |
| Tatsächliche generierte Union-Verbraucher | [`UnionCompilerTests.cs`](../../tests/Nami.IntegrationTests/UnionCompilerTests.cs) | Den vorhandenen Nachweis prüfen/erweitern; nicht erneut einen losgelösten Beispiel-Switch als Ersatz testen. |

Die Existenz dieser Eigentümer wurde erneut geprüft. Der damalige Erfolg mit 657 Tests bleibt historische Evidenz für den damaligen Stand; hier wurde kein solcher Lauf wiederholt.

## 3. Entscheidung zu allen angefragten Altdateien

Die Pfade dieser Tabelle liegen unter der alten Downloads-Kopie. Verzeichnisse wie `skills/`, der jeweilige Skill-Ordner und dessen `agents/` sind mit ihren enthaltenen Dateien erfasst; sie sind keine zusätzlichen Migrationsaufgaben.

| Altdatei beziehungsweise Gruppe | Entscheidung | Heutiger Bezug / Begründung |
| --- | --- | --- |
| `Nami.Telemetry.Tests/AGENTS.md` | Als historische Vorgabe stilllegen, nicht kopieren. | Die Rolle der Supportbibliothek steht in Nami-`AGENTS.md` und [testing.md](../testing.md). |
| `Nami.Telemetry.Tests/skills/nami-telemetry-support/SKILL.md` | Fachlich nützliche Hinweise sind am bestehenden Harness zu prüfen; die Datei wird nicht als verbindlicher Skill übernommen. | Fixture-, Capture-, Compiler-, Prozess- und Evidenzfunktionen sind bereits vorhanden. Alte Parent-Checkout-, Push- und pauschale Umschreibregeln gelten nicht automatisch weiter. |
| Support-Skill: `agents/openai.yaml` und Elternverzeichnisse | Nicht separat übernehmen. | UI-Metadaten aktivieren einen Skill; ohne übernommene Skill-Aufgabe besteht kein eigener Nutzen. |
| `Nami.Telemetry.UnitTests/AGENTS.md` und `skills/nami-telemetry-unit/SKILL.md` | Stilllegen, nicht kopieren. | Die Forderungen nach unversiegelten Klassen und pauschalem `Fast`-Tag widersprechen der heutigen Anleitung. Deterministische Tests bleiben sinnvoll; die alte Ausgestaltung ist kein Zwang. |
| Unit-Skill: `agents/openai.yaml` und Elternverzeichnisse | Nicht separat übernehmen. | Keine zweite Auffindungsstelle für dieselben Testregeln erzeugen. |
| `Nami.Telemetry.IntegrationTests/AGENTS.md` und `skills/nami-telemetry-integration/SKILL.md` | Stilllegen, nicht kopieren. | Enthält Parent-Collector-Annahmen und alte Ausführungsbefehle. Der AGENTS-Link benutzt außerdem `Nami-telemetry-integration`, der tatsächliche Ordner heißt `nami-telemetry-integration`. |
| Integration-Skill: `agents/openai.yaml` und Elternverzeichnisse | Nicht separat übernehmen. | Erst eine konkrete aktive Skill-Aufgabe rechtfertigt neue Metadaten. |
| `Nami.Telemetry.EndToEndTests/AGENTS.md` und `skills/qyl-telemetry-end-to-end/SKILL.md` | Stilllegen, nicht kopieren. | Verweist auf alte REST-/Package-Delivery-Pfade. Der AGENTS-Link nennt sogar einen anderen Skill-Ordner. Nami prüft den tatsächlichen TypeScript-MCP und die native Engine. |
| EndToEnd-Skill: `agents/openai.yaml` und Elternverzeichnisse | Nicht separat übernehmen. | Qyl-Ordnername und Nami-Skillname sind historische Rename-Reste, kein neues Architekturkonzept. |
| Die drei `testconfig.json` | Bereits funktional übernommen und in Nami weiterentwickelt. **Nicht überschreiben.** | Siehe den direkten Vergleich unten. |
| Root-`global.json` | Nicht übernehmen. | Alt: SDK `10.0.402`. Nami pinnt `11.0.100-rc.3.26522.148`; MSTest.Sdk `4.5.1`. |
| Root-`NuGet.Config` | Nicht übernehmen. | Der Feed `artifacts/product/current/feed` für `Nami.MaterializedTestSnapshot.*` gehört zum alten Produkt-Snapshot-Modell. Nami hat aktuell keine Root-NuGet.Config; temporäre Consumer haben ihre eigene Isolation. Aus dieser Abwesenheit folgt kein fehlender Import. |
| `Nami.Telemetry.EndToEndTests/Delivery/inputs.json` | Als historische Delivery-Eingabe archivieren, nicht aktivieren. | Pinnt Collector `daf9ae1…` und npm-Paket `nami-mcp-server@17.0.14`. Das ist nicht die heutige lokale C#-Engine-/TypeScript-MCP-Kette. Eine spätere veröffentlichte Paket-Abnahme braucht ein ausdrücklich neues Ziel. |

### Die vorhandenen Nami-Testkonfigurationen sind weiter

Alle drei aktiven Dateien verweisen wie der Prototyp auf das MSTest-4.5.1-Schema. In Nami setzen sie zusätzlich `captureAssertionFailureDiagnostics: true` und `treatDiscoveryWarningsAsErrors: true`.

| Suite | Gemeinsam beibehalten | Heutige Unterschiede zum Prototyp |
| --- | --- | --- |
| [Unit](../../tests/Nami.UnitTests/testconfig.json) | 30 Sekunden, kooperative Cancellation, Ergebnis-Capture. | Zusätzliche Execution-Einstellungen. |
| [Integration](../../tests/Nami.IntegrationTests/testconfig.json) | 120 Sekunden, kooperative Cancellation. | Ausgabe nur Fehler/Skips; stdout/stderr bei Fehler; `captureTrace: Result`; ANSI aus; zusätzliche Execution-Einstellungen. |
| [EndToEnd](../../tests/Nami.EndToEndTests/testconfig.json) | 300 Sekunden, kooperative Cancellation, ausführliche Ausgabe und Live-Capture. | ANSI aus; zusätzliche Execution-Einstellungen. |

Das ist ein Konfigurationsvergleich, keine erneute Bestätigung sämtlicher Runner-Effekte. Auch eine kooperative Suite-Deadline behebt nicht automatisch eine fehlerhafte interne Wartekette.

### Warum nicht vier alte Skills umbenennen?

Gleiche Informationen an vielen Orten erzeugen konkurrierende Anweisungen. Für Nami reichen zurzeit die bestehenden Rollen in Root-`AGENTS.md`, [testing.md](../testing.md), die [.agents/skills/fixture-lifecycle/SKILL.md](../../.agents/skills/fixture-lifecycle/SKILL.md) und die jeweils fachlich zuständigen Skills. Ein zusätzlicher projektspezifischer Skill wird erst angelegt, wenn er einen eigenen, wiederkehrenden Arbeitsablauf beschreibt.

Die früheren gebrochenen Downloads-Skill-Verweise gehören zum abgeschlossenen Cleanup in
[skill-plugin-dedup.md](skill-plugin-dedup.md). Sie werden nicht nach Nami repliziert.

## 4. Playground-Entscheidungen (2026-10-27)

Alle 50 archivierten Quell-, Konfigurations- und Metadatendateien sind in
[playground-migration.csv](playground-migration.csv) mit SHA256, Ziel, Entscheidung und Prüfnamen zugeordnet.
Die vorhandenen Nami-Eigentümer wurden erweitert; es gibt keine zweite Fixture-Infrastruktur.

| Kandidat | Entscheidung | Konkreter Nachweis |
| --- | --- | --- |
| `WriteIfDifferent` | In `FixtureProject` übernommen: Quellen, Projekt-/Config-XML und SDK-Auswahl werden nur bei geänderten Bytes geschrieben. | `FixtureMaterializationTests`; tatsächlicher Wiederholungsbuild in `FixtureIncrementalBuildTests`. |
| SDK-passende Roslyn-Referenzen | Compiler und Probe verwenden Roslyn des gepinnten SDK. Der Aufrufer übergibt Frameworkreferenzen ausdrücklich; die feste Net100-Referenzbibliothek ist entfernt. | `InProcessCompilerTests`, `PeImageTests`; C# 15 und .NET-11-API werden tatsächlich kompiliert. |
| Explizite Diagnoseauswahl | `FixtureDiagnostics.None/Full`; ohne expliziten TestContext keine Evidenz. Nur der MSTest-Adapter liest seinen Frameworkkontext. | `FixtureMaterializationTests`, `FixtureIncrementalBuildTests`, `FixtureEvidenceTests`, `InvocationContractTests`. |
| Paket-/HTTP-Cache-Besitz | Beide Caches gehören dem ausdrücklich geteilten `PackageFeedFixture`; private Projekte bleiben isoliert. | Materialisierungs-, Lebenszeit- und reale SDK-Build-Tests. |
| Phasenmessung | Nicht übernommen. Kein aktueller Verbraucher und keine konkrete Performancefrage rechtfertigen weitere Ergebnisfelder. | Quell-/Verbraucherabgleich im Migrationsinventar. |
| Test-Host-DI / Scopes | Nicht übernommen. Bestehende per-Invocation- und Shared-Fixtures decken die tatsächlichen Verbraucher ab. | `InvocationContractTests`, `SharedFixtureTests`; kein neuer Generic Host. |

Die Testnamen bezeichnen die zuständigen Nachweise. Der endgültige Gesamtprüfstand steht erst nach Abschluss des aktuellen Gates fest.

## 5. Sicherung und aktuelle Aufräumgrenze

Der Playground wurde vor der Konsolidierung vollständig nach
`artifacts/migration-checkpoints/nami.playground-2026-10-27.tar.gz` gesichert. Das zugehörige
`.tar.manifest.json` hält Quell-Hashes fest. Das Archiv ist kein Build- oder Test-Eingang.
Nach Abschluss der Konsolidierung hat der Nutzer die Löschung von `~/RiderProjects/nami.playground`
ausdrücklich beauftragt. Der Ordner wurde am 27. Oktober 2026 gelöscht; seine Abwesenheit wurde
geprüft. Vorher wurden die archivierten Quelldateien erneut abgeglichen; Änderungen betrafen nur
`.DS_Store`. Das Archiv bleibt erhalten (SHA256:
`15071ed3c0de32426cc0fc1a0c0736a37e606f6aa2f12c4ec9a4b3b857418aef`).
**Qyl bleibt erhalten.** Downloads-Prototyp und seine alte Sicherung wurden zuvor gesondert
ausdrücklich gelöscht. Diese Entscheidungen eröffnen keinen weiteren Migrationsauftrag.

## 6. Abschlussumfang

- Die bestehenden Fixture-, Compiler-, Prozess-, Capture- und Evidenz-Eigentümer bleiben maßgeblich.
- Die 50 Playground-Dateien haben eine Entscheidung mit Ziel, Begründung und zuständigem Nachweis.
- Bytebewahrende Materialisierung, SDK-Roslyn, explizite Diagnostik und Cache-Besitz sind übernommen.
- Unbenutzte Phasenmessung und zusätzliche Test-Host-DI wurden nicht übernommen.
- Die vier primären Testprojekte und ihre gemeinsamen Props/Targets bilden die aktive Struktur.
- Der Gesamtprüfstand wird getrennt durch das aktuelle Build-/Test-/Inspektionsgate belegt.

Der [Request-/Response-Review](../prd/nami-request-response-review.md) bleibt der Produktmaßstab.
Fehlende Anwendungsinstrumentierung und Beobachtungslebenszeit sind offene Produktanforderungen;
sie werden durch diese Infrastrukturkonsolidierung weder umgesetzt noch gestrichen.
