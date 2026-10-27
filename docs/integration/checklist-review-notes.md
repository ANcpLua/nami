# Historische Rückfragen zur Contract-Checkliste

Die folgenden Rückfragen standen am 27. Oktober 2026 inline in `docs/checklist.md`
(Quellstand `a3f4e74`). Sie bleiben hier unverändert erhalten, während die Checkliste
wieder den geprüften Implementierungsstand zeigt. Dieser Auszug ist historische
Diskussionsgrundlage, kein neuer Arbeitsauftrag und keine Wiederaufnahme alter Migrationen.

Die inzwischen geklärten Begriffe stehen im
[Request-/Response-Review, Abschnitt 5](../prd/nami-request-response-review.md#5-was-eine-request-response-grenze-leisten-muss):
sechs Analyse-Tools, gemeinsam verwendete Typen, private Prozessverbindung, Nachrichtengrenzen
und Vertragsrevision. Die Revision erkennt versehentlich unterschiedlich erzeugte Prozessartefakte;
sie verpflichtet das unveröffentlichte Produkt nicht zu Abwärtskompatibilität.

[Abschnitt 4](../prd/nami-request-response-review.md#4-modelle-und-bedeutung-über-alle-grenzen)
erklärt Varianten, Präsenz und exakte Zahlen. Änderungen gehören in TypeSpec/Emitter und werden
regeneriert. `eng/build.sh Contracts` erzeugt die C#-/TypeScript-Ausgaben und Schemas;
der Paritätscheck vergleicht die reproduzierbar erzeugten Dateien mit dem eingecheckten Stand.
`JsonTypeInfo<T>` ist ein Metadatenmechanismus und nicht exklusiv eine .NET-11-Neuerung.
Die Benennung allein belegt keine Verwendung einer neuen Optimierung.

Die frühere Behauptung, projektgebundene Zustandskennungen seien vollständig erledigt,
ist korrigiert: Snapshots existieren, die Beobachtungslebenszeit I11/I12 ist offen.
Rust ist keine Nami-Komponente. Die aktuellen Nachweise und Grenzen stehen in der
[Anforderungsmatrix](requirement-evidence.md).

## Unveränderter Abschnitt aus der damaligen Checkliste

## 2. Contract generation — `contracts/`, `internal/nami.contracts/`

- [x] Maintain the TypeScript emitter for the six nami input/result pairs and shared types./// warum sind es genau six input result pairs und was ist shared types?
      Output: `src/nami.mcp/src/contracts/generated/`; generated files are never edited by hand.  /// wieso sollte man generierten code ändern statt den generator?
- [x] Model private request/result framing and explicit project-bound state identifiers in TypeSpec. /// warum wird hier privat und von framing gesprochen? was hat es mit dem explicit project bound state identifierst für einen hintergedanken?
      Define tool payloads and wire names in TypeSpec; update consumers together when they change. /// was soll preserved werden es gab nie etwas das tool payloads oder wire names in production genutzt geschweige den überhaupt jemals released wurde nami ist in privat auf github LOL, das prd wird wie eine regeln enforced bitte nicht solange wir für ordnung gesorgt haben
- [x] Generate one semantic revision for both sides; reject incompatible private peers before dispatch. /// was ist semantic revision, von welchen both sides wird hier gesprochen, was wird rejected von woher und warum? bahnhof:incompatible private peers before dispatch
- [ ] Preserve `kind`, required fields, absent/null distinctions and exact integer encodings across
      C# and TypeScript (S, I8/I9, D2). /// alles was mit rust zu tun hat ignoriere das ist falsch info rust ist ein anderes projekt das wie nami ist nur in rust und rust only nichts hat mit nami zu tun und kannst du extrahieren in riderprojects/rustgraveyard(folder erstellen) danach die frage warum muss kind require fields absent null distincation and exact integer encodings across c# typescript für welche dinge getan werden?
- [x] Extend `./eng/build.sh Contracts` and parity checks to include the TypeScript artifacts. /// was ist dieses eng/build.sh contracts und für was ist es erstellt worden, parity checks sind was und welche typescript artifacts wird hier gesprochen?
- [x] Keep C# serialization AOT-safe with generated `JsonTypeInfo<T>`; validate TS inputs and outputs
      with the same generated JSON Schemas. /// ist das jsontypeinfo<T> die neue .NET 11 system.text api? wenn nein wir wollen die neue api dafür folgende skills laden /Users/alexandernachtmann/.codex/plugins/cache/dotnet-agent-skills/dotnet11/0.1.2/skills/process-api-net11 /Users/alexandernachtmann/.codex/plugins/cache/dotnet-agent-skills/dotnet11/0.1.2/skills/system-text-json-net11
