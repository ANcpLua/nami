# `db` semantic conventions

Generated from Nami's pinned Weaver registry. Do not edit.

## Attributes

| Key | Type | Stability | Deprecated | Brief | Examples | Annotations |
| --- | --- | --- | --- | --- | --- | --- |
| `db.cassandra.consistency_level` | `enum` | development | Replaced by `cassandra.consistency.level`. | Deprecated, use `cassandra.consistency.level` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.coordinator.dc` | `string` | development | Replaced by `cassandra.coordinator.dc`. | Deprecated, use `cassandra.coordinator.dc` instead. | `"us-west-2"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.coordinator.id` | `string` | development | Replaced by `cassandra.coordinator.id`. | Deprecated, use `cassandra.coordinator.id` instead. | `"be13faa2-8574-4d71-926d-27f16cf8a7af"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.idempotence` | `boolean` | development | Replaced by `cassandra.query.idempotent`. | Deprecated, use `cassandra.query.idempotent` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.page_size` | `int` | development | Replaced by `cassandra.page.size`. | Deprecated, use `cassandra.page.size` instead. | `5000` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.speculative_execution_count` | `int` | development | Replaced by `cassandra.speculative_execution.count`. | Deprecated, use `cassandra.speculative_execution.count` instead. | `0`, `2` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cassandra.table` | `string` | development | Replaced by `db.collection.name`. | Deprecated, use `db.collection.name` instead. | `"mytable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.client.connection.pool.name` | `string` | development |  | The name of the connection pool; unique within the instrumented application. In case the connection pool implementation doesn't provide a name, instrumentation SHOULD use a combination of parameters that would make the name unique, for example, combining attributes `server.address`, `server.port`, and `db.namespace`, formatted as `server.address:server.port/db.namespace`. Instrumentations that generate connection pool name following different patterns SHOULD document it. | `"myDataSource"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.client.connection.state` | `enum` | development |  | The state of a connection in the pool | `"idle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.client.connections.pool.name` | `string` | development | Replaced by `db.client.connection.pool.name`. | Deprecated, use `db.client.connection.pool.name` instead. | `"myDataSource"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.client.connections.state` | `enum` | development | Replaced by `db.client.connection.state`. | Deprecated, use `db.client.connection.state` instead. | `"idle"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.collection.name` | `string` | stable |  | The name of a collection (table, container) within the database. | `"public.users"`, `"customers"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.connection_string` | `string` | development | Replaced by `server.address` and `server.port`. | Deprecated, use `server.address`, `server.port` attributes instead. | `"Server=(localdb)\\v11.0;Integrated Security=true;"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.client_id` | `string` | development | Replaced by `azure.client.id`. | Deprecated, use `azure.client.id` instead. | `"3ba4827d-4422-483f-b59f-85b74211c11d"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.connection_mode` | `enum` | development | Replaced by `azure.cosmosdb.connection.mode`. | Deprecated, use `azure.cosmosdb.connection.mode` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.consistency_level` | `enum` | development | Replaced by `azure.cosmosdb.consistency.level`. | Deprecated, use `cosmosdb.consistency.level` instead. | `"Eventual"`, `"ConsistentPrefix"`, `"BoundedStaleness"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.container` | `string` | development | Replaced by `db.collection.name`. | Deprecated, use `db.collection.name` instead. | `"mytable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.operation_type` | `enum` | development | Removed, no replacement at this time. | Deprecated, no replacement at this time. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.regions_contacted` | `string[]` | development | Replaced by `azure.cosmosdb.operation.contacted_regions`. | Deprecated, use `azure.cosmosdb.operation.contacted_regions` instead. | `["North Central US","Australia East","Australia Southeast"]` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.request_charge` | `double` | development | Replaced by `azure.cosmosdb.operation.request_charge`. | Deprecated, use `azure.cosmosdb.operation.request_charge` instead. | `46.18`, `1.0` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.request_content_length` | `int` | development | Replaced by `azure.cosmosdb.request.body.size`. | Deprecated, use `azure.cosmosdb.request.body.size` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.status_code` | `int` | development | Use `db.response.status_code` instead. | Deprecated, use `db.response.status_code` instead. | `200`, `201` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.cosmosdb.sub_status_code` | `int` | development | Replaced by `azure.cosmosdb.response.sub_status_code`. | Deprecated, use `azure.cosmosdb.response.sub_status_code` instead. | `1000`, `1002` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.elasticsearch.cluster.name` | `string` | development | Replaced by `db.namespace`. | Deprecated, use `db.namespace` instead. | `"e9106fc68e3044f0b1475b04bf4ffd5f"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.elasticsearch.node.name` | `string` | development | Replaced by `elasticsearch.node.name`. | Deprecated, use `elasticsearch.node.name` instead. | `"instance-0000000001"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.elasticsearch.path_parts` | `template[string]` | development | Replaced by `db.operation.parameter`. | Deprecated, use `db.operation.parameter` instead. | `"test-index"`, `"123"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.instance.id` | `string` | development | Removed, no general replacement at this time. For Elasticsearch, use `db.elasticsearch.node.name` instead. | Deprecated, no general replacement at this time. For Elasticsearch, use `db.elasticsearch.node.name` instead. | `"mysql-e26b99z.example.com"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.jdbc.driver_classname` | `string` | development | Removed, no replacement at this time. | Removed, no replacement at this time. | `"org.postgresql.Driver"`, `"com.microsoft.sqlserver.jdbc.SQLServerDriver"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.mongodb.collection` | `string` | development | Replaced by `db.collection.name`. | Deprecated, use `db.collection.name` instead. | `"mytable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.mssql.instance_name` | `string` | development | Removed, no replacement at this time. | Deprecated, SQL Server instance is now populated as a part of `db.namespace` attribute. | `"MSSQLSERVER"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.name` | `string` | development | Replaced by `db.namespace`. | Deprecated, use `db.namespace` instead. | `"customers"`, `"main"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.namespace` | `string` | stable |  | The name of the database, fully qualified within the server address and port. | `"customers"`, `"test.users"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.operation` | `string` | development | Replaced by `db.operation.name`. | Deprecated, use `db.operation.name` instead. | `"findAndModify"`, `"HMSET"`, `"SELECT"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.operation.batch.size` | `int` | stable |  | The number of database operations included in a batch operation. | `2`, `3`, `4` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.operation.name` | `string` | stable |  | The name of the operation or command being executed. | `"findAndModify"`, `"HMSET"`, `"SELECT"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.operation.parameter` | `template[string]` | development |  | A database operation parameter, with `<key>` being the parameter name, and the attribute value being a string representation of the parameter value. | `"someval"`, `"55"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.query.parameter` | `template[string]` | development |  | A database query parameter, with `<key>` being the parameter name, and the attribute value being a string representation of the parameter value. | `"someval"`, `"55"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.query.summary` | `string` | stable |  | Low cardinality summary of a database query. | `"SELECT wuser_table"`, `"INSERT shipping_details SELECT orders"`, `"get user by id"` | `nami.persisted`: `true`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `true` |
| `db.query.text` | `string` | stable |  | The database query being executed. | `"SELECT * FROM wuser_table where username = ?"`, `"SET mykey ?"` | `nami.persisted`: `false`, `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"`, `nami.token_exempt`: `false` |
| `db.redis.database_index` | `int` | development | Replaced by `db.namespace` (string). | Deprecated, use `db.namespace` instead. | `0`, `1`, `15` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.response.returned_rows` | `int` | development |  | Number of rows returned by the operation. | `10`, `30`, `1000` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.response.status_code` | `string` | stable |  | Database response status code. | `"102"`, `"ORA-17002"`, `"08P01"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.sql.table` | `string` | development | Replaced by `db.collection.name`, but only if not extracting the value from `db.query.text`. | Deprecated, use `db.collection.name` instead. | `"mytable"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.statement` | `string` | development | Replaced by `db.query.text`. | The database statement being executed. | `"SELECT * FROM wuser_table"`, `"SET mykey \"WuValue\""` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.stored_procedure.name` | `string` | stable |  | The name of a stored procedure within the database. | `"GetCustomer"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.system` | `enum` | development | Replaced by `db.system.name`. | Deprecated, use `db.system.name` instead. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.system.name` | `enum` | stable |  | The database management system (DBMS) product as identified by the client instrumentation. |  | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |
| `db.user` | `string` | development | Removed, no replacement at this time. | Deprecated, no replacement at this time. | `"readonly_user"`, `"reporting_user"` | `nami.source_schema`: `"https://opentelemetry.io/schemas/1.44.0"` |

### `db.cassandra.consistency_level` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `all` | development |  | `all`. |
| `any` | development |  | `any`. |
| `each_quorum` | development |  | `each_quorum`. |
| `local_one` | development |  | `local_one`. |
| `local_quorum` | development |  | `local_quorum`. |
| `local_serial` | development |  | `local_serial`. |
| `one` | development |  | `one`. |
| `quorum` | development |  | `quorum`. |
| `serial` | development |  | `serial`. |
| `three` | development |  | `three`. |
| `two` | development |  | `two`. |

### `db.client.connection.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `idle` | development |  | `idle`. |
| `used` | development |  | `used`. |

### `db.client.connections.state` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `idle` | development |  | `idle`. |
| `used` | development |  | `used`. |

### `db.cosmosdb.connection_mode` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `direct` | development |  | Direct connection. |
| `gateway` | development |  | Gateway (HTTP) connection. |

### `db.cosmosdb.consistency_level` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `BoundedStaleness` | development |  | `BoundedStaleness`. |
| `ConsistentPrefix` | development |  | `ConsistentPrefix`. |
| `Eventual` | development |  | `Eventual`. |
| `Session` | development |  | `Session`. |
| `Strong` | development |  | `Strong`. |

### `db.cosmosdb.operation_type` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `batch` | development |  | `batch`. |
| `create` | development |  | `create`. |
| `delete` | development |  | `delete`. |
| `execute` | development |  | `execute`. |
| `execute_javascript` | development |  | `execute_javascript`. |
| `head` | development |  | `head`. |
| `head_feed` | development |  | `head_feed`. |
| `invalid` | development |  | `invalid`. |
| `patch` | development |  | `patch`. |
| `query` | development |  | `query`. |
| `query_plan` | development |  | `query_plan`. |
| `read` | development |  | `read`. |
| `read_feed` | development |  | `read_feed`. |
| `replace` | development |  | `replace`. |
| `upsert` | development |  | `upsert`. |

### `db.system` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `adabas` | development |  | Adabas (Adaptable Database System) |
| `cache` | development | Replaced by `intersystems_cache`. | Deprecated, use `intersystems_cache` instead. |
| `cassandra` | development |  | Apache Cassandra |
| `clickhouse` | development |  | ClickHouse |
| `cloudscape` | development | Replaced by `other_sql`. | Deprecated, use `other_sql` instead. |
| `cockroachdb` | development |  | CockroachDB |
| `coldfusion` | development | Obsoleted. | Deprecated, no replacement at this time. |
| `cosmosdb` | development |  | Microsoft Azure Cosmos DB |
| `couchbase` | development |  | Couchbase |
| `couchdb` | development |  | CouchDB |
| `db2` | development |  | IBM Db2 |
| `derby` | development |  | Apache Derby |
| `dynamodb` | development |  | Amazon DynamoDB |
| `edb` | development |  | EnterpriseDB |
| `elasticsearch` | development |  | Elasticsearch |
| `filemaker` | development |  | FileMaker |
| `firebird` | development |  | Firebird |
| `firstsql` | development | Replaced by `other_sql`. | Deprecated, use `other_sql` instead. |
| `geode` | development |  | Apache Geode |
| `h2` | development |  | H2 |
| `hanadb` | development |  | SAP HANA |
| `hbase` | development |  | Apache HBase |
| `hive` | development |  | Apache Hive |
| `hsqldb` | development |  | HyperSQL DataBase |
| `influxdb` | development |  | InfluxDB |
| `informix` | development |  | Informix |
| `ingres` | development |  | Ingres |
| `instantdb` | development |  | InstantDB |
| `interbase` | development |  | InterBase |
| `intersystems_cache` | development |  | InterSystems Caché |
| `mariadb` | development |  | MariaDB |
| `maxdb` | development |  | SAP MaxDB |
| `memcached` | development |  | Memcached |
| `mongodb` | development |  | MongoDB |
| `mssql` | development |  | Microsoft SQL Server |
| `mssqlcompact` | development | Replaced by `other_sql`. | Deprecated, Microsoft SQL Server Compact is discontinued. |
| `mysql` | development |  | MySQL |
| `neo4j` | development |  | Neo4j |
| `netezza` | development |  | Netezza |
| `opensearch` | development |  | OpenSearch |
| `oracle` | development |  | Oracle Database |
| `other_sql` | development |  | Some other SQL database. Fallback only. See notes. |
| `pervasive` | development |  | Pervasive PSQL |
| `pointbase` | development |  | PointBase |
| `postgresql` | development |  | PostgreSQL |
| `progress` | development |  | Progress Database |
| `redis` | development |  | Redis |
| `redshift` | development |  | Amazon Redshift |
| `spanner` | development |  | Cloud Spanner |
| `sqlite` | development |  | SQLite |
| `sybase` | development |  | Sybase |
| `teradata` | development |  | Teradata |
| `trino` | development |  | Trino |
| `vertica` | development |  | Vertica |

### `db.system.name` values

| Value | Stability | Deprecated | Brief |
| --- | --- | --- | --- |
| `actian.ingres` | development |  | [Actian Ingres](https://www.actian.com/databases/ingres/) |
| `aws.dynamodb` | development |  | [Amazon DynamoDB](https://aws.amazon.com/pm/dynamodb/) |
| `aws.redshift` | development |  | [Amazon Redshift](https://aws.amazon.com/redshift/) |
| `azure.cosmosdb` | development |  | [Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db) |
| `cassandra` | development |  | [Apache Cassandra](https://cassandra.apache.org/) |
| `clickhouse` | development |  | [ClickHouse](https://clickhouse.com/) |
| `cockroachdb` | development |  | [CockroachDB](https://www.cockroachlabs.com/) |
| `couchbase` | development |  | [Couchbase](https://www.couchbase.com/) |
| `couchdb` | development |  | [Apache CouchDB](https://couchdb.apache.org/) |
| `derby` | development |  | [Apache Derby](https://db.apache.org/derby/) |
| `elasticsearch` | development |  | [Elasticsearch](https://www.elastic.co/elasticsearch) |
| `firebirdsql` | development |  | [Firebird](https://www.firebirdsql.org/) |
| `gcp.spanner` | development |  | [Google Cloud Spanner](https://cloud.google.com/spanner) |
| `geode` | development |  | [Apache Geode](https://geode.apache.org/) |
| `h2database` | development |  | [H2 Database](https://h2database.com/) |
| `hbase` | development |  | [Apache HBase](https://hbase.apache.org/) |
| `hive` | development |  | [Apache Hive](https://hive.apache.org/) |
| `hsqldb` | development |  | [HyperSQL Database](https://hsqldb.org/) |
| `ibm.db2` | development |  | [IBM Db2](https://www.ibm.com/db2) |
| `ibm.informix` | development |  | [IBM Informix](https://www.ibm.com/products/informix) |
| `ibm.netezza` | development |  | [IBM Netezza](https://www.ibm.com/products/netezza) |
| `influxdb` | development |  | [InfluxDB](https://www.influxdata.com/) |
| `instantdb` | development |  | [Instant](https://www.instantdb.com/) |
| `intersystems.cache` | development |  | [InterSystems Caché](https://www.intersystems.com/products/cache/) |
| `mariadb` | stable |  | [MariaDB](https://mariadb.org/) |
| `memcached` | development |  | [Memcached](https://memcached.org/) |
| `microsoft.sql_server` | stable |  | [Microsoft SQL Server](https://www.microsoft.com/sql-server) |
| `mongodb` | development |  | [MongoDB](https://www.mongodb.com/) |
| `mysql` | stable |  | [MySQL](https://www.mysql.com/) |
| `neo4j` | development |  | [Neo4j](https://neo4j.com/) |
| `opensearch` | development |  | [OpenSearch](https://opensearch.org/) |
| `oracle.db` | development |  | [Oracle Database](https://www.oracle.com/database/) |
| `other_sql` | development |  | Some other SQL database. Fallback only. |
| `postgresql` | stable |  | [PostgreSQL](https://www.postgresql.org/) |
| `redis` | development |  | [Redis](https://redis.io/) |
| `sap.hana` | development |  | [SAP HANA](https://www.sap.com/products/technology-platform/hana/what-is-sap-hana.html) |
| `sap.maxdb` | development |  | [SAP MaxDB](https://maxdb.sap.com/) |
| `softwareag.adabas` | development |  | [Adabas (Adaptable Database System)](https://documentation.softwareag.com/?pf=adabas) |
| `sqlite` | development |  | [SQLite](https://www.sqlite.org/) |
| `teradata` | development |  | [Teradata](https://www.teradata.com/) |
| `trino` | development |  | [Trino](https://trino.io/) |

## Metrics

| Name | Instrument | Unit | Stability | Deprecated | Brief |
| --- | --- | --- | --- | --- | --- |
| `db.client.connection.count` | updowncounter | `{connection}` | development |  | The number of connections that are currently in state described by the `state` attribute. |
| `db.client.connection.create_time` | histogram | `s` | development |  | The time it took to create a new connection. |
| `db.client.connection.idle.max` | updowncounter | `{connection}` | development |  | The maximum number of idle open connections allowed. |
| `db.client.connection.idle.min` | updowncounter | `{connection}` | development |  | The minimum number of idle open connections allowed. |
| `db.client.connection.max` | updowncounter | `{connection}` | development |  | The maximum number of open connections allowed. |
| `db.client.connection.pending_requests` | updowncounter | `{request}` | development |  | The number of current pending requests for an open connection. |
| `db.client.connection.timeouts` | counter | `{timeout}` | development |  | The number of connection timeouts that have occurred trying to obtain a connection from the pool. |
| `db.client.connection.use_time` | histogram | `s` | development |  | The time between borrowing a connection and returning it to the pool. |
| `db.client.connection.wait_time` | histogram | `s` | development |  | The time it took to obtain an open connection from the pool. |
| `db.client.connections.create_time` | histogram | `ms` | development | Replaced by `db.client.connection.create_time` with unit `s`. | Deprecated, use `db.client.connection.create_time` instead. Note: the unit also changed from `ms` to `s`. |
| `db.client.connections.idle.max` | updowncounter | `{connection}` | development | Replaced by `db.client.connection.idle.max`. | Deprecated, use `db.client.connection.idle.max` instead. |
| `db.client.connections.idle.min` | updowncounter | `{connection}` | development | Replaced by `db.client.connection.idle.min`. | Deprecated, use `db.client.connection.idle.min` instead. |
| `db.client.connections.max` | updowncounter | `{connection}` | development | Replaced by `db.client.connection.max`. | Deprecated, use `db.client.connection.max` instead. |
| `db.client.connections.pending_requests` | updowncounter | `{request}` | development | Replaced by `db.client.connection.pending_requests`. | Deprecated, use `db.client.connection.pending_requests` instead. |
| `db.client.connections.timeouts` | counter | `{timeout}` | development | Replaced by `db.client.connection.timeouts`. | Deprecated, use `db.client.connection.timeouts` instead. |
| `db.client.connections.usage` | updowncounter | `{connection}` | development | Replaced by `db.client.connection.count`. | Deprecated, use `db.client.connection.count` instead. |
| `db.client.connections.use_time` | histogram | `ms` | development | Replaced by `db.client.connection.use_time` with unit `s`. | Deprecated, use `db.client.connection.use_time` instead. Note: the unit also changed from `ms` to `s`. |
| `db.client.connections.wait_time` | histogram | `ms` | development | Replaced by `db.client.connection.wait_time` with unit `s`. | Deprecated, use `db.client.connection.wait_time` instead. Note: the unit also changed from `ms` to `s`. |
| `db.client.cosmosdb.active_instance.count` | updowncounter | `{instance}` | development | Replaced by `azure.cosmosdb.client.active_instance.count`. | Deprecated, use `azure.cosmosdb.client.active_instance.count` instead. |
| `db.client.cosmosdb.operation.request_charge` | histogram | `{request_unit}` | development | Replaced by `azure.cosmosdb.client.operation.request_charge`. | Deprecated, use `azure.cosmosdb.client.operation.request_charge` instead. |
| `db.client.operation.duration` | histogram | `s` | stable |  | Duration of database client operations. |
| `db.client.response.returned_rows` | histogram | `{row}` | development |  | The actual number of records returned by the database operation. |
