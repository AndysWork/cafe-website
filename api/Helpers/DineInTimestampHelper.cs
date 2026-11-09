using MongoDB.Bson;

namespace Cafe.Api.Helpers;

public static class DineInTimestampHelper
{
    private static readonly TimeZoneInfo IstZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public static bool IsLegacyWallClock(DateTime timestamp, string? id)
    {
        if (timestamp.Kind != DateTimeKind.Utc || !ObjectId.TryParse(id, out var objectId))
            return false;
        var difference = timestamp - objectId.CreationTime;
        return difference >= TimeSpan.FromMinutes(325) && difference <= TimeSpan.FromMinutes(335);
    }

    public static DateTimeOffset ToIstOffset(DateTime timestamp, bool? timestampIsUtc, bool legacyWallClock)
    {
        if (timestampIsUtc != true && legacyWallClock)
            return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified), IstZone.GetUtcOffset(timestamp));

        var utc = timestamp.Kind == DateTimeKind.Local
            ? timestamp.ToUniversalTime()
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), IstZone);
    }
}
