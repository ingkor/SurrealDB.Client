namespace SurrealDB.Client.Tests.Unit;

using SurrealDB.Client.Batch;
using Xunit;

[Trait("Category", "Unit")]
public class BatchOperationTests
{
    [Fact]
    public void BatchOperations_BuildBatchInsert_SingleItem()
    {
        var result = BatchOperations.BuildBatchInsert("user", new[] { "{\"id\":\"1\"}" });
        Assert.Equal("INSERT INTO user [{\"id\":\"1\"}];", result);
    }

    [Fact]
    public void BatchOperations_BuildBatchInsert_MultipleItems()
    {
        var result = BatchOperations.BuildBatchInsert("user",
            new[] { "{\"id\":\"1\"}", "{\"id\":\"2\"}" });
        Assert.Contains("INSERT INTO user", result);
        Assert.Contains("{\"id\":\"1\"}", result);
        Assert.Contains("{\"id\":\"2\"}", result);
    }

    [Fact]
    public void BatchOperations_BuildBatchUpdate_SingleItem()
    {
        var result = BatchOperations.BuildBatchUpdate(
            new[] { ("user:1", "{\"name\":\"Alice\"}") });
        Assert.Contains("UPDATE user:1 CONTENT", result);
        Assert.Contains("{\"name\":\"Alice\"}", result);
    }

    [Fact]
    public void BatchOperations_BuildBatchDelete_MultipleIds()
    {
        var result = BatchOperations.BuildBatchDelete(
            new[] { "user:1", "user:2", "user:3" });
        Assert.Contains("DELETE user:1", result);
        Assert.Contains("DELETE user:2", result);
        Assert.Contains("DELETE user:3", result);
    }

    [Fact]
    public void BatchOperations_Chunk_ExactlyMaxChunkSize_ReturnsSingleChunk()
    {
        var items = Enumerable.Range(0, BatchOperations.MaxChunkSize).ToList();
        var chunks = BatchOperations.Chunk(items).ToList();
        Assert.Single(chunks);
        Assert.Equal(BatchOperations.MaxChunkSize, chunks[0].Count);
    }

    [Fact]
    public void BatchOperations_Chunk_OverMaxChunkSize_ReturnsTwoChunks()
    {
        var items = Enumerable.Range(0, BatchOperations.MaxChunkSize + 1).ToList();
        var chunks = BatchOperations.Chunk(items).ToList();
        Assert.Equal(2, chunks.Count);
        Assert.Equal(BatchOperations.MaxChunkSize, chunks[0].Count);
        Assert.Equal(1, chunks[1].Count);
    }

    [Fact]
    public void BatchOperations_Chunk_EmptyInput_ReturnsNoChunks()
    {
        var chunks = BatchOperations.Chunk(Enumerable.Empty<int>()).ToList();
        Assert.Empty(chunks);
    }

    [Fact]
    public void SurrealDbClientOptions_BatchThreshold_DefaultIsFive()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test"
        };
        Assert.Equal(5, opts.BatchThreshold);
    }

    [Fact]
    public void SurrealDbClientOptions_BatchThreshold_NegativeThrows()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            BatchThreshold = -1
        };
        Assert.Throws<Exceptions.ValidationException>(() => opts.Validate());
    }

    [Fact]
    public void SurrealDbClientOptions_BatchThreshold_ZeroIsValid()
    {
        var opts = new SurrealDbClientOptions
        {
            ConnectionString = "surreal://localhost:8000",
            Namespace = "test",
            Database = "test",
            BatchThreshold = 0
        };
        var ex = Record.Exception(() => opts.Validate());
        Assert.Null(ex);
    }
}
