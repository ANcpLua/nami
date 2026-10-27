# Genehmigtes Nami-Ziel und Zuständigkeit der Quellen

Stand: 27. Oktober 2026. Der Nutzer hat [nami.md](nami.md) und den
[Request-/Response-Abgleich](nami-request-response-review.md) gemeinsam als
Ziel- und Prüfbasis genehmigt. Offene Entscheidungen in diesen Dokumenten bleiben
offen. Die Freigabe beschreibt das Ziel, keinen abgeschlossenen Produktnachweis.

## Eine Zuständigkeit je Ebene

| Quelle | Zuständigkeit | Grenze |
| --- | --- | --- |
| Die beiden genehmigten PRDs | Produktnutzen, erforderliche Datenwege und Abnahmekriterien | Eine vorhandene Implementierung oder ein Skill überschreibt das Ziel nicht. |
| Nami `AGENTS.md` und `.agents/skills/nami-contract-models` | Repositorystruktur, Modellgenerierung, aktuelle Quellen und bekannte Lücken | Historische Versionen und Beispiele bleiben datierte Beobachtungen. |
| Nami `fixture-lifecycle` | Eigentümer der Test-Workspaces, begrenzte Aufbewahrung und Ablauf | Diese Nami-spezifische Betriebsregel ist kein universeller MSTest-Vertrag. |
| Plugin `nami` 0.1.1 aus `ANcpLua/opusfix` | MSTest-4.5.1-APIs, Assertions, Fixture-Besitz, Unions und native Codex-Nachrichten | Allgemeine Testanleitung, keine Nami-Produktarchitektur. |
| `nami.playground` | Isolierte Testinfrastruktur und ausführbare Fixture-/Hosting-Beispiele | Kein Ersatz für Nami-Produktnachweise; Unit/E2E dürfen dort absichtlich ohne Produktfälle sein. |
| Downloads-Prototyp `Qyl.Telemetry.Tests-checkpoint-2026-10-24-000952-nami-prd` | Historische Materialisierungs-, Compiler-, Transport- und Policy-Beispiele | Keine aktive Build-, Generator- oder Laufzeitabhängigkeit des Nami-Produkts. |
| `ancplua-openai` | Ein gemeinsamer Einstieg zu den eigenen Plugin-Paketen | Ein Katalog ist keine zweite Kopie der Skills. |

## Was erhalten bleibt

Nami ist agentengesteuertes Debugging mit einem TypeScript-MCP und einer
C#-NativeAOT-Engine. TypeSpec besitzt die Nami-eigenen Datenformen, Darstellungen
und strukturellen Abbildungen einschließlich Ingestion und Persistenz;
Weaver besitzt SemConv und zugehörige Persistenzregeln. Benannte Algorithmen und
externe Protokollimplementierungen werden dadurch nicht zu generierten DTOs.

Die aktuelle Nami-Modellierungsanleitung benennt noch handgeschriebene
`StoredAttributeValue`-Formen sowie Ingestion-/Storage-Zuordnungen. Die genannten
Quelldateien sind vorhanden. Ihre Existenz bestätigt die offene Migration;
sie belegt keine vollständige semantische oder Laufzeitprüfung.

Die Fixture-Korrekturen aus Nami 0.1.1 bleiben erhalten: Eine gemeinsam genutzte
Klassen-Fixture besitzt ihre Erstellungs- und Disposal-Lebenszeit. Ein einzelner
Test darf sein Warten abbrechen, ohne die gemeinsame Erstellung für andere
Verbraucher zu beenden. Der Playground verweist auf diese Quelle.

## Was nicht aus dem Prototyp übernommen wird

Die Downloads-Kopie pinnt .NET 10.0.402 und trägt ältere Regeln für
`Nami.Telemetry.*`-Projekte. Ihre pauschale NativeAOT-Präferenz für ausführbare
Tests ersetzt weder den aktiven .NET-11-Stand noch den begründeten verwalteten
MSTest-Host. Ein verwalteter Testhost kann eine veröffentlichte NativeAOT-Engine
prüfen. Die beiden Ausführungsarten brauchen getrennte Nachweise.

Die historische große Test-SKILL.md, die gebündelten .NET-Plugins und früheren
Testergebniszahlen werden nicht als neue globale Regeln oder aktuelle Erfolge
importiert. Alte Fälle können als konkrete Regression übernommen werden, wenn
sie eine heutige PRD-Anforderung prüfen und noch nicht abgedeckt sind.

## Sichtbar offene Punkte

Die PRD nennt Interceptors für den deklarierten zusätzlichen
Instrumentierungsumfang. Der Request-/Response-Abgleich verlangt gleichzeitig,
zuerst die benötigte Fähigkeit zu prüfen: Ein vorhandener zentraler
Tool-Aufrufpfad darf nicht allein wegen fehlender Interceptors als funktionslos
bewertet werden. Zusätzliche Generatoren und Beobachtungs-APIs brauchen den
konkreten Anwendungsfall und ihre eigenen Nachweise.

Die beiden bereits dokumentierten Timeout-Risiken sind im aktuellen Code noch
sichtbar: [HostTransportTests.cs](../../tests/Nami.EndToEndTests/HostTransportTests.cs)
prüft eine äußere 20-Sekunden-Frist, reicht an den einzelnen Call aber den
Test-Token weiter. [McpHarness.cs](../../tests/Nami.Tests/Infrastructure/McpHarness.cs)
wartet im Child-Exit-Fehlerpfad auf den stderr-Drain. Das sind statische Befunde;
diese Quellenprüfung reproduziert oder behebt keinen Laufzeitfehler.

## Pflege und Dedup

Die alte globale Assertions-Kopie wurde entfernt. Die fünf installierten
Nami-0.1.1-SKILL.md-Dateien wurden bytegenau mit der aktuellen Plugin-Quelle
verglichen. Die `opusfix`-Quelle bleibt erhalten. Die sechzehn allgemeinen
.NET-/MSTest-Kopien in qyl bleiben entfernt; ihre ursprünglichen Referenzen und
Hilfen bleiben bei den kanonischen Companion-Plugins.

Ein Alter über sieben Tage markiert eine Prüfpriorität. Die Auswahl einer
Quelle folgt ihrem tatsächlichen Inhalt, dem verwendeten Paketstand und ihren
Nachweisen. Kopierzeitstempel sowie ein neueres Datum allein entscheiden nicht.

Diese Abstimmung verändert keine Produktimplementierung, keine Compiler-Pins
und keine Fixture-Laufzeit. Sie schützt die jüngeren Quellen und ordnet den
alten Prototyp gegenüber dem genehmigten Produktziel ein.
