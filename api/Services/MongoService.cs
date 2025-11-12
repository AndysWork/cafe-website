using MongoDB.Driver;
using Cafe.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Cafe.Api.Services;

public class MongoService
{
    private readonly IMongoCollection<CafeMenuItem> _menu;
    public MongoService(IConfiguration config)
    {
        var cs = config["Mongo__ConnectionString"];
        var dbName = config["Mongo__Database"];
        var client = new MongoClient(cs);
        var db = client.GetDatabase(dbName);
        _menu = db.GetCollection<CafeMenuItem>("menu");
    }

    public async Task<List<CafeMenuItem>> GetMenuAsync() =>
        await _menu.Find(_ => true).ToListAsync();
}
