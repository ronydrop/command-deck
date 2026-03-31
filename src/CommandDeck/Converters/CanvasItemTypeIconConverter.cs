using System;
using System.Globalization;
using System.Windows.Data;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>Maps a <see cref="CanvasItemType"/> to a unicode glyph for the card titlebar.</summary>
public class CanvasItemTypeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CanvasItemType type)
        {
            return type switch
            {
                CanvasItemType.Terminal => "⬛",
                CanvasItemType.GitWidget => "",
                CanvasItemType.ProcessWidget => "⚡",
                CanvasItemType.ShortcutWidget => "⌨",
                CanvasItemType.NoteWidget => "📝",
                CanvasItemType.ImageWidget => "🖼",
                _ => "□"
            };
        }
        return "□";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
