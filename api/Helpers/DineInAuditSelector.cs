using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public sealed class DineInAuditSelector
{
    private readonly DateTime _start;
    private readonly DateTime _endExclusive;
    private readonly DateTime _maxTimestamp;
    private readonly int _limit;
    private readonly PriorityQueue<EventLog, DateTimeOffset> _latest = new();

    public DineInAuditSelector(DateTime start, DateTime endExclusive, DateTime maxTimestamp, int limit)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        _start = start;
        _endExclusive = endExclusive;
        _maxTimestamp = maxTimestamp;
        _limit = limit;
    }

    private static DateTimeOffset Normalize(EventLog entry) =>
        DineInTimestampHelper.ToIstOffset(entry.Timestamp, null,
            DineInTimestampHelper.IsLegacyWallClock(entry.Timestamp, entry.Id));

    public void Add(EventLog entry)
    {
        var timestamp = Normalize(entry);
        var wallTime = timestamp.DateTime;
        if (wallTime < _start || wallTime >= _endExclusive || wallTime > _maxTimestamp) return;

        _latest.Enqueue(entry, timestamp);
        if (_latest.Count > _limit) _latest.Dequeue();
    }

    public List<EventLog> GetLatest() =>
        _latest.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(Normalize)
            .ToList();
}
