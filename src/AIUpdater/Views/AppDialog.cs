using AIUpdater.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIUpdater.Views;

internal static class AppDialog
{
    public static bool Show(Window owner, string message, string title, bool confirmation = false)
    {
        return Create(owner, message, title, confirmation).ShowDialog() == true;
    }

    internal static Window Create(Window? owner, string message, string title, bool confirmation)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Icon = owner?.Icon, Width = 520,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)Application.Current.FindResource("WindowBrush"),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            FontFamily = new("Segoe UI"), FontSize = 13,
            ShowInTaskbar = false,
        };

        dialog.SourceInitialized += (_, _) => WindowTheme.Apply(dialog);
        var content = new Grid { Margin = new(24) };
        content.RowDefinitions.Add(new()
            { Height = GridLength.Auto });

        content.RowDefinitions.Add(new()
            { Height = GridLength.Auto });

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        Grid.SetRow(actions, 1);
        var accept = new Button { Content = confirmation ? "Удалить" : "ОК", IsDefault = !confirmation };
        accept.Click += (_, _) => dialog.DialogResult = true;
        if (confirmation)
        {
            var cancel = new Button { Content = "Отмена", IsCancel = true, IsDefault = true, Margin = new(0, 0, 10, 0) };
            actions.Children.Add(cancel);
            dialog.Loaded += (_, _) => cancel.Focus();
        }
        else
        {
            accept.IsCancel = true;
            dialog.Loaded += (_, _) => accept.Focus();
        }

        actions.Children.Add(accept);
        content.Children.Add(actions);
        dialog.Content = content;
        return dialog;
    }
}
