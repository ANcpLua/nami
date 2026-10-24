# `artifact` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `artifact.attestation.filename` | `string` | development |  | The provenance filename of the built attestation which directly relates to the build artifact filename. This filename SHOULD accompany the artifact at publish time. See the [SLSA Relationship](https://slsa.dev/spec/v1.0/distributing-provenance#relationship-between-artifacts-and-attestations) specification for more information. | `"golang-binary-amd64-v0.1.0.attestation"`, `"docker-image-amd64-v0.1.0.intoto.json1"`, `"release-1.tar.gz.attestation"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.attestation.hash` | `string` | development |  | The full [hash value (see glossary)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf), of the built attestation. Some envelopes in the [software attestation space](https://github.com/in-toto/attestation/tree/main/spec) also refer to this as the **digest**. | `"1b31dfcd5b7f9267bf2ff47651df1cfb9147b9e4df1f335accf65b4cda498408"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.attestation.id` | `string` | development |  | The ID of the build [software attestation](https://slsa.dev/attestation-model). | `"123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.filename` | `string` | development |  | The human readable filename of the artifact, typically generated during build and release processes. Often includes the package name and version in the filename. | `"golang-binary-amd64-v0.1.0"`, `"docker-image-amd64-v0.1.0"`, `"release-1.tar.gz"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.hash` | `string` | development |  | The full [hash value (see glossary)](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.186-5.pdf), often found in checksum.txt on a release of the artifact and used to verify package integrity. | `"9ff4c52759e2c4ac70b7d517bc7fcdc1cda631ca0045271ddd1b192544f8a3e9"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.purl` | `string` | development |  | The [Package URL](https://github.com/package-url/purl-spec) of the [package artifact](https://slsa.dev/spec/v1.0/terminology#package-model) provides a standard way to identify and locate the packaged artifact. | `"pkg:github/package-url/purl-spec@1209109710924"`, `"pkg:npm/foo@12.12.3"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `artifact.version` | `string` | development |  | The version of the artifact. | `"v0.1.0"`, `"1.2.1"`, `"122691-build"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
