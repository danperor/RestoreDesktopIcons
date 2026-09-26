using RestoreDesktopIcons.Models;

namespace RestoreDesktopIcons.Services;

public static class GravityClampingEngine
{
    private const int DefaultMargin = 16;
    private const int EstimatedIconWidth = 80;
    private const int EstimatedIconHeight = 80;

    /// <summary>
    /// 校验并将坐标限制在当前活动屏幕可视工作区内（桌面客户区坐标系），防止出现幽灵图标 (Off-Screen)
    /// </summary>
    public static (int ClampedX, int ClampedY, bool WasClamped) ClampToVisibleWorkArea(
        int targetX,
        int targetY,
        DisplayTopologyModel currentTopology,
        int gridSpacingX = 0,
        int gridSpacingY = 0)
    {
        if (currentTopology.Monitors.Count == 0)
        {
            var vs = currentTopology.VirtualScreen;
            int safeX = Math.Clamp(targetX, 0, Math.Max(0, vs.Width - EstimatedIconWidth));
            int safeY = Math.Clamp(targetY, 0, Math.Max(0, vs.Height - EstimatedIconHeight));
            return (safeX, safeY, safeX != targetX || safeY != targetY);
        }

        // 1. 检查是否完全落于当前任一显示器的客户区工作区内 (允许留有少量边缘缓冲区)
        foreach (var monitor in currentTopology.Monitors)
        {
            var wa = monitor.ClientWorkArea.Width > 0 ? monitor.ClientWorkArea : monitor.WorkArea;
            if (targetX >= wa.Left && targetX < wa.Right - 10 &&
                targetY >= wa.Top && targetY < wa.Bottom - 10)
            {
                // 正常可见，无需引力吸附
                return (targetX, targetY, false);
            }
        }

        // 2. 发生越界（幽灵坐标）：寻找距离最近的显示器客户区（优先主显示器）
        var targetMonitor = currentTopology.Monitors.FirstOrDefault(m => m.IsPrimary) ?? currentTopology.Monitors[0];

        double minDistance = double.MaxValue;
        foreach (var monitor in currentTopology.Monitors)
        {
            var wa = monitor.ClientWorkArea.Width > 0 ? monitor.ClientWorkArea : monitor.WorkArea;
            int centerX = (wa.Left + wa.Right) / 2;
            int centerY = (wa.Top + wa.Bottom) / 2;
            double dist = Math.Pow(targetX - centerX, 2) + Math.Pow(targetY - centerY, 2);
            if (dist < minDistance)
            {
                minDistance = dist;
                targetMonitor = monitor;
            }
        }

        var selectedWorkArea = targetMonitor.ClientWorkArea.Width > 0 ? targetMonitor.ClientWorkArea : targetMonitor.WorkArea;

        // 3. 计算在目标屏幕内的安全吸附范围
        int minX = selectedWorkArea.Left + DefaultMargin;
        int maxX = Math.Max(minX, selectedWorkArea.Right - EstimatedIconWidth - DefaultMargin);
        int minY = selectedWorkArea.Top + DefaultMargin;
        int maxY = Math.Max(minY, selectedWorkArea.Bottom - EstimatedIconHeight - DefaultMargin);

        int clampedX = Math.Clamp(targetX, minX, maxX);
        int clampedY = Math.Clamp(targetY, minY, maxY);

        // 4. 若提供了网格物理间距，按网格物理间距对齐
        if (gridSpacingX > 20 && gridSpacingY > 20)
        {
            int relX = clampedX - selectedWorkArea.Left;
            int relY = clampedY - selectedWorkArea.Top;

            int col = Math.Max(0, (int)Math.Round((double)relX / gridSpacingX));
            int row = Math.Max(0, (int)Math.Round((double)relY / gridSpacingY));

            int alignedX = selectedWorkArea.Left + col * gridSpacingX;
            int alignedY = selectedWorkArea.Top + row * gridSpacingY;

            clampedX = Math.Clamp(alignedX, minX, maxX);
            clampedY = Math.Clamp(alignedY, minY, maxY);
        }

        return (clampedX, clampedY, true);
    }
}
