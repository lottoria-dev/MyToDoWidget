using System.Text;

namespace MyToDoWidget.Core;

public static class MemoCsv
{
    public const int MaxNotes = 5000;
    public const int MaxCellLength = 50000;

    // RFC 4180 quoting, embedded line breaks, empty cells and UTF-8 BOM.
    // No header is removed: A1 is a memo just like every other A-column cell.
    public static List<string> ReadFirstColumn(string csv)
    {
        if (csv.Length > 2 * 1024 * 1024) throw TooLarge();
        var result = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false, afterQuote = false, fieldStart = true;
        int column = 0;

        void EndField()
        {
            if (column == 0 && !string.IsNullOrWhiteSpace(cell.ToString()))
            {
                result.Add(cell.ToString().Replace("\r\n", "\n").Replace('\r', '\n'));
                if (result.Count > MaxNotes) throw TooLarge();
            }
            cell.Clear();
            column++;
            afterQuote = false;
            fieldStart = true;
        }

        for (int i = csv.StartsWith('\uFEFF') ? 1 : 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        if (column == 0) cell.Append('"');
                        i++;
                    }
                    else { inQuotes = false; afterQuote = true; }
                }
                else if (column == 0) cell.Append(c);
            }
            else if (c == ',' || c == '\r' || c == '\n')
            {
                EndField();
                if (c != ',')
                {
                    column = 0;
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                }
            }
            else if (c == '"' && fieldStart) { inQuotes = true; fieldStart = false; }
            else
            {
                if (afterQuote || c == '"') throw Invalid();
                if (column == 0) cell.Append(c);
                fieldStart = false;
            }
            if (cell.Length > MaxCellLength) throw TooLarge();
        }
        if (inQuotes) throw Invalid();
        EndField();
        return result;
    }

    private static MemoException Invalid() => new("시트의 CSV 내용을 읽을 수 없습니다. 메모 탭 주소를 확인하고 다시 불러와 주세요.");
    private static MemoException TooLarge() => new("메모가 너무 큽니다. 전용 탭의 A열에 5,000개 이하, 셀당 50,000자 이하로 나누어 주세요. 게시 데이터는 2MB까지 읽을 수 있습니다.");
}
