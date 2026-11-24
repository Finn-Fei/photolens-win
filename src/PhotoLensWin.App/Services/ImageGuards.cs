using System.IO;
using SkiaSharp;

namespace PhotoLensWin.App.Services;

/// <summary>
/// 解码前的尺寸闸值：畸形/炸弹图（例如声明 40000×40000）会让解码器一次性申请 GB 级缓冲，
/// 结果是 OOM 被 catch 吞成"无法解码"，或进程被系统杀掉。先按像素上限拒绝。
/// </summary>
public static class ImageGuards
{
    /// <summary>允许解码的最大像素数（约 2 亿像素；Bgra8888 下约 800MB）。</summary>
    public const long MaxPixelCount = 200_000_000L;

    /// <summary>只读取头部信息判断像素总数是否在预算内；探测失败时返回 true（交给解码器自己报错）。</summary>
    public static bool WithinPixelBudget(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return true;
            var info = codec.Info;
            long pixels = (long)info.Width * info.Height;
            return pixels > 0 && pixels <= MaxPixelCount;
        }
        catch
        {
            return true;
        }
    }
}
