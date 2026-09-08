namespace Oxce.Rendering;

/// <summary>Small asset-independent interface font. Installations may supply a Unicode sprite font.</summary>
public static class IndexedInterfaceFont
{
    public static IndexedSpriteFont Create()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789?.,:;!+-=/()[]<>_#'\"%";
        string[] rows = [
            "0E11111F111111", "1E11111E11111E", "0E11101010110E", "1E11111111111E", "1F10101E10101F", "1F10101E101010",
            "0E11101711110F", "1111111F111111", "0E04040404040E", "0702020212120C", "11121418141211", "1010101010101F",
            "111B1515111111", "11191915131311", "0E11111111110E", "1E11111E101010", "0E11111115120D", "1E11111E141211",
            "0F10100E01011E", "1F040404040404", "1111111111110E", "11111111110A04", "11111115151B11", "11110A040A1111",
            "11110A04040404", "1F01020408101F", "0E11131519110E", "040C040404040E", "0E11010204081F", "1F01020601110E",
            "02060A121F0202", "1F101E0101110E", "0608101E11110E", "1F010204080808", "0E11110E11110E", "0E11110F01020C",
            "0E110102040004", "00000000000606", "00000000000604", "00060600060600", "00060600060604", "04040404040004",
            "0004041F040400", "0000001F000000", "00001F001F0000", "01010204081010", "02040808080402", "08040202020408",
            "0E08080808080E", "0E02020202020E", "02040810080402", "08040201020408", "0000000000001F", "0A0A1F0A1F0A0A",
            "04040400000000", "0A0A0A00000000", "19190204081313"];
        var surface = new IndexedSurface(5 * chars.Length, 7);
        for (var glyph = 0; glyph < chars.Length; glyph++)
            for (var y = 0; y < 7; y++)
            {
                var bits = Convert.ToByte(rows[glyph].Substring(y * 2, 2), 16);
                for (var x = 0; x < 5; x++)
                    if ((bits & (1 << (4 - x))) != 0) surface.Pixels[y * surface.Width + glyph * 5 + x] = 1;
            }
        // Lowercase shares the same readable glyphs in this compact fallback.
        return new([new(surface, 5, 7, 1, chars), new(surface, 5, 7, 1, chars[..26].ToLowerInvariant())], monospace: true);
    }
}
