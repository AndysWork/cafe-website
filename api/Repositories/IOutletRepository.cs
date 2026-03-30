using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IOutletRepository
{
    Task<List<Outlet>> GetAllOutletsAsync();
    Task<List<Outlet>> GetActiveOutletsAsync();
    Task<Outlet?> GetOutletByIdAsync(string id);
    Task<Outlet?> GetOutletByCodeAsync(string code);
    Task<Outlet> CreateOutletAsync(CreateOutletRequest request, string userId);
    Task<bool> UpdateOutletAsync(string id, UpdateOutletRequest request, string userId);
    Task<bool> DeleteOutletAsync(string id);
    Task<bool> ToggleOutletStatusAsync(string id);
    Task EnsureDefaultOutletAsync();

    // Public Stats
    Task<PublicStats> GetPublicStatsAsync();
}
