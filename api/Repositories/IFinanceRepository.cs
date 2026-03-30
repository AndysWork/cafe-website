using Cafe.Api.Models;
using MongoDB.Bson;

namespace Cafe.Api.Repositories;

public interface IFinanceRepository
{
    // Sales
    Task<List<Sales>> GetAllSalesAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetAllSalesCountAsync(string? outletId = null);
    Task<List<Sales>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate, string? outletId = null);
    Task<Sales?> GetSalesByIdAsync(string id);
    Task<Sales> CreateSalesAsync(Sales sales);
    Task<bool> UpdateSalesAsync(string id, Sales sales);
    Task<bool> DeleteSalesAsync(string id);
    Task<SalesSummary> GetSalesSummaryByDateAsync(DateTime date, string? outletId = null);

    // Expenses
    Task<List<Expense>> GetAllExpensesAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetAllExpensesCountAsync(string? outletId = null);
    Task<List<Expense>> GetExpensesByDateRangeAsync(DateTime startDate, DateTime endDate, string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetExpensesByDateRangeCountAsync(DateTime startDate, DateTime endDate, string? outletId = null);
    Task<Expense?> GetExpenseByIdAsync(string id);
    Task<Expense> CreateExpenseAsync(Expense expense);
    Task<bool> UpdateExpenseAsync(string id, Expense expense);
    Task<bool> DeleteExpenseAsync(string id);
    Task<ExpenseSummary> GetExpenseSummaryByDateAsync(DateTime date);
    Task<BsonDocument?> GetExpenseAnalyticsAggregationAsync(DateTime startDate, DateTime endDate, string? source, string? outletId);

    // Operational Expenses
    Task<List<OperationalExpense>> GetAllOperationalExpensesAsync(string? outletId = null);
    Task<OperationalExpense?> GetOperationalExpenseByMonthYearAsync(int month, int year, string? outletId = null);
    Task<OperationalExpense?> GetOperationalExpenseByIdAsync(string id);
    Task<decimal> CalculateRentForMonthAsync(int month, int year, string? outletId = null);
    Task<OperationalExpense> CreateOperationalExpenseAsync(OperationalExpense expense);
    Task<bool> UpdateOperationalExpenseAsync(string id, OperationalExpense expense);
    Task<bool> DeleteOperationalExpenseAsync(string id);
    Task<List<OperationalExpense>> GetOperationalExpensesByYearAsync(int year, string? outletId = null);

    // Sales Item Types
    Task<List<SalesItemType>> GetAllSalesItemTypesAsync();
    Task<List<SalesItemType>> GetActiveSalesItemTypesAsync();
    Task<SalesItemType?> GetSalesItemTypeByIdAsync(string id);
    Task<SalesItemType> CreateSalesItemTypeAsync(SalesItemType itemType);
    Task<SalesItemType?> UpdateSalesItemTypeAsync(string id, SalesItemType itemType);
    Task<bool> DeleteSalesItemTypeAsync(string id);
    Task InitializeDefaultSalesItemTypesAsync();

    // Offline Expense Types
    Task<List<OfflineExpenseType>> GetAllOfflineExpenseTypesAsync();
    Task<List<OfflineExpenseType>> GetActiveOfflineExpenseTypesAsync();
    Task<OfflineExpenseType?> GetOfflineExpenseTypeByIdAsync(string id);
    Task<OfflineExpenseType> CreateOfflineExpenseTypeAsync(CreateOfflineExpenseTypeRequest request);
    Task<bool> UpdateOfflineExpenseTypeAsync(string id, CreateOfflineExpenseTypeRequest request);
    Task<bool> DeleteOfflineExpenseTypeAsync(string id);
    Task InitializeDefaultOfflineExpenseTypesAsync();

    // Online Expense Types
    Task<List<OnlineExpenseType>> GetAllOnlineExpenseTypesAsync();
    Task<List<OnlineExpenseType>> GetActiveOnlineExpenseTypesAsync();
    Task<OnlineExpenseType?> GetOnlineExpenseTypeByIdAsync(string id);
    Task<OnlineExpenseType> CreateOnlineExpenseTypeAsync(CreateOnlineExpenseTypeRequest request);
    Task UpdateOnlineExpenseTypeAsync(string id, CreateOnlineExpenseTypeRequest request);
    Task DeleteOnlineExpenseTypeAsync(string id);
    Task InitializeDefaultOnlineExpenseTypesAsync();

    // Cash Reconciliation
    Task<DailyCashReconciliation> CreateCashReconciliationAsync(DailyCashReconciliation reconciliation, string userId);
    Task<DailyCashReconciliation?> GetCashReconciliationByDateAsync(DateTime date, string? outletId = null);
    Task<List<DailyCashReconciliation>> GetCashReconciliationsAsync(DateTime? startDate = null, DateTime? endDate = null, string? outletId = null);
    Task<DailyCashReconciliation?> UpdateCashReconciliationAsync(string id, DailyCashReconciliation reconciliation);
    Task<List<DailyCashReconciliation>> BulkCreateCashReconciliationsAsync(List<DailyCashReconciliation> reconciliations, string userId);
    Task<bool> DeleteCashReconciliationAsync(string id);
    Task<object> GetCashReconciliationSummaryAsync(DateTime startDate, DateTime endDate);
    Task<object> GetDailySalesSummaryForReconciliationAsync(DateTime date, string? outletId = null);

    // Online Sales
    Task<List<OnlineSale>> GetOnlineSalesAsync(string? platform = null, string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetOnlineSalesCountAsync(string? platform = null, string? outletId = null);
    Task<List<OnlineSale>> GetOnlineSalesByDateRangeAsync(string? platform, DateTime startDate, DateTime endDate, string? outletId = null);
    Task<List<DailyOnlineIncomeResponse>> GetDailyOnlineIncomeAsync(DateTime startDate, DateTime endDate, string? outletId = null);
    Task<List<OnlineSaleResponse>> GetFiveStarReviewsAsync(int limit = 10, string? outletId = null);
    Task<List<DiscountCouponResponse>> GetUniqueDiscountCouponsAsync(string? outletId = null);
    Task<List<DiscountCouponResponse>> GetActiveDiscountCouponsAsync();
    Task<OnlineSale?> GetOnlineSaleByIdAsync(string id);
    Task<OnlineSale> CreateOnlineSaleAsync(OnlineSale sale, string userId);
    Task<BulkInsertResult> BulkCreateOnlineSalesAsync(List<OnlineSale> sales);
    Task<OnlineSale?> UpdateOnlineSaleAsync(string id, UpdateOnlineSaleRequest request);
    Task<bool> DeleteOnlineSaleAsync(string id);
    Task<long> BulkDeleteOnlineSalesAsync(string? platform, DateTime? startDate, DateTime? endDate);
    Task<string?> FindMenuItemIdByNameAsync(string itemName);

    // Platform Charges
    Task<List<PlatformCharge>> GetAllPlatformChargesAsync(string? outletId = null);
    Task<PlatformCharge?> GetPlatformChargeByKeyAsync(string platform, int year, int month);
    Task<List<PlatformCharge>> GetPlatformChargesByPlatformAsync(string platform);
    Task CreatePlatformChargeAsync(PlatformCharge charge);
    Task<bool> UpdatePlatformChargeAsync(string id, UpdatePlatformChargeRequest request);
    Task<bool> DeletePlatformChargeAsync(string id);

    // Discount Coupons
    Task<DiscountCoupon?> GetDiscountCouponAsync(string couponCode, string platform);
    Task<DiscountCoupon> CreateOrUpdateDiscountCouponAsync(string couponCode, string platform, bool isActive, string userId);
    Task<bool> UpdateDiscountCouponStatusAsync(string id, bool isActive);
    Task<bool> UpdateDiscountCouponMaxValueAsync(string id, decimal? maxValue);
    Task<bool> UpdateDiscountCouponPercentageAsync(string id, decimal? discountPercentage);
}
