# `openai` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `openai.api.type` | `enum` | development |  | The type of OpenAI API being used. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `openai.request.service_tier` | `enum` | development |  | The service tier requested. May be a specific tier, default, or auto. | `"auto"`, `"default"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `openai.response.service_tier` | `string` | development |  | The service tier used for the response. | `"scale"`, `"default"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `openai.response.system_fingerprint` | `string` | development |  | A fingerprint to track any eventual change in the Generative AI environment. | `"fp_44709d6fcb"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |

### `openai.api.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `chat_completions` | development |  | The OpenAI [Chat Completions API](https://developers.openai.com/api/reference/chat-completions/overview). |
| `responses` | development |  | The OpenAI [Responses API](https://developers.openai.com/api/reference/responses/overview). |

### `openai.request.service_tier` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `auto` | development |  | The system will utilize scale tier credits until they are exhausted. |
| `default` | development |  | The system will utilize the default scale tier. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
