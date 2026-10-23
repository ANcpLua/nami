using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    // 17.0.11 and earlier stored each create's first response beside its idempotency key. A repeat answers the record
    // as it is now, so nothing read it. The schema check refuses the extra column, so a database that still has it,
    // the hosted volume among them, drops it first and keeps every key and the record each one names. DuckDB drops
    // no column while an index on a later column exists, even one dropped earlier in the same transaction, so the
    // indexes go in a transaction of their own; an open interrupted between the two finds the column and drops it.
    private static void DropStoredIdempotencyResponses(DuckDBConnection connection)
    {
        for (var step = 0; step < 2; step++)
        {
            using var transaction = connection.BeginTransaction();
            if (!TableExists(connection, transaction, IdempotencyKeyRow.TableName) ||
                ReadColumns(connection, transaction, IdempotencyKeyRow.TableName).All(static column => column.Name != "response_json")) return;
            if (step is 0) DropDeclaredNonuniqueIndexes(connection, transaction, IdempotencyKeyRow.TableName, "idempotency response");
            else ExecuteSchemaSql(connection, transaction, "ALTER TABLE idempotency_keys DROP COLUMN response_json");
            transaction.Commit();
        }
    }
}
