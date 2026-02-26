using Xunit;
using SurrealDB.Client;
using SurrealDB.Client.Exceptions;
using SurrealDB.Client.Serialization;

namespace SurrealDB.Client.Tests.Unit;

/// <summary>
/// Tests for SurrealDbResponse&lt;T&gt; model and deserialization via SystemTextJsonSerializer.
/// </summary>
public class SurrealDbResponseTests
{
    // ---------------------------------------------------------------------------
    // Helper types used across tests
    // ---------------------------------------------------------------------------

    private sealed record Person(string Name, int Age);

    private readonly SystemTextJsonSerializer _serializer = new();

    // ---------------------------------------------------------------------------
    // SurrealDbResponse<T> model property tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void SurrealDbResponse_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var response = new SurrealDbResponse<Person>();

        // Assert
        Assert.Equal(string.Empty, response.Status);
        Assert.Equal(string.Empty, response.Time);
        Assert.NotNull(response.Result);
        Assert.Empty(response.Result);
    }

    [Fact]
    public void SurrealDbResponse_IsSuccess_TrueWhenStatusIsOK()
    {
        // Arrange
        var response = new SurrealDbResponse<Person> { Status = "OK" };

        // Act & Assert
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void SurrealDbResponse_IsSuccess_TrueWhenStatusIsOKCaseInsensitive()
    {
        // Arrange
        var response = new SurrealDbResponse<Person> { Status = "ok" };

        // Act & Assert
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void SurrealDbResponse_IsSuccess_FalseWhenStatusIsError()
    {
        // Arrange
        var response = new SurrealDbResponse<Person> { Status = "ERR" };

        // Act & Assert
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void SurrealDbResponse_IsSuccess_FalseWhenStatusIsEmpty()
    {
        // Arrange
        var response = new SurrealDbResponse<Person> { Status = string.Empty };

        // Act & Assert
        Assert.False(response.IsSuccess);
    }

    // ---------------------------------------------------------------------------
    // EnsureSuccess / GetResultOrThrow validation tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void EnsureSuccess_DoesNotThrow_WhenStatusIsOK()
    {
        // Arrange
        var response = new SurrealDbResponse<Person>
        {
            Status = "OK",
            Time = "1.2ms",
            Result = new[] { new Person("Alice", 30) }
        };

        // Act & Assert - should not throw
        response.EnsureSuccess();
    }

    [Fact]
    public void EnsureSuccess_Throws_WhenStatusIsNotOK()
    {
        // Arrange
        var response = new SurrealDbResponse<Person>
        {
            Status = "ERR",
            Time = "0.5ms",
            Result = Array.Empty<Person>()
        };

        // Act & Assert
        var ex = Assert.Throws<QueryException>(() => response.EnsureSuccess());
        Assert.Contains("ERR", ex.Message);
    }

    [Fact]
    public void EnsureSuccess_Throws_WithErrorStatusInMessage()
    {
        // Arrange
        var response = new SurrealDbResponse<Person> { Status = "SOME_ERROR_CODE" };

        // Act
        var ex = Assert.Throws<QueryException>(() => response.EnsureSuccess());

        // Assert
        Assert.Contains("SOME_ERROR_CODE", ex.Message);
    }

    [Fact]
    public void GetResultOrThrow_ReturnsResult_WhenStatusIsOK()
    {
        // Arrange
        var expected = new[] { new Person("Alice", 30), new Person("Bob", 25) };
        var response = new SurrealDbResponse<Person>
        {
            Status = "OK",
            Time = "1.5ms",
            Result = expected
        };

        // Act
        var result = response.GetResultOrThrow();

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void GetResultOrThrow_ReturnsEmptyArray_WhenResultIsEmpty()
    {
        // Arrange
        var response = new SurrealDbResponse<Person>
        {
            Status = "OK",
            Time = "0.1ms",
            Result = Array.Empty<Person>()
        };

        // Act
        var result = response.GetResultOrThrow();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetResultOrThrow_Throws_WhenStatusIsError()
    {
        // Arrange
        var response = new SurrealDbResponse<Person>
        {
            Status = "ERR",
            Time = "0.5ms",
            Result = Array.Empty<Person>()
        };

        // Act & Assert
        Assert.Throws<QueryException>(() => response.GetResultOrThrow());
    }

    // ---------------------------------------------------------------------------
    // Result typed as T[] array tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void SurrealDbResponse_Result_IsTypedArray()
    {
        // Arrange
        var response = new SurrealDbResponse<Person>
        {
            Status = "OK",
            Result = new[] { new Person("Alice", 30) }
        };

        // Act
        var result = response.Result;

        // Assert
        Assert.IsType<Person[]>(result);
    }

    [Fact]
    public void SurrealDbResponse_Result_SupportsValueTypes()
    {
        // Arrange
        var response = new SurrealDbResponse<int>
        {
            Status = "OK",
            Result = new[] { 1, 2, 3 }
        };

        // Act & Assert
        Assert.Equal(new[] { 1, 2, 3 }, response.Result);
        Assert.IsType<int[]>(response.Result);
    }

    [Fact]
    public void SurrealDbResponse_Result_SupportsStringType()
    {
        // Arrange
        var response = new SurrealDbResponse<string>
        {
            Status = "OK",
            Result = new[] { "alpha", "beta", "gamma" }
        };

        // Act
        var result = response.GetResultOrThrow();

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal("alpha", result[0]);
    }

    // ---------------------------------------------------------------------------
    // Deserialization via SystemTextJsonSerializer.DeserializeResponse<T>
    // ---------------------------------------------------------------------------

    [Fact]
    public void DeserializeResponse_WithValidTypedResult_ReturnsPopulatedResponse()
    {
        // Arrange
        var json = """
            {
                "status": "OK",
                "time": "1.5ms",
                "result": [
                    { "name": "Alice", "age": 30 },
                    { "name": "Bob",   "age": 25 }
                ]
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.Equal("OK", response.Status);
        Assert.Equal("1.5ms", response.Time);
        Assert.Equal(2, response.Result.Length);
        Assert.Equal("Alice", response.Result[0].Name);
        Assert.Equal(30, response.Result[0].Age);
        Assert.Equal("Bob", response.Result[1].Name);
        Assert.Equal(25, response.Result[1].Age);
    }

    [Fact]
    public void DeserializeResponse_WithEmptyResultArray_ReturnsEmptyResult()
    {
        // Arrange
        var json = """
            {
                "status": "OK",
                "time": "0.2ms",
                "result": []
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.Equal("OK", response.Status);
        Assert.Equal("0.2ms", response.Time);
        Assert.NotNull(response.Result);
        Assert.Empty(response.Result);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void DeserializeResponse_WithErrorStatus_DeserializesCorrectly()
    {
        // Arrange
        var json = """
            {
                "status": "ERR",
                "time": "0.5ms",
                "result": []
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.Equal("ERR", response.Status);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void DeserializeResponse_WithErrorStatus_EnsureSuccessThrows()
    {
        // Arrange
        var json = """
            {
                "status": "ERR",
                "time": "0.5ms",
                "result": []
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.Throws<QueryException>(() => response.EnsureSuccess());
    }

    [Fact]
    public void DeserializeResponse_WithSingleItemResult_ReturnsCorrectArray()
    {
        // Arrange
        var json = """
            {
                "status": "OK",
                "time": "0.8ms",
                "result": [
                    { "name": "Charlie", "age": 42 }
                ]
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.Single(response.Result);
        Assert.Equal("Charlie", response.Result[0].Name);
        Assert.Equal(42, response.Result[0].Age);
    }

    [Fact]
    public void DeserializeResponse_WithNullOrEmptyJson_ThrowsSerializationException()
    {
        // Act & Assert
        Assert.Throws<SerializationException>(() => _serializer.DeserializeResponse<Person>(string.Empty));
        Assert.Throws<SerializationException>(() => _serializer.DeserializeResponse<Person>("   "));
    }

    [Fact]
    public void DeserializeResponse_WithInvalidJson_ThrowsSerializationException()
    {
        // Arrange
        const string badJson = "this is not valid JSON {{{";

        // Act & Assert
        Assert.Throws<SerializationException>(() => _serializer.DeserializeResponse<Person>(badJson));
    }

    [Fact]
    public void DeserializeResponse_ResultIsProperlyTypedAsArray()
    {
        // Arrange
        var json = """
            {
                "status": "OK",
                "time": "1.0ms",
                "result": [
                    { "name": "Dave", "age": 35 }
                ]
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);

        // Assert
        Assert.IsType<Person[]>(response.Result);
        Assert.IsType<SurrealDbResponse<Person>>(response);
    }

    [Fact]
    public void DeserializeResponse_IntegrationFlow_GetResultOrThrow_ReturnsData()
    {
        // Arrange
        var json = """
            {
                "status": "OK",
                "time": "2.1ms",
                "result": [
                    { "name": "Eve", "age": 28 },
                    { "name": "Frank", "age": 33 }
                ]
            }
            """;

        // Act
        var response = _serializer.DeserializeResponse<Person>(json);
        var results = response.GetResultOrThrow();

        // Assert
        Assert.Equal(2, results.Length);
        Assert.Equal("Eve", results[0].Name);
        Assert.Equal("Frank", results[1].Name);
    }
}
