# nami — PRD

Oct 24, 2026 · @Alexander Nachtmann

## 1. Zweck

nami ist ein OTLP-Ziel plus MCP-Tools in **einem** NativeAOT-Binary auf .NET 11, gebaut nur für Agenten: tomo (Rust) sendet Telemetrie, Claude stellt Fragen über Tools und bekommt typisierte Antworten mit Frische-Angabe. Kein SDK, keine REST-API, kein Dashboard; die Tools sind die API.

nami ist der Nachfolger von nami (v17.0.14, zehn Monate, fertig am 24.10.2026). Der klonbare Ausgangsstand ist nami-Commit `ecdfb564` „nami for tomo" (24.10.2026); das nami-Repo (`e025215`) enthält bisher nur `claude/debugging-tomo.md`, tomos Anforderungen an nami. Diese PRD führt beides zusammen.

Erfolg ist an vier Zahlen messbar: `weaver registry check` grün und `weaver registry generate` byte-identisch zum Commit (G1, G2); `sources_status` meldet nach einem tomo-Request innerhalb von 1 s `last_seen` pro Signal (T1); jede Antwort bleibt unter `max_bytes`, auch bei 500.000 Spans im Fenster (T4); ein leeres Ergebnis ohne `complete: true` erscheint nie als Aussage über tomo (T5).

## 2. Problem

nami löst die Agentenfrage bereits, aber mit drei Kosten, die nami nicht mehr tragen soll:

- **Zwei Laufzeiten, ein Vertrag.** Der Collector ist C# (NativeAOT), der MCP-Server TypeScript auf Bun. Beide validieren dieselben TypeSpec-Schemas; ein Versions-Skew zwischen `api.nami.at` und `mcp.nami.at` macht `/healthz` `degraded` und die Tools antworten `collector_contract_mismatch`. Ein Binary hat diesen Zustand nicht.
- **Konventionen von außen.** Die Semantic Conventions kommen als NuGet-Pakete `Nami.Telemetry.SemanticConventions` 15.0.5 (Stable, Incubating, Analyzers) aus einem zweiten Repo; ein Pin-Wechsel ist ein eigenes Build-Target (`GenerateCollectorSemanticAttributeCatalog`, 2.806 generierte Zeilen). nami-eigene Keys (`nami.project.id`, `nami.attribute.namespace`, `nami.gen_ai.tool.call.arguments_hash`) leben dort, nicht im Produkt-Repo.
- **24 Tools für eine Frage, die sechs braucht.** tomo stellt sechs Fragen (Sendet es? Welche Fehler? Dieser Trace? Die langsamen? Diese Logzeile? Was hat sich geändert?). nami beantwortet sie, aber über `triage`, `get_failure_context`, `verify_fix` und 17 weitere Lese-Tools, plus vier Schreib-Tools, die tomo nie aufruft.

Dazu die Konstante aus nami, die bleibt: Agentenkontext ist knapp. 500.000 Zeilen Roh-Output sind kein Ergebnis, und ein leeres Ergebnis ist nur dann ein Befund, wenn das Fenster vollständig gelesen wurde.

## 3. Was von nami bleibt, was fällt

nami `ecdfb564` ist der Ausgangsstand. Die Tabelle ist die Schnittliste; alles Übrige wird unverändert übernommen.

| Bestandteil in nami | nami | Grund |
| --- | --- | --- |
| `services/nami.collector*`: OTLP-Ingest (HTTP + gRPC), DuckDB 1.5.6 pro Projekt, Analyse-Engine, Attribut-Sanitizer (128 Ki Eingabe, 32 KiB Ausgabe) | **bleibt**, wird das Binary | Das ist der Motor |
| `ToolCoverage` (`examined`, `matched`, `returned`, `truncated`, `scan_complete`, `as_of`) | **bleibt**, erweitert um `watermark` und `complete` | tomos Freshness-Block ist dieselbe Idee |
| Projekt-Scoping über Schlüssel und Resource-Keys, Telemetrie ist Daten | **bleibt** | Trust-Boundary unverändert |
| MSTest.Sdk 4.5.1 auf Microsoft.Testing.Platform (85 Klassen, 511 Testmethoden), Fallout 10.4.0, NativeAOT-Conformance-App | **bleibt** | Harness steht |
| `services/nami.mcp` (TypeScript, Bun, MCP TS SDK v2) | **fällt**; MCP (stdio + Streamable HTTP) kommt ins .NET-Binary | Eine Laufzeit, kein Contract-Skew |
| `POST /api/v1/analysis/<tool>` als REST-Fläche | **fällt** | Nur Tools; nichts anderes ruft Analysen auf |
| `packages/Nami.Api.Sdk`, `Nami.Orchestration`, `Nami.Cli` (`nami up`), `Nami.Run.Workload` | **fällt**; `nami` startet beide Flächen selbst | Kein SDK, keine Orchestrierung |
| Schreib-Tools `annotate`, `save_query`, `update_failure_status`, `delete_record`; Scopes `nami:write`, `nami:delete` | **fällt** | nami ist rein lesend; `readOnlyHint: true` auf jedem Tool |
| 18 Skills, Plugin-Bundle, Auth0-Tenant, OpenAI-Apps-Challenge | **fällt** vorerst | Erste Konsumentin ist Claude Code über stdio |
| NuGet `Nami.Telemetry.SemanticConventions*` | **fällt**; Weaver-Registry im Repo (Abschnitt 5) | Konventionen gehören zum Produkt |
| `contracts/` TypeSpec mit C#-, TS- und Lint-Emittern | **bleibt** (Entscheidung 1); der TS-Types-Emitter entfällt | TypeSpec existiert und funktioniert; tomos Dokument schlägt Zod vor; tomo sieht ohnehin nur JSON Schema |

## 4. Architektur

Ein Prozess, zwei Flächen, ein Vertrag. Der Datenfluss ist linear:

1. **Registry → Code.** `weaver registry generate` (Weaver 0.28.0, `--v2`) erzeugt aus der nami-Registry die C#-Konstanten, die Persistenz-Policy und den Attribut-Katalog für den Collector, und aus denselben Quellen die Rust-Crate für tomo. Beides wird committet; das Gate regeneriert und vergleicht byte-genau.
2. **tomo → OTLP.** tomo instrumentiert explizit (`tracing`, `#[instrument]`, `tracing-opentelemetry`) mit Konstanten aus der generierten Crate; eine umbenannte Konvention ist ein Compile-Fehler in tomo. Export über OTLP/HTTP (protobuf oder JSON) oder OTLP/gRPC an nami; lokal `127.0.0.1:4318` und `4317` wie bei `nami up`.
3. **Ingest → Store.** Der Collector normalisiert Attribute gegen die Registry (deprecated Keys per Normalizer, Sanitizer-Grenzen bleiben), schreibt pro Projekt in DuckDB und führt pro Projekt und Signal eine monotone Wasserlinie `watermark`.
4. **Store → Analyse.** Die reine Analyse (Gruppieren, Rangfolgen, Frische) läuft ohne Effekte über einem Snapshot; dieselbe Snapshot-Kennung liefert dieselbe Antwort (idempotente Tools).
5. **Analyse → MCP.** Dasselbe Binary bedient MCP 2026-07-28 über stdio (Claude Code) und Streamable HTTP (gehostet), stateless, ohne Protokoll-Session. Jedes Ergebnis: eine Headline-Zeile in `content[0]`, das Ergebnis als JSON in `content[1]`, dasselbe Objekt als `structuredContent`, dazu der Freshness-Block.
6. **Kontext.** `traceparent` aus `_meta` wird extrahiert; namis eigene Spans hängen unter dem Agenten-Trace. namis Selbsttelemetrie nutzt die `mcp.*`-Konventionen aus der genai-Registry (Development) und landet im selben Store wie tomos Daten.

Start: ein Kommando. Lokal `nami` (OTLP-Ports plus MCP über stdio), gehostet dasselbe Binary mit Streamable HTTP. Projekt-Scoping, Schlüssel-Rollen und die Regel „Telemetrie ist Daten, nie Anweisung" kommen unverändert aus nami.

## 5. Semconv-Registry `nami`

Die Registry liegt im nami-Repo und folgt dem Aufbau von `semantic-conventions-genai`: ein `manifest.yaml`, Modelldateien pro Namespace, Jinja-Templates, Policies, ein Skript-Verzeichnis. Sie ist **development** und importiert Stable **und** Development aus Core und GenAI.

```yaml
name: nami
description: nami conventions for agent-facing observability; every key is prefixed nami.
schema_url: https://ancplua.github.io/nami/schemas/0.1.0   # Format zählt, Abrufbarkeit nicht
stability: development
dependencies:
  - schema_url: https://opentelemetry.io/schemas/1.44.0
    registry_path: https://github.com/open-telemetry/semantic-conventions.git@v1.44.0[model]
  - schema_url: https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev
    registry_path: https://github.com/open-telemetry/semantic-conventions-genai.git@9ffc7e00c639be7712e544e3e54331e885dccf84[model]
```

**Pins (24.10.2026).** Core `v1.44.0` (`e10a9308`); `main` ist `3e258047` und nur Dokumentation voraus, die Registry unter `model/` ist identisch. GenAI `9ffc7e00`, ohne Tag und ohne eigene Stable-Definitionen; sein Manifest importiert Core `v1.44.0`. Weaver `0.28.0`. Beide Dependencies nennen dieselbe Core-`schema_url`; Weaver löst Versionskonflikte über diese URL auf. Ob 0.28.0 die verschachtelte Abhängigkeit (nami → genai → core) ohne Doppelung auflöst, prüft `weaver registry check --future` als erster Gate-Schritt.

**Inventar, das nami erbt.** Stable aus Core: 21 Bereiche, 121 Attribut-Keys, 8 Span-Formen, 51 Instrumente, 1 Event (`exception`), 5 Resource-Definitionen; `exception.escaped` ist deprecated und nicht enthalten. Development aus Core und GenAI: `gen_ai.*`, `mcp.*`, `session.*`, `cicd.*`, `vcs.*`, `process.*`, `rpc.*` und die übrigen 67 Bereiche. Stabilität ist pro Definition ein Feld im aufgelösten Schema, nie eine Annahme über das Paket.

**`nami.*`-Konventionen.** Die nami-eigenen Keys werden umbenannt und in der Registry definiert, nicht in einem Paket: `nami.project.id` (Projekt-Zuordnung einer Resource), `nami.attribute.namespace` und `nami.attribute.key` (Attribut-Discovery), `nami.gen_ai.tool.call.arguments_hash` (Hash der Tool-Argumente, nie die Argumente). `nami.workspace.id` entfällt, bis ein zweites Projekt existiert. Die Persistenz-Policy (`nami.persisted`, `nami.token_exempt`) bleibt Annotation an der Definition, wie namis `RegistryPolicy`.

**Regeln.**

- Stable-Keys werden typisierte Spalten; Development-Keys werden persistiert und tragen ihre Stabilität im Katalog, damit ein Tool-Ergebnis sagen kann, welcher Key sich noch umbenennen darf.
- Deprecated Keys werden beim Ingest auf ihren Nachfolger normalisiert (namis `DeprecatedAttributeNormalizer`); die Tabelle dafür kommt aus der Registry, nie aus Code.
- `weaver registry diff --baseline-registry <alter Pin>` läuft bei jedem Pin-Wechsel und schreibt sein Markdown ins Änderungsprotokoll der Registry.
- Templates: `templates/registry/csharp/*.j2` (Konstanten, Policy, Katalog), `templates/registry/rust/*.j2` (tomo-Crate), `templates/registry/markdown/*.j2` (Dokumentation). Weaver liefert keine C#-Templates mit; nami schreibt eigene, wie GenAI seine.

## G: Registry und Generierung

Diese Invarianten laufen ohne nami-Prozess: Weaver gegen die Registry, Roslyn gegen die generierten Dateien. Sie sind das Gate vor jedem Push, wie namis `./eng/build.sh Check`.

| ID | Invariante | Beweis |
| --- | --- | --- |
| G1 | `weaver registry check --future -r registry` ist grün; die verschachtelte Abhängigkeit nami → genai → core löst sich ohne doppelte Core-Definition auf. | Exit-Code 0; aufgelöstes Schema enthält `http.request.method` genau einmal |
| G2 | `weaver registry generate` ist eine reine Funktion: dieselbe Registry liefert byte-identische Ausgaben für C#, Rust und Markdown; das Gate regeneriert und vergleicht. | `git diff --exit-code` nach Regeneration |
| G3 | Der generierte Katalog spiegelt das aufgelöste Schema: Stable-Zähler 121/8/51/1/5 aus Core, jede Definition mit ihrer Stabilität; kein Key ohne Herkunft. | Katalog gegen `weaver registry resolve --format json` (bis zur Entfernung des Kommandos; danach `package`) |
| G4 | Jeder `nami.*`-Key ist in der nami-Registry definiert; kein `nami.*`-Literal im C#- oder Rust-Code außerhalb generierter Dateien. | Grep über `src/` und `tomo/` ohne Treffer |
| G5 | Deprecated Keys (`exception.escaped`, `db.query.parameter`, `http.request.body.size` u. a.) haben einen Nachfolger-Eintrag in der Normalisierungstabelle oder sind als „drop" markiert; kein deprecated Key wird als Spalte angelegt. | Tabelle ⊇ `deprecated`-Einträge des Schemas |
| G6 | Enum-Attribute kennen nur Werte des Schemas: `db.system.name` ∈ {mariadb, microsoft.sql\_server, mysql, postgresql} stable, alle anderen Werte development; `http.request.method` ohne `QUERY`. | Generierte Werteklassen gegen Schema |
| G7 | Generierter C#-Code ist AOT-sauber: keine Reflection, kein `dynamic`; `IsAotCompatible=true` mit Warnungen als Fehler. | 0 × IL2xxx/IL3xxx im Build |
| G8 | Die Persistenz-Policy (`nami.persisted`, `nami.token_exempt`) wird aus Annotationen der Registry generiert; eine Policy-Datei, die einen nicht persistierten Key nennt, bricht die Generierung (namis Regel). | Negativtest mit erfundenem Key |
| G9 | Ein Pin-Wechsel von Core oder GenAI erzeugt einen `weaver registry diff`-Eintrag im Änderungsprotokoll der Registry, sonst schlägt das Gate fehl. | Protokoll-Datei enthält den neuen Commit |
| G10 | Die Rust-Crate für tomo exportiert exakt die Keys des Katalogs; ein Key, den tomo nutzt und nami nicht kennt, ist ein Compile-Fehler in tomo, nie ein Laufzeitfund in nami. | Crate-Konstanten == Katalog-Keys |

## R: Ingest und Store

Diese Invarianten brauchen den laufenden Collector mit In-Memory-DuckDB; Eingabe ist echtes OTLP, nie ein internes Objekt.

| ID | Invariante | Assertion |
| --- | --- | --- |
| R1 | OTLP/HTTP (protobuf und JSON) und OTLP/gRPC landen in denselben Zeilen; Transport ist nie an einer Zeile ablesbar. | Drei Sendewege, ein Snapshot, `Assert.AreEquivalent` |
| R2 | `watermark` pro Projekt und Signal ist monoton: ein Record mit älterem Zeitstempel hebt sie nicht, senkt sie nicht. | Rückdatierter Record → Wasserlinie unverändert |
| R3 | `last_seen` pro Signal entspricht dem jüngsten **Empfangs**zeitpunkt, nicht dem Record-Zeitstempel; Stille ist damit vom Clock-Skew des Senders unabhängig. | Record mit Zukunfts-Zeitstempel → `last_seen` = jetzt |
| R4 | Deprecated Keys werden beim Ingest auf den Nachfolger umgeschrieben; der Originalname bleibt nur in `nami.attribute.key` für Discovery. | `exception.escaped` → nicht gespeichert; `db.query.parameter.*` → Nachfolger |
| R5 | Stable-Keys landen in typisierten Spalten, Development-Keys im Attribut-Store mit Stabilitätsmarke; beide sind abfragbar. | `gen_ai.usage.input_tokens` abfragbar und als development markiert |
| R6 | Sanitizer-Grenzen aus nami bleiben: `exception.message` und `exception.stacktrace` über 128 Ki UTF-16 → `[SANITIZATION_FAILED]`, Ausgabe auf 32 KiB UTF-8 gekürzt. | Grenzwerte ±1 |
| R7 | Exceptions werden aus **beiden** Quellen erkannt: Log-Record mit `event_name == exception` (stabil) und Span-Event `exception` (tomos `tracing`-Pfad); `errors_recent` gruppiert sie gleich. | Zwei Sendewege, eine Gruppe |
| R8 | Resource-Pflicht: `service.name` und `telemetry.sdk.name/version/language`; eine Resource ohne `service.name` wird als eigenes Projekt-Problem gezählt, nicht verworfen. | Zähler `resource.missing_service_name` steigt |
| R9 | Projekt-Scoping: `nami.project.id` aus der Resource entscheidet die Ablage; ein Tool-Argument, ein Header oder `_meta` wählen nie ein Projekt. | Cross-Project-Read liefert 0 Zeilen |
| R10 | Retention ist der einzige Löschpfad; kein Tool und kein Endpunkt löscht Telemetrie. | Kein Delete-Handler außerhalb von Retention |

## S: Vertrag und Serialisierung

Der Vertrag ist spec-first: Records (Produkttypen) und Unions (Summentypen, diskriminiert über `kind`), sprachunabhängig, mit einer reinen Funktion von Spezifikation zu Artefakten. Die Spezifikation steht in TypeSpec, übernommen aus nami (Entscheidung 1); tomo sieht davon nur das emittierte JSON Schema.

| ID | Invariante | Assertion |
| --- | --- | --- |
| S1 | Jede Union ist exhaustiv: C# `switch` ohne `default` über alle Cases kompiliert; eine fünfte Case bricht den Build. | Compile-Time-Test über `CSharpCompilation` |
| S2 | Der Diskriminator heißt `kind`, trägt den Variantennamen aus der Spezifikation und steht auf oberster Ebene (kein Envelope). | JSON enthält `"kind":"<variant>"` top-level |
| S3 | Round-Trip ist verlustfrei und idempotent bei kanonischer Property-Reihenfolge: `Serialize(Deserialize(json)) == json`. | Byte-Vergleich |
| S4 | JSON läuft ausschließlich über den Source-Generator (`JsonSerializerContext`); kein Reflection-Fallback, auch nicht in Tests. | `TypeInfoResolver` liefert für jeden Vertragstyp |
| S5 | Zeit ist Wire-Text: RFC 3339 mit `T` und `Z` in beliebiger Schreibung, Offset mit Doppelpunkt, keine Schaltsekunde; Schreibweise, Offset und Nachkommastellen bleiben erhalten (namis `ContractDateTime`). Relative Angaben (`-15m`) werden an der Tool-Grenze in dieselbe Form aufgelöst. | `[DataRow]` über Schreibweisen; `-15m` → absolutes Fenster |
| S6 | `OptionalValue<T>` unterscheidet Abwesenheit von explizitem `null`; `default` heißt abwesend. | Drei Zustände, drei Serialisierungen |
| S7 | Ganzzahlen auf dem Draht sind exakt darstellbar; OTLP-64-Bit und Nanosekunden bleiben kanonische Dezimal-Strings; String-Enums nehmen keine Ordinale. | Grenzwerte 2⁵³±1, `"1"` ≠ `1` |
| S8 | Fehler sind Werte: `ToolError { code, message, retryable, field, allowed, hint }`; `field` und `allowed` nennen das Argument und was es annimmt; `not_found` nennt `resource_type` und `resource_id`. Kein Tool wirft. | Jede Refusal-Klasse hat genau eine Fehlerform |
| S9 | Vertrags-Revision ist der SHA-256 der semantischen Projektion; Dokumentation und Versionsnummer ändern sie nicht. Mit einer Laufzeit gibt es keinen Skew mehr, die Revision bleibt als Fingerabdruck in `_meta`. | Kommentaränderung → Revision gleich |

## T: Toolvertrag

Sechs Lese-Tools, in der Reihenfolge, in der Claude sie beim Debuggen von tomo aufruft. Wire-Namen sind snake\_case wie in nami, weil das Anthropic-API in Tool-Namen nur `[a-zA-Z0-9_-]` annimmt; tomos Dokument schreibt sie mit Punkt.

| Tool (Wire-Name) | Frage | Argumente | nami-Analyse, aus der es entsteht |
| --- | --- | --- | --- |
| `sources_status` | Sendet tomo überhaupt? | `service` | `check_telemetry_health` |
| `errors_recent` | Welche Fehler, gruppiert nach Typ und Operation, mit `first_seen`, `count`, einem Beispiel-`trace_id` | `service`, `since` | `triage` + `get_failure_context` |
| `traces_get` | Dieser eine Trace, projiziert auf `fields` | `trace_id`, `fields?` | `get_trace` |
| `traces_slowest` | Der langsame Tail, nicht der Durchschnitt | `service`, `operation?`, `since` | `list_traces` (sortiert) + `aggregate_telemetry` |
| `logs_search` | Diese Logzeile, literal | `query`, `service`, `since` | `search_logs` |
| `changes_recent` | Was hat sich seit wann geändert | `service`, `since` | `list_releases` + `ci_log`, aus `vcs.*`, `cicd.*` und `deployment.*` der Resource |

Jedes Tool nimmt zusätzlich `fields`, `max_bytes` und relative Zeiten (`-15m`). Jedes Ergebnis trägt den Freshness-Block, namis `ToolCoverage` um die Wasserlinie erweitert:

```yaml
freshness:
  watermark: 2026-10-26T21:43:12Z   # Ingest vollständig bis hier (R2)
  complete: false                  # Fensterende <= watermark?
  examined: 184220                 # gelesene Records, null wenn unbekannt
  matched: 12                      # passende Records, null wenn der Read gekürzt wurde
  returned: 10                     # zurückgegebene Items
  truncated: true                  # etwas blieb außen vor; next_cursor setzt fort
```

| ID | Invariante | Assertion |
| --- | --- | --- |
| T1 | Stille ist ein Befund: `sources_status` ohne `last_seen` für ein Signal meldet `silent` mit Dauer, nie „keine Fehler". | Kein Record → `kind == "silent"` |
| T2 | Idempotent pro Snapshot: derselbe Call auf derselben Snapshot-Kennung liefert dieselbe Antwort, byte-gleich. | Zwei Calls, `Assert.AreSequenceEqual` |
| T3 | Stateless: kein Protokoll-Session-Zustand; alles Persistente (Snapshots, Wasserlinien) ist namis eigener Zustand und auf jeder Instanz gültig. | Neustart zwischen zwei Calls → gleiche Antwort |
| T4 | `max_bytes` gilt für `structuredContent`; 500.000 Spans im Fenster bleiben darunter, `truncated: true` und `next_cursor` setzen fort. | Byte-Länge ≤ `max_bytes` |
| T5 | Leer und `complete: true` ist eine Aussage; leer und `complete: false` ist „nach der Verzögerung erneut fragen" und wird als solches gerendert. Nie ein leeres `complete`-Ergebnis für ein Fenster jenseits der Wasserlinie. | Fensterende > `watermark` → `complete == false` |
| T6 | `fields` projiziert; ein unbekanntes Feld ist `ToolError` mit `field` und `allowed`, nicht stilles Weglassen. | Unbekanntes Feld → Fehler nennt die erlaubten |
| T7 | Kein Roh-Dump: Ergebnisse tragen Gruppen, Ränge, Beispiel-IDs; vollständige Spans nur über `traces_get` mit `fields`. | `errors_recent` enthält keinen Stacktrace |
| T8 | `logs_search` ist literal: Anführungszeichen, Backslashes und Nicht-ASCII matchen wie getippt; kein Regex, kein Case-Folding. | `"\\"` und `ü` als Suchtext |
| T9 | Jede Antwort: `content[0]` Headline mit Top-Zeilen, IDs und dem nächsten Tool; `content[1]` das JSON; `structuredContent` dasselbe Objekt; `outputSchema` deklariert. | Drei Teile, eine Quelle |
| T10 | Alle Tools `readOnlyHint: true`, `idempotentHint: true`, `destructiveHint: false`, `openWorldHint: false`; `tools/list` deterministisch sortiert mit `ttlMs` und `cacheScope`. | Manifest-Snapshot gegen Verhalten |
| T11 | Profiles und Baggage antworten `DataUnavailable` mit Grund; kein leerer Erfolg für ein Signal ohne Ziel. | `kind == "data_unavailable"` |
| T12 | `traceparent` aus `_meta` wird extrahiert; der Tool-Span ist Kind des Agenten-Spans; fehlendes `_meta` → neuer Root, kein Fehler. | Parent-Beziehung; Root ohne Parent |
| T13 | `since` ist halboffen `[start, now)` gegen die Wasserlinie; ein Record exakt bei `start` zählt, einer jenseits der Wasserlinie nie. | Randwerte |

## E2E-Harness

Der Harness ist namis: MSTest.Sdk 4.5.1 auf Microsoft.Testing.Platform, Fallout als Gate, eine NativeAOT-Conformance-App ohne Testframework. Drei Testprojekte, nach Beweisart getrennt:

| Projekt | Beweist | Läuft | Wurzel-Test |
| --- | --- | --- | --- |
| `Nami.Registry.Tests` | G1–G10, S1 | JIT; braucht `weaver` auf dem PATH (`[ExecutableCondition("weaver", "--version")]`) und Roslyn | `Catalog_MatchesResolvedRegistry` |
| `Nami.Collector.Tests` | R1–R10, S2–S9, T1–T13 | JIT; In-Memory-DuckDB unter `TestContext.TestTempDirectory`, OTLP über Loopback, MCP-Client in-process über stdio | `Catalog_EmbeddedConstants_MatchRegistrySnapshot` |
| `Nami.Conformance` | Das veröffentlichte AOT-Binary: ein OTLP-Batch rein, sechs Tools über stdio raus, Freshness-Block korrekt | `PublishAot`, kein MSTest (namis `Nami.Sdk.Conformance`-Muster) | der Prozess selbst |

**Regeln.**

- Testklassen `sealed`, `TestContext` über den Primär-Konstruktor, `context.CancellationToken` an jeden Call (`MSTEST0049`); `[Timeout]` auf jedem Test, der einen Socket oder einen Prozess öffnet (nami: 23 Stellen).
- `Assert.Scope()` (experimentell, `#pragma warning disable MSTESTEXP`) für Attribut- und Freshness-Prüfungen: alle Abweichungen eines Ergebnisses in einem Lauf.
- `[CombinatorialData]` über die sechs Tools × drei OTLP-Transporte; Werte aus dem geladenen Katalog, nie Literale im Test.
- `[DependsOn]` nur innerhalb einer Assembly, jeweils auf den Wurzel-Test; die Reihenfolge der Assemblies erzwingt Fallout.
- `[ResourceLock(WellKnownResources.EnvironmentVariables)]` für `NAMI_*`/`OTEL_*`-Variablen; `[DoNotParallelize]` nur für Tests, die feste Ports binden (nami: 13 Stellen); alles andere parallel.
- Eingabe ist immer echtes OTLP (protobuf-Bytes oder JSON), erzeugt aus dem aufgelösten Schema; synthetische Telemetrie existiert nur in Tests (namis Regel).
- Golden-Dateien für `tools/list` und für den Katalog; Vergleich über `Assert.AreEquivalent(expected, actual, strict: true)` auf dem deserialisierten Modell, IDs und Zeitstempel normalisiert.
- `testconfig.json`: `captureTrace: Live`, `captureAssertionFailureDiagnostics: true` (nicht unter AOT wirksam).

**Was der Harness bewusst nicht tut:** sampeln, filtern, Telemetrie erfinden. Jeder gesendete Record wird gelesen; die Begrenzung der Antwort (T4) ist Eigenschaft des Tools, nicht des Harness.

## Testmethoden-Katalog

Namensschema `Subjekt_Bedingung_ErwartetesErgebnis`; eine Klasse pro Invariantenfamilie. Die Assertion-Spalte ist das, was die Methode beweist.

**`RegistryGateTests` (G)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `Registry_Check_PassesWithFuture` | G1 | Exit-Code 0, keine `violation` |
| `Registry_NestedCoreDependency_ResolvesOnce` | G1 | `http.request.method` genau einmal im aufgelösten Schema |
| `Generate_TwiceSameRegistry_IsByteIdentical` | G2 | `Assert.AreSequenceEqual(bytes1, bytes2)` für C#, Rust, Markdown |
| `Catalog_MatchesResolvedRegistry` | G3 | Zähler 121/8/51/1/5; jede Definition mit `stability` |
| `NamiKeys_AppearOnlyInGeneratedFiles` | G4 | Grep über `src/`, `tomo/` ohne `nami.`-Literal |
| `DeprecatedKeys_HaveSuccessorOrDrop` | G5 | `[DataRow]` über `exception.escaped`, `db.query.parameter`, `http.request.body.size` |
| `EnumValues_MatchSchemaStability` | G6 | `redis` development, `postgresql` stable, `QUERY` fehlt |
| `GeneratedCode_AotAnalyzers_ReportNothing` | G7 | 0 × IL2xxx/IL3xxx |
| `PolicyNamingUnpersistedKey_FailsGeneration` | G8 | `Assert.ThrowsExactly<InvalidOperationException>` |
| `PinChange_WithoutDiffEntry_FailsGate` | G9 | Fehlermeldung nennt den neuen Commit |
| `RustCrate_Constants_EqualCatalogKeys` | G10 | Mengen gleich |

**`IngestStoreTests` (R)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `Ingest_ThreeTransports_SameRows` | R1 | `[CombinatorialData]` protobuf/JSON/gRPC, ein Snapshot |
| `Watermark_BackdatedRecord_DoesNotMove` | R2 | Wasserlinie vorher == nachher |
| `LastSeen_FutureTimestamp_UsesReceiptTime` | R3 | `last_seen` ≈ jetzt, nicht 2030 |
| `Ingest_DeprecatedKey_RewrittenToSuccessor` | R4 | Nachfolger gespeichert, Original nur in `nami.attribute.key` |
| `Ingest_DevelopmentKey_StoredWithStabilityMark` | R5 | `gen_ai.usage.input_tokens` abfragbar, `stability == development` |
| `Sanitizer_Boundaries_HoldAtPlusMinusOne` | R6 | 128 Ki ± 1, 32 KiB ± 1 |
| `Exception_LogEventAndSpanEvent_GroupTogether` | R7 | Eine Gruppe, `count == 2` |
| `Resource_MissingServiceName_CountedNotDropped` | R8 | Zeile vorhanden, Zähler +1 |
| `Read_OtherProject_ReturnsNothing` | R9 | 0 Zeilen |
| `Store_HasNoDeletePathOutsideRetention` | R10 | Statische Analyse der Store-API |

**`ContractTests` (S)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `Union_SwitchWithoutDefault_IsExhaustive` | S1 | Compilation ohne Diagnostic |
| `Union_AddedCase_BreaksBuild` | S1 | Diagnostic nach fünfter Case |
| `Serialize_EachCase_EmitsKindTopLevel` | S2 | `[DataRow]` über alle Cases |
| `RoundTrip_EachCase_IsByteIdentical` | S3 | Byte-Vergleich |
| `SerializerContext_CoversEveryContractType` | S4 | `GetTypeInfo` non-null |
| `Timestamp_Spellings_PreservedAndRelativeResolved` | S5 | `[DataRow]` über `t`/`z`, Offsets, `-15m` |
| `OptionalValue_ThreeStates_ThreeSerializations` | S6 | abwesend / null / Wert |
| `Integers_BeyondSafeRange_RefusedOrString` | S7 | 2⁵³+1 → Fehler; Nanosekunden als String |
| `ToolError_EachRefusalClass_HasOneShape` | S8 | `field`, `allowed`, `hint` gesetzt |
| `ContractRevision_DocChange_Unchanged` | S9 | SHA gleich |

**`ToolContractTests` (T)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `SourcesStatus_NoRecords_ReportsSilentWithDuration` | T1 | `kind == "silent"`, `silent_for` gesetzt |
| `SourcesStatus_AfterOneRequest_LastSeenWithinOneSecond` | T1 | Erfolgszahl aus Abschnitt 1 |
| `AnyTool_SameSnapshot_ByteIdentical` | T2 | `[CombinatorialData]` über sechs Tools |
| `AnyTool_AfterRestart_SameAnswer` | T3 | Prozess neu, Antwort gleich |
| `ErrorsRecent_FiveHundredThousandSpans_UnderMaxBytes` | T4 | Byte-Länge ≤ `max_bytes`, `truncated == true` |
| `Window_BeyondWatermark_NeverComplete` | T5 | `complete == false` |
| `Window_WithinWatermark_EmptyIsComplete` | T5 | `complete == true`, 0 Items |
| `Fields_Unknown_RefusedNamingAllowed` | T6 | `ToolError.allowed` enthält die Feldliste |
| `ErrorsRecent_ContainsNoStacktrace` | T7 | Negativ-Suche |
| `LogsSearch_QuotesBackslashesNonAscii_MatchLiterally` | T8 | `[DataRow]` über `"`, `\\`, `ü` |
| `Result_ThreeParts_OneSource` | T9 | `content[1].text == JSON(structuredContent)` |
| `ToolsList_Snapshot_MatchesAnnotationsAndCache` | T10 | `readOnlyHint`, `idempotentHint`, `ttlMs > 0`, sortiert |
| `ProfilesOrBaggage_ReturnsDataUnavailable` | T11 | `kind == "data_unavailable"` |
| `IncomingMeta_Traceparent_ParentsToolSpan` | T12 | `ParentSpanId == agentSpanId` |
| `Since_Boundaries_HalfOpenAgainstWatermark` | T13 | Record bei `start` drin, jenseits Wasserlinie draußen |

Wurzeln: `Catalog_MatchesResolvedRegistry` in der Registry-Assembly, `Catalog_EmbeddedConstants_MatchRegistrySnapshot` in der Collector-Assembly; `[DependsOn]` zeigt innerhalb jeder Assembly auf ihre Wurzel. Scheitert der Pin, fällt alles kontrolliert aus.

## Entscheidungen und Risiken

nami wird von Agenten für Agenten gebaut. Entscheidungen trifft der Agent, der baut, und schreibt sie hierher; ein Mensch widerspricht per Kommentar im Dokument, sonst gilt die Entscheidung. Die drei unten hat Claude (Fable 5.1) am 24.10.2026 getroffen. Die Risiken darunter sind Fleißarbeit mit bekanntem Ausgang; was dort „prüfen" heißt, entscheidet der Testlauf am Sonntag, nicht eine Person.

| Nr. | Frage | Entscheidung | Begründung |
| --- | --- | --- | --- |
| 1 | Spezifikationssprache des Vertrags | **TypeSpec**, übernommen aus nami `contracts/`. Der TS-Types-Emitter entfällt; C#-Emitter (818 Zeilen), Lint-Regeln und JSON-Schema-Emitter bleiben. | Existiert, ist getestet und emittiert das JSON Schema, das `typify` in Rust-Typen für tomo übersetzt. Zod (tomos Vorschlag) brächte die dritte Laufzeit zurück, die nami gerade abschafft; reine C#-Records machten tomos Typen von C#-Tooling abhängig. tomo sieht nur JSON Schema, die Wahl ist für tomo unsichtbar. |
| 2 | Zeitpunkt von .NET 11 | **RC2 jetzt**: `global.json` auf `11.0.100-rc.2`, `allowPrerelease: true`, `rollForward: latestFeature`; der GA-Pin am 9.11.2026 ist ein eigener Commit. | Unions, `AddTracing`, `ActivitySourceFactory` und `SerializeAsyncEnumerable` mit `JsonTypeInfo<T>` sind der Grund für .NET 11; zwei Wochen Warten auf GA haben keinen Gegenwert. Bricht `ANcpLua.NET.Sdk`, MSTest.Sdk oder DuckDB.NET unter net11, wird das Paket gepinnt oder ersetzt, nicht die Entscheidung. |
| 3 | Metriken-Tool | **Keines.** `sources_status` meldet `last_seen` für alle drei Signale, Metrics eingeschlossen; mehr fragt tomo nicht. | Sechs Fragen, sechs Tools; `traces_slowest` deckt den Tail. Ein `metrics_query` kommt erst, wenn tomo eine Metrikfrage stellt, und wird dann aus namis `query_metric` abgeleitet; bis dahin ist es Oberfläche ohne Frage. |

| Risiko | Wo es auffällt | Gegenmaßnahme |
| --- | --- | --- |
| MCP im .NET-Binary: das C#-SDK v2 spricht 2026-07-28 (stateless, stdio, Streamable HTTP, MRTR); ob Tool-Registrierung und Schema-Erzeugung unter NativeAOT ohne Reflection laufen, ist nicht belegt. | `Nami.Conformance` | Erster Conformance-Lauf vor jedem Tool-Code; Tool-Metadaten aus dem Vertrag generieren, nicht per `WithTools<T>` reflektieren |
| Weaver 0.28.0 liefert keine C#-Templates; verschachtelte Registry-Abhängigkeit (nami → genai → core) ist in der Dokumentation nicht als Beispiel belegt. | G1, G2 | `weaver registry check --future` am Sonntag als erster Befehl; eigene `.j2` wie GenAI; bei Doppelauflösung Core nur über GenAI importieren |
| Development-Konventionen (`gen_ai.*`, `mcp.*`) benennen sich zwischen GenAI-Commits um; ohne Tag kein Semver. | G9, R4 | Commit-Pin plus `weaver registry diff` als Gate; Normalisierungstabelle aus dem Diff pflegen |
| tomos `tracing-opentelemetry` schreibt `error!`-Events als Span-Events, nicht als Log-Records; die stabile Exception-Konvention ist der Log-Pfad. | R7 | nami erkennt beide Quellen; tomo entscheidet am Sonntag, ob `opentelemetry-appender-tracing` mitläuft |
| `changes_recent` braucht eine Quelle für „seit wann". | T-Tabelle | Keine externe Abfrage: tomo setzt `vcs.ref.head.revision`, `deployment.environment.name` und `cicd.*` in der Resource; nami liest Änderungen aus Resource-Wechseln (namis `list_releases`-Prinzip) |
| Tool-Namen mit Punkt (`sources.status`) werden vom Anthropic-API abgelehnt. | T10 | Wire-Namen snake\_case; tomos Dokument anpassen |
| `.NET`-Logging-SDK: Spalte .NET in der Compliance-Matrix hat kein `Logger.Emit(LogRecord)` und kein belegtes `EventName`; für namis **eigene** Exception-Events ist der Log-Pfad in .NET nicht garantiert. Rust hat beides. | R7 (Selbsttelemetrie) | nami-eigene Fehler als Span-Event plus `ILogger`-Record mit `exception.*`; der Receiver gruppiert ohnehin beide |
| Ein Binary heißt ein Deployment: ohne den TS-Server entfällt `mcp.nami.at`; Hosting läuft über dasselbe Railway-Service wie OTLP. | Deployment | Zwei Ports, ein Service, ein Healthcheck; OAuth erst, wenn ein zweiter Konsument existiert |

**Fallback-Kriterium Rust.** Nur wenn G7 (AOT-Sauberkeit) oder die MCP-Konformanz unter NativeAOT nicht erreichbar sind. Registry, Vertrag und Tool-Invarianten sind sprachneutral und wandern unverändert mit; namis Collector-Code ist das Argument für C#.

**Termine.** Sonntag, 25.10.2026: tomos Rust-Marathon, dazu `weaver registry check` und der erste Conformance-Lauf. 9.11.2026: .NET 11 GA, Pin-Wechsel. 12.12.2026: was 2808-vibing braucht.

**Was nach dieser PRD als Erstes grün werden muss:** `Registry_Check_PassesWithFuture`. Es setzt nichts voraus außer Weaver 0.28.0 und dem Manifest aus Abschnitt 5.

### Änderungsprotokoll

| Datum | Änderung |
| --- | --- |
| 2026-10-24 | Die drei offenen Entscheidungen getroffen: TypeSpec bleibt, .NET 11 RC2 jetzt, kein Metriken-Tool. Regel aufgenommen: Entscheidungen trifft der bauende Agent, Einspruch per Kommentar. Abschnitte 3 und S angepasst. |
| 2026-10-24 | Umbau von observe_runtime (C#-Interceptor-Architektur) auf nami: Zweck, Problem, Schnittliste gegen nami `ecdfb564`, Architektur mit einem Binary, Weaver-Registry `nami` (Core v1.44.0, GenAI 9ffc7e00, Weaver 0.28.0, stable und development), sechs Tools mit Freshness-Block, Invarianten G/R/S/T neu, Harness auf drei Projekte, Risiken. Grundlage: nami-Repo, nami-Repo, `claude/debugging-tomo.md`, Weaver-Dokumentation, C#-MCP-SDK-v2-Dokumentation. |
| 2026-10-23 | Review der observe_runtime-Fassung: sieben Korrekturen, drei Ergänzungen; Intro von Gesprächsform auf PRD umgestellt. |
| 2026-10-23 | Erste Fassung als observe_runtime-Invarianten und Testspezifikation. |

### Quellen

- [nami @ ecdfb564 „nami for tomo"](https://github.com/ANcpLua/nami/commit/ecdfb5645081bb532e1b00eaccc09606d9f5b04b) (README, AGENTS.md, `docs/architecture.yaml`, `services/nami.mcp/README.md`, `eng/build/BuildCollectorSemanticCatalog.cs`, `contracts/common/telemetry-query.tsp`, `Version.props`, `global.json`)
- [nami @ e025215](https://github.com/ANcpLua/nami) (`claude/debugging-tomo.md`)
- [semantic-conventions v1.44.0](https://github.com/open-telemetry/semantic-conventions/releases/tag/v1.44.0) und [main 3e258047](https://github.com/open-telemetry/semantic-conventions/commit/3e2580475b1fa266fe85a1d1ea7fe9c41a4783b5); Stable-Inventar aus dem Handoff vom 23.10.2026
- [semantic-conventions-genai 9ffc7e00, model/manifest.yaml](https://github.com/open-telemetry/semantic-conventions-genai/blob/9ffc7e00c639be7712e544e3e54331e885dccf84/model/manifest.yaml)
- Weaver 0.28.0: „Define your own telemetry schema" und CLI-Referenz (beiliegende Dokumente)
- [MCP 2026-07-28 Key Changes](https://modelcontextprotocol.io/specification/2026-07-28/changelog)
- [MCP C# SDK v2: Stateless and stateful mode](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html), [Beta SDKs for the 2026-07-28 release candidate](https://blog.modelcontextprotocol.io/posts/sdk-betas-2026-07-28/)
- [OTel Semantic Conventions for MCP](https://opentelemetry.io/docs/specs/semconv/gen-ai/mcp)
- OpenTelemetry Spec Compliance Matrix (beiliegend; Spalten .NET und Rust, Abschnitt Logs)
- MSTest 4.5.1 API-Referenz (modern-mstest Skill)
