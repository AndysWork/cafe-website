using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class DeliveryFeeCalculator
{
    public static (DeliveryZone Zone, double DistanceKm, string FeeConfidence) SelectZoneForDistance(
        IReadOnlyCollection<DeliveryZone> zones,
        double? routeDistanceKm)
    {
        var orderedZones = zones.OrderBy(z => z.MinDistance).ToList();
        if (orderedZones.Count == 0)
            throw new InvalidOperationException("At least one delivery zone is required");

        DeliveryZone selectedZone;
        if (routeDistanceKm.HasValue)
        {
            var distanceKm = routeDistanceKm.Value;
            selectedZone = orderedZones.FirstOrDefault(z => distanceKm >= z.MinDistance && distanceKm <= z.MaxDistance)
                ?? (distanceKm < orderedZones[0].MinDistance ? orderedZones[0] : orderedZones[^1]);
        }
        else
        {
            selectedZone = orderedZones[0];
        }

        var resolvedDistanceKm = routeDistanceKm
            ?? Math.Round((selectedZone.MinDistance + selectedZone.MaxDistance) / 2d, 1, MidpointRounding.AwayFromZero);

        var feeConfidence = routeDistanceKm.HasValue
            ? "high"
            : resolvedDistanceKm <= 2d
                ? "medium"
                : "low";

        return (selectedZone, resolvedDistanceKm, feeConfidence);
    }

    public static decimal ResolveDeliveryFee(decimal subtotal, DeliveryZone zone)
    {
        var freeDeliveryThreshold = zone.FreeDeliveryAbove.GetValueOrDefault();
        var isFreeDelivery = freeDeliveryThreshold > 0m && subtotal >= freeDeliveryThreshold;
        return isFreeDelivery ? 0m : zone.DeliveryFee;
    }
}
