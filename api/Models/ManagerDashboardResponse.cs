namespace Cafe.Api.Models;

/// <summary>Read-only dashboard data for one authorized outlet.</summary>
public sealed record ManagerDashboardResponse(
    IReadOnlyList<DashboardMenuItemResponse> MenuItems,
    IReadOnlyList<DashboardOnlineSaleResponse> OnlineSales,
    IReadOnlyList<DashboardOfflineSaleResponse> OfflineSales,
    IReadOnlyList<DashboardExpenseResponse> Expenses,
    IReadOnlyList<DashboardAttendanceResponse>? Attendance,
    IReadOnlyList<DashboardStockAlertResponse>? StockAlerts,
    IReadOnlyList<string> UnavailableSources,
    IReadOnlyList<DashboardZomatoAdjustmentResponse>? ZomatoAdjustments = null);

/// <summary>Report-level Zomato additional deductions/additions (e.g. ads) for one uploaded settlement period.</summary>
public sealed record DashboardZomatoAdjustmentResponse(
    string PeriodStart, string PeriodEnd, decimal AdditionalDeductions, decimal AdditionalAdditions);

/// <summary>Catalog identity and availability, including items hidden from customers.</summary>
public sealed record DashboardMenuItemResponse(string Id, string Name, string CategoryId, string Category, decimal OnlinePrice, bool IsAvailable);

/// <summary>Recorded payout and item information used by dashboard analytics.</summary>
public sealed record DashboardOnlineSaleResponse(
    string Platform, string OrderId, string? CustomerName, DateTimeOffset OrderAt,
    IReadOnlyList<DashboardOrderedItemResponse> OrderedItems, decimal BillSubTotal,
    decimal PackagingCharges, decimal DiscountAmount, decimal Payout, decimal PlatformDeduction,
    decimal? Rating, string? Review, decimal Freebies);

/// <summary>Item quantities in a recorded online sale.</summary>
public sealed record DashboardOrderedItemResponse(string ItemName, int Quantity);

/// <summary>Recorded offline sale without administrative audit fields.</summary>
public sealed record DashboardOfflineSaleResponse(
    string? Id, DateTimeOffset Date, IReadOnlyList<DashboardSalesItemResponse> Items,
    decimal TotalAmount, string PaymentMethod, string RecordedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Item values in a recorded offline sale.</summary>
public sealed record DashboardSalesItemResponse(string ItemName, int Quantity, decimal UnitPrice, decimal TotalPrice);

/// <summary>Recorded expenses used by the dashboard's historical insights.</summary>
public sealed record DashboardExpenseResponse(
    string? Id, DateTimeOffset Date, string ExpenseType, string ExpenseSource, decimal Amount,
    string PaymentMethod, string RecordedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Attendance coverage without leave details or private staff notes.</summary>
public sealed record DashboardAttendanceResponse(
    string StaffId, string Status, DateTimeOffset? ClockIn, DateTimeOffset? ClockOut,
    IReadOnlyList<DashboardAttendanceSessionResponse> Sessions);

/// <summary>Recorded session timestamps used to determine current shift coverage.</summary>
public sealed record DashboardAttendanceSessionResponse(DateTimeOffset? ClockIn, DateTimeOffset? ClockOut);

/// <summary>Read-only stock alert details for the selected outlet.</summary>
public sealed record DashboardStockAlertResponse(
    string? Id, string InventoryId, string IngredientName, string Type, string Severity,
    string Message, bool IsResolved, DateTimeOffset CreatedAt);
