using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexQuotaMonitor.Wpf;

public sealed class RefreshStatusBlock : FrameworkElement
{
    private System.Windows.Media.Color _accentColor = Formatting.ColorFromHex("#8794A2");

    public RefreshStatusBlock()
    {
        Width = 12;
        Height = 12;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        VerticalAlignment = System.Windows.VerticalAlignment.Top;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 30000);
    }

    public void SetStatus(DateTimeOffset? refreshedAt, DateTimeOffset currentTime, string status, string accentHex,
        string? error = null, bool refreshing = false)
    {
        _accentColor = Formatting.ColorFromHex(accentHex);
        var description = status switch
        {
            "WAIT" => "等待首次更新",
            "OLD" => "最近刷新失败，当前显示上次有效额度",
            "ERR" => "额度读取失败，暂无可用数据",
            "STALE" => "额度数据已过期，等待成功更新",
            _ => "额度更新正常"
        };
        if (refreshedAt.HasValue)
        {
            var minutes = Math.Max(0, (int)(currentTime - refreshedAt.Value).TotalMinutes);
            description += minutes == 0 ? "\n刚刚更新" : $"\n{minutes} 分钟前更新";
            description += $"\n上次成功：{refreshedAt.Value:MM-dd HH:mm:ss}";
        }
        if (refreshing) description += "\n正在刷新…";
        if (!string.IsNullOrWhiteSpace(error)) description += $"\n失败原因：{error}";
        ToolTip = description;
        AutomationProperties.SetName(this, description);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // A generous invisible hover target surrounds the quiet 4.5 DIP indicator.
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawEllipse(new SolidColorBrush(_accentColor), null, new System.Windows.Point(6, 6), 2.25, 2.25);
    }
}
