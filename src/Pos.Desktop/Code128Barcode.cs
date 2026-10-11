namespace Pos.Desktop;

internal static class Code128Barcode
{
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111", "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    public static bool TryEncode(string? value, out IReadOnlyList<int> widths, out string error)
    {
        widths = [];
        error = string.Empty;
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "El producto no tiene un código para generar la etiqueta.";
            return false;
        }

        var values = new List<int>();
        if (text.Length >= 2 && text.All(char.IsAsciiDigit))
        {
            values.Add(105); // Code Set C comprime pares numéricos.
            var pairedLength = text.Length - (text.Length % 2);
            for (var index = 0; index < pairedLength; index += 2) values.Add(int.Parse(text.AsSpan(index, 2), System.Globalization.CultureInfo.InvariantCulture));
            if (pairedLength < text.Length)
            {
                // EAN-13 y otros códigos numéricos impares conservan la compresión
                // de sus pares y cambian a Code B únicamente para el último dígito.
                values.Add(100);
                values.Add(text[^1] - ' ');
            }
        }
        else
        {
            values.Add(104); // Code Set B para códigos alfanuméricos imprimibles.
            foreach (var character in text)
            {
                if (character is < ' ' or > '~')
                {
                    error = "El código contiene caracteres que no se pueden representar en una etiqueta Code 128.";
                    return false;
                }
                values.Add(character - ' ');
            }
        }

        var checksum = values[0];
        for (var index = 1; index < values.Count; index++) checksum += values[index] * index;
        values.Add(checksum % 103);
        values.Add(106);

        widths = values.SelectMany(valueIndex => Patterns[valueIndex].Select(width => width - '0')).ToArray();
        return true;
    }
}
