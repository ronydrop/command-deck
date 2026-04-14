using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommandDeck.Models;

namespace CommandDeck.Controls;

/// <summary>
/// Dialog shown when the user selects a <see cref="PromptTemplate"/> that has dynamic fields.
/// Each field renders as a labeled TextBox; on OK the values are returned via <see cref="FieldValues"/>.
/// </summary>
public class TemplateFieldsDialog : Window
{
    private readonly List<(string Key, TextBox Box, bool Required)> _fields = new();

    public Dictionary<string, string> FieldValues { get; } = new();

    public TemplateFieldsDialog(PromptTemplate template)
    {
        Title = template.Title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Colors.White);
        WindowStyle = WindowStyle.ToolWindow;

        var root = new StackPanel { Margin = new Thickness(16) };

        // Title + description
        root.Children.Add(new TextBlock
        {
            Text = $"{template.Icon} {template.Title}",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = template.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // Fields
        foreach (var field in template.Fields)
        {
            root.Children.Add(new TextBlock
            {
                Text = field.IsRequired ? $"{field.Label} *" : field.Label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var isMultiline = field.Key == "code" || field.Key == "schema" || field.Key == "changes";
            var box = new TextBox
            {
                Text = field.DefaultValue,
                MinHeight = isMultiline ? 80 : 32,
                MaxHeight = isMultiline ? 160 : 60,
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Colors.White),
                CaretBrush = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = isMultiline,
                VerticalScrollBarVisibility = isMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 12)
            };

            if (!string.IsNullOrEmpty(field.Placeholder))
            {
                // Simple placeholder via Tag
                box.Tag = field.Placeholder;
            }

            root.Children.Add(box);
            _fields.Add((field.Key, box, field.IsRequired));
        }

        // Buttons
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var btnOk = new Button
        {
            Content = "Usar Template",
            Width = 110, Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
            Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        btnOk.Click += OnOkClick;

        var btnCancel = new Button
        {
            Content = "Cancelar",
            Width = 80, Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        btnCancel.Click += (_, _) => DialogResult = false;

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        root.Children.Add(btnPanel);

        var scroll = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Content = scroll;

        Loaded += (_, _) => _fields.FirstOrDefault().Box?.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        foreach (var (key, box, required) in _fields)
        {
            if (required && string.IsNullOrWhiteSpace(box.Text))
            {
                MessageBox.Show($"O campo \"{key}\" é obrigatório.", "Campo obrigatório",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                box.Focus();
                return;
            }
        }

        FieldValues.Clear();
        foreach (var (key, box, _) in _fields)
            FieldValues[key] = box.Text;

        DialogResult = true;
    }
}
