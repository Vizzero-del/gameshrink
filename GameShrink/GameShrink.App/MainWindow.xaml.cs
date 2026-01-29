using System.Windows;
using GameShrink.App.Themes;

namespace GameShrink.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();
    }
}
