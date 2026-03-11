namespace SurrealDB.Client.Batch;

internal static class BatchOperations
{
    public const int MaxChunkSize = 1000;

    public static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source)
    {
        var chunk = new List<T>(MaxChunkSize);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count == MaxChunkSize)
            {
                yield return chunk;
                chunk = new List<T>(MaxChunkSize);
            }
        }
        if (chunk.Count > 0) yield return chunk;
    }

    public static string BuildBatchInsert(string table, IReadOnlyList<string> jsonItems)
        => $"INSERT INTO {table} [{string.Join(", ", jsonItems)}];";

    public static string BuildBatchUpdate(IReadOnlyList<(string Id, string Json)> items)
        => string.Join(" ", items.Select(i => $"UPDATE {i.Id} CONTENT {i.Json};"));

    public static string BuildBatchDelete(IReadOnlyList<string> recordIds)
        => string.Join(" ", recordIds.Select(id => $"DELETE {id};"));
}
