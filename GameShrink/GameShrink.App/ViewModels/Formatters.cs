namespace GameShrink.App.ViewModels;

public static class Formatters
{
    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return $"{v:0.##} {units[i]}";
    }
}
