using System.Windows.Markup;

namespace MyToDoWidget.Services;

public sealed record FontOption(string FamilyName, string DisplayName, string Group, int Order, int Recommendation = 100);

public static class FontCatalog
{
    private static readonly Lazy<IReadOnlyList<FontOption>> Installed = new(ReadInstalled);
    private const string HangulSample = "가나다라마바사아자차카타파하한글메모";

    public static IReadOnlyList<FontOption> Choices(string savedFamily)
    {
        var choices = Installed.Value.ToList();
        if (!choices.Any(f => string.Equals(f.FamilyName, savedFamily, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(savedFamily, savedFamily + " (설치 확인 필요)", "저장된 글꼴", 4));
        return choices.OrderBy(f => f.Order).ThenBy(f => f.Recommendation)
            .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IReadOnlyList<FontOption> ReadInstalled()
    {
        var choices = new List<FontOption>();
        var korean = XmlLanguage.GetLanguage("ko-KR");
        foreach (var family in Fonts.SystemFontFamilies)
        {
            string display = family.FamilyNames.TryGetValue(korean, out var local) ? local : family.Source;
            int order = 3;
            try
            {
                var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (typeface.TryGetGlyphTypeface(out var glyph))
                {
                    bool hangul = HangulSample.All(c => HasGlyph(glyph, c));
                    bool latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".All(c => HasGlyph(glyph, c));
                    order = hangul ? (latin && IsKoreanMonospaced(glyph) ? 0 : 1) : latin ? 2 : 3;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException
                or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // An unreadable face remains selectable under Other without blocking settings.
            }
            string group = order switch
            {
                0 => "한글 고정폭 · 추천",
                1 => "한글 지원",
                2 => "영문 중심 · 한글 대체 표시",
                _ => "기타 · 기호 / 지원 확인 필요"
            };
            choices.Add(new(family.Source, display, group, order, RecommendedRank(family.Source)));
        }
        return choices.DistinctBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasGlyph(GlyphTypeface font, int character) =>
        font.CharacterToGlyphMap.TryGetValue(character, out var glyph) && glyph != 0;

    private static bool IsKoreanMonospaced(GlyphTypeface font)
    {
        double unit = font.AdvanceWidths[font.CharacterToGlyphMap['0']];
        if (unit <= 0) return false;
        // Full ASCII cells have one width; Hangul has either one or two cells.
        for (int c = 32; c <= 126; c++)
            if (!HasGlyph(font, c) || Math.Abs(font.AdvanceWidths[font.CharacterToGlyphMap[c]] - unit) > 0.005)
                return false;
        double hangulWidth = font.AdvanceWidths[font.CharacterToGlyphMap['가']];
        if (Math.Abs(hangulWidth - unit) > 0.005 && Math.Abs(hangulWidth - unit * 2) > 0.005) return false;
        return HangulSample.All(c => Math.Abs(font.AdvanceWidths[font.CharacterToGlyphMap[c]] - hangulWidth) <= 0.005);
    }

    private static int RecommendedRank(string family) => family.ToUpperInvariant() switch
    {
        "D2CODING" or "D2CODING LIGATURE" => 0,
        "NANUMGOTHICCODING" or "NANUM GOTHIC CODING" or "나눔고딕코딩" => 1,
        "DOTUMCHE" or "돋움체" => 2,
        "GULIMCHE" or "굴림체" => 3,
        "BATANGCHE" or "바탕체" => 4,
        "GUNGSUHCHE" or "궁서체" => 5,
        _ => 100
    };
}
