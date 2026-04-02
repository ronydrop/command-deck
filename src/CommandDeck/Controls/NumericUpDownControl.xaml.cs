using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommandDeck.Controls;

/// <summary>
/// Numeric input control with up/down spinner buttons.
/// Supports integer and decimal values with configurable min, max, step and decimal places.
/// </summary>
public partial class NumericUpDownControl : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericUpDownControl),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericUpDownControl),
            new PropertyMetadata(double.MinValue));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericUpDownControl),
            new PropertyMetadata(double.MaxValue));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericUpDownControl),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericUpDownControl),
            new PropertyMetadata(0, OnDecimalPlacesChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public NumericUpDownControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateTextFromValue();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((NumericUpDownControl)d).UpdateTextFromValue();
    }

    private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((NumericUpDownControl)d).UpdateTextFromValue();
    }

    private void UpdateTextFromValue()
    {
        if (ValueBox == null) return;
        ValueBox.Text = Value.ToString($"F{DecimalPlaces}", CultureInfo.InvariantCulture);
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Min(Maximum, Math.Round(Value + Step, DecimalPlaces));
        if (newValue != Value) Value = newValue;
    }

    private void DownButton_Click(object sender, RoutedEventArgs e)
    {
        var newValue = Math.Max(Minimum, Math.Round(Value - Step, DecimalPlaces));
        if (newValue != Value) Value = newValue;
    }

    private void ValueBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textWithoutSelection = ValueBox.Text.Remove(ValueBox.SelectionStart, ValueBox.SelectionLength);
        foreach (char c in e.Text)
        {
            if (char.IsDigit(c)) continue;
            if (c == '.' && DecimalPlaces > 0 && !textWithoutSelection.Contains('.')) continue;
            if (c == '-' && Minimum < 0 && ValueBox.SelectionStart == 0 && !ValueBox.Text.Contains('-')) continue;
            e.Handled = true;
            return;
        }
    }

    private void ValueBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(ValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            Value = Math.Max(Minimum, Math.Min(Maximum, Math.Round(parsed, DecimalPlaces)));
        else
            UpdateTextFromValue();
    }
}
