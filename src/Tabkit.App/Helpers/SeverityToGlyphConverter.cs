using System.Globalization;
using System.Windows.Data;
using Tabkit.Core.Audit;

namespace Tabkit.App.Helpers;

/// <summary>
/// Brand rule: status conveyed by glyph shape, never by colored fill.
/// info → "i" glyph, warn → "!" glyph, error → "x" glyph.
/// </summary>
public sealed class SeverityToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Severity.Info  => "ⓘ", // CIRCLED LATIN SMALL LETTER I
            Severity.Warn  => "⚠", // WARNING SIGN
            Severity.Error => "✗", // BALLOT X
            _ => "·",              // MIDDLE DOT
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
