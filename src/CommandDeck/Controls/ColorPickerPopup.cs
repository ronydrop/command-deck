using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;

namespace CommandDeck.Controls;

/// <summary>
/// Lightweight color-picker popup for tile accent color selection.
/// Contains the same Catppuccin Mocha color palette as the Note widget.
/// </summary>
public class ColorPickerPopup : Popup
{
    public event Action<string>? ColorSelected;

    private static readonly (string Hex, string Name)[] Palette =
    [
        ("#cba6f7", "Mauve"),
        ("#89b4fa", "Blue"),
        ("#a6e3a1", "Green"),
        ("#fab387", "Peach"),
        ("#f9e2af", "Yellow"),
        ("#f5c2e7", "Pink"),
        ("#89dceb", "Sky"),
        ("#f38ba8", "Red"),
        ("#b4befe", "Lavender"),
        ("#94e2d5", "Teal"),
        ("#ffffff", "Branco"),
        (string.Empty, "Padrão"),
    ];

    public ColorPickerPopup()
    {
        StaysOpen = false;
        AllowsTransparency = true;
        PopupAnimation = PopupAnimation.Fade;

        var panel = new WrapPanel { MaxWidth = 200, Margin = new Thickness(8) };

        foreach (var (hex, name) in Palette)
        {
            var btn = CreateSwatch(hex, name);
            panel.Children.Add(btn);
        }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 88, 91, 112)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        Child = border;
    }

    private Button CreateSwatch(string hex, string name)
    {
        Color color;
        if (string.IsNullOrEmpty(hex))
            color = Color.FromRgb(49, 50, 68);
        else
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex); }
            catch { color = Colors.Gray; }
        }

        var btn = new Button
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(3),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = name,
            Tag = hex
        };

        btn.Template = CreateSwatchTemplate();
        btn.Click += (_, _) =>
        {
            ColorSelected?.Invoke(string.IsNullOrEmpty(hex) ? null! : hex);
            IsOpen = false;
        };

        return btn;
    }

    private static System.Windows.Controls.ControlTemplate CreateSwatchTemplate()
    {
        var template = new System.Windows.Controls.ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        template.VisualTree = factory;
        return template;
    }
}
