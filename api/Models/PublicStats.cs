namespace Cafe.Api.Models;

public class PublicStats
{
    public long TotalOrders { get; set; }
    public long MenuItemCount { get; set; }
    public int OutletCount { get; set; }
    public double AverageRating { get; set; }
    public long FiveStarReviewCount { get; set; }
    public int YearsServing { get; set; }
}
