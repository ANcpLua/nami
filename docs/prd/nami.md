# nami — PRD

Oct 24, 2026 · @Alexander Nachtmann

Status 2026-10-27: Vom Nutzer als Produktziel und Prüfbasis genehmigt, zusammen mit
[nami-request-response-review.md](nami-request-response-review.md). Die Freigabe ist kein
Nachweis einer vollständigen Implementierung oder Veröffentlichung. Ausdrücklich offene
Entscheidungen bleiben offen; spätere ausdrückliche Nutzerentscheidungen haben Vorrang.

## 1. Zweck und Nutzer

nami ist ein Stack für **agentengesteuertes Debugging**: Instrumentierung erfasst die Ausführung einer Anwendung, eine C#/.NET-11-NativeAOT-Engine nimmt Telemetrie an und speichert sie in DuckDB, und ein TypeScript-MCP-Server liefert Agenten begrenzte, typisierte Antworten. Die Produktoberfläche besteht aus Tools. Es gibt kein Dashboard und keine für Menschen bestimmte Analyseoberfläche.

Ein Entwickler beauftragt einen Agenten mit der Untersuchung seiner Anwendung. Der Agent nutzt nami über einen **Claude.ai-Connector und/oder ein OpenAI-Plugin mit gebündelter MCP-Anbindung**. Lokale agentische Clients wie Claude Code können denselben MCP-Vertrag nutzen. Menschen konfigurieren und autorisieren den Zugang; die Debugging-Aufrufe und ihre Auswertung erfolgen durch Agenten.

**Beispiel:** tomo ist hier ein Entwickler, der seine Anwendung mit dem nami-Stack durch einen Agenten debuggen lässt. tomo bezeichnet weder eine vorgeschriebene Runtime noch die einzige Zielanwendung. Die Produktanforderungen gelten für alle unterstützten Anwendungen und Agentenhosts.

nami implementiert Collector, Analyse und TypeScript-MCP in diesem Repository. TypeSpec bleibt die gemeinsame Quelle für Nachrichten, Typen und JSON-Schemas; Weaver bleibt die Quelle für semantische Definitionen und Persistenzregeln. Die Herkunft schränkt weder Zielsprachen noch Connectoren ein.

**Zielstand und Umsetzung:** Diese Fassung ersetzt die frühere Festlegung auf einen C#-MCP-Server in genau einem Prozess. Die TypeScript-MCP-Schicht auf Bun und die private Verbindung zur C#-NativeAOT-Engine sind implementiert. Offene Instrumentierungs- und Produktanforderungen werden in [der Checkliste](../checklist.md) separat geführt. Bestehende grüne Tests belegen nur die von ihnen ausgeführten Pfade.

Erfolg ist an überprüfbarer Ausführung erkennbar: Die Registry und Generierung sind reproduzierbar; eine deklarierte Operation wird genau einmal mit korrektem Kontext beobachtet; ihre angenommenen Daten werden unter kontrollierter Last innerhalb einer Sekunde abfragbar; jede Antwort bleibt unter `max_bytes`; ein leeres unvollständiges Fenster wird nie als Fehlerfreiheit ausgegeben. Abschnitt I definiert die Instrumentierungsnachweise, Abschnitt T die sechs Analyse-Tools.

## 2. Problem und Produktgrenze

Agenten benötigen Belege darüber, was eine Anwendung tatsächlich ausgeführt hat, wie lange es dauerte, welche Fehler auftraten und welche Daten fehlen. Ein Roh-Dump und eine vermeintlich leere Fehlerliste ohne Vollständigkeitsangabe reichen dafür nicht.

- **Ein semantischer Vertrag über Sprachgrenzen.** C# und TypeScript erhalten ihre Typen aus derselben TypeSpec-Quelle. Abweichende Releases werden durch Vertragsrevisionen erkannt. Zwei Sprachen sind kein Vertragsfehler; unkoordinierte oder inkompatible Artefakte sind ein Betriebsproblem.
- **Explizite Instrumentierungsabdeckung.** Marker, Modellmanifest und generierte Wrapper bestimmen, welche Aufrufstellen erfasst werden. Nicht erfasste Abhängigkeiten und Aufrufformen werden ausdrücklich ausgewiesen. Ein laufender Prozess allein beweist keine vollständige Beobachtung.
- **Begrenzte Antworten für Agenten.** Sechs lesende Analyse-Tools liefern Gruppen, Ränge, IDs, Freshness und Fortsetzungen. Kein Dashboard, kein menschlicher Analyseworkflow, keine synthetische Produktionstelemetrie.
- **Beobachtungszustand mit eigener Lebenszeit.** Eine langlebige Beobachtung ist Anwendungszustand mit expliziter Kennung. Sie hängt weder an einer MCP-Verbindung noch an einer pauschalen 15-Minuten-Frist.

## 3. Umfang der Architektur

| Bestandteil | Entscheidung für nami | Grund |
| --- | --- | --- |
| C#-Collector, OTLP/HTTP und OTLP/gRPC, DuckDB, Analyse-Engine und Sanitizer | Bleiben Grundlage der Engine | Vorhandene Aufnahme, Speicherung und Abfragen erhalten |
| TypeScript-MCP auf Bun und offiziellem MCP-TS-SDK v2 | Wird als Muster für die nami-MCP-Schicht übernommen | Tool-Entwicklung und agententaugliche SDK-Dokumentation bleiben im TypeScript-Ökosystem |
| TypeSpec mit C#-, TS- und Lint-Emittern sowie JSON Schema | Bleibt; TS-Typgenerierung wird für nami wieder ergänzt | Ein Vertrag für beide Implementierungen, keine handgepflegten doppelten Modelle |
| Öffentliche REST-Analyse-API | Entfällt | Agenten nutzen MCP; die lokale Engine-Anbindung braucht keine zusätzliche Produkt-API |
| Interne HTTP-Anbindung | Wird lokal durch private, langlebige Streams/Pipes ersetzt | Gemeinsam gestartete Prozesse brauchen keinen internen REST-Endpunkt; keine unbelegte Performancebehauptung |
| Projektbindung, geprüfte Credentials und Vertragsrevision | Bleiben an allen Grenzen | Transportwechsel darf Mandantentrennung und Validierung nicht abschwächen |
| Weaver-Registry, C#-Generierung und Persistenzregeln | Bleiben im Repository | Semantische Definitionen sind unabhängig von der Sprache des MCP-Servers |
| Compile-Time-Interception und konfigurierte Beobachtung | Gehören zum Produktumfang | Unterstützte Aufrufstellen werden beim Build gebunden; Laufzeitcode verwaltet Dauer, Export und Abschluss |
| Skills und Connector-/Plugin-Bundles | Gehören zur Agentenintegration | Claude.ai und OpenAI sollen denselben Toolvertrag verwenden können |
| Schreibende Analyse-Tools und Telemetrie-Löschung durch Tools | Entfallen | Die sechs Analyse-Tools sind lesend; Retention bleibt der einzige Telemetrie-Löschpfad |
| Menschliches Dashboard und UI-Analyse | Entfallen | Ausschließlich agentische Debugging-Oberfläche |
| MSTest/MTP, reale OTLP-/DuckDB- und NativeAOT-Nachweise | Bleiben und werden erweitert | Der gesamte Stack muss am veröffentlichten Artefakt nachgewiesen werden |

## 4. Architektur und Instrumentierung

### 4.1 Komponenten und Datenfluss

Ein gemeinsamer Release enthält eine **TypeScript-MCP-Schicht** und eine **C#-NativeAOT-Engine**. Ein einzelnes natives Binary für den ganzen Stack ist kein Zielkriterium. Die Engine braucht keine separat installierte .NET-Laufzeit; TypeScript/Bun-Abhängigkeiten und native Bibliotheken gehören in die deklarierte Paketierung.

nami läuft nativ unter macOS und Linux. Unter Windows läuft nami in WSL2 (der Linux-Build) oder als Linux-Container unter Docker Desktop; einen nativen Windows-Build gibt es nicht.

```mermaid
flowchart LR
    A["Agent in Claude.ai / OpenAI / lokalem Client"] <-->|MCP| M["TypeScript-MCP"]
    M <-->|"Private Streams / Pipes, TypeSpec-Nachrichten"| E["C# NativeAOT-Engine"]
    P["Instrumentierte Anwendung"] -->|OTLP| E
    E --> D["DuckDB und Snapshots"]
    W["Weaver + TypeSpec"] --> P
    W --> M
    W --> E
```

1. **Definition → Build.** Weaver löst den gepinnten semantischen Katalog auf. TypeSpec definiert Nachrichten und Toolformen. Die Generierung erzeugt C#-/TypeScript-Typen, JSON-Schemas und die für unterstützte Zielsprachen benötigten semantischen Artefakte reproduzierbar.
2. **Build → Aufrufstelle.** Bei C#/.NET binden Compiler und Generator deklarierte, unterstützte Aufrufstellen an generierte Interceptor-Wrapper. Ein Marker kann per Enum eine Modellkennung wählen; der Generator löst sie beim Build eindeutig auf. Bereits kompilierte Abhängigkeiten werden dadurch nicht pauschal intern instrumentiert.
3. **Aufruf → Beobachtung.** Zur Laufzeit führt die bereits kompilierte Aufrufstelle den generierten Wrapper aus. Dieser verwendet den beim Build aufgelösten Telemetrievertrag, beobachtet die ursprüngliche Operation und übergibt deren Telemetrie an die konfigurierte Pipeline. Die Laufzeitlogik verwaltet die Beobachtungsdauer.
4. **Export → Speicherung.** Reale OTLP-Daten gelangen über HTTP (protobuf/JSON) oder gRPC in die projektgebundene Engine. Sie speichert Telemetrie, wie sie gesendet wurde (Keys, Typen, Container, Event-Felder und Inhalte), greift beim Ingest nur mit der Credential-Stufe und mit Größengrenzen ein und führt Wasserlinien und Empfangszeiten.
5. **Abfrage → Antwort.** Die C#-Analyse arbeitet über einem konsistenten Snapshot. TypeScript validiert die privaten Nachrichten, registriert die MCP-Tools und formt die begrenzte Agentenantwort mit Headline, strukturiertem Ergebnis und Freshness.
6. **Durchgängige Zuordnung.** Trace-/Span-IDs, Projektbindung, Vertragsrevision und explizite Beobachtungs-/Snapshot-Kennungen halten Ausführung, Export, Speicherung und Abfrage zusammen. namis Selbsttelemetrie benutzt dieselbe semantische Grundlage, ohne eine unendliche Export-/Ingest-Schleife zu erzeugen.

### 4.2 Lokale Streams, gehostetes MCP und Sicherheit

Lokal startet der TypeScript-Launcher die Engine und besitzt die privaten Ein-/Ausgabekanäle. Nachrichten sind gerahmtes UTF-8-JSON nach TypeSpec; JSON Lines eignet sich für vollständige kompakte Nachrichten, nicht für unmarkiert ineinander verschachtelte Teilantworten. Request-IDs ordnen konkurrierende Antworten zu. Abbruch, Fehler, maximale Nachrichtengröße, Backpressure, Prozessende und Vertragsrevision sind Teil der internen Grenze. Externes MCP-stdout und der private Engine-Kanal bleiben getrennt; Diagnoseausgabe geht auf stderr.

Die Engine kann mit generiertem `JsonTypeInfo<T>` direkt in `Stream`/`PipeWriter` serialisieren. .NET-11-Speicher-Streams vermeiden unnötige Zwischenkopien innerhalb des Prozesses; sie teilen keinen verwalteten Heap mit TypeScript. Gemeinsamer Speicher oder FFI sind keine Voraussetzung. Weniger Kopien und geringere Latenz werden gemessen, nicht aus dem Transportnamen abgeleitet. Internes HTTP ist kein Pflichttransport.

Gehostete Claude.ai-Connectoren und OpenAI-MCP-Integrationen erreichen die TypeScript-Schicht über das vom Host unterstützte Remote-MCP-Verfahren, insbesondere Streamable HTTP. Ein Bundle umfasst die MCP-Anbindung und die zum Gebrauch erforderlichen Skills/Metadaten; Paketierung und Authentifizierung richten sich nach dem konkreten Host. Eine allgemeine C#- oder Single-Binary-Vorgabe wird daraus nicht abgeleitet. Lokales stdio ist eine zusätzliche Bereitstellung, nicht der einzige Produktzugang.

Jede Anfrage bleibt im authentifizierten Projekt. Die MCP-Schicht leitet die interne Projektbindung aus geprüften Credentials ab; Toolargumente, unbestätigte Header und freie `_meta`-Felder wählen kein Projekt. Auch die Engine prüft ihre private Vertrauensgrenze. Telemetrie und fremde Dokumente sind untrusted Daten, niemals Instruktionen.

### 4.3 Beobachtungslebenszeit und Sprachgrenzen

MCP `2026-07-28` ist stateless auf Protokollebene. Eine dauerhaft offene Pipe oder ein Prozess darf für Effizienz bestehen bleiben. Projekt, Fähigkeiten und Beobachtungszuordnung werden trotzdem ausdrücklich übertragen; über mehrere Requests benötigter Zustand wird durch eine explizite, projektgebundene Kennung referenziert. Verbindungsidentität ersetzt keine Beobachtungsidentität.

XML beschreibt die konfigurierte Lebenszeitregel; ausführbarer Laufzeitcode setzt sie durch. Absolute Frist, Inaktivitätsfrist, Erneuerung, explizites Beenden, Request-Timeout, Datenaufbewahrung und Exportverzögerung sind getrennte Regeln. Es gibt keine versteckte 15-Minuten-Grenze. Die sechs Analyse-Tools bleiben lesend; Build-/Start-/Beobachtungskonfiguration wird nicht als zusätzliches schreibendes Analyse-Tool eingeschmuggelt.

Skills können Builds orchestrieren und vorhandene Tools ausführen. Ein bereits veröffentlichtes NativeAOT-Artefakt erhält durch einen Skill keine neuen kompilierten Interceptors. Neue Konfiguration im vorbereiteten Umfang ist möglich; neue generierte Implementierung benötigt einen neuen Build. C#-Interceptors benötigen keinen CLR-Profiler und keinen JIT; benötigte .NET-Laufzeitbestandteile bleiben im nativen Artefakt enthalten.

C#-Interception erfasst C#-Aufrufstellen der jeweiligen Compilation. Andere Zielsprachen können über ihre eigene Instrumentierung OTLP liefern. Ein externer C#-Collector instrumentiert deren Funktionen nicht durch seine bloße Existenz.

### 4.4 Definitionsarten und Signalabdeckung

`ResourceDefinition` beschreibt signalübergreifenden Kontext. `SpanDefinition` beschreibt Spans, `MetricDefinition` Instrumente, Einheiten und Aggregationsanforderungen. `EventDefinition` legt ausdrücklich fest, ob das Ziel ein Log-Ereignis oder ein Span-Ereignis ist. Allgemeine Logs benötigen ebenfalls eine deklarierte Darstellung, wenn sie zum unterstützten Umfang gehören. Die vier Namen allein beweisen keine vollständige Signalabdeckung.

Die Modellmanifest-Prüfung umfasst sämtliche deklarierten Definitionen, referenzierten Attribute und eingeschränkten Enum-Werte. Bei einer Stable-only-Auswahl muss jedes dieser Elemente die Stable-Regel erfüllen; ein stabiles Modell legitimiert kein instabiles Mitglied. Offene String-Werte bleiben offen. Die Registry darf weiterhin ausdrücklich gewählte Development-Definitionen für MCP-/GenAI-Selbsttelemetrie führen.

Die verbindlichen Einzelbeweise stehen in Abschnitt I. Unterstützter Umfang, Ausschlüsse, Zeitmessung, Exportverluste und Abfragbarkeit werden am veröffentlichten Stack belegt, nicht aus erfolgreich generiertem Code geschlossen.

## 5. Semconv-Registry `nami`

Die Registry liegt im nami-Repo und folgt dem Aufbau von `semantic-conventions-genai`: ein `manifest.yaml`, Modelldateien pro Namespace, Jinja-Templates, Policies, ein Skript-Verzeichnis. Sie ist **development** und importiert Stable **und** Development aus Core und GenAI.

Manifest und Pins stehen je an einer Stelle: [`registry/source/manifest.yaml`](../../registry/source/manifest.yaml) (Schema-URL von nami und die GenAI-Abhängigkeit, über die Core kommt) und [`registry/source-pins.json`](../../registry/source-pins.json) (Core-, GenAI- und Weaver-Pin). Weaver löst Versionskonflikte über die `schema_url` auf; ob die verschachtelte Abhängigkeit (nami → genai → core) ohne Doppelung aufgelöst wird, prüft `weaver registry check --future` als erster Gate-Schritt (G1).

**Inventar, das nami erbt.** Stable aus Core: 21 Bereiche, 121 Attribut-Keys, 8 Span-Formen, 51 Instrumente, 1 Event (`exception`), 5 Resource-Definitionen; `exception.escaped` ist deprecated und nicht enthalten. Development aus Core und GenAI: `gen_ai.*`, `mcp.*`, `session.*`, `cicd.*`, `vcs.*`, `process.*`, `rpc.*` und die übrigen 67 Bereiche. Stabilität ist pro Definition ein Feld im aufgelösten Schema, nie eine Annahme über das Paket.

**`nami.*`-Konventionen.** Die Nami-eigenen Keys werden in der Registry definiert, nicht in einem Paket: `nami.project.id` (Projekt-Zuordnung einer Resource), `nami.attribute.namespace` und `nami.attribute.key` (Attribut-Discovery), `nami.gen_ai.tool.call.arguments_hash` (Hash der Tool-Argumente), `nami.value.replacement.reason` (Grund, aus dem der Ingest einen Wert ersetzt hat: `depth`, `invalid_utf16`; Attribut des Zählers `nami.collector.values.replaced`). Inhalte (Prompts, Antworten, Tool-Argumente und -Ergebnisse, Request- und Response-Bodies) speichert nami, wie sie gesendet wurden, abzüglich erkannter Credentials; was ein Sender erfasst, entscheidet die Quelle (bei Claude Code die `OTEL_LOG_*`-Schalter), nicht nami. Keine Annotation der Registry entscheidet über die Speicherung (G8).

**Regeln.**

- Stable-Keys werden typisierte Spalten; Development-Keys werden persistiert und tragen ihre Stabilität im Katalog, damit ein Tool-Ergebnis sagen kann, welcher Key sich noch umbenennen darf. Ein Key, den die Registry nicht kennt, wird ebenfalls gespeichert und steht im Katalog als `unknown`.
- Deprecated Keys werden gespeichert, wie sie gesendet wurden; der Ingest normalisiert nicht. Die Nachfolger-Tabelle kommt weiter aus der Registry, nie aus Code, und dient dem Lesen: Ein Tool, das den Nachfolger braucht, leitet ihn beim Lesen ab.
- `weaver registry diff --baseline-registry <alter Pin>` läuft bei jedem Pin-Wechsel und schreibt sein Markdown ins Änderungsprotokoll der Registry.
- Templates: `templates/registry/csharp/*.j2` (Konstanten, Policy, Katalog), `templates/registry/markdown/*.j2` (Dokumentation). Weaver liefert keine C#-Templates mit; nami schreibt eigene, wie GenAI seine.

## G: Registry und Generierung

Diese Invarianten laufen ohne nami-Prozess: Weaver gegen die Registry, Roslyn gegen die generierten Dateien. Sie sind das Gate vor jedem Push, über `./eng/build.sh Check`.

| ID | Invariante | Beweis |
| --- | --- | --- |
| G1 | `weaver registry check --future -r registry` ist grün; die verschachtelte Abhängigkeit nami → genai → core löst sich ohne doppelte Core-Definition auf. | Exit-Code 0; aufgelöstes Schema enthält `http.request.method` genau einmal |
| G2 | `weaver registry generate` ist eine reine Funktion: dieselbe Registry liefert byte-identische Ausgaben für C# und Markdown; das Gate regeneriert und vergleicht. | `git diff --exit-code` nach Regeneration |
| G3 | Der generierte Katalog spiegelt das aufgelöste Schema: Stable-Zähler 125/14/53/1/5 aus Core v1.45.0 (Registry 0.3.0), jede Definition mit ihrer Stabilität; kein Key ohne Herkunft. | Katalog gegen `weaver registry resolve --format json` (bis zur Entfernung des Kommandos; danach `package`) |
| G4 | Jeder `nami.*`-Key ist in der nami-Registry definiert; kein `nami.*`-Literal im C#-Code außerhalb generierter Dateien. | Prüfung aller handgeschriebenen Produkt- und Instrumentierungsquellen ohne Treffer |
| G5 | Deprecated Keys (`exception.escaped`, `db.query.parameter`, `http.request.body.size` u. a.) haben einen Nachfolger-Eintrag in der Nachfolger-Tabelle (zum Lesen) oder sind als „drop" markiert; kein deprecated Key wird als Spalte angelegt. | Tabelle ⊇ `deprecated`-Einträge des Schemas |
| G6 | Enum-Attribute kennen nur Werte des Schemas: `db.system.name` ∈ {mariadb, microsoft.sql\_server, mysql, postgresql} stable, alle anderen Werte development; `http.request.method` ohne `QUERY`. | Generierte Werteklassen gegen Schema |
| G7 | Generierter C#-Code ist AOT-sauber: keine Reflection, kein `dynamic`; `IsAotCompatible=true` mit Warnungen als Fehler. | 0 × IL2xxx/IL3xxx im Build |
| G8 | Die Registry steuert den Ingest nicht: Keine Annotation entscheidet, was gespeichert, umgewandelt oder gekürzt wird (`nami.persisted`, `nami.token_exempt`, `nami.producer`, `nami.instrumentation_scope`, `nami.wire_type`, `nami.wire_container` und `nami.max_utf8_bytes` sind entfernt, ebenso die Definitionen der Claude-Code-Telemetrie). Die verbleibende Registry-Regel: Eine `nami.*`-Definition ohne Brief oder ohne Stabilität bricht die Generierung. Was Claude Code und die Claude API senden, steht ohne nami-Regeln in `docs/telemetry/claude-code-anthropic-reference.md`. | Negativtest: `nami.project.id` ohne Brief |
| G9 | Ein Pin-Wechsel von Core oder GenAI erzeugt einen `weaver registry diff`-Eintrag im Änderungsprotokoll der Registry, sonst schlägt das Gate fehl. | Protokoll-Datei enthält den neuen Commit |

## R: Ingest und Store

Diese Invarianten brauchen den laufenden Collector mit In-Memory-DuckDB; Eingabe ist echtes OTLP, nie ein internes Objekt.

| ID | Invariante | Assertion |
| --- | --- | --- |
| R1 | OTLP/HTTP (protobuf und JSON) und OTLP/gRPC landen in denselben Zeilen; Transport ist nie an einer Zeile ablesbar. | Drei Sendewege, ein Snapshot, `Assert.AreEquivalent` |
| R2 | `watermark` pro Projekt und Signal ist monoton: ein Record mit älterem Zeitstempel hebt sie nicht, senkt sie nicht. | Rückdatierter Record → Wasserlinie unverändert |
| R3 | `last_seen` pro Signal entspricht dem jüngsten **Empfangs**zeitpunkt, nicht dem Record-Zeitstempel; Stille ist damit vom Clock-Skew des Senders unabhängig. | Record mit Zukunfts-Zeitstempel → `last_seen` = jetzt |
| R4 | Keys werden unter dem Namen gespeichert, unter dem sie gesendet wurden: Der Ingest benennt keinen deprecated Key auf seinen Nachfolger um und verwirft weder unbekannte noch deprecated Keys, `baggage.*` oder die Keys eines anderen Senders. Der Katalog nennt die Stabilität jedes gespeicherten Keys (`deprecated`, `unknown`); den Nachfolger leitet ein Tool beim Lesen ab und kennzeichnet ihn als abgeleitet. | `http.method` → als `http.method` gespeichert, kein `http.request.method`; `exception.escaped` gespeichert; keine Umbenennung protokolliert |
| R5 | Stable-Keys landen in typisierten Spalten, Development-Keys im Attribut-Store mit Stabilitätsmarke; beide sind abfragbar. | `gen_ai.usage.input_tokens` abfragbar und als development markiert |
| R6 | Die zwei Eingriffe des Ingests. **Credential-Stufe:** Jeder String, den nami aus einem OTLP-Export speichert (Attribut-Keys und -Werte aller Träger, Array-Elemente, Map-Einträge, Log-Bodies, Span-, Event- und Metriknamen, Statusmeldungen, Scope- und Schema-Text), durchläuft vor jeder Kürzung dieselbe Redaktion; kein Schlüssel und kein Feld ist ausgenommen. Ein erkanntes Credential wird durch `[REDACTED]` ersetzt, der Rest des Strings bleibt. Erkannt werden auch Credentials auf einer Kommandozeile (lange Flags mit Credential-Namen wie `--password x`, `--api-key=x`; das Passwort von `curl -u user:pass`; der an `-p` gehängte Wert von `mysql`, `mariadb`, `mysqldump`; der Wert von `sshpass -p`; ein bloßes `-p` anderswo ist ein Port und bleibt), in einer Argumentliste das Element nach einem solchen Flag, eine Zahl unter einem Credential-Namen (sie wird zu `[REDACTED]`) und Bytes unter einem Credential-Namen (das Attribut wird verworfen und gezählt). Benutzernamen und IDs (`user`, `uid`, `user id`) sind keine Credentials und bleiben. Aus einer URL werden Userinfo, Query und Fragment entfernt; an der Stelle jedes entfernten Teils steht `[REDACTED]`, damit die Entfernung im gespeicherten Wert sichtbar ist; dieselbe Regel gilt für relative Verweise (`/pfad?…`, `/pfad#…`), für Userinfo in URL-kodierten Parametern und für `user:passwort@host:` ohne Schema. Weiter erkannt: PEM-Blöcke privater Schlüssel (von der BEGIN-Zeile bis zur END-Zeile oder zum Textende, auch einzeilig, mit `\n` oder in JSON); Namen aus `.env`-Dateien, die vor oder nach dem Credential-Wort weitergehen (`DB_PASS=`, `MYSQL_PWD=`, `SECRET_KEY_BASE=`, `API_TOKEN_V2=`, `TOKEN_GITHUB=`), nicht aber Namen, deren Fortsetzung sagt, dass der Wert kein Credential ist (`token_count=`, `secret_name=`, `password_policy=`); URL-kodierte Zuweisungen (`token%3D…`); AWS-Secrets neben ihrer Key-ID und nach `aws_secret_access_key`; `Cookie:`- und `Set-Cookie:`-Zeilen; `docker login -p`, `redis-cli -a`, `curl -su`; SQL `PASSWORD '…'` und `IDENTIFIED BY '…'`; `<password>…</password>`; netrc; Kubernetes-`name:`/`value:` über zwei Zeilen; die Token-Formen `AIza…`, `hf_…`, `SG.…` und JWE; Geheimnisse, die nur ihr Ort benennt: das letzte Segment eines Slack-Webhook-Pfads (`/services/T…/B…/<Geheimnis>`, IDs in Großbuchstaben und Ziffern, Geheimnis ab 12 Zeichen), das Token eines Telegram-Bot-Pfads (`/bot<Ziffern>:<Token>`), der Pfadparameter `;jsessionid=`, der Wert eines .NET-Konfigurationseintrags `<add key="…" value="…">`, dessen Key `secret`, `password`, `pwd`, `token` oder `credential` enthält oder auf `key` endet, das Passwort einer pgpass-Zeile (`host:port:datenbank:benutzer:passwort`, genau fünf Felder, Host als Name mit Punkt, Adresse, `localhost` oder `*`), der Wert nach `requirepass` und `masterauth`, der Teil nach dem Doppelpunkt in `echo`/`printf … | base64`, die privaten Member eines JWK (`d`, `p`, `q`, `dp`, `dq`, `qi`, `k` neben `kty`, als JSON und in Fließtext); Namen in Camel-Case, die auf `Pwd`, `Pass` oder `Sig` enden (`DbPwd=`, `smtpPass:`, auch als Attribut-Key und JSON-Member), eigene Header, die auf `auth` enden (`X-Auth:`, `X-Api-Auth:`, `http.request.header.x-auth`), und nach `Bearer`/`Basic` auch ein Wert aus Kleinbuchstaben ab 16 Zeichen. Unter einem Credential-Namen fällt alles weg, wie tief es auch liegt: Maps, Listen, Listen von Maps, Zahlen; das gilt in Attributen, in JSON-Strings (bis zwölf Ebenen JSON in JSON) und in maskiertem JSON in Fließtext. Bytes, die gültiges UTF-8 sind, durchlaufen die Stufe als Text; findet sie ein Credential, wird das Attribut (oder der Body) verworfen und gezählt; Bytes, die kein UTF-8 sind, bleiben. Die Leseseite wendet dieselben Regeln auf Zeilen an, die vor einer Regel gespeichert wurden. Die Stufe liest einen Text eine begrenzte Zahl von Malen, unabhängig von der Zeilenlänge. **Bekannte Falschtreffer** (Sicherheit geht vor): ein Wert nach `token:`, `token =`, `authorization:`, `pass=`, `pwd=`, `cookie:`, `credentials:` auch in Prosa und Code (`var token = tokenizer.Next();` wird zu `var token = [REDACTED];`, `## Authorization: how it works` zu `## Authorization: [REDACTED]`, `PWD=/home/…` zu `PWD=[REDACTED]`, `pass=12` in einer Testzusammenfassung); jede Query und jedes Fragment einer URL, auch ohne Geheimnis; ein Camel-Case-Name auf `Pass`, `Pwd` oder `Sig`, der kein Credential meint (`numPass: 12`, `testPass=ok`; nur `true` und `false` bleiben); ein Konfigurations-Key, der auf `key` endet und öffentlich ist (`<add key="PublicKey" value=…>`); eine Zeile aus fünf Feldern `name.mit.punkt:zahl:wort:wort:wort`, die keine pgpass-Zeile ist; ein Pfad `/services/T…/B…/…` oder `/bot<Ziffern>:…` eines anderen Dienstes; das Wort nach `requirepass` in Prosa; der Teil nach einem Doppelpunkt in jedem `echo … | base64`; ein Wort aus 16 oder mehr Kleinbuchstaben nach `Bearer` oder `Basic`; kurze Member `d`, `p`, `q`, `k` in jedem JSON-Objekt mit `kty`. **Nicht erkannt** (kein Muster): ein Geheimnis als Segment eines URL-Pfads außerhalb der zwei genannten Formen (etwa Discord-Webhooks), ein XML-Eintrag mit `value` vor `key`, mit anderen Attributnamen oder mit maskierten Anführungszeichen in Fließtext, eine pgpass-Zeile mit einem Host ohne Punkt oder einem maskierten Doppelpunkt im Passwort, ein Passwort, das nur Prosa benennt, eine Liste unter dem Plural `tokens` (meist ein Zähler), zusammengeschriebene Namen in Kleinbuchstaben (`dbpwd=`, `userpass=`), Sitzungs-IDs unter anderen Namen als `;jsessionid=` (`session.id` ist ein Schlüssel-Attribut und bleibt), Header der Form `X-Auth-…` mit weiterem Namen, ein Wert aus weniger als 16 Kleinbuchstaben nach `Bearer`/`Basic`, ein JWK als Map-Attribut (statt als JSON-Text) und das Base64-Ergebnis eines `echo user:pass | base64`. **Größengrenzen:** Was ein Wert ist, übernimmt nami aus dem Datenmodell der OpenTelemetry Common Specification (https://opentelemetry.io/docs/specs/otel/common/#anyvalue): AnyValue, Maps, gemischte Arrays, Verschachtelung, leere Werte, Groß- und Kleinschreibung der Keys. Alles davon wie gesendet zu speichern ist namis eigene Entscheidung; die MUSS-Regeln der Spezifikation richten sich dort an APIs und SDKs, nicht an Empfänger. GenAI-Nachrichten sind auf Spans wie auf Events als strukturierter AnyValue definiert; ein JSON-String ist auf Spans nur der Ausweg für Instrumentierungen, die keine strukturierten Werte aufzeichnen können (so die Spans der Claude API). nami speichert beide Formen wie gesendet und wandelt keine in die andere um. Die Namen der semantischen Konventionen bleiben auf v1.45.0. Die Grenzen sind namis Empfängerregel; die Attribut-Limits der Spezifikation sind für SDKs geschrieben und dienen nur als Bezugspunkt. Ein Attributwert behält höchstens 256 KiB dekodierte Größe: bei Strings die UTF-8-Bytes, bei Bytes die rohen Bytes, bei Arrays und Maps die Summe aller verschachtelten Keys und Werte, gezählt beim Lesen. Ein String bis einschließlich 262.144 Bytes bleibt ganz; ein längerer wird an einer Code-Point-Grenze auf höchstens 262.130 Bytes gekürzt und endet auf den Marker `…[truncated]` (14 Bytes), zusammen genau 262.144. Ein einzelnes Surrogat wird vor allem anderen durch U+FFFD ersetzt (drei Bytes, die zählen); der JSON-Writer täte das sonst stillschweigend. Ein Array oder eine Map über der Grenze wird ganz verworfen und gezählt; darüber entscheidet eine einzige Regel (`IsOversizedStructure`, nach der Zusammenführung die des Ingress-Vorscans), und der Platzhalter des Vorscans wird auf jedem Träger genauso behandelt. Ein Bytes-Wert über der Grenze wird ganz verworfen und gezählt, bis ein Record in einem eigenen Feld sagen kann, dass ein Wert gekürzt wurde (N45); ein Marker kann nicht in Binärdaten stehen, und ein gekürzter Wert darf nie vollständig aussehen. Tiefer als 16 Ebenen steht ein leerer Wert. Span, Span-Event, Span-Link, Log-Record und Scope behalten höchstens 128 Attribute. Ein Record behält höchstens 1 MiB Attribute, gemessen an dem, was nami speichert: dem JSON der Attributspalten mit Escaping, Base64, Keys und Struktur, gezählt beim Lesen des Records. Ein Wert innerhalb seiner eigenen Grenze kann deshalb für den Record zu groß sein: 262.144 Steuerzeichen sind 1,5 MB JSON, drei Bytes-Werte von 262.144 Bytes sind 1.048.584 Bytes Base64. Jeder Verlust wird zu `dropped_attributes_count` addiert und im Zähler `nami.collector.attributes.dropped` gezählt. OTLP kennt an Metrik-Datenpunkten und Exemplaren keinen Dropped-Zähler; für dort entfernte Attribute ist namis Zähler der einzige Nachweis. Ein Log-Body ist ein Wert wie jeder andere: Ein String wird an der Grenze gekürzt; ein strukturierter Body innerhalb von 256 KiB dekodierter Größe wird wie gesendet mit seiner Struktur gespeichert, und seine gespeicherte Größe zählt zum Budget des Records; über der Grenze wird er ganz verworfen (leerer Body) und in `dropped_attributes_count` des Log-Records gezählt, dem einzigen Zähler, den ein Log-Record hat. Ersetzt der Ingest einen Wert und behält das Attribut (leerer Wert statt einer Struktur ab Tiefe 16; U+FFFD statt eines einzelnen Surrogats), zählt das der Zähler `nami.collector.values.replaced` einmal pro Wert mit dem Grund `depth` oder `invalid_utf16`; ein Feld am Record dafür steht aus (N45). Passen schon die Prioritäts-Attribute (Session, Token-Verbrauch, Kosten) nicht, wird der Record über Partial Success abgelehnt; sonst bleibt der Record immer erhalten. Eine Grenze gilt für alle Werte; die Statusmeldung eines Spans behält 1024 Bytes. **Einstellungen:** `NAMI_INGEST_MAX_VALUE_BYTES` (262144), `NAMI_INGEST_MAX_ATTRIBUTES` (128), `NAMI_INGEST_MAX_RECORD_BYTES` (1048576), `NAMI_INGEST_MAX_VALUE_NESTING` (16, höchstens 28), definiert in `IngestLimits`. Die HTTP- und gRPC-Empfänger reichen die gesetzten Werte an den Konverter weiter; die Speicherabbildung schreibt, was der Konverter begrenzt hat, und kennt keine eigene Grenze. Claude Code kürzt Inhalte selbst nach `CLAUDE_CODE_OTEL_CONTENT_MAX_LENGTH`, gezählt in UTF-16-Code-Units (Standard 61.440, also höchstens 184.320 UTF-8-Bytes), mit eigenem Marker; nami zählt UTF-8-Bytes. Wer den Wert in Claude Code über 87.376 Code-Units anhebt, hebt `NAMI_INGEST_MAX_VALUE_BYTES` mit an (drei Bytes pro Code-Unit plus 14), sonst kürzt nami ein zweites Mal. **Worin die Regel von den SDK-Limits der Spezifikation abweicht:** (1) UTF-8-Bytes statt Zeichen; (2) Tiefe 16 statt 64 (die Leser scheitern bei 29); (3) das Budget von 1 MiB pro Record umfasst auch Resource- und Metrik-Attribute; (4) eine deterministische Reduktion mit den Prioritäts-Keys zuerst (`anthropic.session.id`, `session.id`, `ccr.session.id`, `gen_ai.usage.*`, `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_creation_tokens`, `cache_creation_5m_tokens`, `cache_creation_1h_tokens`, `cost_usd`, `cost_usd_micros`), dann das kleinste Attribut zuerst nach seiner Größe auf dem Draht, dann nach Key, unabhängig von der Sendereihenfolge; (5) namis eigener In-Band-Marker an einem gekürzten Wert, den die Spezifikation nicht definiert. | Genau 262.144 gespeicherte Bytes; Mehrbyte-Zeichen an der Schnittstelle; Struktur über der Grenze verworfen und gezählt; drei Reihenfolgen → byte-identische Zeile; Prioritäts-Keys überstehen 1 MiB; Prioritäts-Überlauf → Record abgelehnt; in jedes Feld gepflanztes Credential → in keiner gespeicherten Zeile |
| R7 | Exceptions werden aus **beiden** Quellen erkannt: Log-Record mit `event_name == exception` (stabil) und Span-Event `exception`; `errors_recent` gruppiert sie gleich. | Zwei Sendewege, eine Gruppe |
| R8 | Resource-Pflicht: `service.name` und `telemetry.sdk.name/version/language`; eine Resource ohne `service.name` wird als eigenes Projekt-Problem gezählt, nicht verworfen. | Zähler `resource.missing_service_name` steigt |
| R9 | Projekt-Scoping: Der authentifizierte Ingest-Key bestimmt die Ablage; ein `nami.project.id` in der Resource muss zu dieser Bindung passen. Reads bleiben in der authentifizierten Projektbindung; Toolargumente und unbestätigte Metadaten wählen kein Projekt. | Cross-Project-Read liefert 0 Zeilen |
| R10 | Retention ist der einzige Löschpfad; kein Tool und kein Endpunkt löscht Telemetrie. | Kein Delete-Handler außerhalb von Retention |

## S: Vertrag und Serialisierung

Der Vertrag ist spec-first: Records (Produkttypen) und Unions (Summentypen, diskriminiert über `kind`), sprachunabhängig, mit einer reinen Funktion von Spezifikation zu Artefakten. Die Spezifikation steht in TypeSpec. Sie erzeugt C#- und TypeScript-Typen sowie JSON-Schemas; weitere unterstützte Sprachen verwenden die dafür vorgesehenen Generatoren. JSON/YAML sind mögliche Schema-Ausgabeformate, keine zusätzliche Anwendungslaufzeit.

| ID | Invariante | Assertion |
| --- | --- | --- |
| S1 | Jede Union ist exhaustiv: C# `switch` und TypeScript-Exhaustiveness-Check decken alle Cases ab; eine zusätzliche Variante bricht nicht aktualisierte Verbraucher beim Build. | Compile-Time-Test über `CSharpCompilation` |
| S2 | Der Diskriminator heißt `kind`, trägt den Variantennamen aus der Spezifikation und steht auf oberster Ebene (kein Envelope). | JSON enthält `"kind":"<variant>"` top-level |
| S3 | Round-Trip erhält die Bedeutung über C# und TypeScript. Kanonische Ausgabe ist innerhalb einer Implementierung deterministisch; Property-Reihenfolge ist keine sprachübergreifende Semantik. | Gemeinsame Fixtures strukturell vergleichen; kanonische Ausgabe separat bytegenau prüfen |
| S4 | C#-Produktserialisierung läuft über generiertes `JsonSerializerContext`/`JsonTypeInfo<T>` ohne Reflection-Fallback. TypeScript validiert mit den generierten Schemas. | Jeder C#-Vertragstyp hat Metadaten; beide Seiten validieren dieselben Fixtures |
| S5 | Zeit ist Wire-Text: RFC 3339 mit `T` und `Z` in beliebiger Schreibung, Offset mit Doppelpunkt, keine Schaltsekunde; Schreibweise, Offset und Nachkommastellen bleiben erhalten (`ContractDateTime`). Relative Angaben (`-15m`) werden an der Tool-Grenze in dieselbe Form aufgelöst. | `[DataRow]` über Schreibweisen; `-15m` → absolutes Fenster |
| S6 | `OptionalValue<T>` unterscheidet Abwesenheit von explizitem `null`; `default` heißt abwesend. | Drei Zustände, drei Serialisierungen |
| S7 | Ganzzahlen auf dem Draht sind exakt darstellbar; OTLP-64-Bit und Nanosekunden bleiben kanonische Dezimal-Strings; String-Enums nehmen keine Ordinale. | Grenzwerte 2⁵³±1, `"1"` ≠ `1` |
| S8 | Fehler sind Werte: `ToolError { code, message, retryable, field, allowed, hint }`; `field` und `allowed` nennen das Argument und was es annimmt; `not_found` nennt `resource_type` und `resource_id`. Kein Tool wirft. | Jede Refusal-Klasse hat genau eine Fehlerform |
| S9 | Vertrags-Revision ist der SHA-256 der semantischen Projektion; Dokumentation und Versionsnummer ändern sie nicht. MCP-Schicht und Engine prüfen die Revision beim Verbindungsaufbau; ein Mismatch wird eindeutig abgewiesen und nie als leeres Analyseergebnis dargestellt. Die Revision bleibt als Fingerabdruck in `_meta`. | Kommentaränderung → Revision gleich |

## T: Toolvertrag

Sechs Lese-Tools für Agenten, unabhängig von Zielanwendung und Agentenhost. Wire-Namen sind snake_case und bleiben in allen Connector-/Plugin-Varianten identisch.

| Tool (Wire-Name) | Frage | Argumente | Analysefunktion |
| --- | --- | --- | --- |
| `sources_status` | Sendet die untersuchte Anwendung überhaupt? | `service` | `check_telemetry_health` |
| `errors_recent` | Welche Fehler, gruppiert nach Typ und Operation, mit `first_seen`, `count`, einem Beispiel-`trace_id` | `service`, `since` | `triage` + `get_failure_context` |
| `traces_get` | Dieser eine Trace, projiziert auf `fields` | `trace_id`, `fields?` | `get_trace` |
| `traces_slowest` | Der langsame Tail, nicht der Durchschnitt | `service`, `operation?`, `since` | `list_traces` (sortiert) + `aggregate_telemetry` |
| `logs_search` | Diese Logzeile, literal | `query`, `service`, `since` | `search_logs` |
| `changes_recent` | Was hat sich seit wann geändert | `service`, `since` | `list_releases` + `ci_log`, aus `vcs.*`, `cicd.*` und `deployment.*` der Resource |

Jedes Tool nimmt zusätzlich `fields`, `max_bytes` und relative Zeiten (`-15m`). Jedes Ergebnis trägt den Freshness-Block, `ToolCoverage` um die Wasserlinie erweitert:

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
| T3 | Stateless auf MCP-Ebene: Snapshots, Wasserlinien und Beobachtungen sind explizit referenzierter Anwendungszustand. Persistierte Snapshots bleiben nach Neustart der zuständigen Engine mit demselben Store lesbar; eine fremde Instanz ohne diesen Store darf keinen Erfolg vortäuschen. | Neustart zwischen zwei Calls → gleiche Antwort |
| T4 | `max_bytes` gilt für `structuredContent`; 500.000 Spans im Fenster bleiben darunter, `truncated: true` und `next_cursor` setzen fort. | Byte-Länge ≤ `max_bytes` |
| T5 | Leer und `complete: true` ist eine Aussage; leer und `complete: false` ist „nach der Verzögerung erneut fragen" und wird als solches gerendert. Nie ein leeres `complete`-Ergebnis für ein Fenster jenseits der Wasserlinie. | Fensterende > `watermark` → `complete == false` |
| T6 | `fields` projiziert; ein unbekanntes Feld ist `ToolError` mit `field` und `allowed`, nicht stilles Weglassen. | Unbekanntes Feld → Fehler nennt die erlaubten |
| T7 | Kein Roh-Dump: Ergebnisse tragen Gruppen, Ränge, Beispiel-IDs; vollständige Spans nur über `traces_get` mit `fields`. | `errors_recent` enthält keinen Stacktrace |
| T8 | `logs_search` ist literal: Anführungszeichen, Backslashes und Nicht-ASCII matchen wie getippt; kein Regex, kein Case-Folding. | `"\\"` und `ü` als Suchtext |
| T9 | Jede Antwort: `content[0]` Headline mit Top-Zeilen, IDs und dem nächsten Tool; `content[1]` das JSON; `structuredContent` dasselbe Objekt; `outputSchema` deklariert. | Drei Teile, eine Quelle |
| T10 | Alle Tools `readOnlyHint: true`, `idempotentHint: true`, `destructiveHint: false`, `openWorldHint: false`; `tools/list` deterministisch sortiert mit `ttlMs` und `cacheScope`. | Manifest-Snapshot gegen Verhalten |
| T11 | Profiles und Baggage antworten `DataUnavailable` mit Grund; kein leerer Erfolg für ein Signal ohne Ziel. | `code == "data_unavailable"` |
| T12 | `traceparent` aus `_meta` wird extrahiert; der Tool-Span ist Kind des Agenten-Spans; fehlendes `_meta` → neuer Root, kein Fehler. | Parent-Beziehung; Root ohne Parent |
| T13 | `since` ist halboffen `[start, now)` gegen die Wasserlinie; ein Record exakt bei `start` zählt, einer jenseits der Wasserlinie nie. | Randwerte |

## E2E-Harness

Der Harness verwendet ausschließlich MSTest.Sdk 4.5.2 auf der nativen Microsoft.Testing.Platform
2.5.2, gesteuert durch `./eng/test.sh` und das Fallout-Gate. Gemeinsame Hilfen werden einmal in
`Nami.Tests` kompiliert. Es gibt drei ausführbare Suiten nach Beweisart:

| Projekt | Beweist | Läuft |
| --- | --- | --- |
| `Nami.Tests` | Gemeinsame OTLP-Daten, Prozess- und MCP-Hilfen | Bibliothek, keine Testanwendung |
| `Nami.UnitTests` | Reine Vertrags-, Serialisierungs- und Werteprüfungen | MSTest/MTP mit generierter Discovery |
| `Nami.IntegrationTests` | Registry/Compiler, echte DuckDB-Persistenz, Tool-Verträge und Diagnose-Einstiege | MSTest/MTP; reale Integrationen |
| `Nami.EndToEndTests` | HTTP/protobuf/gRPC → Host → DuckDB → MCP sowie Snapshot-Replay nach Neustart | MSTest/MTP gegen TypeScript-MCP und veröffentlichte NativeAOT-Engine |

Der frühere Konsolen-Conformance-Lauf ist als `NativeConformanceTests` Teil von EndToEnd. Die
Prüfung der drei OTLP-Signale, aller sechs Tools, der Freshness, der Inhaltsparität und des
persistierten Snapshot-Replays bleibt erhalten. Es gibt keinen eigenen Test-`Main` und keinen
VSTest-Ausweichpfad. Reflexionsabhängige Compiler-/Vertragsprüfungen bleiben im verwalteten
MSTest-Harness; die Produktgrenze wird durch den tatsächlich gestarteten TypeScript-MCP-Prozess samt veröffentlichter nativer Engine bewiesen. Sprachwechsel ersetzen diese Nachweise nicht.

**Regeln.**

- Testklassen sind `sealed`; benötigter `TestContext` wird über den Primär-Konstruktor injiziert.
  Jeder unterstützte asynchrone Call erhält `context.CancellationToken`.
- Jede ausführbare Suite besitzt ein eigenes `testconfig.json`. Unit zeigt vor allem Fehler,
  Integration Fehler und übersprungene Fälle, EndToEnd alle Ergebnisse und erfasste Ausgabe.
  Kooperative Standardfristen betragen 30/120/300 Sekunden; vorhandene testspezifische Fristen
  bleiben erhalten, einschließlich 600 Sekunden für den realen 500.000-Spans-Nachweis.
- `[DependsOn]` verbindet nur Tests derselben Assembly. Registry-/Build-Prüfungen, die gemeinsame
  Ausgaben neu erzeugen, werden nicht parallel zu anderen Integrationsfällen ausgeführt.
  Der All-Aufruf führt Unit → Integration → EndToEnd aus und stoppt beim ersten Fehler.
- Jeder Lauf prüft MTP-Erkennung und Restore-Zustand, explizite Mindest-Testzahlen und frische TRX-
  Ergebnisse. Fehlende, übersprungene oder zu wenige Fälle sind kein erfolgreicher Gate-Lauf.
- `TestContext.TestTempDirectory` besitzt veränderliche Testdaten; Prozesse, Ports und Datenbanken
  haben pro Fall eigene Lebenszeiten. Echte OTLP-Daten sind Testeingaben, niemals Produktionstelemetrie.
- Golden-Dateien werden mit expliziten Werten, Byte-/Sequenzvergleichen oder struktureller
  `JsonElement.DeepEquals`-Parität geprüft. Keine Assertion wird für AOT oder bequemere Ausgabe
  abgeschwächt; benötigte Diagnoseartefakte werden erhalten.

Der Harness sampelt oder filtert keine angefragten Tests stillschweigend. Der Skripteinstieg,
Ergebnisverzeichnisse und die Grenzen von `--no-build`/`--test-modules` stehen in `docs/testing.md`.

## Testmethoden-Katalog

Namensschema `Subjekt_Bedingung_ErwartetesErgebnis`; eine Klasse pro Invariantenfamilie. Die Assertion-Spalte ist das, was die Methode beweist.

**`RegistryGateTests` (G)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `Registry_Check_PassesWithFuture` | G1 | Exit-Code 0, keine `violation` |
| `Registry_NestedCoreDependency_ResolvesOnce` | G1 | `http.request.method` genau einmal im aufgelösten Schema |
| `Generate_TwiceSameRegistry_IsByteIdentical` | G2 | `Assert.AreSequenceEqual(bytes1, bytes2)` für C# und Markdown |
| `Catalog_MatchesResolvedRegistry` | G3 | Zähler 125/14/53/1/5; jede Definition mit `stability` |
| `NamiKeys_AppearOnlyInGeneratedFiles` | G4 | Alle handgeschriebenen Produkt-/Instrumentierungsquellen ohne `nami.`-Literal |
| `DeprecatedKeys_HaveSuccessorOrDrop` | G5 | `[DataRow]` über `exception.escaped`, `db.query.parameter`, `http.request.body.size` |
| `EnumValues_MatchSchemaStability` | G6 | `redis` development, `postgresql` stable, `QUERY` fehlt |
| `GeneratedCode_AotAnalyzers_ReportNothing` | G7 | 0 × IL2xxx/IL3xxx |
| `NamiKeyWithoutBrief_FailsGeneration` | G8 | Exit-Code ≠ 0 und `nami_brief_required` |
| `PinChange_WithoutDiffEntry_FailsGate` | G9 | Fehlermeldung nennt den neuen Commit |

**`IngestStoreTests` (R)**

| Methode | Inv. | Assertion |
| --- | --- | --- |
| `Ingest_ThreeTransports_SameRows` | R1 | `[CombinatorialData]` protobuf/JSON/gRPC, ein Snapshot |
| `Watermark_BackdatedRecord_DoesNotMove` | R2 | Wasserlinie vorher == nachher |
| `LastSeen_FutureTimestamp_UsesReceiptTime` | R3 | `last_seen` ≈ jetzt, nicht 2030 |
| `Ingest_DeprecatedKey_StoredUnderTheNameItWasSent` | R4 | Original gespeichert, kein Nachfolger geschrieben, Katalog nennt `deprecated` |
| `Ingest_DevelopmentKey_StoredWithStabilityMark` | R5 | `gen_ai.usage.input_tokens` abfragbar, `stability == development` |
| `Value_AStringOverTheCap_IsStoredAsExactlyTheCap_CutBetweenCharacters` | R6 | genau 262.144 Bytes, Schnitt zwischen Zeichen, für jeden Key |
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
| `RoundTrip_EachCase_IsByteIdentical` | S3 | Kanonische Ausgabe innerhalb eines Serializers bytegenau; sprachübergreifende Bedeutungsparität zusätzlich nach I8 |
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
| `ProfilesOrBaggage_ReturnsDataUnavailable` | T11 | `code == "data_unavailable"` |
| `IncomingMeta_Traceparent_ParentsToolSpan` | T12 | `ParentSpanId == agentSpanId` |
| `Since_Boundaries_HalfOpenAgainstWatermark` | T13 | Record bei `start` drin, jenseits Wasserlinie draußen |

Wurzeln: `Catalog_MatchesResolvedRegistry` in der Registry-Assembly, `Catalog_EmbeddedConstants_MatchRegistrySnapshot` in der Collector-Assembly; `[DependsOn]` zeigt innerhalb jeder Assembly auf ihre Wurzel. Scheitert der Pin, fällt alles kontrolliert aus.

## I: Instrumentierung und Beobachtungsnachweis

Diese Kriterien gelten für den ausdrücklich unterstützten Umfang. Jede Modellprüfung ist datengetrieben über das vollständige generierte Manifest; eine handgeschriebene Auswahl einiger Beispiele genügt nicht. Ein fehlender Nachweis bleibt offen. Vorhandene Tests werden nicht durch Umbenennung als neue Beweise gezählt.

| ID | Testmethode | Verbindlicher Nachweis |
| --- | --- | --- |
| I1 | `Marker_ResolvesToExactlyOnePinnedSemanticContract` | Jede erlaubte Kennung ergibt genau eine Definition mit Registry-Version, Definitionsart und Signalzuordnung. Unbekannte Kennungen und widersprüchliche Kombinationen erzeugen eine Build-Diagnose. |
| I2 | `StablePolicy_RejectsUnstableAndDeprecatedContractMembers` | Bei Stable-only-Auswahl erfüllen Modell, referenzierte Attribute und eingeschränkte Enum-Werte die Stable-Regel. Offene String-Werte werden nicht zu geschlossenen Enums umgedeutet. |
| I3 | `DeclaredTargets_HaveExecutableCoverageOrAnExplicitExclusion` | Jede Manifest-Kennung besitzt eine ausgeführte Probe oder eine konkrete Ausschlussbegründung. Nicht unterstützte Aufrufformen und bereits kompilierte Abhängigkeiten gelten nicht automatisch als erfasst. |
| I4 | `Interceptor_PreservesInvocationSemantics` | Receiver und Argumente werden gleich oft und in derselben Reihenfolge ausgewertet; die Operation läuft genau einmal. Rückgabewert, Seiteneffekte, Exception und Cancellation bleiben erhalten. |
| I5 | `Interceptor_ObservesCompletionIncludingAwaitAndEnumeration` | Async-Beobachtung endet nach tatsächlichem Abschluss. Unterstützte Streams/deferred Enumerationen werden während ihrer vereinbarten Enumerationslebenszeit beobachtet. Jeder Abschlussweg beendet genau einmal. |
| I6 | `ConcurrentOperations_PreserveParentageAndRestoreAmbientContext` | Verschachtelte Aufrufe erhalten ihre Parent-Beziehung; parallele Aufrufe übernehmen keinen fremden Kontext. Erfolg, Fehler und Abbruch stellen den vorherigen Kontext wieder her. |
| I7 | `EveryModel_EmitsItsDeclaredTelemetryShape` | Alle unterstützten Modelle erzeugen die deklarierte Signalform, Span-Kind/Instrumenttyp, Einheit, Attributtypen sowie Resource-/Scope-Zuordnung. Log-Ereignis und Span-Ereignis bleiben unterscheidbar; Resource-Modelle erzeugen kein eigenes Signal. |
| I8 | `DiscriminatedContracts_RoundTripAcrossEnabledImplementations` | Gemeinsame Fixtures werden von C# und TypeScript gleich interpretiert: `kind`, Pflichtfelder, Zahlenbereiche, Abwesenheit/null und unbekannte Varianten. Property-Reihenfolge ist kein Unterschied. |
| I9 | `UnionBoundaries_HandleDefaultAndUnknownCasesExplicitly` | Leerer/default Union-Zustand, fehlender Discriminator und unbekannte Variante werden nach Vertrag abgewiesen oder ausdrücklich modelliert; nie stillschweigend zu Erfolg oder einer anderen Signalform umgewandelt. |
| I10 | `PublishedNativeAotBinary_CompletesTheObservationPipeline` | Die veröffentlichte Engine startet ohne installiertes .NET und CLR-Profiler. Ein echter Tool-Aufruf durch den veröffentlichten TypeScript-MCP-Prozess belegt Instrumentierung, Export, Ingestion und Abfrage einschließlich aller benötigten JSON-Fälle. |
| I11 | `ObservationLifetime_IsIsolatedAndReferencedExplicitly` | Zugehörige Requests adressieren die Beobachtung per expliziter projektgebundener Kennung. Andere Clients/Projekte erhalten keinen fremden Zustand; Reconnects und konkurrierende Requests verändern die Zuordnung nicht. |
| I12 | `ObservationLifetime_ExpiresOnlyAccordingToConfiguredPolicy` | Kontrollierte Uhr prüft direkt vor, auf und nach der Grenze; absolute und Inaktivitätsfrist, Erneuerung und explizites Beenden werden getrennt geprüft. Wiederholtes Beenden ist folgenlos. |
| I12 | `Observation_ContinuesBeyondFifteenMinutes_WhenConfigured` | Eine länger konfigurierte Lebenszeit wird nicht nach 15 Minuten durch einen versteckten Standard oder Request-Timeout beendet. |
| I13 | `Observation_ReportsCorrectDurationAndBoundedVisibilityDelay` | Kontrollierte Operation belegt Dauer/Einheit und monotone Zeitmessung trotz Wanduhränderung. Unter dokumentierter kontrollierter Last liegen angenommene Daten spätestens nach einer Sekunde abfragbar vor; die Messung weist Erzeugung, Exportannahme und Sichtbarkeit getrennt aus. |
| I14 | `RecordingPolicy_CapturesEveryEligibleOperation` | Bei vollständiger Erfassung sind alle vorgesehenen Quellen vor dem ersten Aufruf aktiv und alle bekannten Operationen erfasst, auch mit nicht gesampeltem eingehendem Parent. Ausfälle/Drops werden nach I17 gezählt, nicht als Sampling verborgen. |
| I15 | `MetricExport_PreservesAggregatesTemporalityAndDimensions` | Bekannte Messfolgen ergeben erwartete Counts, Summen, Buckets bzw. Gauges samt Temporality und Dimensionen. Aggregation wird geprüft; ein Export-Datenpunkt pro Methodenaufruf wird nicht vorausgesetzt. |
| I16 | `Operation_IsCorrelatedFromExecutionThroughQuery` | Eindeutige Operation besitzt Ausführungsevidenz, Telemetrie, Exportannahme und korrektes Abfrageergebnis. Trace-/Span-IDs bleiben erhalten. Fachlicher, Protokoll- und Exportfehler werden getrennt dargestellt. |
| I17 | `ExportFailures_AccountForRetriesRejectionsAndDrops` | Retry-fähige Fehler folgen der Transportregel; permanente Ablehnungen und Drops sind sichtbar. OTLP-Partial-Success wiederholt nicht blind den gesamten Batch. Verlorene Antworten dürfen Duplikate erzeugen; deren Behandlung entspricht dem dokumentierten Ingest-Vertrag. |
| I18 | `SelfIngestion_DoesNotCreateUnboundedTelemetryRecursion` | Ein definierter Eingang erzeugt eine erwartete endliche Menge Selbsttelemetrie, keine fortlaufende Export-/Ingest-Schleife. |
| I18 | `Shutdown_DrainsAcceptedTelemetryOrReportsTheRemainder` | Exporter und Ingestion werden in der richtigen Reihenfolge beendet. Angenommene Daten sind verarbeitet oder als verbleibend/fehlgeschlagen ausgewiesen; ein Flush-Timeout ist kein Erfolg. |

**MSTest-Lifecycle.** Vor jedem E2E-Fall werden isolierte Konfiguration, Testkennung, Store und bereite Empfänger hergestellt. Danach läuft eine kontrollierte Operation. Der Test wartet auf eine konkrete Abschlussbedingung mit Deadline und baut erst danach ab; feste Sleeps ersetzen keine Evidenz. Unit/Integration übernehmen Manifest-, Compiler-, Uhr- und Fehlersimulationen, EndToEnd die veröffentlichte Prozesskette. TypeScript-Fixtures und Compilerchecks werden durch diesen Harness ausgeführt; ein neuer Test-`Main` oder ein zweites .NET-Testsystem wird nicht eingeführt.

**Grenze des Versprechens.** OTLP garantiert keine Ende-zu-Ende-Persistenz und keine duplikatfreie Zustellung. Ein fehlender Span ist deshalb nicht automatisch eine nie ausgeführte Operation. Die Produktantwort unterscheidet Ausführung, Erfassung, Exportannahme und Sichtbarkeit. Ein Bestehen von I10 oder ein erfolgreicher Flush ersetzt die Verlustnachweise nicht.

**Wiederholte Zustellung.** nami bestätigt einen Export erst nach seinem Schreibvorgang. Geht die Antwort verloren (abgebrochene Verbindung, Ende des Stop-Budgets), hat der Sender keine Bestätigung und sendet denselben Export nach OTLP erneut; nami nimmt also at-least-once an. Derselbe Export, erneut gesendet, wird dabei nicht doppelt gespeichert, weil jede Zeile über das geschlüsselt ist, was ihren Datensatz ausmacht:

| Signal | Schlüssel der Zeile | Wirkung der zweiten Zustellung |
| --- | --- | --- |
| Spans | Projekt, Trace-ID, Span-ID | Die Zeile wird durch denselben Span ersetzt. |
| Metrik-Datenpunkte | Projekt, Serie und `observation_id` (Hash aus Zeitstempel, Startzeit, Flags, Wert, Exemplaren, Instrument-Metadaten und den Dropped-Zählern von Resource und Scope) | Die Beobachtung ist schon vorhanden und wird übergangen; Punkt und Exemplare bleiben, wie sie sind. |
| Log-Records | Projekt und `log_id` (Hash über den vollständigen Inhalt des Records samt Resource und Scope und über seine Stelle im Request) | Die Zeile wird durch denselben Record ersetzt. |

Ein Log-Record trägt in OTLP keine Kennung. nami speichert Telemetrie, wie sie gesendet wurde: Zwei Records eines Requests, die in jedem Feld gleich sind, sind zwei Zeilen. Was sie unterscheidet, ist ihre Stelle im Request, gezählt über alle Log-Records des Requests, und nur daran erkennt nami auch eine Wiederholung. Derselbe Request, erneut gesendet, fügt deshalb nichts hinzu. Ein Sender, der vor der Wiederholung neu bündelt, sendet die Records an anderen Stellen: Sie werden ein zweites Mal gespeichert. Für Logs gilt bei neu gebündelter Wiederholung also at-least-once mit Duplikaten. Zeilen, die vor dieser Regel gespeichert wurden, behalten ihren Schlüssel; Records mit Zeit hatten darin keine Stelle, ihre Wiederholung über die Umstellung hinweg wird einmal doppelt gespeichert. Die einzige Spur einer folgenlosen zweiten Zustellung ist die Wasserlinie des Signals (`signal_watermarks`): Sie nennt den letzten Empfang, und eine Wiederholung ist ein Empfang. Nachweis: `RedeliveryTests.Redelivery_OfAStoredExport_AddsNoRowAndChangesNone` für alle drei Signale, mit einem Neustart der Engine zwischen den Zustellungen, und `RedeliveryTests.Redelivery_OfLogRecordsAtOtherPlaces_StoresThemAgain`.

## D: MCP-Auslieferung und private Engine-Anbindung

| ID | Testmethode | Verbindlicher Nachweis |
| --- | --- | --- |
| D1 | `AgentHosts_ExposeTheSameReadOnlyToolContract` | Claude.ai-Connector, OpenAI-Plugin/MCP-Bundle und lokale MCP-Anbindung deklarieren dieselben sechs Toolformen, Fehler und Read-only-Annotationen. Reale Host-Kompatibilität wird separat zu lokalen Protokolltests dokumentiert; Zugangsmangel ist kein bestandener Test. |
| D2 | `PrivateStream_RejectsContractMismatchBeforeDispatch` | Ungleiche Revisionen werden vor einer Analyse erkannt; kein fremdes Schema und kein leeres Erfolgsresultat passieren die Grenze. |
| D3 | `PrivateStream_FramesConcurrentRequestsAndBoundsBuffers` | Fragmentierte und zusammengefasste Reads, konkurrierende IDs, große Nachrichten und langsame Empfänger behalten ihre Zuordnung; Größenlimits und Backpressure begrenzen Speicher. |
| D4 | `PrivateStream_CancellationAndProcessExitCompletePendingCalls` | Abbruch wird weitergegeben; Engine-Ende beendet wartende Calls mit definiertem Fehler und hinterlässt keine verwaisten Prozesse. |
| D5 | `PrivateStream_PreservesAuthenticatedProjectAndTraceContext` | Interne Projektbindung stammt aus Authentifizierung, nicht Toolargumenten. Reconnects, manipulierte Kennungen und parallele Projekte geben weder Daten noch Trace-Kontext weiter. |
| D6 | `ReleaseBundle_StartsMcpAndNativeEngineWithDeclaredDependencies` | Ein veröffentlichter Release startet TypeScript und NativeAOT-Engine mit den deklarierten Abhängigkeiten. Diagnoseausgabe verfälscht weder MCP-stdout noch private Frames; kein installiertes .NET erforderlich. |

## Entscheidungen und Risiken

Die Nutzerkorrektur vom 24.10.2026 ersetzt widersprechende frühere Architekturentscheidungen. Agenten dokumentieren Entscheidungen und offene Nachweise; Schweigen gilt nicht als Zustimmung zu einem neuen Produktumfang. Implementierungsstatus steht in der Checkliste, Beweisergebnisse in den Integrations-/Verifikationsunterlagen.

| Nr. | Frage | Entscheidung | Begründung |
| --- | --- | --- | --- |
| 1 | Gemeinsamer Vertrag | TypeSpec bleibt; C#-, TypeScript- und JSON-Schema-Ausgaben sind erforderlich. | Ein sprachunabhängiger Vertrag mit Unions und expliziten Wire-Namen; keine parallelen handgeschriebenen Modelle. |
| 2 | MCP-Implementierung | TypeScript auf Bun mit MCP-TS-SDK v2. | Vorhandene MCP-Muster und agententauglicher Entwicklungsablauf; C# bleibt Engine. |
| 3 | Engine und Instrumentierung | .NET 11 NativeAOT, gepinnter RC3-Stand bis zum geprüften GA-Wechsel; C#-Interceptors beim Build. | Vorhandener Collector und generierte Wrapper bleiben nutzbar; neue Interceptors benötigen einen Build. Die angekündigte GA ist am 10.11.2026, kein ungeprüfter Pin-Wechsel. |
| 4 | Interne Verbindung | Private langlebige Streams/Pipes mit TypeSpec-Nachrichten, begrenzten Puffern und Vertragsprüfung. | Kein internes HTTP erforderlich; tatsächliche Latenz/Allokationen sind zu messen. |
| 5 | Oberfläche und Zielgruppe | Ausschließlich Agenten; Claude.ai-Connector und/oder OpenAI-Plugin mit MCP-Bundle, optional lokale Clients. | Entwickler autorisieren Debugging ihrer Anwendungen; keine menschliche Analyseoberfläche. |
| 6 | Analyseumfang | Die sechs bestehenden Read-only-Tools bleiben. Kein zusätzliches Metriken-Abfragetool in dieser Fassung. | Instrumentierungs- und Metriknachweise gelten trotzdem; ein weiteres Tool benötigt eigene Anforderungen statt einer Beschränkung auf eine Beispielanwendung. |
| 7 | Beobachtungszustand | Explizite projektgebundene Kennung und konfigurierte Lebenszeit, unabhängig von MCP-Verbindung und Request-Timeout. | Zustandsbehaftete Beobachtung bei stateless MCP; keine pauschale 15-Minuten-Grenze. |

| Risiko / offene Arbeit | Nachweis / Behandlung |
| --- | --- |
| Der TypeScript-MCP-/NativeAOT-Stack ist implementiert; Anwendungsinstrumentierung, Beobachtungslebenszeit und vollständige I/D-Nachweise sind noch offen. | [Checkliste](../checklist.md) und [Anforderungsmatrix](../integration/requirement-evidence.md) trennen vorhandene Implementierung, konkrete Beweise und fehlenden Abschluss. |
| Zwei Artefakte können inkompatibel veröffentlicht werden. | Gemeinsamer Release, deterministische Generierung und Revisionsprüfung vor Dispatch; D2. |
| Private Streams können blockieren, Frames vermischen oder verwaiste Prozesse hinterlassen. | D3/D4/D6 prüfen Framing, Speichergrenzen, Cancellation und Prozess-Lifecycle. |
| Connector-/Plugin-Paketierung und Authentifizierung unterscheiden sich zwischen Hosts. | Hostbezogene Integration und D1; keine hypothetische Single-Binary- oder C#-Pflicht. |
| Generierter Wrapper deckt nicht jede Sprache oder Aufrufform ab. | Manifest und explizite Ausschlüsse I1–I7; keine universelle Auto-Instrumentierungsbehauptung. |
| Ungültige Union-Zustände, große Ganzzahlen und null/abwesend unterscheiden sich zwischen Sprachen. | Gemeinsame Fixtures I8/I9 und S; generierte AOT-Metadaten, keine stillen Defaults. |
| Verlust, Partial-Success und Shutdown verfälschen Vollständigkeit. | I13–I18 und Freshness; bestätigte Annahme ist kein automatischer Persistenzbeweis. |
| Development-Konventionen ändern sich. | Gepinnte Quellen, Weaver-Diff und stabile Auswahl pro Modell/Attribut/Enum; keine globale Gleichsetzung von Registry und Stable-only-Instrumentierung. |

Die beobachtbare Zusage lautet: **Dieser veröffentlichte Stack beobachtet diesen deklarierten Funktionsumfang über diese konfigurierte Lebenszeit mit nachgewiesener Zuordnung, Genauigkeit und Verlustbehandlung.** Die Nachweise geben keine pauschale Aufwandsschätzung vor.

### Änderungsprotokoll

| Datum | Änderung |
| --- | --- |
| 2026-11-16 | Absatz „Wiederholte Zustellung" unter I ergänzt (Ledger N62, N105): Was nami speichert, wenn ein gespeicherter Export erneut eintrifft, je Signal; Nachweis `RedeliveryTests`. Entscheidung der Leitung vom 16.11.2026: Der Schlüssel eines Log-Records enthält seine Stelle im Request; gleiche Records eines Requests sind eigene Zeilen, eine neu gebündelte Wiederholung speichert doppelt; die Wasserlinie rückt mit jeder Zustellung vor. |
| 2026-11-16 | R6 erweitert (Ledger N77, vierte Runde der Credential-Stufe): Slack-Webhook- und Telegram-Bot-Pfade, `;jsessionid=`, .NET-Konfigurationseinträge `<add key value>`, pgpass-Zeilen, `requirepass`/`masterauth`, `echo … \| base64`, private JWK-Member, Camel-Case-Namen auf `Pwd`/`Pass`/`Sig`, Header auf `auth`, Kleinbuchstaben-Werte ab 16 Zeichen nach `Bearer`/`Basic`; die Listen der bekannten Falschtreffer und der nicht erkannten Formen sind angepasst. Tests: `Stage_FurtherShapes_AreRedacted_AndTheRestStays` (19 neue Zeilen), Korpus in `CredentialShapes`, `Tools_RowsStoredBeforeARuleOfTheFourthRound_ShowNoSecret`, drei Zeilen im 4-MiB-Budget. |
| 2026-11-16 | Abschnitt 4.1 ergänzt (Entscheidung des Beobachters vom 16.11.2026, 04:35): nami unterstützt Windows nicht nativ. Nativ unterstützt sind macOS und Linux; unter Windows läuft nami in WSL2 (der Linux-Build) oder als Linux-Container unter Docker Desktop. Keine der Invarianten G, R, S, T, I und D ändert sich. |
| 2026-11-16 | R6 erweitert (Befund der Evaluation vom 16.11.2026 an 588d081, auf Grundlage der Regel des Beobachters von 01:15: kein Token und kein Passwort wird gespeichert oder gezeigt): PEM-Blöcke; Bytes durchlaufen die Stufe; alles unter einem Credential-Namen fällt weg, in jeder Tiefe; `.env`-Namen; AWS-Paare; relative und kodierte URLs; die Leseseite folgt dem Ingest; lineare Laufzeit auf langen Zeilen; bekannte Falschtreffer und nicht erkannte Formen stehen in R6. Tests `CredentialShapeTests` (Korpus `CredentialShapes`). |
| 2026-11-16 | R6 ergänzt (Entscheidung der Leitung vom 16.11.2026 zum Review Request, Punkte 9d und 10, auf Grundlage von E1 und Byte-Regel 3 des Beobachters): Ein strukturierter Log-Body innerhalb der Wertgrenze wird wie gesendet gespeichert statt als gekürzter Text; über der Grenze wird er verworfen und gezählt. Festgehalten: Datenpunkte und Exemplare haben in OTLP keinen Dropped-Zähler. Test `Body_AStructureWithinTheCap_IsStoredAsSent_AndOneOverItIsDroppedAndCounted`. |
| 2026-11-16 | R6 erweitert (Entscheidungen der Leitung vom 16.11.2026 zum Review Request, auf Grundlage der Regel des Beobachters von 01:15: kein Token und kein Passwort wird gespeichert oder gezeigt): Die Credential-Stufe erkennt Credentials auf Kommandozeilen, Zahlen und Bytes unter Credential-Namen und das Argument nach einem Credential-Flag; `user`, `uid` und `user id` gelten nicht mehr als Credentials; entfernte URL-Teile hinterlassen `[REDACTED]` an ihrer Stelle. Tests `Redact_ACredentialOnACommandLine_IsReplaced_AndTheRestStays`, `Convert_NumbersBytesAndArgumentsThatAreCredentials_AreRemoved`. |
| 2026-11-16 | R6 ergänzt (Entscheidungen des Beobachters vom 16.11.2026, 02:47, zu Bytes und Ersetzungen): neuer Zähler `nami.collector.values.replaced` mit `nami.value.replacement.reason` (`depth`, `invalid_utf16`) für Werte, die der Ingest ersetzt, ohne das Attribut zu verwerfen; Test `Replaced_Values_AreCountedOncePerValue_ByReason`. |
| 2026-11-16 | G8 geändert (Beobachter-Entscheidung 16.11.: Telemetrie wird gespeichert wie gesendet; Credential-Stufe und Größengrenzen sind die einzigen Eingriffe beim Ingest): Die Persistenz-Policy aus Annotationen entfällt mit den sieben Annotationen, ihren Rego-Regeln, dem generierten Code (`RegistryPolicy`, `ValueLimits`, `Producers`, `WireContainers`) und den Claude-Code-Definitionen (Registry 0.5.0). G8 hält jetzt fest, dass die Registry den Ingest nicht steuert und dass eine `nami.*`-Definition Brief und Stabilität braucht. Test `PolicyNamingUnpersistedKey_FailsGeneration` und `TokenExemption_ForUnpersistedKey_FailsGeneration` ersetzt durch `NamiKeyWithoutBrief_FailsGeneration`. Die Referenz der Sender-Telemetrie liegt in `docs/telemetry/claude-code-anthropic-reference.md`. |
| 2026-11-16 | R6 neu gefasst (Entscheidung des Beobachters vom 16.11.2026, 02:00, zu den Größengrenzen; Beobachter-Entscheidung 16.11.: Telemetrie wird gespeichert wie gesendet; Credential-Stufe und Größengrenzen sind die einzigen Eingriffe beim Ingest): 256 KiB pro Attributwert (ganze Struktur, gespeicherte Kodierung), 128 Attribute pro Record, Tiefe 16 mit leerem Wert statt Ablehnung des Records, 1 MiB Attribute pro Record, deterministische Reduktion mit Prioritäts-Keys (Entscheidungen von 02:25 und 02:28, berichtigt 02:40: das Datenmodell stammt aus der Common Specification, die Grenzen sind namis Empfängerregel und weichen in fünf Punkten von den SDK-Limits ab). Die Einzelgrenzen (`exception.*` 128 Ki/32 KiB mit `[SANITIZATION_FAILED]`, `error` 1 KiB, `full_command` 4 KiB, Log-Body 64 KiB) gehen in der einen Grenze auf; die Statusmeldung bleibt bei 1024 Bytes. Bytes-Werte über der Grenze werden ganz verworfen. Die Zeile `Sanitizer_Boundaries_HoldAtPlusMinusOne` der Testtabelle ist durch die Tests der Größengrenzen ersetzt. |
| 2026-11-16 | R6 ergänzt (Entscheidung des Beobachters vom 15.11.2026, am 16.11. für jeden Sender bestätigt): Die Statusmeldung eines Spans wird für jeden Sender auf 1024 Bytes UTF-8 gekürzt, nicht nur unter den Scopes von Claude Code. Der Log-Body ist seit jeher auf 64 KiB begrenzt (Speicherschicht). |
| 2026-11-16 | R6 erweitert (Regel des Beobachters vom 16.11.2026, 01:15: nami speichert und zeigt kein Token und kein Passwort; Beobachter-Entscheidung 16.11.: Telemetrie wird gespeichert wie gesendet; Credential-Stufe und Größengrenzen sind die einzigen Eingriffe beim Ingest): Die Credential-Redaktion gilt für jeden gespeicherten String statt nur für `exception.message` und `exception.stacktrace`; eine Ausnahme pro Schlüssel gibt es nicht, auch `nami.token_exempt` ist keine. Ein Wert unter einem Exception-Text-Key, der kein String ist, bleibt, wie er gesendet wurde. Die Grenzwerte für Exception-Text (128 Ki, 32 KiB) bleiben bis zur Größengrenzen-Regel. Mustererkennung senkt das Risiko und ist keine Garantie. |
| 2026-11-16 | Abschnitt 4 (Export → Speicherung) und Absatz „`nami.*`-Konventionen" geändert (Beobachter-Entscheidung 16.11.: Telemetrie wird gespeichert wie gesendet; Credential-Stufe und Größengrenzen sind die einzigen Eingriffe beim Ingest): Inhalts- und Identitäts-Keys werden gespeichert, wie sie gesendet wurden; das kehrt die Inhaltsregel (Finish-Line 5, „nie die Argumente") um. Typen bleiben wie gesendet (kein Umwandeln von Zeichenketten in die deklarierten Typen), Container wie `tool_parameters` bleiben die Zeichenkette, die sie sind, und Event-Name-Feld, Body und `event.name`-Attribut bleiben, wie sie ankommen. `nami.persisted` wird beim Ingest nicht mehr gelesen; G8 folgt mit der Bereinigung der Registry. |
| 2026-11-16 | R4 geändert (Beobachter-Entscheidung 16.11.: Telemetrie wird gespeichert wie gesendet; Credential-Stufe und Größengrenzen sind die einzigen Eingriffe beim Ingest): Keys werden unter ihrem gesendeten Namen gespeichert. Die Umbenennung von deprecated Keys beim Ingest und die Allowlist für unbekannte Keys, deprecated Keys, `baggage.*` und Sender-eigene Keys entfallen; `DeprecatedAttributeNormalizer` ist gelöscht, `attribute_discovery` wird nicht mehr beschrieben. Die Regeln in Abschnitt „Regeln" (deprecated Keys, unbekannte Keys) sind angepasst. |
| 2026-11-15 | Hinweis ohne Änderung des genehmigten Textes: Die Nutzerentscheidungen vom 10.11.2026 ([0001 bis 0003](../decisions/README.md)) haben Vorrang vor jeder Nennung von OpenAI in dieser Fassung (Abschnitte 1, 3, 4.1 und 4.2, Zeile D1, Entscheidung 5 und Quellen). nami erreicht Claude als Remote-Connector; ein OpenAI-Plugin gehört nicht zu nami. D1 gilt damit für den Claude-Connector und die lokale MCP-Anbindung. Der Nachweis mit den Claude-Clients steht aus. |
| 2026-10-24 | Nutzerkorrektur: TypeScript-MCP und C#-Engine mit privatem Stream-Vertrag; TypeSpec einschließlich TS-Ausgabe bleibt; Claude.ai/OpenAI als Agentenoberflächen; tomo ist ein Entwicklerbeispiel. Compile-Time-Instrumentierung, Beobachtungslebenszeit und I1–I18/D1–D6 mit Nachweisen aufgenommen; frühere Single-Binary-/C#-MCP-Festlegung ersetzt. |
| 2026-10-24 | Historisch, durch die obige Nutzerkorrektur ersetzt: Die drei offenen Entscheidungen getroffen: TypeSpec bleibt, .NET 11 RC2 jetzt, kein Metriken-Tool. Regel aufgenommen: Entscheidungen trifft der bauende Agent, Einspruch per Kommentar. Abschnitte 3 und S angepasst. |
| 2026-10-24 | Migration auf Nami mit TypeScript-MCP, C#-Engine, TypeSpec-Verträgen und eigener Weaver-Registry. |
| 2026-10-23 | Review der observe\_runtime-Fassung: sieben Korrekturen, drei Ergänzungen; Intro von Gesprächsform auf PRD umgestellt. |
| 2026-10-23 | Erste Fassung als observe\_runtime-Invarianten und Testspezifikation. |

### Quellen

Normative technische Referenzen (Abruf 24.10.2026; ersetzen keine ausgeführten Produktnachweise):

- [Roslyn Interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md): Bindung unterstützter Aufrufstellen beim Build.
- [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): native Veröffentlichung ohne JIT oder separat installiertes .NET.
- [.NET-11-Bibliotheken](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries): Speicher-Streams, Diagnostics und JSONL-Serialisierung mit generierten Metadaten.
- [MCP 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/basic#statelessness): expliziter Zustand über Requests und unabhängige Transportbindung.
- [TypeSpec Discriminated Types](https://typespec.io/docs/standard-library/discriminated-types/): deklarierte Varianten und Wire-Discriminator.
- [OTel Trace SDK](https://opentelemetry.io/docs/specs/otel/trace/sdk/), [Metrikdatenmodell](https://opentelemetry.io/docs/specs/otel/metrics/data-model/), [OTLP](https://opentelemetry.io/docs/specs/otlp/): Sampling, Aggregation, Export und Verlustgrenzen.
- [Claude-Connectoren](https://claude.com/docs/connectors/building) und [Directory-Auslieferung](https://claude.com/docs/directory/publish): Remote-MCP und Plugin-Paketierung; OpenAI-Details werden bei der hostbezogenen Implementierung gegen dessen aktuelle Anforderungen geprüft.

Historische Herkunft:

- [nami @ e025215](https://github.com/ANcpLua/nami) (`claude/debugging-tomo.md`, inzwischen auf das korrigierte Entwicklerbeispiel und diese PRD ausgerichtet)
- [semantic-conventions v1.44.0](https://github.com/open-telemetry/semantic-conventions/releases/tag/v1.44.0) und [main 3e258047](https://github.com/open-telemetry/semantic-conventions/commit/3e2580475b1fa266fe85a1d1ea7fe9c41a4783b5); Stable-Inventar aus dem Handoff vom 23.10.2026
- [semantic-conventions-genai 9ffc7e00, model/manifest.yaml](https://github.com/open-telemetry/semantic-conventions-genai/blob/9ffc7e00c639be7712e544e3e54331e885dccf84/model/manifest.yaml)
- Weaver 0.28.0: „Define your own telemetry schema" und CLI-Referenz (beiliegende Dokumente)
- [MCP 2026-07-28 Key Changes](https://modelcontextprotocol.io/specification/2026-07-28/changelog)
- [MCP C# SDK v2: Stateless and stateful mode](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html), [Beta SDKs for the 2026-07-28 release candidate](https://blog.modelcontextprotocol.io/posts/sdk-betas-2026-07-28/)
- [OTel Semantic Conventions for MCP](https://opentelemetry.io/docs/specs/semconv/gen-ai/mcp)
- OpenTelemetry Spec Compliance Matrix (beiliegend; Spalte .NET, Abschnitt Logs)
- MSTest 4.5.1 API-Referenz (modern-mstest Skill)
