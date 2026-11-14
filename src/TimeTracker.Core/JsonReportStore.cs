using System.Globalization;
using System.Text.Json;

namespace TimeTracker.Core;

public sealed class JsonReportStore : IReportStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SortedDictionary<string, List<TimeEntry>>? _data;

    public JsonReportStore(string path) => _path = path;

    public event Action<TimeEntry>? ReportAdded;

    public static string DayKey(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<TimeEntry> AddAsync(DateTime startUtc, DateTime endUtc, string description)
    {
        var entry = new TimeEntry(Guid.NewGuid(), DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), description);

        await _lock.WaitAsync();
        try
        {
            var data = Load();
            var key = DayKey(entry.StartUtc);
            if (!data.TryGetValue(key, out var list))
                data[key] = list = new List<TimeEntry>();
            list.Add(entry);
            Save(data);
        }
        finally
        {
            _lock.Release();
        }

        ReportAdded?.Invoke(entry);
        return entry;
    }

    public async Task<IReadOnlyList<TimeEntry>> GetByDateAsync(DateOnly date)
    {
        await _lock.WaitAsync();
        try
        {
            var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Load().TryGetValue(key, out var list)
                ? list.OrderBy(e => e.StartUtc).ToList()
                : new List<TimeEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    private SortedDictionary<string, List<TimeEntry>> Load()
    {
        if (_data is not null) return _data;

        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                _data = new SortedDictionary<string, List<TimeEntry>>(
                    JsonSerializer.Deserialize<Dictionary<string, List<TimeEntry>>>(json, Options) ?? new());
            }
            catch (JsonException)
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
            }
        }

        return _data ??= new SortedDictionary<string, List<TimeEntry>>();
    }

    private void Save(SortedDictionary<string, List<TimeEntry>> data)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
