using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhotoLensWin.Core;

/// <summary>
/// 文件名自然排序比较器：数字按数值比较、大小写不敏感、音调符号不敏感，
/// 中文按系统区域文化排序。对应 Swift 版 compare(options: [.numeric, .caseInsensitive, .diacriticInsensitive])。
/// </summary>
public static class NaturalNameComparer
{
    private static readonly CompareOptions Options =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        int ia = 0, ib = 0;
        var culture = CultureInfo.CurrentCulture;
        while (ia < a.Length && ib < b.Length)
        {
            bool digitA = char.IsDigit(a[ia]);
            bool digitB = char.IsDigit(b[ib]);
            if (digitA && digitB)
            {
                int cmp = CompareNumericRun(a, ref ia, b, ref ib);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = culture.CompareInfo.Compare(a, ia, 1, b, ib, 1, Options);
                if (cmp != 0) return cmp;
                ia++;
                ib++;
            }
        }

        // 剩余长度差：先结束的更小
        int lengthCmp = (a.Length - ia).CompareTo(b.Length - ib);
        if (lengthCmp != 0) return lengthCmp;

        // 字符逐个等值但字面不同（如 a01 vs a1）：按文化比较兜底，保证顺序确定
        return culture.CompareInfo.Compare(a, b, Options);
    }

    private static int CompareNumericRun(string a, ref int ia, string b, ref int ib)
    {
        int startA = ia, startB = ib;
        while (ia < a.Length && char.IsDigit(a[ia])) ia++;
        while (ib < b.Length && char.IsDigit(b[ib])) ib++;

        int endA = ia, endB = ib;
        // 跳过前导零再比长度/字面
        while (startA < endA - 1 && a[startA] == '0') startA++;
        while (startB < endB - 1 && b[startB] == '0') startB++;

        int lenA = endA - startA, lenB = endB - startB;
        if (lenA != lenB) return lenA.CompareTo(lenB);
        for (int i = 0; i < lenA; i++)
        {
            int cmp = a[startA + i].CompareTo(b[startB + i]);
            if (cmp != 0) return cmp;
        }
        return 0; // 数值相等（如 01 与 1）：继续比较后续字符
    }
}

public sealed class NaturalNameComparer<T> : IComparer<T>
{
    private readonly Func<T?, string?> _key;
    public static readonly NaturalNameComparer<T> Instance = new(e => e?.ToString());

    public NaturalNameComparer(Func<T?, string?> key) => _key = key;

    public int Compare(T? x, T? y) => NaturalNameComparer.Compare(_key(x), _key(y));
}
