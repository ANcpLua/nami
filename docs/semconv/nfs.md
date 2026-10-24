# `nfs` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `nfs.operation.name` | `string` | development |  | NFSv4+ operation name. | `"OPEN"`, `"READ"`, `"GETATTR"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `nfs.server.repcache.status` | `string` | development |  | Linux: one of "hit" (NFSD_STATS_RC_HITS), "miss" (NFSD_STATS_RC_MISSES), or "nocache" (NFSD_STATS_RC_NOCACHE -- uncacheable) | `"hit"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `nfs.client.net.count` | counter | `{record}` | development |  | Reports the count of kernel NFS client TCP segments and UDP datagrams handled. |
| `nfs.client.net.tcp.connection.accepted` | counter | `{connection}` | development |  | Reports the count of kernel NFS client TCP connections accepted. |
| `nfs.client.operation.count` | counter | `{operation}` | development |  | Reports the count of kernel NFSv4+ client operations. |
| `nfs.client.procedure.count` | counter | `{procedure}` | development |  | Reports the count of kernel NFS client procedures. |
| `nfs.client.rpc.authrefresh.count` | counter | `{authrefresh}` | development |  | Reports the count of kernel NFS client RPC authentication refreshes. |
| `nfs.client.rpc.count` | counter | `{request}` | development |  | Reports the count of kernel NFS client RPCs sent, regardless of whether they're accepted/rejected by the server. |
| `nfs.client.rpc.retransmit.count` | counter | `{retransmit}` | development |  | Reports the count of kernel NFS client RPC retransmits. |
| `nfs.server.fh.stale.count` | counter | `{fh}` | development |  | Reports the count of kernel NFS server stale file handles. |
| `nfs.server.io` | counter | `By` | development |  | Reports the count of kernel NFS server bytes returned to receive and transmit (read and write) requests. |
| `nfs.server.net.count` | counter | `{record}` | development |  | Reports the count of kernel NFS server TCP segments and UDP datagrams handled. |
| `nfs.server.net.tcp.connection.accepted` | counter | `{connection}` | development |  | Reports the count of kernel NFS server TCP connections accepted. |
| `nfs.server.operation.count` | counter | `{operation}` | development |  | Reports the count of kernel NFSv4+ server operations. |
| `nfs.server.procedure.count` | counter | `{procedure}` | development |  | Reports the count of kernel NFS server procedures. |
| `nfs.server.repcache.requests` | counter | `{request}` | development |  | Reports the kernel NFS server reply cache request count by cache hit status. |
| `nfs.server.rpc.count` | counter | `{request}` | development |  | Reports the count of kernel NFS server RPCs handled. |
| `nfs.server.thread.count` | updowncounter | `{thread}` | development |  | Reports the count of kernel NFS server available threads. |
