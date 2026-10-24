using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    // Commit the authoritative copy before schema initialization can reset derived tables.
    // A failed copy rolls back its new table and leaves every v13 source row untouched.
    private static bool PreserveHistoricalMetricPoints(DuckDBConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (!TableExists(connection, transaction, "metric_points") ||
            TableExists(connection, transaction, MetricObservationRow.TableName)) return false;

        ExecuteSchemaSql(connection, transaction, MetricObservationRow.CreateTableDdl);
        ExecuteSchemaSql(connection, transaction, MetricObservationRow.IndexesDdl);
        var hasExemplars = TableExists(connection, transaction, "metric_exemplars");
        MetricPointRow? after = null;
        while (true)
        {
            var batch = new List<MetricObservationRow>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                const string Columns = "p.project_id, p.series_id, p.time_unix_nano, p.start_time_unix_nano, " +
                                       "p.value, p.histogram_count, p.histogram_sum, p.histogram_min, p.histogram_max, " +
                                       "p.bucket_bounds_json, p.bucket_counts_json";
                var traces = hasExemplars
                    ? "(SELECT to_json(list(e.trace_id ORDER BY e.trace_id)) FROM metric_exemplars e " +
                      "WHERE e.project_id = p.project_id AND e.series_id = p.series_id AND e.time_unix_nano = p.time_unix_nano)"
                    : "NULL";
                read.CommandText = "SELECT " + Columns + ", " + traces + " FROM metric_points p " +
                                   (after is null
                                       ? ""
                                       : "WHERE (p.project_id, p.series_id, p.time_unix_nano) > ($1, $2, $3) ") +
                                   "ORDER BY p.project_id, p.series_id, p.time_unix_nano LIMIT 1000";
                if (after is not null)
                    AddParameters(read, after.ProjectId, after.SeriesId, (decimal)after.TimeUnixNano);
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    after = new MetricPointRow
                    {
                        ProjectId = reader.GetString(0),
                        SeriesId = reader.GetString(1),
                        TimeUnixNano = DuckDbValueReader.Read<ulong>(reader, 2, 0),
                        StartTimeUnixNano = DuckDbValueReader.Read<ulong>(reader, 3),
                        Value = DuckDbValueReader.Read<double>(reader, 4),
                        HistogramCount = DuckDbValueReader.Read<ulong>(reader, 5),
                        HistogramSum = DuckDbValueReader.Read<double>(reader, 6),
                        HistogramMin = DuckDbValueReader.Read<double>(reader, 7),
                        HistogramMax = DuckDbValueReader.Read<double>(reader, 8),
                        BucketBoundsJson = DuckDbValueReader.ReadString(reader, 9),
                        BucketCountsJson = DuckDbValueReader.ReadString(reader, 10)
                    };
                    var traceJson = DuckDbValueReader.ReadString(reader, 11);
                    var traceIds = traceJson is null
                        ? []
                        : JsonSerializer.Deserialize(traceJson,
                              StorageJsonSerializerContext.Default.IReadOnlyListString) ??
                          throw new InvalidDataException("Historical metric exemplar trace list is null.");
                    batch.Add(MetricObservationCodec.FromHistorical(after, traceIds));
                }
            }

            foreach (var row in batch)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = MetricObservationRow.BuildMultiRowInsertSql(1);
                MetricObservationRow.AddParameters(insert, row);
                insert.ExecuteNonQuery();
            }

            if (batch.Count < 1000)
            {
                transaction.Commit();
                return true;
            }
        }
    }
}
