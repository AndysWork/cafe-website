using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using OfficeOpenXml;

namespace Cafe.Api.Functions;

public class ExpenseFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public ExpenseFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<ExpenseFunction>();
    }

    // GET: Get all expenses (Admin only)
    [Function("GetAllExpenses")]
    public async Task<HttpResponseData> GetAllExpenses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            _log.LogInformation("Fetching all expenses from database...");
            var expenses = await _mongo.GetAllExpensesAsync();
            _log.LogInformation($"Successfully fetched {expenses.Count} expenses");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expenses: {ex.Message}");
            _log.LogError($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                _log.LogError($"Inner exception: {ex.InnerException.Message}");
            }
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expenses", details = ex.Message });
            return error;
        }
    }

    // GET: Get expense by ID (Admin only)
    [Function("GetExpenseById")]
    public async Task<HttpResponseData> GetExpenseById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/detail/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expense = await _mongo.GetExpenseByIdAsync(id);
            
            if (expense == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Expense not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expense by ID: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expense" });
            return error;
        }
    }

    // GET: Get expenses by date range (Admin only)
    [Function("GetExpensesByDateRange")]
    public async Task<HttpResponseData> GetExpensesByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/range")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];

            if (!DateTime.TryParse(startDateStr, out var startDate) || 
                !DateTime.TryParse(endDateStr, out var endDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }

            // Convert to IST date (date-only, no time component)
            startDate = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, 0, DateTimeKind.Unspecified);
            endDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 23, 59, 59, 999, DateTimeKind.Unspecified);

            var expenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expenses by date range: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expenses" });
            return error;
        }
    }

    // GET: Get expense summary by date (Admin only)
    [Function("GetExpenseSummary")]
    public async Task<HttpResponseData> GetExpenseSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/summary")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var dateStr = query["date"];

            if (!DateTime.TryParse(dateStr, out var date))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }

            // Convert to IST date (date-only, no time component)
            date = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, 0, DateTimeKind.Unspecified);

            var summary = await _mongo.GetExpenseSummaryByDateAsync(date);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expense summary: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expense summary" });
            return error;
        }
    }

    // GET: Get expenses grouped by year/month/week (Admin only)
    [Function("GetExpensesHierarchical")]
    public async Task<HttpResponseData> GetExpensesHierarchical(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/hierarchical")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var source = query["source"] ?? "Offline"; // "Offline", "Online", or "All"

            var allExpenses = await _mongo.GetAllExpensesAsync();

            // Filter by source
            var filteredExpenses = source == "All" 
                ? allExpenses 
                : allExpenses.Where(e => e.ExpenseSource == source).ToList();

            // Group by Year -> Month -> Week
            var hierarchical = filteredExpenses
                .GroupBy(e => e.Date.Year)
                .Select(yearGroup => new
                {
                    Year = yearGroup.Key,
                    TotalAmount = yearGroup.Sum(e => e.Amount),
                    ExpenseCount = yearGroup.Count(),
                    Months = yearGroup
                        .GroupBy(e => e.Date.Month)
                        .Select(monthGroup => new
                        {
                            Month = monthGroup.Key,
                            MonthName = new DateTime(yearGroup.Key, monthGroup.Key, 1).ToString("MMMM"),
                            TotalAmount = monthGroup.Sum(e => e.Amount),
                            ExpenseCount = monthGroup.Count(),
                            Weeks = monthGroup
                                .GroupBy(e => GetWeekOfMonth(e.Date))
                                .Select(weekGroup => new
                                {
                                    Week = weekGroup.Key,
                                    WeekLabel = $"Week {weekGroup.Key}",
                                    TotalAmount = weekGroup.Sum(e => e.Amount),
                                    ExpenseCount = weekGroup.Count(),
                                    Expenses = weekGroup.OrderByDescending(e => e.Date).ToList()
                                })
                                .OrderBy(w => w.Week)
                                .ToList()
                        })
                        .OrderByDescending(m => m.Month)
                        .ToList()
                })
                .OrderByDescending(y => y.Year)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(hierarchical);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting hierarchical expenses: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get hierarchical expenses" });
            return error;
        }
    }

    // GET: Get expense analytics (Admin only)
    [Function("GetExpenseAnalytics")]
    public async Task<HttpResponseData> GetExpenseAnalytics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "expenses/analytics")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];
            var source = query["source"] ?? "All"; // "Offline", "Online", or "All"

            DateTime startDate = string.IsNullOrEmpty(startDateStr) 
                ? MongoService.GetIstNow().AddMonths(-6) 
                : DateTime.Parse(startDateStr);
            DateTime endDate = string.IsNullOrEmpty(endDateStr) 
                ? MongoService.GetIstNow() 
                : DateTime.Parse(endDateStr);

            var allExpenses = await _mongo.GetExpensesByDateRangeAsync(startDate, endDate);

            // Filter by source
            var expenses = source == "All" 
                ? allExpenses 
                : allExpenses.Where(e => e.ExpenseSource == source).ToList();

            // Calculate analytics
            var totalExpenses = expenses.Sum(e => e.Amount);
            var expenseCount = expenses.Count;
            var averageExpense = expenseCount > 0 ? totalExpenses / expenseCount : 0;

            // Top expense types
            var topExpenseTypes = expenses
                .GroupBy(e => e.ExpenseType)
                .Select(g => new
                {
                    ExpenseType = g.Key,
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count(),
                    AverageAmount = g.Average(e => e.Amount),
                    Percentage = totalExpenses > 0 ? (g.Sum(e => e.Amount) / totalExpenses) * 100 : 0
                })
                .OrderByDescending(x => x.TotalAmount)
                .Take(10)
                .ToList();

            // Payment method breakdown
            var paymentMethodBreakdown = expenses
                .GroupBy(e => e.PaymentMethod)
                .Select(g => new
                {
                    PaymentMethod = g.Key,
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count(),
                    Percentage = totalExpenses > 0 ? (g.Sum(e => e.Amount) / totalExpenses) * 100 : 0
                })
                .OrderByDescending(x => x.TotalAmount)
                .ToList();

            // Source breakdown (if All)
            var sourceBreakdown = expenses
                .GroupBy(e => e.ExpenseSource)
                .Select(g => new
                {
                    Source = g.Key,
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count(),
                    Percentage = totalExpenses > 0 ? (g.Sum(e => e.Amount) / totalExpenses) * 100 : 0
                })
                .OrderByDescending(x => x.TotalAmount)
                .ToList();

            // Daily average
            var dateRange = (endDate - startDate).Days + 1;
            var dailyAverage = totalExpenses / Math.Max(dateRange, 1);

            // Weekly trend (last 8 weeks)
            var weeklyTrend = expenses
                .GroupBy(e => GetWeekStartDate(e.Date))
                .Select(g => new
                {
                    WeekStartDate = g.Key,
                    WeekStart = g.Key.ToString("MMM dd, yyyy"),
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.WeekStartDate)
                .Take(8)
                .OrderBy(x => x.WeekStartDate)
                .Select(x => new
                {
                    x.WeekStart,
                    x.TotalAmount,
                    x.Count
                })
                .ToList();

            // Monthly comparison (last 12 months)
            var monthlyComparison = expenses
                .GroupBy(e => new { Year = e.Date.Year, Month = e.Date.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count(),
                    AverageExpense = g.Average(e => e.Amount)
                })
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .Take(12)
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();

            // Peak expense days
            var peakExpenseDays = expenses
                .GroupBy(e => e.Date.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("MMM dd, yyyy"),
                    TotalAmount = g.Sum(e => e.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.TotalAmount)
                .Take(10)
                .ToList();

            // Growth rate calculation - compare with same date range from previous month
            var daysDiff = (endDate - startDate).Days;
            var prevStartDate = startDate.AddMonths(-1);
            var prevEndDate = prevStartDate.AddDays(daysDiff);
            
            var prevPeriodExpenses = await _mongo.GetExpensesByDateRangeAsync(prevStartDate, prevEndDate);
            var prevPeriodFiltered = source == "All" 
                ? prevPeriodExpenses 
                : prevPeriodExpenses.Where(e => e.ExpenseSource == source).ToList();
            
            var prevPeriodTotal = prevPeriodFiltered.Sum(e => e.Amount);
            var currentPeriodTotal = totalExpenses;
            
            var growthRate = prevPeriodTotal > 0 
                ? ((currentPeriodTotal - prevPeriodTotal) / prevPeriodTotal) * 100 
                : (currentPeriodTotal > 0 ? 100 : 0);

            // Expense trends by type over time
            var expenseTypesTrend = expenses
                .GroupBy(e => e.ExpenseType)
                .Select(typeGroup => new
                {
                    ExpenseType = typeGroup.Key,
                    MonthlyTrend = typeGroup
                        .GroupBy(e => new { Year = e.Date.Year, Month = e.Date.Month })
                        .Select(g => new
                        {
                            MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                            TotalAmount = g.Sum(e => e.Amount)
                        })
                        .OrderBy(x => x.MonthName)
                        .ToList()
                })
                .Where(x => x.MonthlyTrend.Count > 0)
                .ToList();

            var analytics = new
            {
                Summary = new
                {
                    TotalExpenses = totalExpenses,
                    ExpenseCount = expenseCount,
                    AverageExpense = averageExpense,
                    DailyAverage = dailyAverage,
                    GrowthRate = growthRate,
                    DateRange = new { StartDate = startDate, EndDate = endDate },
                    Source = source
                },
                TopExpenseTypes = topExpenseTypes,
                PaymentMethodBreakdown = paymentMethodBreakdown,
                SourceBreakdown = sourceBreakdown,
                WeeklyTrend = weeklyTrend,
                MonthlyComparison = monthlyComparison,
                PeakExpenseDays = peakExpenseDays,
                ExpenseTypesTrend = expenseTypesTrend
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(analytics);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting expense analytics: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to get expense analytics" });
            return error;
        }
    }

    private static int GetWeekOfMonth(DateTime date)
    {
        var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
        return (date.Day - 1) / 7 + 1;
    }

    private static DateTime GetWeekStartDate(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var diff = dayOfWeek == 0 ? -6 : 1 - dayOfWeek; // Monday as start of week
        return date.AddDays(diff).Date;
    }

    // POST: Create new expense (Admin only)
    [Function("CreateExpense")]
    public async Task<HttpResponseData> CreateExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseRequest = await req.ReadFromJsonAsync<CreateExpenseRequest>();
            if (expenseRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid expense data" });
                return badRequest;
            }

            // Get username for recordedBy
            var user = await _mongo.GetUserByIdAsync(userId!);
            var username = user?.Username ?? "Admin";

            // Parse date as IST date (date-only, no time component)
            if (!DateTime.TryParse(expenseRequest.Date, out var expenseDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }
            var istDate = new DateTime(expenseDate.Year, expenseDate.Month, expenseDate.Day, 0, 0, 0, 0, DateTimeKind.Unspecified);

            var expense = new Expense
            {
                Date = istDate,
                ExpenseType = expenseRequest.ExpenseType,
                ExpenseSource = expenseRequest.ExpenseSource,
                Amount = expenseRequest.Amount,
                PaymentMethod = expenseRequest.PaymentMethod,
                Notes = expenseRequest.Notes,
                RecordedBy = username,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            var createdExpense = await _mongo.CreateExpenseAsync(expense);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdExpense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to create expense" });
            return error;
        }
    }

    // PUT: Update expense (Admin only)
    [Function("UpdateExpense")]
    public async Task<HttpResponseData> UpdateExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "expenses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseRequest = await req.ReadFromJsonAsync<CreateExpenseRequest>();
            if (expenseRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid expense data" });
                return badRequest;
            }

            var existingExpense = await _mongo.GetExpenseByIdAsync(id);
            if (existingExpense == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Expense not found" });
                return notFound;
            }

            // Parse date as IST date (date-only, no time component)
            if (!DateTime.TryParse(expenseRequest.Date, out var expenseDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid date format" });
                return badRequest;
            }
            var istDate = new DateTime(expenseDate.Year, expenseDate.Month, expenseDate.Day, 0, 0, 0, 0, DateTimeKind.Unspecified);

            existingExpense.Date = istDate;
            existingExpense.ExpenseType = expenseRequest.ExpenseType;
            existingExpense.Amount = expenseRequest.Amount;
            existingExpense.PaymentMethod = expenseRequest.PaymentMethod;
            existingExpense.Notes = expenseRequest.Notes;

            var success = await _mongo.UpdateExpenseAsync(id, existingExpense);

            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to update expense" });
                return error;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existingExpense);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error updating expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to update expense" });
            return error;
        }
    }

    // DELETE: Delete expense (Admin only)
    [Function("DeleteExpense")]
    public async Task<HttpResponseData> DeleteExpense(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "expenses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteExpenseAsync(id);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Expense not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Expense deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error deleting expense: {ex.Message}");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "Failed to delete expense" });
            return error;
        }
    }

    // POST: Upload expenses from Excel (Admin only)
    [Function("UploadExpensesExcel")]
    public async Task<HttpResponseData> UploadExpensesExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "expenses/upload")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Get username for recordedBy
            var user = await _mongo.GetUserByIdAsync(userId!);
            var username = user?.Username ?? "Admin";

            // Get expense source from query parameter (default to Offline)
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var expenseSource = query["source"] ?? "Offline";

            // Set EPPlus license context
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Read the file
            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No file data received" });
                return badRequest;
            }

            var result = await ProcessExpensesExcel(fileBytes, username, expenseSource);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading expenses Excel file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    private async Task<object> ProcessExpensesExcel(byte[] fileBytes, string recordedBy, string expenseSource = "Offline")
    {
        using var package = new ExcelPackage(new MemoryStream(fileBytes));
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
            throw new Exception("No worksheet found in Excel file");

        var rowCount = worksheet.Dimension?.Rows ?? 0;
        if (rowCount < 2)
            throw new Exception("Excel file is empty or has no data rows");

        // Get valid expense types from database based on source
        List<string> validTypeNames;
        if (expenseSource == "Online")
        {
            var validExpenseTypes = await _mongo.GetActiveOnlineExpenseTypesAsync();
            validTypeNames = validExpenseTypes.Select(t => t.ExpenseType).ToList();
        }
        else
        {
            var validExpenseTypes = await _mongo.GetActiveOfflineExpenseTypesAsync();
            validTypeNames = validExpenseTypes.Select(t => t.ExpenseType).ToList();
        }
        
        if (!validTypeNames.Any())
            throw new Exception($"No {expenseSource.ToLower()} expense types found in database. Please initialize expense types first.");

        var expenses = new List<Expense>();
        var invalidTypes = new List<string>();

        // Expected columns: Date, ExpenseType, Amount, PaymentMethod
        for (int row = 2; row <= rowCount; row++)
        {
            var dateValue = worksheet.Cells[row, 1].Value?.ToString();
            var expenseType = worksheet.Cells[row, 2].Value?.ToString();
            var amountStr = worksheet.Cells[row, 3].Value?.ToString();
            var paymentMethod = worksheet.Cells[row, 4].Value?.ToString() ?? "Cash";

            if (string.IsNullOrEmpty(dateValue) || string.IsNullOrEmpty(expenseType) || 
                string.IsNullOrEmpty(amountStr))
                continue;

            if (!DateTime.TryParse(dateValue, out var date))
                continue;

            // Convert to IST date (date-only, no time component)
            var istDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);

            if (!decimal.TryParse(amountStr, out var amount))
                continue;

            // Validate expense type
            if (!validTypeNames.Contains(expenseType))
            {
                if (!invalidTypes.Contains(expenseType))
                    invalidTypes.Add(expenseType);
                continue; // Skip this expense
            }

            expenses.Add(new Expense
            {
                Date = istDate,
                ExpenseType = expenseType,
                ExpenseSource = expenseSource,
                Amount = amount,
                PaymentMethod = paymentMethod,
                RecordedBy = recordedBy,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            });
        }

        // Insert all expenses
        foreach (var expense in expenses)
        {
            await _mongo.CreateExpenseAsync(expense);
        }

        var warningMessage = invalidTypes.Any() 
            ? $" Warning: {invalidTypes.Count} records skipped due to invalid expense types: {string.Join(", ", invalidTypes)}" 
            : "";

        return new
        {
            message = $"Successfully uploaded {expenses.Count} {expenseSource.ToLower()} expense records.{warningMessage}",
            processedRecords = expenses.Count,
            skippedRecords = invalidTypes.Count,
            invalidExpenseTypes = invalidTypes,
            totalAmount = expenses.Sum(e => e.Amount),
            expenseSource = expenseSource
        };
    }
}
