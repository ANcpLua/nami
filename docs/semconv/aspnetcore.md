# `aspnetcore` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `aspnetcore.authentication.result` | `enum` | development |  | The result of the authentication operation. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.authentication.scheme` | `string` | development |  | The identifier that names a particular authentication handler. | `"Cookies"`, `"Bearer"`, `"Identity.Application"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.authorization.policy` | `string` | development |  | The name of the authorization policy. | `"RequireAdminRole"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.authorization.result` | `enum` | development |  | The result of calling the authorization service. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.diagnostics.exception.result` | `enum` | stable |  | ASP.NET Core exception middleware handling result. | `"handled"`, `"unhandled"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.diagnostics.handler.type` | `string` | stable |  | Full type name of the [`IExceptionHandler`](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.diagnostics.iexceptionhandler) implementation that handled the exception. | `"Contoso.MyHandler"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.identity.error_code` | `string` | development |  | The error code for a failed identity operation. | `"DefaultError"`, `"PasswordMismatch"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.identity.password_check_result` | `enum` | development |  | The result from checking the password. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.identity.result` | `enum` | development |  | The result of the identity operation. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.identity.sign_in.result` | `enum` | development |  | Whether the sign in result was success or failure. | `"password"`, `"two_factor"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.identity.sign_in.type` | `enum` | development |  | The authentication type. | `"password"`, `"two_factor"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.identity.token_purpose` | `enum` | development |  | What the token will be used for. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.identity.token_verified` | `enum` | development |  | The result of token verification. | `"success"`, `"failure"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.identity.user.update_type` | `enum` | development |  | The user update type. | `"update"`, `"user_name"`, `"reset_password"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.identity.user_type` | `string` | development |  | The full name of the identity user type. | `"Contoso.ContosoUser"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.memory_pool.owner` | `string` | development |  | The name of the library or subsystem using the memory pool instance. | `"kestrel"`, `"iis"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.rate_limiting.policy` | `string` | stable |  | Rate limiting policy name. | `"fixed"`, `"sliding"`, `"token"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.rate_limiting.result` | `enum` | stable |  | Rate-limiting result, shows whether the lease was acquired or contains a rejection reason | `"acquired"`, `"request_canceled"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `aspnetcore.request.is_unhandled` | `boolean` | stable |  | Flag indicating if request was handled by the application pipeline. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.routing.is_fallback` | `boolean` | stable |  | A value that indicates whether the matched route is a fallback route. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.routing.match_status` | `enum` | stable |  | Match result - success or failure | `"success"`, `"failure"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.sign_in.is_persistent` | `boolean` | development |  | A flag indicating whether the sign in is persistent. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `aspnetcore.user.is_authenticated` | `boolean` | stable |  | A value that indicates whether the user is authenticated. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `aspnetcore.authentication.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Authentication failed. |
| `none` | development |  | No authentication information returned. |
| `success` | development |  | Authentication was successful. |

### `aspnetcore.authorization.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Authorization failed. |
| `success` | development |  | Authorization was successful. |

### `aspnetcore.diagnostics.exception.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `aborted` | stable |  | Exception handling didn't run because the request was aborted. |
| `handled` | stable |  | Exception was handled by the exception handling middleware. |
| `skipped` | stable |  | Exception handling was skipped because the response had started. |
| `unhandled` | stable |  | Exception was not handled by the exception handling middleware. |

### `aspnetcore.identity.password_check_result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Password check failed. |
| `password_missing` | development |  | Password check couldn't proceed because the password was missing from the user. |
| `success` | development |  | Password check was successful. |
| `success_rehash_needed` | development |  | Password check was successful however the password was encoded using a deprecated algorithm and should be rehashed and updated. |
| `user_missing` | development |  | Password check couldn't proceed because the user was missing. |

### `aspnetcore.identity.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Identity operation failed. |
| `success` | development |  | Identity operation was successful. |

### `aspnetcore.identity.sign_in.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Sign in failed. |
| `locked_out` | development |  | User is locked out. |
| `not_allowed` | development |  | User is not allowed to sign in. |
| `requires_two_factor` | development |  | User requires two factory authentication to sign in. |
| `success` | development |  | Sign in was successful. |

### `aspnetcore.identity.sign_in.type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `external` | development |  | Sign in with a previously registered third-party login. |
| `passkey` | development |  | Sign in with passkey. |
| `password` | development |  | Sign in with password. |
| `two_factor` | development |  | Sign in with a two factor provider. |
| `two_factor_authenticator` | development |  | Sign in with two factor authenticator app. |
| `two_factor_recovery_code` | development |  | Sign in with two factory recovery code. |

### `aspnetcore.identity.token_purpose` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `change_email` | development |  | The token is for changing the user email address. |
| `change_phone_number` | development |  | The token is for changing a user phone number. |
| `email_confirmation` | development |  | The token is for confirming user email address. |
| `_OTHER` | development |  | Any token purpose that the instrumentation has no prior knowledge of. |
| `reset_password` | development |  | The token is for resetting a user password. |
| `two_factor` | development |  | The token is for changing user two factor settings. |

### `aspnetcore.identity.token_verified` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | development |  | Token verification failed. |
| `success` | development |  | Token verification was successful. |

### `aspnetcore.identity.user.update_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `access_failed` | development |  | Identity user access failure recorded. |
| `add_claims` | development |  | Identity user claims added. |
| `add_login` | development |  | Identity user login added. |
| `add_password` | development |  | Identity user password added. |
| `add_to_roles` | development |  | Identity user added to roles. |
| `change_email` | development |  | Identity user email changed. |
| `change_password` | development |  | Identity user password changed. |
| `change_phone_number` | development |  | Identity user phone number changed. |
| `confirm_email` | development |  | Identity user email confirmed. |
| `generate_new_two_factor_recovery_codes` | development |  | Identity user new two-factor recovery codes generated. |
| `_OTHER` | development |  | Any update type that the instrumentation has no prior knowledge of. |
| `password_rehash` | development |  | Identity user password rehashed. |
| `redeem_two_factor_recovery_code` | development |  | Identity user two-factor recovery code redeemed. |
| `remove_authentication_token` | development |  | Identity user authentication token removed. |
| `remove_claims` | development |  | Identity user claims removed. |
| `remove_from_roles` | development |  | Identity user removed from roles. |
| `remove_login` | development |  | Identity user login removed. |
| `remove_passkey` | development |  | Identity user passkey removed. |
| `remove_password` | development |  | Identity user password removed. |
| `replace_claim` | development |  | Identity user claim replaced. |
| `reset_access_failed_count` | development |  | Identity user access failure count reset. |
| `reset_authenticator_key` | development |  | Identity user authenticator key reset. |
| `reset_password` | development |  | Identity user password reset. |
| `security_stamp` | development |  | Identity user security stamp updated. |
| `set_authentication_token` | development |  | Identity user authentication token set. |
| `set_email` | development |  | Identity user email set. |
| `set_lockout_enabled` | development |  | Identity user lockout enabled or disabled. |
| `set_lockout_end_date` | development |  | Identity user lockout end date set. |
| `set_passkey` | development |  | Identity user passkey set. |
| `set_phone_number` | development |  | Identity user phone number set. |
| `set_two_factor_enabled` | development |  | Identity user two-factor authentication enabled or disabled. |
| `update` | development |  | Identity user updated. |
| `user_name` | development |  | Identity username updated. |

### `aspnetcore.rate_limiting.result` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `acquired` | stable |  | Lease was acquired |
| `endpoint_limiter` | stable |  | Lease request was rejected by the endpoint limiter |
| `global_limiter` | stable |  | Lease request was rejected by the global limiter |
| `request_canceled` | stable |  | Lease request was canceled |

### `aspnetcore.routing.match_status` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `failure` | stable |  | Match failed |
| `success` | stable |  | Match succeeded |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `aspnetcore.authentication.authenticate.duration` | histogram | `s` | development |  | The authentication duration for a request. |
| `aspnetcore.authentication.challenges` | counter | `{challenge}` | development |  | The total number of times a scheme is challenged. |
| `aspnetcore.authentication.forbids` | counter | `{forbid}` | development |  | The total number of times an authenticated user attempts to access a resource they are not permitted to access. |
| `aspnetcore.authentication.sign_ins` | counter | `{sign_in}` | development |  | The total number of times a principal is signed in with a scheme. |
| `aspnetcore.authentication.sign_outs` | counter | `{sign_out}` | development |  | The total number of times a principal is signed out with a scheme. |
| `aspnetcore.authorization.attempts` | counter | `{attempt}` | development |  | The total number of authorization attempts. |
| `aspnetcore.diagnostics.exceptions` | counter | `{exception}` | stable |  | Number of exceptions caught by exception handling middleware. |
| `aspnetcore.identity.sign_in.authenticate.duration` | histogram | `s` | development |  | The duration of authenticate attempts. The authenticate metrics is recorded by sign in methods such as PasswordSignInAsync and TwoFactorSignInAsync. |
| `aspnetcore.identity.sign_in.check_password_attempts` | counter | `{attempt}` | development |  | The total number of check password attempts. Checks that the account is in a state that can log in and that the password is valid using the UserManager.CheckPasswordAsync method. |
| `aspnetcore.identity.sign_in.sign_ins` | counter | `{sign_in}` | development |  | The total number of calls to sign in user principals. |
| `aspnetcore.identity.sign_in.sign_outs` | counter | `{sign_out}` | development |  | The total number of calls to sign out user principals. |
| `aspnetcore.identity.sign_in.two_factor_clients_forgotten` | counter | `{client}` | development |  | The total number of two factor clients forgotten. |
| `aspnetcore.identity.sign_in.two_factor_clients_remembered` | counter | `{client}` | development |  | The total number of two factor clients remembered. |
| `aspnetcore.identity.user.check_password_attempts` | counter | `{attempt}` | development |  | The number of check password attempts. Only checks whether the password is valid and not whether the user account is in a state that can log in. |
| `aspnetcore.identity.user.create.duration` | histogram | `s` | development |  | The duration of user creation operations. |
| `aspnetcore.identity.user.delete.duration` | histogram | `s` | development |  | The duration of user deletion operations. |
| `aspnetcore.identity.user.generated_tokens` | counter | `{count}` | development |  | The total number of token generations. |
| `aspnetcore.identity.user.update.duration` | histogram | `s` | development |  | The duration of user update operations. |
| `aspnetcore.identity.user.verify_token_attempts` | counter | `{attempt}` | development |  | The total number of token verification attempts. |
| `aspnetcore.memory_pool.allocated` | counter | `By` | development |  | Total number of bytes allocated by the memory pool. Allocation occurs when a memory rental request exceeds the available pooled memory. |
| `aspnetcore.memory_pool.evicted` | counter | `By` | development |  | Total number of bytes evicted from the memory pool. Eviction occurs when idle pooled memory is reclaimed. |
| `aspnetcore.memory_pool.pooled` | updowncounter | `By` | development |  | Number of bytes currently pooled and available for reuse. |
| `aspnetcore.memory_pool.rented` | counter | `By` | development |  | Total number of bytes rented from the memory pool. |
| `aspnetcore.rate_limiting.active_request_leases` | updowncounter | `{request}` | stable |  | Number of requests that are currently active on the server that hold a rate limiting lease. |
| `aspnetcore.rate_limiting.queued_requests` | updowncounter | `{request}` | stable |  | Number of requests that are currently queued, waiting to acquire a rate limiting lease. |
| `aspnetcore.rate_limiting.request.time_in_queue` | histogram | `s` | stable |  | The time the request spent in a queue waiting to acquire a rate limiting lease. |
| `aspnetcore.rate_limiting.request_lease.duration` | histogram | `s` | stable |  | The duration of rate limiting lease held by requests on the server. |
| `aspnetcore.rate_limiting.requests` | counter | `{request}` | stable |  | Number of requests that tried to acquire a rate limiting lease. |
| `aspnetcore.routing.match_attempts` | counter | `{match_attempt}` | stable |  | Number of requests that were attempted to be matched to an endpoint. |
