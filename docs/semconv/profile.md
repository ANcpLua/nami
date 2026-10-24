# `profile` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `profile.frame.type` | `enum` | development |  | Describes the interpreter or compiler of a single frame. | `"cpython"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `profile.frame.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `beam` | development |  | [Erlang](https://en.wikipedia.org/wiki/BEAM_(Erlang_virtual_machine)) |
| `cpython` | development |  | [Python](https://wikipedia.org/wiki/Python_(programming_language)) |
| `dotnet` | development |  | [.NET](https://wikipedia.org/wiki/.NET) |
| `go` | development |  | [Go](https://wikipedia.org/wiki/Go_(programming_language)), |
| `jvm` | development |  | [JVM](https://wikipedia.org/wiki/Java_virtual_machine) |
| `kernel` | development |  | [Kernel](https://wikipedia.org/wiki/Kernel_(operating_system)) |
| `luajit` | development |  | [LuaJIT](https://en.wikipedia.org/wiki/LuaJIT) |
| `native` | development |  | Can be one of but not limited to [C](https://wikipedia.org/wiki/C_(programming_language)), [C++](https://wikipedia.org/wiki/C%2B%2B), [Go](https://wikipedia.org/wiki/Go_(programming_language)) or [Rust](https://wikipedia.org/wiki/Rust_(programming_language)). If possible, a more precise value MUST be used. |
| `perl` | development |  | [Perl](https://wikipedia.org/wiki/Perl) |
| `php` | development |  | [PHP](https://wikipedia.org/wiki/PHP) |
| `ruby` | development |  | [Ruby](https://wikipedia.org/wiki/Ruby_(programming_language)) |
| `rust` | development |  | [Rust](https://wikipedia.org/wiki/Rust_(programming_language)) |
| `v8js` | development |  | [V8JS](https://wikipedia.org/wiki/V8_(JavaScript_engine)) |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
