using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class DeliveryPartnerProfileSelector
{
    public static DeliveryPartner? Select(
        IReadOnlyList<DeliveryPartner> profiles,
        IEnumerable<Outlet> outlets)
    {
        var activeOutletIds = outlets
            .Where(outlet => outlet.IsActive && !outlet.IsDeleted)
            .Select(outlet => outlet.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return profiles.FirstOrDefault(profile => activeOutletIds.Contains(profile.OutletId))
            ?? profiles.FirstOrDefault();
    }
}
