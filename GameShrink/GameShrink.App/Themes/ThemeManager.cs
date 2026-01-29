using MediaColor = System.Windows.Media.Color;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace GameShrink.App.Themes;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void ApplyLightTheme()
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(BaseTheme.Light);

        // Accent: teal-ish modern look
        theme.SetPrimaryColor(MediaColor.FromRgb(0x17, 0x8F, 0x8F));
        theme.SetSecondaryColor(MediaColor.FromRgb(0x7C, 0x3A, 0xED));

        paletteHelper.SetTheme(theme);
        ApplyAppSurface(isDark: false);
        IsDark = false;
    }

    public static void ApplyDarkTheme()
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(BaseTheme.Dark);

        theme.SetPrimaryColor(MediaColor.FromRgb(0x2A, 0xC7, 0xC7));
        theme.SetSecondaryColor(MediaColor.FromRgb(0xA7, 0x8B, 0xFA));

        paletteHelper.SetTheme(theme);
        ApplyAppSurface(isDark: true);
        IsDark = true;
    }

    public static void ToggleTheme()
    {
        if (IsDark) ApplyLightTheme();
        else ApplyDarkTheme();
    }

    private static void ApplyAppSurface(bool isDark)
    {
        if (System.Windows.Application.Current is null) return;

        // Softer, higher-contrast surfaces than the default MD dark palette.
        if (isDark)
        {
            SetBrush("App.Brush.WindowBackground", MediaColor.FromRgb(0x0F, 0x17, 0x2A)); // slate-900
            SetBrush("App.Brush.CardBackground", MediaColor.FromRgb(0x1E, 0x29, 0x3B));   // slate-800
            SetBrush("App.Brush.CardAltBackground", MediaColor.FromRgb(0x23, 0x32, 0x4D));
            SetBrush("App.Brush.HelperText", MediaColor.FromRgb(0xB6, 0xC2, 0xD3));
        }
        else
        {
            SetBrush("App.Brush.WindowBackground", MediaColor.FromRgb(0xF7, 0xF8, 0xFA));
            SetBrush("App.Brush.CardBackground", Colors.White);
            SetBrush("App.Brush.CardAltBackground", MediaColor.FromRgb(0xF3, 0xF4, 0xF6));
            SetBrush("App.Brush.HelperText", MediaColor.FromRgb(0x6B, 0x72, 0x80));
        }
    }

    private static void SetBrush(string key, MediaColor color)
    {
        // Replace (or create) the brush so DynamicResource bindings update.
        System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
