# Nami: Übergabe und Aufräumregister

> **Stand: 27. Oktober 2026 · Quelltext- und Dateivergleich, kein neuer Testlauf**  
> Arbeitsort für Nami: `/Users/alexandernachtmann/RiderProjects/nami`.  
> Zweck: vorhandene Arbeit finden, alte Regeln nicht wieder aktivieren und dieselbe Migration nicht zweimal ausführen.

## Vor einer erneuten Migration lesen

Die Übernahme des Materialisierungs-Prototyps ist bereits im [Test-Harness-Handover](test-harness.md) und im [Dateiinventar](test-harness-inventory.csv) dokumentiert. **Die 257 Inventareinträge sind keine offenen Importaufträge.** Die neun historischen Stationen stehen im [Request-/Response-Review](../prd/nami-request-response-review.md#8-abgleich-mit-meinen-früheren-antworten).

Eine vorhandene Datei beweist nicht, dass ihre komplette Implementierung optimal oder jeder frühere Test aktuell bestanden ist. Sie zeigt aber den Ort, an dem Verbesserungen ansetzen. Vor einem neuen Import: bestehenden Eigentümer finden, konkrete fehlende Fähigkeit benennen, Verhalten vergleichen und nur diese Lücke bearbeiten. Keine zweite Fixture-Infrastruktur neben die vorhandene stellen.

## 1. Welcher Arbeitsbereich wofür zuständig ist

| Ort | Jetzt beobachteter Stand | Verwendung und Aufräumentscheidung |
| --- | --- | --- |
| `~/RiderProjects/nami` | Branch `snapshot/1932-current-code`, HEAD `9e151e0`; 44 Status-Einträge vor dieser Dokumentationsänderung. | Aktive Produktimplementierung, Tests, Build und Vertragsgenerierung. Den Arbeitsbaum erhalten; `e0dd040` ist nicht mehr der neueste lokale Commit. |
| `~/Downloads/Qyl.Telemetry.Tests-checkpoint-2026-10-24-000952-nami-prd` | `materialized-project`, Branch `main`, HEAD `34d902a`; 235 Status-Einträge. | Historischer Prototyp. Keine aktive Nami-Build-Abhängigkeit und kein Ort für neue Nami-Features. Nach abschließender Sicherung stilllegen; keine erneute Gesamtmigration. |
| `~/RiderProjects/qyl` | Eigenständiges Repository `ANcpLua/qyl`, Branch `main`, HEAD `80a63fbf`; drei vom Elternrepository nicht verfolgte, jeweils eigenständige Git-Repositories. | Separates Altprodukt. Nicht als Nami-Basis verwenden. Vor Entfernung auch die drei verschachtelten Repositories berücksichtigen. |
| `~/RiderProjects/nami.playground` | Kein Git-Repository. | Begrenzte Sammlung von Verbesserungskandidaten. Keine zweite aktive Testarchitektur. Vor Entfernung vollständig sichern; die Kandidaten aus Abschnitt 4 einmalig entscheiden. |
| `~/RiderProjects/nami-plugin` | Eigenständiges Repository, Remote `ANcpLua/opusfix`, HEAD `861d657`; eine lokale Löschung. | Gesonderte Plugin-Arbeit, weder `materialized-project` noch bereits vollständig nach Nami übernommen. Nicht zusammen mit dem Prototyp entsorgen. |
| `~/RiderProjects/nami-architecture-review` | Lokale Mission, Fragen, User Stories, Quellen und Review-Protokolle. | Entscheidungsarbeit. Keine Produktlaufzeit, kein zweiter Build und keine zweite Implementierung. Beschlossene Änderungen in Nami umsetzen; Status hier nachvollziehen. |

Diese Git-Zahlen sind ein datierter Befund und werden durch weitere Änderungen überholt. Remotes wurden hier nicht auf einen neuen Push-Stand geprüft. Ein Remote schützt unversionierte Dateien grundsätzlich nicht.

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

Die Downloads-Kopie enthält außerdem vier gebrochene `.agents/skills/qyl-telemetry-*`-Symlinks auf vor der Umbenennung gültige Qyl-Verzeichnisse. Diese werden nicht in Nami repariert oder repliziert. Der aktuelle Chat stellt einige Skills noch aus der Downloads-Kopie bereit; deren Auffindung ist von der Produktabhängigkeit getrennt. Vor endgültiger Entfernung auch diese Agent-Konfiguration beziehungsweise den neuen Arbeitsort berücksichtigen.

## 4. Was vom Playground wirklich noch offen ist

Der [gesonderte Review](../../../nami-architecture-review/REVIEW-TIME-PLAYGROUND.md) bleibt die ausführlichere lokale Quelle. Die folgenden Punkte sind **Kandidaten**, keine zweite Gesamtmigration und keine automatisch beschlossenen Verbesserungen:

| Kandidat | Verhältnis zum vorhandenen Nami | Einmalig zu entscheidende Frage |
| --- | --- | --- |
| `WriteIfDifferent` | Nami schreibt Fixture-Quellen derzeit direkt; der Playground erhält unveränderte Bytes/Zeitstempel. | Verbessert das einen tatsächlich wiederverwendeten Build, ohne veraltete Eingaben zu behalten? |
| SDK-passende Roslyn-Referenzen | Nami besitzt bereits den Compiler-Harness und nutzt `Basic.Reference.Assemblies.Net100`; der Playground hat eine andere Referenzstrategie. | Welche Ziel-Frameworks muss eine konkrete Probe semantisch testen? Parser-Version und Referenz-API-Fläche getrennt entscheiden. |
| Explizite Diagnoseauswahl | Nami besitzt Evidenz- und Run-Retention-Eigentümer; der Playground bietet `None`/`Full`. | Ist eine auswählbare Menge nötig? Bestehenden Besitz und Fehlernachweis erhalten. |
| Paket-/HTTP-Cache-Besitz | Der Paketfeed existiert in Nami bereits. | Nur eine konkret fehlende Cache-Zuständigkeit ergänzen. |
| Phasenmessung | Der Playground trennt Materialisierung, Ausführung und Diagnostik. | Gibt es eine beantwortbare Performancefrage? Die Felder sind kein Benchmark-Ergebnis. |
| Test-Host-DI / Scopes | Nami besitzt per-Invocation- und Shared-Fixtures. | Nur übernehmen, wenn ein benötigter Lebenszeitfall dadurch besser und mit dem tatsächlichen Host kompatibel wird. |

## 5. Sicherung und Löschbereitschaft

### Downloads-Prototyp

Die bestehende Sicherung liegt unter:

`~/.local/state/nami/handoffs/retired-prototype-20261026T070739Z`

Erneut geprüft: Git-Bundle gültig und vollständig für die enthaltene Historie; Archiv mit 672 Einträgen; Hashes der enthaltenen regulären Dateien stimmen mit dem Sicherungsmanifest überein. Gegen den heutigen Ordner fehlen keine zuvor manifestierten Dateien. Abweichungen seit der Sicherung betreffen `.DS_Store`-Dateien und Rider-`workspace.xml`; neu sind eine weitere `.DS_Store` und Rider-`vcs.xml`.

**Damit ist die Entwicklungsarbeit des damaligen Prototyps gesichert.** Wer auch den neuesten IDE-Zustand erhalten möchte, muss diese wenigen geänderten/neuen Dateien ergänzend sichern. Rebuildbare Verzeichnisse wie `bin`, `obj`, `artifacts`, `node_modules` und `TestResults` wurden bewusst nicht archiviert. Das Archiv ist kein Remote-Push der lokalen Änderungen.

### Qyl

Der Elternstatus `??` ist hier missverständlich: Die drei Verzeichnisse sind eigenständige Git-Repositories, bei der Prüfung jeweils auf `main` und mit sauberem Arbeitsbaum:

| Verschachteltes Repository | Lokaler HEAD | Eingetragener GitHub-Remote |
| --- | --- | --- |
| `Qyl.Telemetry.SemanticConventions/` | `684fa819` | `ANcpLua/Qyl.Telemetry.SemanticConventions` |
| `Qyl.Telemetry.Tests/` | `a196ccb` | `ANcpLua/Qyl.Telemetry.Tests` |
| `qyl.automation-for-agents/` | `69514d8` | `ANcpLua/qyl.automation-for-agents` |

Sauberer Arbeitsbaum bedeutet, dass keine vom jeweiligen Git-Status angezeigten lokalen Änderungen vorliegen. Es beweist weder, dass alle Commits gepusht sind, noch dass ignorierte Dateien entbehrlich sind. Diese Repositories wurden nicht erneut nach Nami migriert. Vor dem Löschen des Elternordners ihre Historie und benötigte ignorierte Inhalte ebenfalls sichern beziehungsweise den Remote-Stand bestätigen. **Der gesamte Qyl-Ordner ist hier noch nicht als verlustfrei löschbar freigegeben.**

### Playground und Plugin

Der Playground hat keine Git-Historie. Vor dem Entfernen einmal vollständig sichern und die begrenzten Kandidaten aus Abschnitt 4 mit Status versehen. `nami-plugin` ist separat; seine lokale Löschung `.github/agents/specification.agent.md` und seine Remote-Historie gehören nicht zum Prototyp-Archiv.

## 6. Abschlussliste gegen Doppelarbeit

- [x] Die neun Stationen und die späteren Architekturkorrekturen dokumentiert.
- [x] Bestehende Nami-Eigentümer für Materialisierung, Compiler, Metadaten, Paketfeed, Capture, Prozesse und Evidenz erneut gefunden.
- [x] Alle angefragten Altregeln, Skill-Metadaten, Konfigurationen und Delivery-Pins zugeordnet.
- [x] Weiterentwickelte Nami-Konfigurationen gegen die alten Dateien verglichen.
- [x] Prototyp-Sicherung und verbleibende lokale Abweichungen geprüft.
- [ ] Neueste IDE-Dateien bei Bedarf ergänzend sichern; Agent-Skill-Verweise vor physischer Entfernung umstellen beziehungsweise den neuen Nami-Arbeitsort verwenden.
- [ ] Qyls drei verschachtelte Repositories und benötigte ignorierte Inhalte sichern beziehungsweise ihren Remote-Stand bestätigen.
- [ ] Playground sichern und seine sechs Kandidaten einzeln übernehmen oder verwerfen.
- [ ] Erst dann gewünschte Altordner physisch entfernen. **In diesem Durchgang wurde nichts gelöscht.**

Die beiden offenen Timeout-Risiken und Produktfragen bleiben im [Request-/Response-Review](../prd/nami-request-response-review.md) getrennt aufgeführt. Sie werden durch das Aufräumen weder behoben noch zu Gründen für eine Wiederholung der Migration.
