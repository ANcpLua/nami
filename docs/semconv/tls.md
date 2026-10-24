# `tls` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `tls.cipher` | `string` | development |  | String indicating the [cipher](https://datatracker.ietf.org/doc/html/rfc5246#appendix-A.5) used during the current connection. | `"TLS_RSA_WITH_3DES_EDE_CBC_SHA"`, `"TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.certificate` | `string` | development |  | PEM-encoded stand-alone certificate offered by the client. This is usually mutually-exclusive of `client.certificate_chain` since this value also exists in that list. | `"MII..."` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.certificate_chain` | `string[]` | development |  | Array of PEM-encoded certificates that make up the certificate chain offered by the client. This is usually mutually-exclusive of `client.certificate` since that value should be the first certificate in the chain. | `["MII...","MI..."]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.hash.md5` | `string` | development |  | Certificate fingerprint using the MD5 digest of DER-encoded version of certificate offered by the client. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"0F76C7F2C55BFD7D8E8B8F4BFBF0C9EC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.hash.sha1` | `string` | development |  | Certificate fingerprint using the SHA1 digest of DER-encoded version of certificate offered by the client. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"9E393D93138888D288266C2D915214D1D1CCEB2A"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.hash.sha256` | `string` | development |  | Certificate fingerprint using the SHA256 digest of DER-encoded version of certificate offered by the client. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"0687F666A054EF17A08E2F2162EAB4CBC0D265E1D7875BE74BF3C712CA92DAF0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.issuer` | `string` | development |  | Distinguished name of [subject](https://datatracker.ietf.org/doc/html/rfc5280#section-4.1.2.6) of the issuer of the x.509 certificate presented by the client. | `"CN=Example Root CA, OU=Infrastructure Team, DC=example, DC=com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.ja3` | `string` | development |  | A hash that identifies clients based on how they perform an SSL/TLS handshake. | `"d4e5b18d6b55c71272893221c96ba240"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.not_after` | `string` | development |  | Date/Time indicating when client certificate is no longer considered valid. | `"2021-01-01T00:00:00.000Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.not_before` | `string` | development |  | Date/Time indicating when client certificate is first considered valid. | `"1970-01-01T00:00:00.000Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.server_name` | `string` | development | Replaced by `server.address`. | Deprecated, use `server.address` instead. | `"opentelemetry.io"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.subject` | `string` | development |  | Distinguished name of subject of the x.509 certificate presented by the client. | `"CN=myclient, OU=Documentation Team, DC=example, DC=com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.client.supported_ciphers` | `string[]` | development |  | Array of ciphers offered by the client during the client hello. | `["TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384","TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.curve` | `string` | development |  | String indicating the curve used for the given cipher, when applicable | `"secp256r1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.established` | `boolean` | development |  | Boolean flag indicating if the TLS negotiation was successful and transitioned to an encrypted tunnel. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.next_protocol` | `string` | development |  | String indicating the protocol being tunneled. Per the values in the [IANA registry](https://www.iana.org/assignments/tls-extensiontype-values/tls-extensiontype-values.xhtml#alpn-protocol-ids), this string should be lower case. | `"http/1.1"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.protocol.name` | `enum` | development |  | Normalized lowercase protocol name parsed from original string of the negotiated [SSL/TLS protocol version](https://docs.openssl.org/1.1.1/man3/SSL_get_version/#return-values) |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.protocol.version` | `string` | development |  | Numeric part of the version parsed from the original string of the negotiated [SSL/TLS protocol version](https://docs.openssl.org/1.1.1/man3/SSL_get_version/#return-values) | `"1.2"`, `"3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.resumed` | `boolean` | development |  | Boolean flag indicating if this TLS connection was resumed from an existing TLS negotiation. | `true` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.certificate` | `string` | development |  | PEM-encoded stand-alone certificate offered by the server. This is usually mutually-exclusive of `server.certificate_chain` since this value also exists in that list. | `"MII..."` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.certificate_chain` | `string[]` | development |  | Array of PEM-encoded certificates that make up the certificate chain offered by the server. This is usually mutually-exclusive of `server.certificate` since that value should be the first certificate in the chain. | `["MII...","MI..."]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.hash.md5` | `string` | development |  | Certificate fingerprint using the MD5 digest of DER-encoded version of certificate offered by the server. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"0F76C7F2C55BFD7D8E8B8F4BFBF0C9EC"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.hash.sha1` | `string` | development |  | Certificate fingerprint using the SHA1 digest of DER-encoded version of certificate offered by the server. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"9E393D93138888D288266C2D915214D1D1CCEB2A"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.hash.sha256` | `string` | development |  | Certificate fingerprint using the SHA256 digest of DER-encoded version of certificate offered by the server. For consistency with other hash values, this value should be formatted as an uppercase hash. | `"0687F666A054EF17A08E2F2162EAB4CBC0D265E1D7875BE74BF3C712CA92DAF0"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.issuer` | `string` | development |  | Distinguished name of [subject](https://datatracker.ietf.org/doc/html/rfc5280#section-4.1.2.6) of the issuer of the x.509 certificate presented by the client. | `"CN=Example Root CA, OU=Infrastructure Team, DC=example, DC=com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.ja3s` | `string` | development |  | A hash that identifies servers based on how they perform an SSL/TLS handshake. | `"d4e5b18d6b55c71272893221c96ba240"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.not_after` | `string` | development |  | Date/Time indicating when server certificate is no longer considered valid. | `"2021-01-01T00:00:00.000Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.not_before` | `string` | development |  | Date/Time indicating when server certificate is first considered valid. | `"1970-01-01T00:00:00.000Z"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `tls.server.subject` | `string` | development |  | Distinguished name of subject of the x.509 certificate presented by the server. | `"CN=myserver, OU=Documentation Team, DC=example, DC=com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `tls.protocol.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `ssl` | development |  | SSL |
| `tls` | development |  | TLS |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
