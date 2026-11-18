using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class CsrfTokenTests
{
    private readonly List<CsrfToken> _stored = new();
    private readonly Mock<IMongoCollection<CsrfToken>> _collection = new();
    private readonly Mock<IMongoDatabase> _database = new();

    public CsrfTokenTests()
    {
        var indexes = new Mock<IMongoIndexManager<CsrfToken>>();
        indexes.Setup(i => i.CreateManyAsync(It.IsAny<IEnumerable<CreateIndexModel<CsrfToken>>>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "expiry", "user" });
        _collection.SetupGet(c => c.Indexes).Returns(indexes.Object);
        _collection.Setup(c => c.InsertOneAsync(It.IsAny<CsrfToken>(), It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>())).Callback<CsrfToken, InsertOneOptions, CancellationToken>(
                (token, _, _) => _stored.Add(token)).Returns(Task.CompletedTask);
        _collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<CsrfToken>>(),
            It.IsAny<FindOptions<CsrfToken, CsrfToken>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<CsrfToken> filter, FindOptions<CsrfToken, CsrfToken> options, CancellationToken _) =>
                Task.FromResult(Cursor(_stored.Where(t => Matches(filter, t))
                    .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TokenHash)
                    .Skip(options.Skip ?? 0).ToArray())));
        _collection.Setup(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<CsrfToken>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<CsrfToken> filter, CancellationToken _) =>
                Task.FromResult<DeleteResult>(new DeleteResult.Acknowledged(_stored.RemoveAll(t => Matches(filter, t)))));
        _collection.Setup(c => c.FindOneAndDeleteAsync(It.IsAny<FilterDefinition<CsrfToken>>(),
            It.IsAny<FindOneAndDeleteOptions<CsrfToken, CsrfToken>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<CsrfToken> filter, FindOneAndDeleteOptions<CsrfToken, CsrfToken> _, CancellationToken _) =>
            {
                var token = _stored.FirstOrDefault(t => Matches(filter, t));
                if (token != null) _stored.Remove(token);
                return Task.FromResult(token!);
            });
        _database.Setup(d => d.GetCollection<CsrfToken>("CsrfTokens", It.IsAny<MongoCollectionSettings>()))
            .Returns(_collection.Object);
    }

    [Fact]
    public async Task TokenIssuedByOneWorkerIsValidOnAnotherWorkerWithoutStoringTheRawToken()
    {
        var token = await Worker().GenerateTokenAsync("admin");
        var stored = Assert.Single(_stored);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), stored.TokenHash);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.True(await Worker().ValidateTokenAsync(token, "admin"));
        Assert.True(await Worker().ValidateTokenAsync(token, "admin"));
    }

    [Fact]
    public async Task WrongUserTamperingAndExpiredTokensAreRejectedEvenBeforeTtlDeletion()
    {
        var token = await Worker().GenerateTokenAsync("admin");
        Assert.False(await Worker().ValidateTokenAsync(token, "other-admin"));
        Assert.False(await Worker().ValidateTokenAsync(token + "tampered", "admin"));
        Assert.False(await Worker().ValidateTokenAsync("", "admin"));
        _stored[0].ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        Assert.False(await Worker().ValidateTokenAsync(token, "admin"));
        Assert.Single(_stored);
    }

    [Fact]
    public async Task ConsumptionAndUserRevocationApplyAcrossWorkers()
    {
        var token = await Worker().GenerateTokenAsync("admin");
        Assert.False(await Worker().ValidateAndConsumeTokenAsync(token, "other-admin"));
        Assert.True(await Worker().ValidateAndConsumeTokenAsync(token, "admin"));
        Assert.False(await Worker().ValidateAndConsumeTokenAsync(token, "admin"));
        var next = await Worker().GenerateTokenAsync("admin");
        var other = await Worker().GenerateTokenAsync("other-admin");
        await Worker().RevokeUserTokensAsync("admin");
        Assert.False(await Worker().ValidateTokenAsync(next, "admin"));
        Assert.True(await Worker().ValidateTokenAsync(other, "other-admin"));
    }

    [Fact]
    public async Task IssuingTokensAcrossWorkersKeepsTenNewestTokensPerUser()
    {
        var oldest = await Worker().GenerateTokenAsync("admin");
        _stored[0].CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        var other = await Worker().GenerateTokenAsync("other-admin");
        string newest = "";
        for (var i = 0; i < 10; i++)
            newest = await Worker().GenerateTokenAsync("admin");
        Assert.Equal(10, _stored.Count(t => t.UserId == "admin"));
        Assert.False(await Worker().ValidateTokenAsync(oldest, "admin"));
        Assert.True(await Worker().ValidateTokenAsync(newest, "admin"));
        Assert.True(await Worker().ValidateTokenAsync(other, "other-admin"));
    }

    [Fact]
    public async Task DatabaseFailuresPropagateInsteadOfAllowingTheMutation()
    {
        _collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<CsrfToken>>(),
            It.IsAny<FindOptions<CsrfToken, CsrfToken>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Database unavailable"));
        await Assert.ThrowsAsync<TimeoutException>(() => Worker().ValidateTokenAsync("token", "admin"));
    }

    private CsrfTokenManager Worker() => new(_database.Object);

    private static bool Matches(FilterDefinition<CsrfToken> filter, CsrfToken token)
    {
        var registry = BsonSerializer.SerializerRegistry;
        var query = filter.Render(new RenderArgs<CsrfToken>(registry.GetSerializer<CsrfToken>(), registry));
        var document = token.ToBsonDocument();
        return query.All(field =>
        {
            var actual = document[field.Name];
            if (!field.Value.IsBsonDocument) return actual.Equals(field.Value);
            return field.Value.AsBsonDocument.All(condition => condition.Name switch
            {
                "$gt" => actual.CompareTo(condition.Value) > 0,
                "$lte" => actual.CompareTo(condition.Value) <= 0,
                "$in" => condition.Value.AsBsonArray.Contains(actual),
                _ => throw new NotSupportedException(condition.Name)
            });
        });
    }

    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(c => c.Current).Returns(rows);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}
