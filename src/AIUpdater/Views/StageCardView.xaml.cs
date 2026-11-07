using System.Windows;
using System.Windows.Controls;

namespace AIUpdater.Views;

public partial class StageCardView : UserControl
{
    public StageCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (CardBorder.ContextMenu is { } menu)
            {
                menu.DataContext = DataContext;
            }
        };
    }

    private void CardMenuButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CardBorder.ContextMenu is { } menu)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = CardBorder;
            menu.IsOpen = true;
        }
    }
}
