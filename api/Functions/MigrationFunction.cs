using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;
using System.Globalization;

namespace Cafe.Api.Functions
{
    public class MigrationFunction
    {
        private readonly ILogger _logger;
        private readonly IMongoDatabase _database;
        private readonly AuthService _authService;

        public MigrationFunction(ILoggerFactory loggerFactory, IMongoDatabase database, AuthService authService)
        {
            _logger = loggerFactory.CreateLogger<MigrationFunction>();
            _database = database;
            _authService = authService;
        }

        [Function("DryRunTimezoneAudit")]
        public async Task<HttpResponseData> DryRunTimezoneAudit(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "migrations/timezone-audit")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return authError!;

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var options = string.IsNullOrWhiteSpace(requestBody)
                    ? new TimezoneAuditOptions()
                    : JsonSerializer.Deserialize<TimezoneAuditOptions>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new TimezoneAuditOptions();

                var scope = string.IsNullOrWhiteSpace(options.Scope)
                    ? "all_business_collections"
                    : options.Scope.Trim().ToLowerInvariant();
                var sampleLimit = Math.Clamp(options.SampleLimit, 1, 20);

                var nowIst = MongoService.GetIstNow();
                var futureCutoff = nowIst.AddMinutes(5);

                var collectionSpecs = GetTimezoneAuditSpecs(scope);
                var collectionReports = new List<object>();
                var totalSuspicious = 0L;
                var totalFieldsWithFindings = 0;

                foreach (var spec in collectionSpecs)
                {
                    var collection = _database.GetCollection<BsonDocument>(spec.CollectionName);
                    var suspiciousByField = new List<object>();
                    var collectionTotal = 0L;

                    foreach (var fieldPath in spec.DateTimeFieldPaths)
                    {
                        var filter = Builders<BsonDocument>.Filter.Gt(fieldPath, futureCutoff);
                        var count = await collection.CountDocumentsAsync(filter);
                        if (count == 0) continue;

                        var sampleDocs = await collection.Find(filter)
                            .Project(Builders<BsonDocument>.Projection
                                .Include("_id")
                                .Include(fieldPath))
                            .Sort(Builders<BsonDocument>.Sort.Descending(fieldPath))
                            .Limit(sampleLimit)
                            .ToListAsync();

                        var samples = sampleDocs.Select(doc => new
                        {
                            id = doc.GetValue("_id", BsonNull.Value).ToString(),
                            value = ReadBsonPath(doc, fieldPath)
                        }).ToList();

                        suspiciousByField.Add(new
                        {
                            field = fieldPath,
                            suspiciousCount = count,
                            sampleValues = samples
                        });

                        collectionTotal += count;
                        totalSuspicious += count;
                        totalFieldsWithFindings++;
                    }

                    collectionReports.Add(new
                    {
                        collection = spec.CollectionName,
                        suspiciousTotal = collectionTotal,
                        fields = suspiciousByField
                    });
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    dryRun = true,
                    scope,
                    generatedAtIst = nowIst.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    futureCutoffIst = futureCutoff.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    summary = new
                    {
                        collectionsScanned = collectionSpecs.Count,
                        fieldsScanned = collectionSpecs.Sum(x => x.DateTimeFieldPaths.Count),
                        fieldsWithFindings = totalFieldsWithFindings,
                        suspiciousRecords = totalSuspicious
                    },
                    collections = collectionReports
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timezone audit dry-run failed");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = "Failed to run timezone audit dry-run" });
                return errorResponse;
            }
        }

        [Function("MigrateToMultiOutlet")]
        public async Task<HttpResponseData> MigrateToMultiOutlet(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            _logger.LogInformation("Starting multi-outlet migration");

            try
            {
                var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return authError!;

                // Parse request body
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var options = JsonSerializer.Deserialize<MigrationOptions>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var dryRun = options?.DryRun ?? true;
                var defaultOutletId = options?.DefaultOutletId ?? "MTC001";

                // Get the default outlet
                var outletsCollection = _database.GetCollection<BsonDocument>("Outlets");
                var outlet = await outletsCollection.Find(new BsonDocument("outletCode", defaultOutletId)).FirstOrDefaultAsync();

                if (outlet == null)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { error = $"Default outlet '{defaultOutletId}' not found" });
                    return response;
                }

                var outletObjectId = outlet["_id"].AsObjectId.ToString();
                _logger.LogInformation($"Using default outlet: {defaultOutletId} (ID: {outletObjectId})");

                var results = new Dictionary<string, object>();

                // Collections to migrate
                var collections = new[]
                {
                    "Sales", "OnlineSales", "Expenses", "OperationalExpenses",
                    "OverheadCosts", "DailyCashReconciliation", "Inventory", "PriceForecasts", "PriceHistory"
                };

                foreach (var collectionName in collections)
                {
                    var collection = _database.GetCollection<BsonDocument>(collectionName);
                    
                    // Count documents without outletId
                    var filter = Builders<BsonDocument>.Filter.Exists("outletId", false);
                    var count = await collection.CountDocumentsAsync(filter);

                    if (count > 0)
                    {
                        _logger.LogInformation($"{collectionName}: {count} documents need migration");

                        if (!dryRun)
                        {
                            // Update documents to add outletId
                            var update = Builders<BsonDocument>.Update.Set("outletId", new ObjectId(outletObjectId));
                            var result = await collection.UpdateManyAsync(filter, update);
                            results[collectionName] = new { updated = result.ModifiedCount };
                        }
                        else
                        {
                            results[collectionName] = new { toUpdate = count };
                        }
                    }
                    else
                    {
                        results[collectionName] = new { message = "All documents already have outletId" };
                    }
                }

                // Handle Users collection separately
                var usersCollection = _database.GetCollection<BsonDocument>("Users");
                var usersFilter = Builders<BsonDocument>.Filter.Exists("defaultOutletId", false);
                var usersCount = await usersCollection.CountDocumentsAsync(usersFilter);

                if (usersCount > 0)
                {
                    _logger.LogInformation($"Users: {usersCount} documents need migration");

                    if (!dryRun)
                    {
                        var usersUpdate = Builders<BsonDocument>.Update
                            .Set("defaultOutletId", new ObjectId(outletObjectId))
                            .Set("assignedOutlets", new BsonArray { new ObjectId(outletObjectId) });
                        var result = await usersCollection.UpdateManyAsync(usersFilter, usersUpdate);
                        results["Users"] = new { updated = result.ModifiedCount };
                    }
                    else
                    {
                        results["Users"] = new { toUpdate = usersCount };
                    }
                }
                else
                {
                    results["Users"] = new { message = "All users already have outlet assignments" };
                }

                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    success = true,
                    dryRun,
                    defaultOutlet = new { code = defaultOutletId, id = outletObjectId },
                    results
                });

                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "An internal error occurred" });
                return errorResponse;
            }
        }

        private class MigrationOptions
        {
            public bool DryRun { get; set; } = true;
            public string DefaultOutletId { get; set; } = "MTC001";
        }

        private class TimezoneAuditOptions
        {
            public string Scope { get; set; } = "all_business_collections";
            public int SampleLimit { get; set; } = 5;
        }

        private sealed class TimezoneAuditCollectionSpec
        {
            public string CollectionName { get; init; } = string.Empty;
            public List<string> DateTimeFieldPaths { get; init; } = new();
        }

        private static List<TimezoneAuditCollectionSpec> GetTimezoneAuditSpecs(string scope)
        {
            var dineInOnly = new List<TimezoneAuditCollectionSpec>
            {
                new()
                {
                    CollectionName = "DineInSessions",
                    DateTimeFieldPaths = new List<string>
                    {
                        "createdAt", "updatedAt", "billRequestedAt", "settledAt", "lastPaymentRequestedAt",
                        "timeline.createdAt", "paymentEntries.requestedAt", "paymentEntries.confirmedAt",
                        "voidRequests.requestedAt", "voidRequests.reviewedAt"
                    }
                },
                new()
                {
                    CollectionName = "EventLogs",
                    DateTimeFieldPaths = new List<string> { "timestamp" }
                }
            };

            if (scope == "dinein_only")
                return dineInOnly;

            var all = new List<TimezoneAuditCollectionSpec>(dineInOnly)
            {
                new()
                {
                    CollectionName = "Orders",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "deliveredAt", "scheduledAt", "preparingAt", "readyAt" }
                },
                new()
                {
                    CollectionName = "Sales",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt", "deletedAt" }
                },
                new()
                {
                    CollectionName = "Expenses",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt", "deletedAt" }
                },
                new()
                {
                    CollectionName = "OnlineSales",
                    DateTimeFieldPaths = new List<string> { "orderAt", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "OperationalExpenses",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "PlatformCharges",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "CashReconciliations",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "Attendance",
                    DateTimeFieldPaths = new List<string> { "date", "checkInTime", "checkOutTime", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "TableReservations",
                    DateTimeFieldPaths = new List<string> { "reservationDate", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "WastageRecords",
                    DateTimeFieldPaths = new List<string> { "date", "createdAt", "updatedAt" }
                },
                new()
                {
                    CollectionName = "Inventory",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "receivedDate", "expiryDate" }
                },
                new()
                {
                    CollectionName = "Notifications",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "readAt" }
                },
                new()
                {
                    CollectionName = "DeliveryShifts",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "shiftDate", "startTime", "endTime" }
                },
                new()
                {
                    CollectionName = "PartnerTripLogs",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "tripStartedAt", "tripEndedAt" }
                },
                new()
                {
                    CollectionName = "CODCollectionLogs",
                    DateTimeFieldPaths = new List<string> { "createdAt", "updatedAt", "collectedAt" }
                }
            };

            return all;
        }

        private static string? ReadBsonPath(BsonDocument doc, string path)
        {
            try
            {
                var tokens = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
                BsonValue current = doc;

                foreach (var token in tokens)
                {
                    if (current is BsonDocument currentDoc)
                    {
                        if (!currentDoc.TryGetValue(token, out var next))
                            return null;
                        current = next;
                        continue;
                    }

                    if (current is BsonArray currentArray)
                    {
                        var arrayValues = currentArray
                            .OfType<BsonDocument>()
                            .Select(item => item.TryGetValue(token, out var v) ? v : BsonNull.Value)
                            .Where(v => v != BsonNull.Value)
                            .Select(v => v.ToString())
                            .ToList();
                        return arrayValues.Count == 0 ? null : string.Join(", ", arrayValues.Take(3));
                    }

                    return current.ToString();
                }

                return current.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
