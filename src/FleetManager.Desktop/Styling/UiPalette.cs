using System.Windows.Media;

namespace FleetManager.Desktop.Styling;

internal static class UiPalette
{
    public static readonly Brush ShellBackground = Create("#0E1A24");
    public static readonly Brush SidebarBackground = Create("#13202B");
    public static readonly Brush PanelBackground = Create("#162532");
    public static readonly Brush PanelBackgroundAlt = Create("#1A2A35");
    public static readonly Brush CardBorder = Create("#2C3A43");
    public static readonly Brush SoftCardBackground = Create("#162532");
    public static readonly Brush NeutralText = Create("#F8F5F1");
    public static readonly Brush MutedText = Create("#B9C4CD");
    public static readonly Brush Accent = Create("#47C1A8");
    public static readonly Brush AccentForeground = Create("#09231F");
    public static readonly Brush NeutralBadgeBackground = Create("#253845");
    public static readonly Brush NeutralBadgeForeground = Create("#DCE4EA");

    public static readonly Brush SuccessBackground = Create("#123F37");
    public static readonly Brush SuccessForeground = Create("#84F0DA");

    public static readonly Brush InfoBackground = Create("#12374D");
    public static readonly Brush InfoForeground = Create("#9AD4FF");

    public static readonly Brush WarningBackground = Create("#4F3417");
    public static readonly Brush WarningForeground = Create("#FFD18F");

    public static readonly Brush CriticalBackground = Create("#4B2025");
    public static readonly Brush CriticalForeground = Create("#FFB1B1");

    public static readonly Brush PendingBackground = Create("#2B3842");
    public static readonly Brush PendingForeground = Create("#D4DCE2");

    public static readonly Brush ManualBackground = Create("#4B2025");
    public static readonly Brush ManualForeground = Create("#FFB1B1");

    public static Brush GetNodeStatusBackground(string status) => status switch
    {
        "Online" => SuccessBackground,
        "Degraded" => WarningBackground,
        "Offline" => CriticalBackground,
        _ => NeutralBadgeBackground
    };

    public static Brush GetNodeStatusForeground(string status) => status switch
    {
        "Online" => SuccessForeground,
        "Degraded" => WarningForeground,
        "Offline" => CriticalForeground,
        _ => NeutralBadgeForeground
    };

    private static SolidColorBrush Create(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
