# Nami: vom Auftrag zur belastbaren Antwort

Stand: 27. Oktober 2026. Diese Fassung korrigiert den Request-/Response-Review anhand der vollständig gelesenen Dateien `nami.md`, `checklist(1).md` und `nami-request-response-review.md` sowie der ausdrücklichen Nutzerentscheidung für Namis eigenen Collector.

## 1. Verbindlicher Maßstab

Nami umfasst den benötigten Datenweg von der Instrumentierung einer unterstützten Anwendung über den eigenen Collector und DuckDB bis zur begrenzten Antwort an einen Agenten. Ein Beispielname wie „tomo“ begrenzt weder Produzenten noch Zielsprachen oder Produktumfang. Rust ist keine zusätzliche Nami-Laufzeit und keine Voraussetzung für diesen Entwurf.

Die bereitgestellte PRD und Checkliste legen folgende Architektur fest:

- Nami implementiert seinen Collector selbst. Die C#/.NET-11-NativeAOT-Engine übernimmt OTLP-Ingestion, semantische Normalisierung, Persistenzregeln, Sanitizer, DuckDB und Analyse.
- TypeScript auf Bun stellt mit dem offiziellen MCP-TS-SDK v2 die MCP-Oberfläche bereit. TypeScript-MCP und native Engine werden gemeinsam ausgeliefert.
- Eine private, langlebige Stream-/Pipe-Verbindung transportiert ihre TypeSpec-definierten Nachrichten. Externes MCP-stdout, private Nachrichten und Diagnoseausgabe besitzen getrennte Kanäle.
- TypeSpec definiert Nami-eigene Strukturen und ihre benötigten Ausgaben. Weaver definiert SemConv-Vokabular, Status, Nachfolger und Persistenz-/Redaktionsregeln.
- Compile-Time-Instrumentierung für deklarierte unterstützte Aufrufstellen und ausdrücklich konfigurierte Beobachtungslebenszeiten gehören zum genehmigten Produktziel.
- Agenten erhalten typisierte, begrenzte Antworten mit Quellenbezug, Freshness und expliziten Grenzen. Die Produktoberfläche besteht aus Tools.

Die Entscheidung für den eigenen Collector wird in diesem Review nicht erneut zur Auswahl gestellt. Ein Austausch gegen den OpenTelemetry Collector oder ein zusätzlicher Collector-Dienst wäre eine eigene Architekturänderung. Standardisierte OTLP-Nachrichten und geeignete OTel-Bibliotheken bleiben Bestandteile der eigenen Implementierung.

Diese Festlegungen sind Zielanforderungen. Sie behaupten nicht, dass jeder betreffende Baustein im aktuellen Arbeitsbaum vollständig umgesetzt ist.

## 2. Zwei Datenwege, eine zusammenhängende Architektur

**Beobachtungsweg:** Der Build löst Registry, Modellmanifest und deklarierte Instrumentierungsziele auf. Bei unterstützten C#-Aufrufstellen erzeugt der Generator Wrapper, an die der Compiler die Aufrufe bindet. Zur Laufzeit beobachtet der Wrapper die wirkliche Operation und übergibt ihre Telemetrie an die konfigurierte Pipeline. Der eigene Collector nimmt OTLP entgegen und führt die Daten projektgebunden nach DuckDB.

**Abfrageweg:** Der Agent ruft ein MCP-Tool auf. TypeScript verarbeitet den MCP-Aufruf und übergibt den privaten Engine-Auftrag. Die Engine prüft den Auftrag, bestimmt die autorisierte Projektbindung und liest einen konsistenten Snapshot. Die Analyse liefert ein begrenztes Ergebnis; TypeScript gibt daraus die MCP-Antwort zurück.

Beide Wege werden über die jeweils passenden Identitäten verbunden: Trace-/Span-Kontext für Operationen, Request-ID für einen privaten Aufruf, Snapshot-ID für eine Datenansicht und Beobachtungskennung für eine länger lebende Beobachtung. Keine dieser Kennungen erteilt für sich eine Berechtigung.

Ein zentraler instrumentierter Engine-Handler kann Namis eigene Tool-Aufrufe erfassen. Dieser Pfad und die zugesagten generierten Wrapper für Anwendungscode haben unterschiedliche Einsatzbereiche. Ein zentraler Handler hebt die genehmigte Compile-Time-Instrumentierung nicht auf; zusätzliche Wrapper werden für die tatsächlich deklarierten Ziele erzeugt.

## 3. Instrumentierung, Collector und Beobachtungslebenszeit

Ein C#-Marker bezeichnet einen beim Build eindeutig auflösbaren semantischen Vertrag. Definitionsart und zulässige Signalzuordnung folgen aus diesem Vertrag; frei kombinierbare Modell-/Signal-Flags dürfen keine widersprüchliche Auswahl ermöglichen. Attributargumente verwenden dafür zulässige Konstanten, Enum-Werte oder Typreferenzen. Ein Union-Objekt wird nicht als Attributargument vorausgesetzt.

Der Compiler bindet die Aufrufstelle. Zur Laufzeit führt sie den bereits kompilierten Wrapper aus. Dieser erhält den beim Build aufgelösten Vertrag und beobachtet die ursprüngliche Operation. Der Runtime-Interceptor sucht nicht anhand eines Enums nach beliebigen nachträglich instrumentierbaren Methoden.

Die Instrumentierung gehört in die Compilation der jeweiligen unterstützten Anwendung. Eine bereits veröffentlichte Collector-Engine kann die internen Aufrufstellen eines fremden Prozesses nicht durch ihre bloße Existenz umschreiben. Für andere Zielsprachen gelten deren geeignete Instrumentierungspunkte und derselbe OTLP-Eingang. Ausgeschlossene Aufrufformen und nicht erfasste Abhängigkeiten werden im manifestierten Umfang benannt.

NativeAOT kompiliert die generierten Wrapper und Anwendung beim Publish. Sie benötigen keinen CLR-Profiler, keinen JIT und keine Laufzeit-Codegenerierung; benötigte .NET-Laufzeitbestandteile bleiben Bestandteil des nativen Artefakts. Ein Skill kann vorhandene Konfiguration verwenden oder einen neuen Build orchestrieren. Neue kompilierte Interceptors benötigen einen neuen Build.

Die konfigurierte Beobachtungslebenszeit wird von ausführbarem Code umgesetzt. XML beschreibt die gewählten Regeln; es führt sie nicht selbst aus. Länger lebende Beobachtungen verwenden explizite, projektgebundene Kennungen. Eine fachlich gewünschte Client-Bindung wird ausdrücklich modelliert und autorisiert; sie wird nicht aus einer MCP-Verbindung abgeleitet.

MCP 2026-07-28 bleibt auf Protokollebene stateless. Eine langlebige private Pipe ist zulässig. Über mehrere Requests benötigter Anwendungszustand wird in den Requests ausdrücklich referenziert. Eine Snapshot-Abfrage benötigt nur ihre Snapshot-Identität; sie erzeugt nicht automatisch eine neue Beobachtungssitzung.

Diese Zeiten bleiben getrennt: Beobachtungsdauer, absolute Frist, Inaktivitätsfrist, Erneuerung, Request-Deadline, Snapshot-Gültigkeit, Retention und Exportverzögerung. `since: "-15m"` bezeichnet ein Abfragefenster. Daraus entsteht keine 15-Minuten-Grenze für Monitoring. Die sechs lesenden Analyse-Tools erhalten durch diese Anforderungen keine versteckten Start-/Stop-/Schreiboperationen; die Beobachtungskonfiguration gehört zum vorgesehenen Build-/Start-/Laufzeitpfad.

## 4. Modelle und Bedeutung über alle Grenzen

Weaver liefert das versionierte semantische Wissen. TypeSpec liefert Nami-eigene Modelle und strukturelle Abbildungen für die tatsächlichen Verbraucher: Ingestion/Domain, Storage-JSON, DuckDB-Zeilen, Tools, private Engine-Nachrichten sowie benötigte Konfiguration. Native OTLP-Protokolltypen und deren Drahtformat behalten ihre eigene Zuständigkeit.

Gemeinsame Fachwerte werden gemeinsam definiert. Unterschiedliche Feldmengen, Identitätsrollen, Tags oder Präsenzregeln besitzen benannte Darstellungen und ausdrückliche Abbildungen. Ein Tool-Discriminator `kind` zwingt deshalb keinen Storage-Tag `type` oder ein OTLP-Feld in dieselbe Schreibweise. Dictionary-Schlüssel sind Nutzdaten, auch wenn sie `kind` oder `type` heißen.

Über die tatsächlich verwendeten Abbildungen bleiben insbesondere erhalten:

- Exakte signed/unsigned Integer und Nanosekunden, zulässige Wertebereiche und deren deklarierte JSON-Codierung. TypeScript-`number` genügt für passende begrenzte Werte wie `max_bytes`; volle 64-Bit-Werte benötigen die festgelegte exakte Darstellung.
- Pflichtwert, optionaler Wert, Pflichtwert mit `null` und optionaler nullable Wert. Insbesondere optional + nullable unterscheidet abwesend, `null` und Wert.
- Union-Varianten, deklarierter Discriminator und dessen Werte. Native C#-Unions ergänzen den TypeSpec-Discriminator nicht automatisch. Leere/default Zustände erhalten eine ausdrückliche Behandlung.
- Rekursive Attribute, Bytes, nichtendliche Float-Werte, Normalisierungsherkunft und Resource-/Scope-Bezug.
- Producer-Ereigniszeit, Producer-Beobachtungszeit und Collector-Empfangszeit. Die Empfangszeit beschreibt Eingangsaktivität und ersetzt nicht die ursprüngliche Ereigniszeit.
- Die gültigen Metrik-Kombinationen: Gauge und Sum tragen Number-Daten; Histogram, ExponentialHistogram und Summary ihre jeweiligen Payloads. Metrikart und Payload-Tag sind unterschiedliche Informationen. Temporality, Monotonie, Buckets, Exemplars und Dimensionen bleiben dort erhalten, wo sie Bedeutung haben.

TypeSpec beschreibt Strukturen und Abbildungsregeln. Fachliche Identitätsalgorithmen, Kanonisierung, Messwertgleichheit und Reihenfolge bleiben benannte Algorithmen. Passende Property-Typen allein legen diese Regeln nicht fest.

Semantischer Roundtrip und byteidentischer Replay sind verschiedene Zusagen. C# und TypeScript müssen dieselben Werte interpretieren. Property-Reihenfolge ist dafür unerheblich. Eine kanonische Ausgabe oder die Wiederholung derselben Snapshot-Antwort kann zusätzlich Byteidentität verlangen.

Der deduplizierte Modellumfang und seine zulässigen Signalzuordnungen werden aus den gepinnten Eingaben abgeleitet. Historische Bereichs- oder Attributzahlen ersetzen dieses Manifest nicht. Resource und Scope sind Zuordnungskontext. Ein Span-Ereignis bleibt von einem eigenständigen LogRecord unterscheidbar; allgemeine Logs benötigen im unterstützten Umfang eine Darstellung. Vier Definitionsnamen beweisen keine vollständige Signalabdeckung.

Die hier zugesagten OTLP-Eingänge tragen Traces, Metriken und Logs. Baggage ist propagierter Kontext; Ereignisse folgen der jeweils deklarierten Log- oder Span-Ereignisform. Profiles werden erst durch eine ausdrücklich zugesagte Implementierung zum unterstützten Umfang. Die bereitgestellte PRD führt angefragte Profiles-/Baggage-Daten als `data_unavailable` mit Grund. Daraus wird weder eine Nullmessung noch eine pauschale Unterstützung aller sechs Begriffe je SemConv-Modell abgeleitet.

Eine verlangte Stable-only-Auswahl gilt für Modelle, referenzierte Attribute und eingeschränkte Enum-Werte. Offene String-Werte werden dadurch nicht zu geschlossenen Enums. Diese Auswahl betrifft Instrumentierungsverträge. Namis Collector darf daneben ausdrücklich unterstützte Development-Telemetrie aufnehmen und deren Status erhalten. Der SemConv-Status verbietet keine freigegebenen RC-/Preview-Werkzeuge.

## 5. Was eine Request-/Response-Grenze leisten muss

TypeSpec definiert die Zuordnung von Toolname zu Input und Ergebnis beziehungsweise strukturiertem Fehler. Rohe externe Eingabe darf zunächst `unknown` sein, damit auch ungültige Daten kontrolliert abgewiesen werden. Nach Interpretation und Validierung wird die generierte typisierte Zuordnung verwendet. Ein dekodierter Transport-Umschlag ist noch kein fachlich gültiger Auftrag.

„Private Engine-Verbindung“ bedeutet die interne Verbindung zwischen TypeScript und der C#-Engine, keine zweite öffentliche API. Framing bestimmt die Grenzen vollständiger Nachrichten. Request-IDs ordnen konkurrierende Antworten zu; Revisionen beschreiben den vereinbarten Vertrag. Die Revision ist kein Snapshot, keine Datenbankmigration und keine Produktversionsnummer. Rein interne Storage-Details ändern nicht allein den öffentlichen Tool-Vertrag.

Beide Seiten verwenden die gemeinsam erzeugten Vertragsartefakte. Ein inkompatibler privater Vertrag wird vor der Analyse eindeutig abgewiesen. Diese Prüfung dient der korrekten Ausführung des gemeinsamen Releases; daraus entsteht keine Pflicht zu Abwärtskompatibilität. Definition, Emitter und betroffene Verbraucher können gemeinsam geändert und neu erzeugt werden.

Authentifizierung und Projektbindung werden an ihren Grenzen geprüft. Öffentliche Toolargumente und unbestätigte Metadaten wählen kein anderes Projekt. Auch explizite Snapshot-/Beobachtungskennungen werden innerhalb der autorisierten Projektbindung aufgelöst. Trace-Kontext wird weitergegeben, ohne Parallelaufrufe zu vermischen.

Die private Verbindung behandelt fragmentierte beziehungsweise zusammengefasste Reads, gleichzeitige Aufträge, vollständige JSONL-Nachrichten, maximale Frame-Größe, begrenzte Puffer, Backpressure, Abbruch, Prozessende und ausstehende Antworten. Ein vorhandenes `cancel`-Feld allein garantiert noch keinen abgeschlossenen wartenden Aufruf. JSONL ist das private Transportformat; es wird nicht als zusätzliches öffentliches MCP-Protokoll ausgegeben.

C# serialisiert mit generierten Metadaten passend zur vereinbarten Nachrichtenform. `JsonSerializerContext` und `JsonTypeInfo<T>` sind AOT-Metadatenmechanismen, keine pauschale Zusage über eine neue .NET-11-Optimierung. Die tatsächlich aufgelösten APIs bestimmen die Implementierung. Durch Streaming entfallen nur die Zwischenkopien, die der konkrete Pfad vermeidet; C# und TypeScript erhalten dadurch keinen gemeinsamen verwalteten Heap.

Die sechs Analyse-Tools der bereitgestellten PRD sind `sources_status`, `errors_recent`, `traces_get`, `traces_slowest`, `logs_search` und `changes_recent`. Ihre Zahl beschreibt diesen Analyseumfang, nicht die Anzahl unterstützter semantischer Modelle. Die gemeinsamen Typen umfassen unter anderem Freshness, Zeit-/Werteformen, Projektionen und Fehler, soweit mehrere Verbraucher sie tatsächlich benötigen.

### Beispiel: Quellen prüfen und denselben Datenstand vertiefen

Die folgenden Aufträge veranschaulichen den bereitgestellten Zielvertrag; sie sind keine aufgezeichneteten Laufzeitergebnisse.

```json
{
  "name": "sources_status",
  "arguments": {
    "service": "checkout",
    "since": "-15m",
    "max_bytes": 8192
  }
}
```

Die Antwort beschreibt Empfangsaktivität und Stille für den deklarierten Umfang und nennt ihren Snapshot. Ohne erwartete Quellen beziehungsweise ein Abdeckungsmanifest kann Nami aus Stille allein nicht unterscheiden, ob eine Anwendung inaktiv, nicht instrumentiert oder vom Collector getrennt ist. `silent` beweist keine Fehlerfreiheit und keine erfolgreiche Instrumentierung.

Eine Folgeabfrage verwendet die zurückgegebene Kennung:

```json
{
  "name": "traces_slowest",
  "arguments": {
    "service": "checkout",
    "since": "-15m",
    "snapshot_id": "example-snapshot-1",
    "limit": 3,
    "max_bytes": 8192,
    "fields": ["trace_id", "operation", "duration_ms"]
  }
}
```

Relative Zeiten beziehen sich beim Replay auf die eingefrorene Snapshot-Uhr. Nicht angeforderte Felder bleiben abwesend. Die Antwort benennt passende Trace-IDs und die nächste Untersuchung, ohne aus einer langsamen Trace bereits eine bewiesene Ursache abzuleiten. Ein Cursor ist an Tool, Snapshot und die übrige Abfrage gebunden.

Ungültige Eingaben wie `max_bytes: 12` werden gemäß dem deklarierten Minimum strukturiert abgewiesen. Anwendungsfehler, Protokollfehler, fehlende Daten, fehlende Berechtigung und Infrastrukturfehler behalten ihre passende Fehlerklassifikation. Ein deklarierter `ToolError` wird als solcher transportiert; ein Serverausfall erscheint nicht als erfolgreiche leere Analyse.

### Budget und Freshness

Die PRD begrenzt mit `max_bytes` die UTF-8-Darstellung von `structuredContent`. `content[1]` enthält dasselbe Ergebnis als JSON, `content[0]` eine begrenzte Headline. MCP-/JSON-RPC-Umschlag, duplizierte Inhaltsdarstellung und private Frames besitzen zusätzlichen Aufwand und ihre eigenen deklarierten Grenzen. Das Ergebnisbudget ist nicht automatisch die Gesamtgröße der Transportnachricht.

`examined`, `matched` und `returned` beziehen sich auf die jeweilige Analyse und deren Einheiten. Unbekannte Zähler bleiben entsprechend dem Vertrag unbekannt. Ranking-Limit, Seitenbildung, Bytebudget und abgebrochene Scans dürfen nicht miteinander verwechselt werden. `truncated` und Cursor zeigen die vereinbarte Fortsetzung an.

Die Wasserlinie bezeichnet die dokumentierte Verarbeitungsgrenze des Collectors im betrachteten Snapshot. Sie beweist nicht, dass eine Quelle künftig keine älteren Ereignisse mehr sendet. `complete` darf entsprechend nur die ausgewiesene Vollständigkeit bezüglich angenommener Daten und des Snapshots ausdrücken. Liegt das angefragte Fensterende hinter dieser Grenze, bleibt es unvollständig. „Vollständig bis zur Wasserlinie“ und „das ganze angefragte Fenster vollständig“ sind unterschiedliche Aussagen.

Eine Aussage wie „keine Fehler gefunden“ nennt deshalb den untersuchten Datenstand und Umfang. Vollständige Producer-Erfassung, frischer Eingang und vollständige Abfrage sind getrennte Eigenschaften. Fehlende Instrumentierung oder ein Verlust vor dem Collector lässt sich nicht allein aus einer Collector-Wasserlinie ausschließen.

## 6. Verbindliche Instrumentierungsanforderungen I1–I18

Die folgenden Anforderungen erhalten den vom Nutzer vorgegebenen Umfang. Ihre Implementierungsform wird dort eingehalten, wo die PRD sie ausdrücklich festlegt. Für bereits vorhandene Fähigkeiten werden die tatsächlichen Implementierungen und Verbraucher herangezogen; neue Bausteine werden nicht allein aus einem im Dokument verwendeten Typnamen abgeleitet.

1. **I1 — Modellauflösung.** `Marker_ResolvesToExactlyOnePinnedSemanticContract`: erlaubte Marker ergeben genau einen gepinnten Vertrag mit Definitionsart und Signalzuordnung; unbekannte oder widersprüchliche Auswahl erzeugt eine Build-Diagnose.
2. **I2 — Stable-Auswahl.** `StablePolicy_RejectsUnstableAndDeprecatedContractMembers`: die verlangte Auswahl erfüllt ihre Regel auf Modell-, Attribut- und eingeschränkter Enum-Ebene; offene Werte bleiben offen.
3. **I3 — Deklarierte Abdeckung.** `DeclaredTargets_HaveExecutableCoverageOrAnExplicitExclusion`: der ganze deklarierte Umfang besitzt einen tatsächlichen Instrumentierungspfad oder eine konkrete Ausschlussbegründung. Registry-Wissen allein belegt keine Instrumentierung.
4. **I4 — Aufrufsemantik.** `Interceptor_PreservesInvocationSemantics`: Receiver/Argumente, Auswertungsreihenfolge, genau eine Ausführung, Ergebnis, Seiteneffekte, Exception und Cancellation bleiben erhalten.
5. **I5 — Tatsächlicher Abschluss.** `Interceptor_ObservesCompletionIncludingAwaitAndEnumeration`: unterstützte asynchrone Operationen und Enumerationen werden über ihre vereinbarte wirkliche Lebenszeit beobachtet und genau einmal abgeschlossen.
6. **I6 — Kontext.** `ConcurrentOperations_PreserveParentageAndRestoreAmbientContext`: Verschachtelung, Parallelität, Fehler und Abbruch behalten ihre korrekten Beziehungen und stellen den vorherigen Kontext wieder her.
7. **I7 — Signalform.** `EveryModel_EmitsItsDeclaredTelemetryShape`: die erzeugten Daten erfüllen das deklarierte Modell einschließlich Span-Kind/Instrument, Einheiten, Attributtypen, Ereignisziel und Resource-/Scope-Zuordnung.
8. **I8 — Sprachübergreifende Bedeutung.** `DiscriminatedContracts_RoundTripAcrossEnabledImplementations`: die Nami-Verbraucher in C# und TypeScript interpretieren gemeinsame Werte, Varianten, Präsenz und Zahlenbereiche gleich. Kanonische Bytes werden gesondert beurteilt.
9. **I9 — Ungültige Union-Zustände.** `UnionBoundaries_HandleDefaultAndUnknownCasesExplicitly`: leere/default Zustände und fehlende/unbekannte Discriminator-Werte erhalten die deklarierte Behandlung und werden nicht still zu einer anderen Variante.
10. **I10 — Veröffentlichter Stack.** `PublishedNativeAotBinary_CompletesTheObservationPipeline`: die native Engine läuft ohne installiertes .NET und CLR-Profiler; ein echter Tool-Aufruf über den veröffentlichten TypeScript-MCP und eine tatsächlich instrumentierte Operation verbinden die benötigten Stufen bis zur Abfrage. Die Beobachtung des eigenen Tool-Aufrufs deckt dabei nur dessen ausdrücklich benannten Umfang ab.
11. **I11 — Beobachtungsidentität.** `ObservationLifetime_IsIsolatedAndReferencedExplicitly`: Folgeaufträge verwenden die ausdrücklich autorisierte Beobachtungskennung; parallele Clients/Projekte und Reconnects vermischen den Zustand nicht.
12. **I12 — Konfigurierte Zeitregeln.** `ObservationLifetime_ExpiresOnlyAccordingToConfiguredPolicy` und `Observation_ContinuesBeyondFifteenMinutes_WhenConfigured`: absolute/inaktive Fristen, Erneuerung und idempotentes Beenden folgen der Konfiguration; eine längere Lebenszeit erhält keine versteckte 15-Minuten-Kappung.
13. **I13 — Dauer und Sichtbarkeit.** `Observation_ReportsCorrectDurationAndBoundedVisibilityDelay`: monotone Dauer und Einheiten stimmen; die einsekündige Sichtbarkeitszusage der PRD gilt unter dokumentierter kontrollierter Last ab dem festgelegten Annahmepunkt. Erzeugung, Exportannahme und Abfragbarkeit werden zeitlich getrennt betrachtet; Producer-Exportintervalle werden nicht unsichtbar in dieselbe Frist gerechnet.
14. **I14 — Vollständige Aufzeichnung im zugesagten Umfang.** `RecordingPolicy_CapturesEveryEligibleOperation`: Quellen sind rechtzeitig aktiv; eine verlangte vollständige Erfassung schließt versteckte Parent-Sampling-Entscheidungen einschließlich eines eingehenden Flags `00` aus. AlwaysOn allein verhindert keine Überlast-, Export- oder Prozessverluste.
15. **I15 — Metriksemantik.** `MetricExport_PreservesAggregatesTemporalityAndDimensions`: bekannte Messfolgen behalten die erwarteten Counts, Summen, Buckets beziehungsweise Gauges, Temporality und Dimensionen; ein exportierter Datenpunkt pro Aufruf wird nicht vorausgesetzt.
16. **I16 — Durchgängige Zuordnung.** `Operation_IsCorrelatedFromExecutionThroughQuery`: tatsächliche Ausführung, erzeugte Daten, Exportannahme und Abfrageergebnis werden eindeutig zugeordnet; fachliche, Protokoll- und Exportfehler bleiben getrennt.
17. **I17 — Fehler und Verluste.** `ExportFailures_AccountForRetriesRejectionsAndDrops`: retryfähige Fehler, permanente Ablehnungen, Drops und Wiederholungen folgen dem Transport- und Ingest-Vertrag. Eine OTLP-Partial-Success-Antwort veranlasst keine Wiederholung des gesamten Requests. Mögliche Duplikate aus verlorenen Antworten folgen der dokumentierten Ingest-Semantik.
18. **I18 — Endliche Selbstbeobachtung und Abschluss.** `SelfIngestion_DoesNotCreateUnboundedTelemetryRecursion` und `Shutdown_DrainsAcceptedTelemetryOrReportsTheRemainder`: Selbsttelemetrie bildet keine endlose Rückkopplung; Shutdown verarbeitet angenommene Daten oder weist den Rest beziehungsweise Fehler aus. Ein abgelaufener Flush gilt nicht als erfolgreicher Abschluss.

Diese Anforderungen begründen weder eine globale Exactly-once-Zustellung durch OTLP noch eine Garantie über nicht beobachtete Programme. Ein direkt gebautes OTLP-Payload prüft den Collector-Eingang. Für die Instrumentierungsstufe wird eine reale, kontrollierte Operation mit ihrem tatsächlich ausgeführten Instrumentierungspfad benötigt.

## 7. Private Engine und Agentenauslieferung D1–D6

- **D1:** Die unterstützten Agentenhosts erhalten die deklarierte Tool-Oberfläche und ihre Schemas, Fehler, Freshness, Größenregeln und Annotationen. Host-spezifische Authentifizierung und Bereitstellung bleiben ausdrücklich beschrieben.
- **D2:** TypeScript und Engine erkennen einen inkompatiblen privaten Vertrag vor dem fachlichen Dispatch.
- **D3:** Parallele, fragmentierte und große Nachrichten behalten ihre Zuordnung; Puffer und Backpressure begrenzen den Ressourcenverbrauch.
- **D4:** Cancellation und Prozessende schließen ausstehende Aufträge definiert ab und hinterlassen keine verwaisten Prozesse.
- **D5:** Authentifizierte Projektbindung und Trace-Kontext bleiben über die private Grenze erhalten.
- **D6:** Das gemeinsame Release startet MCP und native Engine mit ihren deklarierten Laufzeit-/Bibliotheksabhängigkeiten und getrennten Diagnosekanälen.

Ein öffentlich nutzbares Plugin erzeugt keinen automatischen Zugriff auf fremde Browserprozesse, Netzwerk-Sockets oder GPU-Zustände. Zusätzliche lokale Adapter benötigen eine konkrete unterstützte Quelle und den passenden Zugriff. Diese Erweiterung ist keine Voraussetzung, um den bereits festgelegten Datenweg instrumentierter Anwendungen fertigzustellen. Veröffentlichungs- und Hostinganforderungen richten sich nach dem konkret unterstützten Host.

## 8. Korrekturen gegenüber der vorigen Reviewfassung

- Die Abschnitte 4 und 10 hatten die Collector-Verantwortung wieder geöffnet. Sie ist durch die Nutzerentscheidung und die PRD festgelegt: Nami besitzt den Collector.
- Die Abschnitte 7, 8 und 10 hatten deklarierte Compile-Time-Instrumentierung und Beobachtungslebenszeit zu optionalen späteren Erweiterungen gemacht. Der zentrale Eigeninstrumentierungspfad erfüllt einen anderen Umfang; die genehmigten I-Anforderungen bleiben bestehen.
- Die Auswahl des ersten Lieferfalls wird nicht erneut als Architekturblocker eingeführt. Der festgelegte Kern ist agentengesteuertes Debugging unterstützter Anwendungen. Browser-/Systemadapter erweitern ihn bei entsprechendem Auftrag.
- Die früher verdichteten N1–N9 ersetzen nicht I1–I18 oder D1–D6. Die verbindlichen Anforderungen sind hier ausdrücklich zugeordnet.
- TypeSpec umfasst die benötigten internen Modelle und Abbildungen ebenso wie die Tool-Nachrichten. `kind` ist keine globale Umbenennungsregel für OTLP oder gespeicherte Nutzdaten.
- Byteidentität ist eine eigene kanonische Ausgabe-/Replay-Zusage, nicht der allgemeine sprachübergreifende Roundtrip-Vertrag.
- Empfangsaktivität, Collector-Verarbeitungsgrenze und vollständige Producer-Erfassung werden getrennt. Eine stille oder unbekannte Quelle wird nicht als gesunde Anwendung interpretiert.
- Rust-Verweise aus älteren Ständen und frühere Single-Binary-/C#-MCP-Annahmen begründen keine zusätzlichen Nami-Komponenten.

Die ursprüngliche Reviewfassung bezeichnete `e0dd040` einschließlich nicht committierter Änderungen als ihren lokalen Vergleich. Dieser Herkunftshinweis wird nicht zu einem neuen aktuellen Quellbefund umgedeutet. Die angehängten Dateien enthalten keine vollständigen aktuellen Produktquellen oder Buildartefakte; der private Live-Stand konnte für diese Dokumentkorrektur nicht gelesen werden. Die hier vorgenommenen Änderungen betreffen Zielauslegung und technische Aussagen.

Ebenso bleiben die dort genannten Polling-/stderr-Timeout-Stellen historische Quelltexthinweise. Ohne die betreffenden aktuellen Quellen werden weder ihr Fortbestehen noch ihre Behebung behauptet. Frühere Testzahlen und Stations-/Commit-Listen sind keine neuen Ergebnisse und bestimmen den Produktumfang nicht.

## 9. Ausführung und noch zu konkretisierende Details

Die gemeinsame Infrastruktur gehört zu `Nami.Tests`; Unit, Integration und EndToEnd unterscheiden sich nach ihrer tatsächlichen Aufgabe. Die NativeAOT-Zusage betrifft die veröffentlichte Produkt-Engine. Compiler-/Metadatenhilfen können im dafür geeigneten verwalteten Harness laufen. MSTest/MTP und die vorhandenen Repository-Einstiege bleiben maßgeblich.

Lifecycle-Hilfen stellen isolierte Konfiguration, Identitäten, Store und bereite Empfänger her. Eine kontrollierte Operation wird bis zur konkreten Abschlussbedingung unter einer gemeinsamen Deadline ausgeführt; anschließend werden die ausstehenden Daten und Prozesse geordnet abgeschlossen. Die Deadline begrenzt auch einzelne wartende Requests und Diagnose-Drains. Feste Sleeps ersetzen diese Abschlussbedingung nicht.

Bei der weiteren Umsetzung werden nur tatsächlich noch offene Details konkretisiert: das aufgelöste Instrumentierungsmanifest samt Ausschlüssen, die ausgeführten Modellabbildungen, die konfigurierten Zeitregeln, die Verlust-/Annahmesemantik, der genaue Scope der Freshness-Felder und die Auslieferung je Host. Collector-Eigentum, C#-/TypeScript-Aufteilung, TypeSpec/Weaver-Zuständigkeiten und die genehmigte Instrumentierung werden dabei nicht erneut zur Entscheidung gestellt.

## 10. Grundlagen

Zielgrundlagen sind die drei bereitgestellten Dateien und die aktuelle ausdrückliche Nutzerentscheidung. Technische Mechanismen wurden gegen folgende Primärquellen eingeordnet; diese Quellen belegen nicht den Implementierungsstatus von Nami:

- [MCP 2026-07-28: Statelessness](https://modelcontextprotocol.io/specification/2026-07-28/basic#statelessness).
- [Roslyn: Interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md) und [Microsoft: Configuration Source Generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator).
- [Microsoft: NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
- [TypeSpec: Discriminated Types](https://typespec.io/docs/standard-library/discriminated-types/) und [System.Text.Json: Union Types](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/union-types).
- [OTel Trace SDK](https://opentelemetry.io/docs/specs/otel/trace/sdk/), [Metrikdatenmodell](https://opentelemetry.io/docs/specs/otel/metrics/data-model/) und [OTLP](https://opentelemetry.io/docs/specs/otlp/).

Diese Fassung beschreibt den korrigierten Reviewmaßstab; der tatsächliche Implementierungsstand wird an seinen aktuellen Quellen festgestellt.
