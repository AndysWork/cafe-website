using System.Globalization;

namespace Cafe.Api.Helpers;

public static class IstDateHelper
{
    private static readonly TimeZoneInfo IstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private static readonly string[] ImportDateFormats =
    {
        "yyyy-MM-dd",
        "d/M/yyyy",
        "d-M-yyyy"
    };

    public static bool? UtcTimestampMarker
    {
        get
        {
            return ParseUtcTimestampMarker(Environment.GetEnvironmentVariable("Timezone__EnableUtcMarkers"));
        }
    }

    public static bool? ParseUtcTimestampMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!bool.TryParse(value, out var enabled))
            throw new InvalidOperationException("Timezone__EnableUtcMarkers must be true or false.");
        return enabled ? true : null;
    }

    public static bool TryParseImportDate(string? raw, out DateTime date)
    {
        if (DateTime.TryParseExact(raw, ImportDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            date = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified);
            return true;
        }

        date = default;
        return false;
    }

    public static bool TryParseIsoDate(string? raw, out DateTime date)
    {
        return DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    public static DateTime NormalizeToIst(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(value, IstTimeZone),
            DateTimeKind.Local => TimeZoneInfo.ConvertTime(value, IstTimeZone),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Unspecified)
        };
    }

    public static DateTime IstWallTimeToUtc(DateTime value)
    {
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), IstTimeZone);
    }

    public static DateTime ToIstDate(DateTime value)
    {
        var ist = NormalizeToIst(value);
        return new DateTime(ist.Year, ist.Month, ist.Day, 0, 0, 0, DateTimeKind.Unspecified);
    }

    public static DateTime ToIstDayEnd(DateTime value)
    {
        return ToIstDate(value).AddDays(1).AddTicks(-1);
    }

    public static int GetSessionElapsedMinutes(DateTime createdAt, bool? createdAtIsUtc, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The current time must be UTC.", nameof(utcNow));

        var clock = createdAtIsUtc == true ? utcNow : NormalizeToIst(utcNow);
        return Math.Max(0, (int)(clock - createdAt).TotalMinutes);
    }

    public static bool TryParseIstDate(string? raw, out DateTime date)
    {
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            date = ToIstDate(exact);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            date = ToIstDate(parsed);
            return true;
        }

        date = default;
        return false;
    }

    public static bool TryParseIstDateRange(string? startRaw, string? endRaw, out DateTime startDate, out DateTime endDate)
    {
        if (!TryParseIstDate(startRaw, out startDate) || !TryParseIstDate(endRaw, out endDate))
        {
            startDate = default;
            endDate = default;
            return false;
        }

        return true;
    }
}
