using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public interface IManagerDashboardService
{
    Task<ManagerDashboardResponse> GetAsync(string outletId, CancellationToken cancellationToken);
}

public sealed class ManagerDashboardService(
    IFinanceRepository finance, IMenuRepository menu, IInventoryRepository inventory,
    IStaffRepository staff, ILogger<ManagerDashboardService> logger) : IManagerDashboardService
{
    public async Task<ManagerDashboardResponse> GetAsync(string outletId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outletId);
        cancellationToken.ThrowIfCancellationRequested();
        var online = finance.GetOnlineSalesAsync(outletId: outletId, includeWebSales: true);
        var offline = finance.GetAllSalesAsync(outletId);
        var expenses = finance.GetAllExpensesAsync(outletId);
        var items = menu.GetMenuAsync(outletId);
        var attendance = ReadOptional(() => staff.GetAllTodayAttendanceAsync(outletId), "Staff attendance");
        var alerts = ReadOptional(() => inventory.GetAllAlertsAsync(outletId), "Stock alerts");
        await Task.WhenAll(online, offline, expenses, items, attendance, alerts).WaitAsync(cancellationToken);
        var attendanceResult = await attendance;
        var alertsResult = await alerts;
        var unavailable = new List<string>();
        if (attendanceResult == null) unavailable.Add("Staff attendance");
        if (alertsResult == null) unavailable.Add("Stock alerts");
        return new(
            (await items).Select(item => new DashboardMenuItemResponse(item.Id, item.Name,
                item.CategoryId ?? "", item.Category, item.OnlinePrice, item.IsAvailable)).ToArray(),
            (await online).Select(sale => new DashboardOnlineSaleResponse(
                sale.Platform, sale.OrderId, sale.CustomerName, Timestamp(sale.OrderAt),
                sale.OrderedItems.Select(item => new DashboardOrderedItemResponse(item.ItemName, item.Quantity)).ToArray(),
                sale.BillSubTotal, sale.PackagingCharges, sale.DiscountAmount, sale.Payout,
                sale.PlatformDeduction, sale.Rating, sale.Review, sale.Freebies)).ToArray(),
            (await offline).Select(sale => new DashboardOfflineSaleResponse(
                sale.Id, Timestamp(sale.Date), sale.Items.Select(item => new DashboardSalesItemResponse(
                    item.ItemName, item.Quantity, item.UnitPrice, item.TotalPrice)).ToArray(),
                sale.TotalAmount, sale.PaymentMethod, sale.RecordedBy, Timestamp(sale.CreatedAt), Timestamp(sale.UpdatedAt))).ToArray(),
            (await expenses).Select(expense => new DashboardExpenseResponse(
                expense.Id, Timestamp(expense.Date), expense.ExpenseType, expense.ExpenseSource,
                expense.Amount, expense.PaymentMethod, expense.RecordedBy,
                Timestamp(expense.CreatedAt), Timestamp(expense.UpdatedAt))).ToArray(),
            attendanceResult?.Select(row => new DashboardAttendanceResponse(
                row.StaffId, row.Status, OptionalTimestamp(row.ClockIn), OptionalTimestamp(row.ClockOut),
                row.Sessions.Select(session => new DashboardAttendanceSessionResponse(
                    OptionalTimestamp(session.ClockIn), OptionalTimestamp(session.ClockOut))).ToArray())).ToArray(),
            alertsResult?.Select(alert => new DashboardStockAlertResponse(
                alert.Id, alert.InventoryId, alert.IngredientName, alert.Type.ToString(), alert.Severity.ToString(),
                alert.Message, alert.IsResolved, Timestamp(alert.CreatedAt))).ToArray(),
            unavailable);
    }

    private async Task<List<T>?> ReadOptional<T>(Func<Task<List<T>>> read, string source)
    {
        try { return await read(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to load Manager dashboard {Source}", source);
            return null;
        }
    }

    // Match the existing client: unzoned financial/attendance dates are IST; UTC dates stay UTC.
    private static DateTimeOffset Timestamp(DateTime date) => date == DateTime.MinValue
        ? DateTimeOffset.MinValue
        : date.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(date, TimeSpan.FromMinutes(330))
            : new DateTimeOffset(date);

    private static DateTimeOffset? OptionalTimestamp(DateTime? date) => date.HasValue ? Timestamp(date.Value) : null;
}
