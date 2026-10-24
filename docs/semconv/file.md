# `file` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `file.accessed` | `string` | development |  | Time when the file was last accessed, in ISO 8601 format. | `"2021-01-01T12:00:00Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.attributes` | `string[]` | development |  | Array of file attributes. | `["readonly","hidden"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.changed` | `string` | development |  | Time when the file attributes or metadata was last changed, in ISO 8601 format. | `"2021-01-01T12:00:00Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.created` | `string` | development |  | Time when the file was created, in ISO 8601 format. | `"2021-01-01T12:00:00Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.directory` | `string` | development |  | Directory where the file is located. It should include the drive letter, when appropriate. | `"/home/user"`, `"C:\\Program Files\\MyApp"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.extension` | `string` | development |  | File extension, excluding the leading dot. | `"png"`, `"gz"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.fork_name` | `string` | development |  | Name of the fork. A fork is additional data associated with a filesystem object. | `"Zone.Identifier"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.group.id` | `string` | development |  | Primary Group ID (GID) of the file. | `"1000"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.group.name` | `string` | development |  | Primary group name of the file. | `"users"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.inode` | `string` | development |  | Inode representing the file in the filesystem. | `"256383"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.lock.mechanism` | `string` | development |  | The lock mechanism such as noted by [POSIX](https://pubs.opengroup.org/onlinepubs/9699919799/functions/fcntl.html) | `"POSIX"`, `"FLOCK"`, `"DELEG"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.lock.mode` | `string` | development |  | Mode of lock or operation such as documented by [POSIX](https://pubs.opengroup.org/onlinepubs/9699919799/functions/fcntl.html) | `"ADVISORY"`, `"MANDATORY"`, `"BREAKING"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.lock.type` | `enum` | development |  | The lock type as represented by i.e. [POSIX](https://pubs.opengroup.org/onlinepubs/9699919799/functions/fcntl.html)'s l_type. | `"read"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.mode` | `string` | development |  | Mode of the file in octal representation. | `"0640"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.modified` | `string` | development |  | Time when the file content was last modified, in ISO 8601 format. | `"2021-01-01T12:00:00Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.name` | `string` | development |  | Name of the file including the extension, without the directory. | `"example.png"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.owner.id` | `string` | development |  | The user ID (UID) or security identifier (SID) of the file owner. | `"1000"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.owner.name` | `string` | development |  | Username of the file owner. | `"root"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.path` | `string` | development |  | Full path to the file, including the filename. It should include the drive letter, when appropriate. | `"/home/alice/example.png"`, `"C:\\Program Files\\MyApp\\myapp.exe"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.size` | `int` | development |  | File size in bytes. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `file.symbolic_link.target_path` | `string` | development |  | Path to the target of a symbolic link. | `"/usr/bin/python3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `file.lock.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `read` | development |  | `read`. |
| `write` | development |  | `write`. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
