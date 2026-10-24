# `gen_ai` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `gen_ai.agent.description` | `string` | development |  | Free-form description of the GenAI agent provided by the application. | `"Helps with math problems"`, `"Generates fiction stories"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.agent.id` | `string` | development |  | The unique and stable identifier of the GenAI hosted agent resource. | `"asst_5j66UpCpwteGg4YSxUnt7lPY"`, `"arn:aws:bedrock:us-east-1:123:agent/42"`, `"urn:agent:projects-123:projects:123:locations:us-east1:aiplatform:reasoningEngines:456"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.agent.name` | `string` | development |  | Human-readable name of the GenAI agent provided by the application. | `"Math Tutor"`, `"Fiction Writer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.agent.version` | `string` | development |  | The version of the GenAI agent. | `"1.0.0"`, `"2025-05-01"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.conversation.compacted` | `boolean` | development |  | Indicates whether the effective conversation context used for this operation is a compacted view of a prior conversation. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.conversation.id` | `string` | development |  | The unique identifier for a conversation (session, thread), used to store and correlate messages within this conversation. | `"conv_5j66UpCpwteGg4YSxUnt7lPY"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.data_source.id` | `string` | development |  | The data source identifier. | `"H7STPQYOND"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.embeddings.dimension.count` | `int` | development |  | The number of dimensions the resulting output embeddings should have. | `512`, `1024` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.evaluation.explanation` | `string` | development |  | A free-form explanation for the assigned score provided by the evaluator. | `"The response is factually accurate but lacks sufficient detail to fully address the question."` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.evaluation.name` | `string` | development |  | The name of the evaluation metric used for the GenAI response. | `"Relevance"`, `"IntentResolution"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.evaluation.score.label` | `string` | development |  | Human readable label for evaluation. | `"relevant"`, `"not_relevant"`, `"correct"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.evaluation.score.value` | `double` | development |  | The evaluation score returned by the evaluator. | `4.0` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.input.messages` | `any` | development |  | The chat history provided to the model as an input. | `"[\n {\n \"role\": \"user\",\n \"parts\": [\n {\n \"type\": \"text\",\n \"content\": \"Weather in Paris?\"\n }\n ]\n },\n {\n \"role\": \"assistant\",\n \"parts\": [\n {\n \"type\": \"tool_call\",\n \"id\": \"call_VSPygqKTWdrhaFErNvMV18Yl\",\n \"name\": \"get_weather\",\n \"arguments\": {\n \"location\": \"Paris\"\n }\n }\n ]\n },\n {\n \"role\": \"tool\",\n \"parts\": [\n {\n \"type\": \"tool_call_response\",\n \"id\": \"call_VSPygqKTWdrhaFErNvMV18Yl\",\n \"response\": \"rainy, 57°F\"\n }\n ]\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.main_agent.description` | `string` | development |  | The free-form description of the top-level Generative AI agent in the process. | `"Helps with math problems"`, `"Generates fiction stories"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.main_agent.id` | `string` | development |  | The unique and stable identifier of the top-level Generative AI agent in the process | `"asst_5j66UpCpwteGg4YSxUnt7lPY"`, `"arn:aws:bedrock:us-east-1:123:agent/42"`, `"urn:agent:projects-123:projects:123:locations:us-east1:aiplatform:reasoningEngines:456"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.main_agent.name` | `string` | development |  | The human-readable name of the top-level Generative AI agent in the process. | `"Math Tutor"`, `"Fiction Writer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.memory.query.text` | `string` | development |  | The search query used to retrieve memories. | `"user dietary preferences"`, `"past flight bookings"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.memory.record.count` | `int` | development |  | The number of memory records relevant to the operation. | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.memory.record.id` | `string` | development |  | The unique identifier of the memory record. | `"mem_5j66UpCpwteGg4YSxUnt7lPY"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.memory.records` | `any` | development |  | The memory records stored or retrieved in a memory operation. | `"[\n {\n \"content\": \"User prefers dark mode\",\n \"id\": \"mem_123\",\n \"score\": 0.95\n },\n {\n \"content\": {\n \"preference\": \"vegetarian meals\",\n \"confidence\": 0.9\n },\n \"metadata\": {\n \"source\": \"profile\"\n }\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.memory.store.id` | `string` | development |  | The unique identifier of the memory store. | `"ms_abc123"`, `"user-preferences-store"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.operation.name` | `enum` | development |  | The name of the operation being performed. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.output.messages` | `any` | development |  | Messages returned by the model where each message represents a specific model response (choice, candidate). | `"[\n {\n \"role\": \"assistant\",\n \"parts\": [\n {\n \"type\": \"text\",\n \"content\": \"The weather in Paris is currently rainy with a temperature of 57°F.\"\n }\n ]\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.output.type` | `enum` | development |  | Represents the content type requested by the client. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.prompt.name` | `string` | development |  | The name of the prompt that uniquely identifies it. | `"analyze-code"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.prompt.variable` | `template[string]` | development |  | The variables supplied to the prompt template, the `<key>` being the variable name, the value being the variable value. | `"Alice"`, `"French"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.prompt.version` | `string` | development |  | The version of the prompt template used. | `"1.0.0"`, `"2025-05-01"`, `"prod"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.provider.name` | `enum` | development |  | The Generative AI provider as identified by the client or server instrumentation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.choice.count` | `int` | development |  | The target number of candidate completions to return. | `3` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.encoding_formats` | `string[]` | development |  | The encoding formats requested in an embeddings operation, if specified. | `["base64"]`, `["float","binary"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.frequency_penalty` | `double` | development |  | The frequency penalty setting for the GenAI request. | `0.1` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.max_tokens` | `int` | development |  | The maximum number of tokens the model generates for a request. | `100` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.model` | `string` | development |  | The name of the GenAI model a request is being made to. | `"gpt-4"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.presence_penalty` | `double` | development |  | The presence penalty setting for the GenAI request. | `0.1` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.previous_response.id` | `string` | development |  | The unique identifier of a previous response or interaction used to provide context for the current operation. | `"resp_0123456789aBCdef"`, `"interaction-123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.reasoning.level` | `string` | development |  | The reasoning or thinking effort level requested for a GenAI model. | `"low"`, `"medium"`, `"high"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.seed` | `int` | development |  | Requests with same seed value more likely to return same result. | `100` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.stop_sequences` | `string[]` | development |  | List of sequences that the model will use to stop generating further tokens. | `["forest","lived"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.stream` | `boolean` | development |  | Indicates whether the GenAI request was made in streaming mode. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.stream_cursor` | `string` | development |  | The cursor identifying the last streamed event already received, used to resume a streamed response from that position. | `"42"`, `"event-abc123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.temperature` | `double` | development |  | The temperature setting for the GenAI request. | `0.0` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.top_k` | `int` | development |  | The top-K sampling setting for the GenAI request: restricts token generation at each step to the K most likely next tokens. | `40` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.request.top_p` | `double` | development |  | The top_p sampling setting for the GenAI request. | `1.0` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.response.finish_reasons` | `string[]` | development |  | Array of reasons the model stopped generating tokens, corresponding to each generation received. | `["stop"]`, `["stop","length"]`, `["stop","length","error"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.response.id` | `string` | development |  | The unique identifier for the completion. | `"chatcmpl-123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.response.model` | `string` | development |  | The name of the model that generated the response. | `"gpt-4-0613"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.response.status` | `enum` | development |  | The lifecycle status of a generated response, as reported by the provider when the response is fetched or polled. | `"completed"`, `"in_progress"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.response.time_to_first_chunk` | `double` | development |  | Time to first chunk in a streaming response, measured from request issuance, in seconds. The value is measured from when the client issues the generation request to when the first chunk is received in the response stream. | `0.5`, `1.2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.retrieval.documents` | `any` | development |  | The documents retrieved. | `"[\n {\n \"id\": \"doc_123\",\n \"score\": 0.95\n },\n {\n \"id\": \"doc_456\",\n \"score\": 0.87\n },\n {\n \"id\": \"doc_789\",\n \"score\": 0.82\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.retrieval.query.text` | `string` | development |  | The query text used for retrieval. | `"What is the capital of France?"`, `"weather in Paris"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.retrieval.top_k` | `int` | development |  | The maximum number of documents the retriever was asked to return for the query (also known as `k`, `limit`, or `max_num_results`). | `5` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.skill.description` | `string` | development |  | The [description of the Agent Skill](https://agentskills.io/specification#skill-md-format). | `"Review a changelist against the team's review policy."` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.skill.name` | `string` | development |  | The name of the [Agent Skill](https://agentskills.io/specification#skill-md-format). | `"code-review"`, `"pdf-processing"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.skill.resource.name` | `string` | development |  | The skill-relative name of the resource (script, reference, or asset) being accessed or executed. | `"references/review_policy.md"`, `"assets/report_template.html"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.skill.source.uri` | `string` | development |  | The source URI for loading the skill. | `"https://skills.example.com/code-review"`, `"gs://example-skills/code-review"`, `"file:///opt/skills/code-review"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.system_instructions` | `any` | development |  | The system message or instructions provided to the GenAI model separately from the chat history. | `"[\n {\n \"type\": \"text\",\n \"content\": \"You are an Agent that greet users, always use greetings tool to respond\"\n }\n]\n"`, `"[\n {\n \"type\": \"text\",\n \"content\": \"You are a language translator.\"\n },\n {\n \"type\": \"text\",\n \"content\": \"Your mission is to translate text in English to French.\"\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.token.modality` | `enum` | development |  | The modality of the tokens being counted. | `"text"`, `"image"`, `"audio"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.tool.call.arguments` | `any` | development |  | Parameters passed to the tool call. | `"{\n \"location\": \"San Francisco?\",\n \"date\": \"2025-10-01\"\n}\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.call.id` | `string` | development |  | The tool call identifier supplied by the model or provider. | `"call_mszuSIzqtI65i1wAUOE8w5H4"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.call.result` | `any` | development |  | The result returned by the tool call (if any and if execution was successful). | `"{\n \"temperature_range\": {\n \"high\": 75,\n \"low\": 60\n },\n \"conditions\": \"sunny\"\n}\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.definitions` | `any` | development |  | The list of tool definitions available to the GenAI agent or model. | `"[\n {\n \"type\": \"function\",\n \"name\": \"get_current_weather\",\n \"description\": \"Get the current weather in a given location\",\n \"parameters\": {\n \"type\": \"object\",\n \"properties\": {\n \"location\": {\n \"type\": \"string\",\n \"description\": \"The city and state, e.g. San Francisco, CA\"\n },\n \"unit\": {\n \"type\": \"string\",\n \"enum\": [\n \"celsius\",\n \"fahrenheit\"\n ]\n }\n },\n \"required\": [\n \"location\",\n \"unit\"\n ]\n }\n }\n]\n"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.description` | `string` | development |  | The tool description. | `"Multiply two numbers"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.name` | `string` | development |  | Name of the tool utilized by the agent. | `"Flights"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.tool.type` | `string` | development |  | Type of the tool utilized by the agent | `"function"`, `"extension"`, `"datastore"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.usage.audio.cache_read.input_tokens` | `int` | development |  | The number of audio input tokens served from a provider-managed cache. | `60` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.audio.input_tokens` | `int` | development |  | The number of audio input tokens. | `120` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.audio.output_tokens` | `int` | development |  | The number of audio output tokens. | `240` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.cache_read.input_tokens` | `int` | development |  | The number of input tokens served from a provider-managed cache. | `50` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.usage.cache_write.input_tokens` | `int` | development |  | The number of input tokens written to a provider-managed cache. | `25` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.image.cache_read.input_tokens` | `int` | development |  | The number of image input tokens served from a provider-managed cache. | `128` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.image.input_tokens` | `int` | development |  | The number of image input tokens. | `258` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.image.output_tokens` | `int` | development |  | The number of image output tokens. | `1290` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.input_tokens` | `int` | development |  | The number of tokens used in the GenAI input (prompt). | `100` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.usage.output_tokens` | `int` | development |  | The number of tokens used in the GenAI response (completion). | `180` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.usage.reasoning.output_tokens` | `int` | development |  | The number of output tokens used for reasoning (e.g. chain-of-thought, extended thinking). | `50` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |
| `gen_ai.usage.text.cache_read.input_tokens` | `int` | development |  | The number of text input tokens served from a provider-managed cache. | `40` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.text.input_tokens` | `int` | development |  | The number of text input tokens. | `100` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.usage.text.output_tokens` | `int` | development |  | The number of text output tokens. | `180` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"`, `nami.token_exempt`: `true` |
| `gen_ai.workflow.name` | `string` | development |  | Human-readable name of the GenAI workflow provided by the application. | `"multi_agent_rag"`, `"customer_support_pipeline"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/gen-ai-dev/1.42.0-dev"` |

### `gen_ai.operation.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `chat` | development |  | Chat completion operation such as [OpenAI Chat API](https://platform.openai.com/docs/api-reference/chat) |
| `create_agent` | development |  | Create GenAI agent |
| `create_memory` | development |  | Create new memory records |
| `create_memory_store` | development |  | Create or initialize a memory store |
| `delete_memory` | development |  | Delete memory records |
| `delete_memory_store` | development |  | Delete or deprovision a memory store |
| `embeddings` | development |  | Embeddings operation such as [OpenAI Create embeddings API](https://platform.openai.com/docs/api-reference/embeddings/create) |
| `execute_tool` | development |  | Execute a tool |
| `fetch_response` | development |  | Fetch a previously generated model response by its identifier, without performing inference, such as [OpenAI Get a model response](https://platform.openai.com/docs/api-reference/responses/get) |
| `generate_content` | development |  | Multimodal content generation operation such as [Gemini Generate Content](https://ai.google.dev/api/generate-content) |
| `invoke_agent` | development |  | Invoke GenAI agent |
| `invoke_workflow` | development |  | Invoke GenAI workflow |
| `plan` | development |  | Agent planning or task decomposition phase |
| `retrieval` | development |  | Retrieval operation such as [OpenAI Search Vector Store API](https://platform.openai.com/docs/api-reference/vector-stores/search) |
| `search_memory` | development |  | Search/query memories from a memory store |
| `text_completion` | development |  | Text completions operation such as [OpenAI Completions API (Legacy)](https://platform.openai.com/docs/api-reference/completions) |
| `update_memory` | development |  | Update existing memory records |
| `upsert_memory` | development |  | Create or update memory records without the caller choosing which |

### `gen_ai.output.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `image` | development |  | Image |
| `json` | development |  | JSON object with known or unknown schema |
| `speech` | development |  | Speech |
| `text` | development |  | Plain text |

### `gen_ai.provider.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `anthropic` | development |  | [Anthropic](https://www.anthropic.com/) |
| `aws.bedrock` | development |  | [AWS Bedrock](https://aws.amazon.com/bedrock) |
| `azure.ai.inference` | development |  | Azure AI Inference |
| `azure.ai.openai` | development |  | [Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-services/openai/overview) |
| `cohere` | development |  | [Cohere](https://cohere.com/) |
| `deepseek` | development |  | [DeepSeek](https://www.deepseek.com/) |
| `gcp.gemini` | development |  | [Gemini](https://cloud.google.com/products/gemini) |
| `gcp.gen_ai` | development |  | Any Google generative AI endpoint |
| `gcp.vertex_ai` | development |  | [Vertex AI](https://cloud.google.com/vertex-ai) |
| `groq` | development |  | [Groq](https://groq.com/) |
| `ibm.watsonx.ai` | development |  | [IBM Watsonx AI](https://www.ibm.com/products/watsonx-ai) |
| `mistral` | development |  | [Mistral](https://mistral.ai/) |
| `moonshot_ai` | development |  | [Moonshot AI](https://www.moonshot.ai/) |
| `openai` | development |  | [OpenAI](https://openai.com/) |
| `perplexity` | development |  | [Perplexity](https://www.perplexity.ai/) |
| `x_ai` | development |  | [xAI](https://x.ai/) |

### `gen_ai.response.status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `cancelled` | development |  | The response generation was cancelled before it completed. |
| `completed` | development |  | The response finished generating successfully. |
| `failed` | development |  | The response generation failed with an error. |
| `in_progress` | development |  | The response is still being generated, for example a background or streamed response that has not finished. |
| `incomplete` | development |  | The response stopped before generation completed, for example because a token limit or content filter was reached. |
| `queued` | development |  | The response has been accepted by the provider but generation has not started yet. |

### `gen_ai.token.modality` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `audio` | development |  | Audio tokens. |
| `image` | development |  | Image tokens. |
| `text` | development |  | Text tokens. |
| `unknown` | development |  | The modality is not known. |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `gen_ai.client.inference.duration` | histogram | `s` | development |  | GenAI client inference operation duration. |
| `gen_ai.client.inference.operation.input_tokens` | histogram | `{token}` | development |  | The number of input (prompt) tokens used per inference operation. |
| `gen_ai.client.inference.operation.output_tokens` | histogram | `{token}` | development |  | The number of output (completion) tokens used per inference operation. |
| `gen_ai.client.inference.time_per_output_chunk` | histogram | `s` | development |  | Time per output chunk, recorded for each chunk received after the first one, measured as the time elapsed from the end of the previous chunk to the end of the current chunk. |
| `gen_ai.client.inference.time_to_first_chunk` | histogram | `s` | development |  | Time to receive the first chunk, measured from when the client issues the generation request to when the first chunk is received in the response stream. |
| `gen_ai.client.inference.usage.cache_read.input_tokens` | counter | `{token}` | development |  | The number of input tokens served from a provider-managed cache. |
| `gen_ai.client.inference.usage.cache_write.input_tokens` | counter | `{token}` | development |  | The number of input tokens written to a provider-managed cache. |
| `gen_ai.client.inference.usage.input_tokens` | counter | `{token}` | development |  | The number of input (prompt) tokens used, including cached tokens. |
| `gen_ai.client.inference.usage.output_tokens` | counter | `{token}` | development |  | The number of output (completion) tokens used, including reasoning tokens. |
| `gen_ai.client.inference.usage.reasoning.output_tokens` | counter | `{token}` | development |  | The number of output tokens used for reasoning (e.g. chain-of-thought, extended thinking). |
| `gen_ai.client.operation.duration` | histogram | `s` | development |  | GenAI operation duration. |
| `gen_ai.execute_tool.duration` | histogram | `s` | development |  | The duration of a single tool execution. |
| `gen_ai.invoke_agent.duration` | histogram | `s` | development |  | The end-to-end duration of a single in-process agent invocation, from the moment the invocation starts until the agent emits the last chunk of its final response or terminates with an error. |
| `gen_ai.invoke_agent.inference_calls` | histogram | `{inference_call}` | development |  | The number of inference (model) calls a GenAI agent makes during a single invocation. |
| `gen_ai.invoke_agent.tool_calls` | histogram | `{tool_call}` | development |  | The number of tool calls a GenAI agent makes during a single invocation. |
| `gen_ai.invoke_workflow.duration` | histogram | `s` | development |  | Records duration of GenAI workflow. |
| `gen_ai.server.request.duration` | histogram | `s` | development |  | Generative AI server request duration such as time-to-last byte or last output token. |
| `gen_ai.server.time_per_output_token` | histogram | `s` | development |  | Time per output token generated after the first token for successful responses. |
| `gen_ai.server.time_to_first_token` | histogram | `s` | development |  | Time to generate first token for successful responses. |
