using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyToDoWidget.Core;

public sealed class MemoBackup
{
    public int Format { get; set; } = 2;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MemoItem> Items { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetSettings? Settings { get; set; }

    public static string ExportJson(AppState state) => JsonSerializer.Serialize(
        new MemoBackup { Items = state.Items.Select(i => i.Copy()).ToList(), Settings = WidgetSettings.FromState(state).Normalized() },
        new JsonSerializerOptions(MemoJson.Options) { WriteIndented = true });
    public static string ExportJson(IEnumerable<MemoItem> items) => JsonSerializer.Serialize(
        new MemoBackup { Items = items.Select(i => i.Copy()).ToList() },
        new JsonSerializerOptions(MemoJson.Options) { WriteIndented = true });
    public static IReadOnlyList<MemoItem> ReadJson(string text) => ReadDocument(text).Items;
    public static MemoBackup ReadDocument(string text)
    {
        if (text.Length > 64 * 1024 * 1024) throw new MemoException("백업 파일이 너무 큽니다.");
        try
        {
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("format", out var format) || format.GetInt32() != 2
                || !json.RootElement.TryGetProperty("items", out var values) || values.ValueKind != JsonValueKind.Array) throw new JsonException();
            var backup = JsonSerializer.Deserialize<MemoBackup>(text, MemoJson.Options);
            if (backup is null || backup.Format != 2 || backup.Items is null || backup.Items.Count > 10000) throw new JsonException();
            foreach (var item in backup.Items)
            {
                if (item is null) throw new JsonException();
                item.Validate();
            }
            backup.Settings = backup.Settings?.Normalized();
            return backup;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { throw new MemoException("MyToDo2 메모 백업 파일인지 확인해 주세요."); }
    }
    public static string ExportCsv(IEnumerable<MemoItem> items)
    {
        var text = new StringBuilder("종류,제목,본문,출처,원문 링크,날짜 용도,날짜,시각,다시 알림,완료,위젯 표시,보관 일시,항목 배경색\r\n");
        foreach (var item in items)
        {
            var fields = new[] { item.Kind == MemoKind.Memo ? "메모" : "일정", item.Title ?? "", item.Body,
                item.Source ?? "", item.OriginalUrl ?? "", item.DateRole.ToString(), item.WhenDate?.ToString("yyyy-MM-dd") ?? "",
                item.WhenAt?.ToString("O") ?? "", item.ReminderAt?.ToString("O") ?? "", item.IsCompleted ? "완료" : "", item.IsDeleted ? "보관" : "표시", item.DeletedAt?.ToString("O") ?? "", item.CardColor ?? "" };
            text.AppendLine(string.Join(',', fields.Select(CsvField)));
        }
        return text.ToString();
    }
    private static string CsvField(string text)
    {
        // Human-readable CSV: avoid formula execution when opened in spreadsheet software.
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
