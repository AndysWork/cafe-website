using System.Globalization;
using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class DeliveryPartnerAvailability
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

    public static PartnerAvailabilityResponse Calculate(Outlet? outlet, DateTime? pausedUntilUtc, DateTimeOffset now)
    {
        if (outlet == null || !outlet.IsActive || outlet.IsDeleted)
            return new(false, false, null, null, null, "Your outlet is unavailable for deliveries.");

        var opening = ParseTime(outlet.Settings.OpeningTime);
        var closing = ParseTime(outlet.Settings.ClosingTime);
        if (opening == closing)
            throw new InvalidOperationException("Outlet opening and closing times must differ.");

        var localNow = now.ToOffset(IstOffset);
        var time = TimeOnly.FromDateTime(localNow.DateTime);
        var shopIsOpen = opening < closing
            ? time >= opening && time < closing
            : time >= opening || time < closing;
        var todayOpening = new DateTimeOffset(localNow.Date + opening.ToTimeSpan(), IstOffset);
        var nextOpening = (localNow < todayOpening ? todayOpening : todayOpening.AddDays(1)).ToUniversalTime();
        var paused = pausedUntilUtc.HasValue
            && new DateTimeOffset(DateTime.SpecifyKind(pausedUntilUtc.Value, DateTimeKind.Utc)) > now;
        var online = shopIsOpen && !paused;
        var message = !shopIsOpen
            ? $"Shop closed. Delivery hours: {outlet.Settings.OpeningTime} - {outlet.Settings.ClosingTime} IST."
            : paused
                ? "You are Offline. Go Online to resume; availability resets at the next shop opening."
                : "Online during shop hours. Switch Offline to pause new assignments.";

        return new(online, shopIsOpen, outlet.Settings.OpeningTime, outlet.Settings.ClosingTime, nextOpening, message);
    }

    public static void Apply(DeliveryPartner partner, Outlet? outlet, DateTimeOffset now)
    {
        partner.Availability = Calculate(outlet, partner.AvailabilityPausedUntilUtc, now);
        partner.Status = !partner.Availability.IsOnline ? "offline"
            : partner.Status == "on-delivery" || !string.IsNullOrWhiteSpace(partner.CurrentOrderId)
                ? "on-delivery" : "available";
    }

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParseExact(value, new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time)
            ? time
            : throw new InvalidOperationException("Outlet hours must use a valid 24-hour HH:mm time.");
}
