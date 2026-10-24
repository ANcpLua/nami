# `pprof` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `pprof.location.is_folded` | `boolean` | development |  | Provides an indication that multiple symbols map to this location's address, for example due to identical code folding by the linker. In that case the line information represents one of the multiple symbols. This field must be recomputed when the symbolization state of the profile changes. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.mapping.has_filenames` | `boolean` | development |  | Indicates that there are filenames related to this mapping. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.mapping.has_functions` | `boolean` | development |  | Indicates that there are functions related to this mapping. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.mapping.has_inline_frames` | `boolean` | development |  | Indicates that there are inline frames related to this mapping. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.mapping.has_line_numbers` | `boolean` | development |  | Indicates that there are line numbers related to this mapping. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.profile.comment` | `string[]` | development |  | Free-form text associated with the profile. This field should not be used to store any machine-readable information, it is only for human-friendly content. | `["hello world","bazinga"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.profile.doc_url` | `string` | development |  | Documentation link for this profile type. | `"http://pprof.example.com/cpu-profile.html"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.profile.drop_frames` | `string` | development |  | Frames with Function.function_name fully matching the regular expression will be dropped from the samples, along with their successors. | `"/foobar/"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.profile.keep_frames` | `string` | development |  | Frames with Function.function_name fully matching the regular expression will be kept, even if it matches drop_frames. | `"/bazinga/"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.scope.default_sample_type` | `string` | development |  | Records the pprof's default_sample_type in the original profile. Not set if the default sample type was missing. | `"cpu"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `pprof.scope.sample_type_order` | `int[]` | development |  | Records the indexes of the sample types in the original profile. | `[3,0,1,2]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
