# Auftrag und Arbeitsstand: TypeScript-MCP und schlanke generierte Engine

## Ziel

Die sechs nami-Tools durch einen aus qyl abgeleiteten TypeScript-7/Bun-MCP-Host bereitstellen,
C#/.NET 11 NativeAOT über einen gemeinsamen TypeSpec-Vertrag und private Streams anbinden,
DuckDB-/Collector-Generierung konsistent neu erzeugen und ungenutzte handgepflegte Modelle,
Projektionen und Durchreichschichten entfernen. Vorhandene Dateien zusammenführen/ersetzen,
statt parallele Implementierungen anzuhängen. Weniger handgeschriebener Produktionscode als
zu Beginn, bei erhaltenen Daten-, Sicherheits- und Toolverträgen.

## Verbindliche Leitplanken

- Keine neuen Tests schreiben. Vorhandene Tests ausführen; notwendige Anpassungen bestehender
  Runner an die neue Prozessarchitektur sind keine Erlaubnis für zusätzliche Testfälle.
- Generierte Dateien ausschließlich über ihre Quellen/Generatoren ändern.
- qyl ist eine lesende Referenz; gearbeitet wird ausschließlich im nami-Repository.
- Die vom Nutzer genannten ungenutzten Member anhand realer Bindungen, Serializer- und
  Generatorverwendungen prüfen. Genuin ungenutzte Flächen löschen, keine Warnungen verstecken.
- Agentenoberfläche ausschließlich MCP; C# bleibt Engine. Kein Dashboard, keine zusätzliche
  öffentliche REST-Analyse-API. Private IPC erhält Projektbindung, Vertrag und Cancellation.
- Vorhandene lokale Änderungen erhalten. Der Ausgangsdiff ist zusätzlich gesichert unter
  `/tmp/nami-refactor-baseline-20261024/tracked.patch`; Quellzeilen/Hashes stehen daneben.
- Vor Push: `./eng/build.sh Check --Configuration Release`. Inspektion getrennt vom Gate und
  vollständiger IDE-Analyse; maximal zwei Tool-/Build-Worker.

## Arbeitsstand

- Ausgangscommit: `605db5e` (korrigierte PRD und Dokumentation).
- Bereits vorhandene lokale Änderungen sind noch nicht von dieser Arbeit erzeugt; keine
  davon wurde verworfen oder als eigener Fortschritt verbucht.
- Ausgangsmessung: 21357 Zeilen in 161 erfassten Quell-Dateien (Produktions-/Toolquellen gesondert auswerten;
  generierte Ausgaben nicht durch Minifizierung als Verbesserung zählen).
- [x] Auftrag und Ziel gespeichert; Ausgangsdiff und Dateimanifest gesichert.
- [x] Bestehende Warnungen, Generatorgrenzen und die sechs Toolpfade abgleichen.
- [x] Vertrag und TypeScript-Artefakte aus TypeSpec erzeugen; qyl-MCP auf sechs Tools reduzieren.
- [x] C#-Engine und TypeScript über private Streams verbinden; alten C#-MCP-Pfad ersetzen.
- [x] Ungenutzte Storage-/Ingestionsmodelle und handgepflegte Duplikate entfernen; neu generieren.
- [x] Build, bestehende Tests, NativeAOT und vollständige Inspektion prüfen; Codeumfang vergleichen.
- [x] Integrationsentscheidungen/Restpunkte dokumentieren und den geprüften Stand für Commit/Push vorbereiten.

## Nutzerauftrag (Wortlaut; Zeilenumbrüche der Diagnosen vereinheitlicht)

kopier auch the für die 6 tools notige qyl mcp parts korrigiere die contracts generier die duckdb collector neu

Auto-property accessor 'SchemaUrl.get' is never used
Auto-property accessor 'HasConflict.get' is never used
Auto-property accessor 'ProjectionUnavailableReason.get' is never used
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.MetricName' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.MetricKind' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.Temporality' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.IsMonotonic' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.Unit' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.Description' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.SeriesCount' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricCatalogEntry.LastSeenUnixNano' is never accessed (except in implicit Equals/ToString implementations)
Auto-property accessor 'InstrumentId.get' is never used
Auto-property accessor 'ServiceName.get' is never used
Auto-property accessor 'ResourceJson.get' is never used
Auto-property accessor 'ResourceEntityRefsJson.get' is never used
Auto-property accessor 'ResourceSchemaUrl.get' is never used
Auto-property accessor 'ResourceDroppedAttributesCount.get' is never used
Auto-property accessor 'ScopeName.get' is never used
Auto-property accessor 'ScopeVersion.get' is never used
Auto-property accessor 'ScopeSchemaUrl.get' is never used
Auto-property accessor 'ScopeAttributesJson.get' is never used
Auto-property accessor 'ScopeDroppedAttributesCount.get' is never used
Auto-property accessor 'Variants.get' is never used
Positional property 'Nami.Collector.Storage.MetricDescriptorVariant.Services' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricAggregatePoint.BucketStartUnixNano' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricAggregatePoint.Value' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricAggregatePoint.PointCount' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricQuerySeries.AttributesJson' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.MetricQuerySeries.Points' is never accessed (except in implicit Equals/ToString implementations)
Auto-property accessor 'ExemplarsJson.get' is never used
Auto-property accessor 'Cursor.init' is never used
Auto-property accessor 'StartUnixNano.init' is never used
Auto-property accessor 'EndUnixNano.init' is never used
Auto-property accessor 'ServiceName.init' is never used
Auto-property accessor 'ErrorsOnly.init' is never used
Auto-property accessor 'SessionId.init' is never used
Positional property 'Nami.Collector.Storage.TraceStoragePage.Items' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.TraceStoragePage.HasMore' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.TraceStoragePageItem.TraceId' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.TraceStoragePageItem.ActivityUnixNano' is never accessed (except in implicit Equals/ToString implementations)
Positional property 'Nami.Collector.Storage.TraceStoragePageItem.Spans' is never accessed (except in implicit Equals/ToString implementations)
Record 'TracePageQuery' is never instantiated
Auto-property accessor 'SessionId.get' is never used
Auto-property accessor 'StartTime.get' is never used
Auto-property accessor 'LastActivity.get' is never used
Auto-property accessor 'LastActivityUnixNano.get' is never used
Auto-property accessor 'DurationMs.get' is never used
Auto-property accessor 'SpanCount.get' is never used
Auto-property accessor 'TraceCount.get' is never used
Auto-property accessor 'ErrorCount.get' is never used
Auto-property accessor 'InputTokens.get' is never used
Auto-property accessor 'OutputTokens.get' is never used
Auto-property accessor 'GenAiRequestCount.get' is never used
Auto-property accessor 'Providers.get' is never used
Auto-property accessor 'Models.get' is never used
Auto-property accessor 'Services.get' is never used
Auto-property accessor 'AllowedOrigins.set' is never used
Auto-property accessor 'AllowedHeaders.set' is never used
Auto-property accessor 'InstrumentMetadataJson.get' is never used
Auto-property accessor 'HistoricalExemplarTraceIdsJson.get' is never used
Auto-property accessor 'ResourceDroppedAttributesCount.get' is never used
Auto-property accessor 'ScopeDroppedAttributesCount.get' is never used
Auto-property accessor 'StartUnixNano.init' is never used

alles was hangepflegt mit generated ersetzt werden kann löschen moderne language features nutzen lad die dotnet 11 skills schreib keine tests entferne das src probelamtisch ist und ersetze indem du .cs files mergest fusinierst mutatest statt sie appendest denk an functionale wege wie du ingestion, collector eine freude machen kannst halte dich an qyl als minimum jedoch nicht die hangepflegte scheiße sondern die verbindung von c# und typescript 7 du hast sogesehen keine vorgaben bis auf das qualität maximum mit weniger code als davor aufhören als 2te ich geh jetzt einkaufen also speicher das prompt und dein ziel in ein markdown file danach bin ich mit dir am iterien bis alles gelöscht ist was die saubere architectur von dem überhaupt ermöglicht hilf mir so gut dir möglich ich bin nicht gott nur deine idee mit mir ist was zielfführend ist.

Auto-property accessor 'EndUnixNano.init' is never used
Auto-property accessor 'Cursor.init' is never used
Positional property 'Nami.Collector.Storage.MetricObservationPageItem.HasConflict' is never accessed (except in implicit Equals/ToString implementations)
Record 'MetricPointCursor' is never instantiated

## Laufende Änderungen

- Tote Session-/Trace-Paging- und Metrik-Analyseoberflächen entfernt; Journal, Retention,
  OTLP-Felder und die bestehenden rohen Metriknachweise bleiben erhalten.
- qyl-TypeScript-Emitter übernommen und Inline-Union-Discriminator ergänzt; Toolmanifest,
  Konfigurationsnamen und private IPC-Grenzen werden generiert.
- TypeScript-MCP, privater JSONL-Transport und native Engine sind verdrahtet; vorhandener
  Test-Harness startet jetzt diese Kombination. Es wurden keine Testfälle hinzugefügt.
- DuckDB.NET 1.5.6.1 wiederhergestellt und im nativen E2E-Lauf geprüft (native Engine weiterhin 1.5.6).

## Zusatzauftrag: Zeitkorrekturen

`~/Downloads/nami-time-fixes.zip` wurde gelesen; der enthaltene Patch wurde per
`git apply --check` gegen den laufenden Stand geprüft und danach angewendet. SHA-256:
`54ae5d2a6fc0604c118655a493276c8759e5cfcf260936b78fc32a7c575e1ec7`.

Übernommen: maximale Retentionspause 71.582 Minuten, vor der Unix-Epoche begrenzter
Retentionsstichtag ohne AddDays-Überlauf, UtcTicks, monotone bestehende Polling-Schleifen
und durchgereichte Health-Deadline mit unterscheidbarer Caller-Cancellation. Im neuen
stdio-Pfad ersetzt bereits der Engine-/MCP-Handshake das alte HTTP-Health-Polling; der
verbesserte HTTP-Wartepfad bleibt nur für den HTTP-Host. Keine neuen Testfälle angelegt.

## Befundquelle

`inspectcode.sarif` (24.10.2026, 08:35) ist der vom Nutzer gelieferte Vorher-Bericht:
217 Befunde, erfolgreicher InspectCode-Lauf. Er ist keine Bewertung des späteren Umbaus.
Alte Session-/Paging-/Metrik-Kataloge, TypedToolRunner, ToolClock, Rückgabehüllen und
nicht verwendete Retentionsausgaben werden entfernt; erforderliche persistierte Felder
werden an tatsächlichen Generator-/Serializer-Bindungen geprüft. Ein neuer vollständiger
Lauf nach Build/Tests dient als Nachher-Nachweis.

Der erste vollständige E2E-Lauf wurde beim Prozessabbau gestoppt und zählt nicht als Nachweis.
Ein durch `Process.Kill(true)` angehaltener Bun-Prozess wurde beobachtet. Der Harness beendet
jetzt über stdin, wartet begrenzt und erzwingt nur bei Bedarf den einzelnen Supervisor-Abbruch;
der Supervisor schließt seine Engine selbst. Dieser Pfad wird erneut ausgeführt.

Der nächste E2E-Lauf: 34/38 bestanden. Drei Altprüfungen setzten JSON-Escape-Schreibweise
mit Inhalt gleich (`+` vs. `\u002B`). Sie prüfen nun das vollständige JSON per DeepEquals,
wie PRD S3/I8 es sprachübergreifend verlangt. Keine Felder/Werte werden weggelassen.
Der vierte Befund betrifft ausschließlich sechs Toolbeschreibungen: die geprüfte Golden-Datei
übernimmt die schon vorhandenen TypeSpec-`@doc`-Texte statt der früheren separaten C#-Tabelle.
Schemas, Namen, Annotationen und Cache-Angaben waren unverändert.

## Checkpoint: Release-Gate bestanden

Der Prozessabbau erfolgt über geordnetes stdin-Ende mit begrenzten Terminate/Kill-Stufen.
Nach den vier beschriebenen Korrekturen bestanden alle 38 vorhandenen E2E-Fälle. Der anschließende
vollständige Release-Check bestand mit 26 Unit-, 124 Integration- und 38 EndToEnd-Fällen;
kein Fehler, kein übersprungener Fall. Auch NativeAOT-Publish, Registry und Contract-Parität
bestanden. Ergebnis: `artifacts/test-results/run.ei1uqy96/summary.json`,
Log: `/tmp/nami-refactor-baseline-20261024/check.log`.

Verbleibende Publish-Diagnosen: DuckDB IL2104/IL3053 und Hinweise der lokalen Apple-Toolchain.
Die Containerdefinition ist aktualisiert, aber mangels lokaler Docker-CLI nicht gebaut.
Vollständige Instrumentierungsnachweise und echte Claude.ai-/OpenAI-Hostintegration bleiben
eigenständige offene Aufgaben in der Checkliste.

Erfasster handgepflegter Quelltext: **16.834 -> 15.844 Zeilen (-990)**. Gezählt werden C#, TS,
MJS und TypeSpec in `src/`, `internal/`, `contracts/`, `eng/`, einschließlich Generatoren;
ohne generierte Artefakte, Abhängigkeiten und Testquellen. Ausgangspunkt ist der gesicherte
Arbeitsbaum, nicht HEAD. Die Zahl ist kein Leistungsnachweis.

Neue vollständige Inspektion: 327 Hinweise insgesamt (0 Fehler, 29 Warnungen, 298 Notizen).
Sie aktiviert auch Compiler-/Analyzer-Stilhinweise und ist deshalb nicht als einfache Differenz
zu den 217 alten ReSharper-Befunden zu interpretieren. Die alten Session-/Paging-/Metrik-Katalog-
Flächen sind entfernt. Die konkreten 29 Warnungen wurden abgearbeitet; erneute Prüfung folgt.

Fünf GeneratedRegex-Deklarationen konnten mit NonBacktracking nur einen Wrapper erzeugen.
Die gleichen gecachten Regex-Instanzen werden jetzt explizit mit unverändertem NonBacktracking,
Timeout und Eingabelimit erstellt; lineare Sanitizer-Laufzeit wird nicht gegen einen kosmetisch
grünen Generator eingetauscht. Engine-Ressourcen besitzen einen expliziten asynchronen Owner,
statt von lokalen Funktionen über einen äußeren Dispose-Scope erfasst zu werden.

Der abschließende vollständige InspectCode-Lauf `artifacts/inspection/run.BTIHNO/inspectcode.sarif`
ist erfolgreich und vollständig: **0 Fehler, 0 Warnungen, 269 Notizen**. Der Wrapper liefert wegen
der Notizen Exitcode 1; dieser wird nicht als sauberer Lauf umgedeutet. Enthalten sind unter
anderem Stil-/API-Vorschläge, SQL-Analysehinweise sowie vier zentrale Konfigurationskonstanten,
die in generiertem TypeScript verwendet werden. Keine pauschalen Unterdrückungen ergänzt.
Der ursprüngliche `inspectcode.sarif` bleibt als Vorher-Bericht unverändert.

## Weitere Nutzerpräzisierung: Modelle und Formate

Fehlende Werkzeuge dürfen installiert werden. TypeSpec bleibt das formatneutrale Modell;
JSON/YAML/Protobuf-Ausgaben werden nur für tatsächliche Verbraucher erzeugt. Gemeinsame
Typquellen sind keine handgepflegten Parallelverträge. Idempotenz der Tools, deterministische
Generierung und semantisch verlustfreie Serialisierung sind getrennte Anforderungen.

Installiert und auf npm aktuell sind compiler/json-schema/openapi3 1.17.0. Der direkte JSON-Schema-
Emitter verwendet weiterhin die Modell-Property-Namen statt JSON-encodedName, weshalb sein
Rohoutput nicht ungeprüft den bisherigen Wire-Vertrag ersetzen darf. Ein Probelauf mit union-extends zeigte: Der Compiler fordert dafür einen zusätzlich benannten
Basistyp; ein Inline-Modell wird abgewiesen. Da die vorhandenen diskriminierten Varianten diese
Form bereits festlegen, wurde keine zusätzliche ungenutzte Modellklasse dafür eingeführt. Kein zusätzliches
YAML-/Protobuf-Artefakt ohne Verbraucher, keine pauschale Aktivierung experimenteller Syntax.

Terminologie: Mit Vertrag ist ausschließlich das technische Datenschema in TypeSpec gemeint:
Tool-Eingaben/-Ergebnisse sowie private Engine-Nachrichten. Es ist weder ein zusätzlicher
Transport noch eine zweite manuell gepflegte Modelldefinition. NativeAOT-Serialisierung und
JSON-Schema-Validierung erfüllen unterschiedliche Aufgaben; weitere Formate werden nicht
vorsorglich zusätzlich erzeugt.

Weitere Reduktion: Das TypeScript-Laufzeitbundle erhält nur die tatsächlich von MCP gelesenen
Input-/Output-Schemas und ToolReadInput-Metadaten. Die native Validierung und die beiden
Typsysteme werden aus derselben Quelle erzeugt; ihre benötigten Artefakte werden nicht mit
unabhängig gepflegten Modellen verwechselt. Persistenzmodelle behalten ihre eigene stabile
Speicherrepräsentation, damit ein Wire-Formatwechsel keine gespeicherten IDs oder Bytes ändert.

## Nutzerpräzisierung: Modellnamen, JSON-Namen und Versionierung

Der TypeScript-Emitter erhält jetzt die TypeSpec-Eigenschaftsnamen in `api.ts` und erzeugt die
JSON-Namen separat in `json.ts`. Die aus demselben Modell erzeugten `toJson`-/`fromJson`-Codecs
setzen `@encodedName` auch in verschachtelten Modellen, Arrays, Maps und diskriminierten Unions
um. Der private Engine-Transport verwendet sie tatsächlich. Die MCP-Durchreichung bleibt beim
JSON-Typ, wenn kein dekodiertes Anwendungsmodell gebraucht wird. Die öffentlichen zwölf
Schemas und die semantische Revision bleiben unverändert.

Preview-Prüfung außerhalb des Repositories: compiler `1.18.0-dev.8`, json-schema `1.18.0-dev.1`,
versioning `0.88.0-dev.2` wurden heruntergeladen. Auch der geprüfte JSON-Schema-Preview-Emitter
wertet weder `resolveEncodedName` noch `getVersioningMutators` aus. Kein ungeprüfter Paketwechsel,
keine doppelte handgepflegte Zuordnung und keine erfundene v1/v2-Historie. Aktuell gibt es eine
unversionierte Schemaansicht. Eine künftig gewählte Versionsansicht muss vor allen Emittern
konsistent aufgelöst werden; diese zusätzliche Versionierung ist noch nicht implementiert.

Direkte Ausführung der generierten Codecs bestätigt `maxBytes -> max_bytes -> maxBytes`,
Histogrammfelder und verschachtelte Attribut-Unions mit einer Ganzzahl über 2^53 als String.
Keine neuen Testfälle oder Testdateien hinzugefügt. Nach dieser Korrektur wird das Release-Gate
erneut gegen den exakten Commit-Kandidaten ausgeführt; frühere grüne Läufe belegen nur ihren Stand.

## Offline-Checkpoint vor möglichem Internetausfall

Der vollständige aktuelle Diff, der vorgemerkte Commit-Diff und die erhaltenen fremden
Arbeitsbaumänderungen sind separat als Binärpatches gespeichert:
`/tmp/nami-refactor-baseline-20261024/current-all.patch`, `current-staged.patch`,
`preserved-unstaged.patch`. `resume.json` daneben enthält HEAD, Kandidaten-Tree,
Prüfpfad und nächste Schritte. Eine dauerhafte Kopie liegt in `.git/nami-checkpoint-20261024/`;
der geprüfte Kandidat ist zusätzlich unter `refs/nami/checkpoints/20261024-naming` gesichert. Die Abhängigkeiten und die geprüften Preview-Pakete
liegen bereits lokal vor; die Preview-Pakete bleiben außerhalb des Repositories.

Letzte Nutzervorgaben: Modellnamen und JSON-Namen aus derselben TypeSpec-Eigenschaft
mit `@encodedName` ableiten; benötigte TS-Konvertierung generieren; mögliche
Versionsansichten ausdrücklich über `getVersioningMutators` auflösen und nicht allein
aus `@versioned` auf Unterstützung aller Emitter schließen. Keine neuen Tests.

Stand: Codecs generiert, im Engine-Transport eingebunden, TypeScript-Check bestanden.
Der isolierte Kandidat `72148c2` bestand `Check --Configuration Release` nach der
Namenskorrektur mit 188/188 Fällen. Seine vollständige Inspektion fand 13 überflüssige
Imports, die im Arbeitsbaum schon entfernt, im isolierten Commit aber noch vorhanden waren.
Diese Zeilen wurden gezielt übernommen, ohne die übrige lokale Formatierung zu übernehmen.
Kandidat `0999be5` wird abschließend geprüft. Danach Ergebnisdokumentation und Commit. Falls
das Netz fehlt, bleibt der geprüfte Commit lokal bis zum späteren Push.

## Abschlussprüfung des Commit-Kandidaten

Kandidat `0999be5` bestand den vollständigen Release-Check: **26 Unit + 124 Integration +
38 EndToEnd = 188 bestanden, 0 fehlgeschlagen, 0 übersprungen**. NativeAOT-Publish,
TypeScript-Build, Registry und deterministische Generierung bestanden ebenfalls.
Ergebnis: `/tmp/nami-ts-mcp-release-check/artifacts/test-results/run.phszsy_b/summary.json`.

Die anschließend wiederholte vollständige Inspektion ist erfolgreich und vollständig:
**0 Fehler, 0 Warnungen, 262 Notizen**. Der Wrapper gibt wegen der Notizen weiterhin 1 zurück.
Bericht: `/tmp/nami-ts-mcp-release-check/artifacts/inspection/run.dq0KQe/inspectcode.sarif`.
Keine Unterdrückungen und keine neuen Testfälle wurden ergänzt. Nach diesem geprüften
Quellstand ändern sich nur die Abschlussnotizen. Bekannte NativeAOT-/Apple-Diagnosen sowie
offene Schema-Versionierung, Instrumentierungsnachweise und reale Connector-Auslieferung
bleiben ausdrücklich offen. Die Berichtskopien liegen dauerhaft im lokalen Git-Checkpoint.
