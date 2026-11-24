namespace PhotoLensWin.Core;

/// <summary>
/// 图片列表的纯下标运算（上下张步进 / 边界钳制 / 幻灯片环绕）。
/// 从 MainWindow 抽出来，行为可单测，避免 UI 层零覆盖。
/// </summary>
public static class ImageNavigator
{
    /// <summary>当前是否还能朝 direction（-1 上一张 / +1 下一张）继续走。</summary>
    public static bool CanStep(int count, int currentIndex, int direction)
    {
        if (count <= 0) return false;
        int next = currentIndex + direction;
        return next >= 0 && next < count;
    }

    /// <summary>把下标钳到 [0, count-1]；空列表返回 -1。</summary>
    public static int ClampIndex(int index, int count)
    {
        if (count <= 0) return -1;
        if (index < 0) return 0;
        if (index >= count) return count - 1;
        return index;
    }

    /// <summary>幻灯片环绕步进：越界时从另一端回卷；空列表返回 -1。</summary>
    public static int WrapStep(int currentIndex, int count, int direction)
    {
        if (count <= 0) return -1;
        int next = currentIndex + direction;
        if (next < 0) next = count - 1;
        if (next >= count) next = 0;
        return next;
    }
}
