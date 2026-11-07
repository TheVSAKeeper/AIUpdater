using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AIUpdater.Infrastructure;

public sealed class FormattedTextBlock : TextBlock
{
    public static readonly DependencyProperty FormattedTextProperty = DependencyProperty.Register(
        nameof(FormattedText),
        typeof(string),
        typeof(FormattedTextBlock),
        new PropertyMetadata(string.Empty, OnFormattedTextChanged));

    static FormattedTextBlock()
    {
        FontSizeProperty.OverrideMetadata(
            typeof(FormattedTextBlock),
            new FrameworkPropertyMetadata(
                14.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                OnFormattedTextChanged));
    }

    public FormattedTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    public string? FormattedText
    {
        get => (string?)GetValue(FormattedTextProperty);
        set => SetValue(FormattedTextProperty, value);
    }

    private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FormattedTextBlock block)
        {
            block.RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        var fontSize = FontSize > 0 ? FontSize : 14;
        LineHeight = Math.Round(fontSize * 1.42);
        Inlines.Clear();

        var text = FormattedText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var parts = Regex.Split(text, @"(`[^`]+`)");
        foreach (var part in parts)
        {
            if (part.Length >= 2 && part.StartsWith('`') && part.EndsWith('`'))
            {
                Inlines.Add(new Run(" " + part[1..^1] + " ")
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = Math.Max(11, fontSize - 1),
                    Background = new SolidColorBrush(Color.FromRgb(22, 38, 30)),
                    Foreground = new SolidColorBrush(Color.FromRgb(167, 232, 197)),
                });
            }
            else if (part.Length > 0)
            {
                Inlines.Add(new Run(part));
            }
        }
    }
}
