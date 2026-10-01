using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IOrderRepository
{
    Task<Order> CreateOrderAsync(Order order);
    Task<List<Order>> GetUserOrdersAsync(string userId, int? page = null, int? pageSize = null);
    Task<long> GetUserOrdersCountAsync(string userId);
    Task<List<Order>> GetAllOrdersAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetAllOrdersCountAsync(string? outletId = null);
    Task<Order?> GetOrderByIdAsync(string orderId);
    Task<bool> UpdateOrderStatusAsync(string orderId, string status);
    Task<bool> TryUpdateOrderStatusAsync(string orderId, IReadOnlyCollection<string> expectedStatuses, string status);
    Task<bool> UpdateOrderAsync(Order order);
    Task<bool> UpdatePaymentStatusAsync(string orderId, string paymentStatus);
    Task<bool> UpdateReceiptImageUrlAsync(string orderId, string? receiptImageUrl);
    Task<bool> DeleteOrderAsync(string orderId);

    // Kitchen Display
    Task<List<Order>> GetOrdersByStatusAsync(string[] statuses, string? outletId = null);

    // Reviews
    Task<CustomerReview> CreateReviewAsync(CustomerReview review);
    Task<CustomerReview?> GetReviewByOrderIdAsync(string orderId);
    Task<List<CustomerReview>> GetReviewsByUserIdAsync(string userId);
    Task<List<CustomerReview>> GetAllReviewsAsync(string? outletId = null, int page = 1, int pageSize = 50);
    Task<double> GetAverageRatingAsync(string? outletId = null);

    // Order issues
    Task<OrderIssue> CreateOrderIssueAsync(OrderIssue issue);
    Task<List<OrderIssue>> GetOrderIssuesAsync(string orderId);
    Task<bool> UpdateOrderIssueStatusAsync(string orderId, string issueId, string status, string? resolutionNotes = null, bool refundProcessed = false);

    // Dine-In Table Sessions
    Task<DineInSession> CreateDineInSessionAsync(DineInSession session);
    Task<DineInSession?> GetActiveDineInSessionByTableAsync(string outletId, string tableNumber);
    Task<DineInSession?> GetLatestDineInSessionByTableAsync(string outletId, string tableNumber);
    Task<DineInSession?> GetDineInSessionByIdAsync(string sessionId);
    Task<List<DineInSession>> GetActiveDineInSessionsAsync(string outletId);
    Task<List<DineInSession>> GetUserDineInSessionsAsync(string userId);
    Task<bool> UpdateDineInSessionAsync(DineInSession session);
    Task<bool> TransitionDineInPaymentStatusAsync(string sessionId, IReadOnlyCollection<string> expectedStatuses, string newStatus);
}
