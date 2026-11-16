namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 14-1 章第 2 項：各平台逐位相同的次方、對數與指數。
/// <c>Math.Pow</c>、<c>Math.Log</c>、<c>Math.Exp</c> 由作業系統的數學函式庫計算，Windows 與 Linux 的最後一位可能不同，
/// 抽樣落在邊界時世界就會分岔（總綱第 4 節法則 8）。這裡只用加減乘除、開根號、取整與二的次方縮放——
/// 這些運算在 IEEE 754 下每個平台結果相同。精度約 1e-15，足夠機率計算使用。
/// </summary>
public static class DemoDeterministicMath
{
    private const double Ln2 = 0.6931471805599453;
    private const double Sqrt2 = 1.4142135623730951;

    /// <summary>x 的 n 次方（n 為非負整數；平方再相乘）。</summary>
    public static double PowInt(double x, long n)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));
        var result = 1.0;
        var factor = x;
        while (n > 0)
        {
            if ((n & 1) == 1)
                result *= factor;
            factor *= factor;
            n >>= 1;
        }
        return result;
    }

    /// <summary>自然對數（x＞0；0 回傳負無限大，負數回傳 NaN）。</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0)
            return double.NaN;
        if (x == 0)
            return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x))
            return x;
        var exponent = 0;
        if (x < 2.2250738585072014E-308)
        {
            x *= 4503599627370496.0; // 次正規數先乘 2^52
            exponent -= 52;
        }
        var bits = BitConverter.DoubleToInt64Bits(x);
        exponent += (int)((bits >> 52) & 0x7FF) - 1023;
        var m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L); // 尾數，1 ≤ m ＜ 2
        if (m > Sqrt2)
        {
            m *= 0.5;
            exponent++;
        }
        // ln m ＝ 2·(s ＋ s³/3 ＋ s⁵/5 ＋ …)，s＝(m−1)/(m＋1)，|s| ≤ 0.172。
        var s = (m - 1) / (m + 1);
        var s2 = s * s;
        var term = s;
        var sum = 0.0;
        for (var k = 1; k <= 41; k += 2)
        {
            sum += term / k;
            term *= s2;
        }
        return 2 * sum + exponent * Ln2;
    }

    /// <summary>e 的 x 次方。</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x))
            return x;
        if (x > 709.78)
            return double.PositiveInfinity;
        if (x < -745.2)
            return 0;
        var k = (long)Math.Floor(x / Ln2 + 0.5);
        var r = x - k * Ln2; // |r| ≤ 0.35
        var term = 1.0;
        var sum = 1.0;
        for (var i = 1; i <= 24; i++)
        {
            term *= r / i;
            sum += term;
        }
        return Math.ScaleB(sum, (int)k);
    }

    /// <summary>b 的 y 次方（b ≥ 0；機率換算用，例如年機率換成週機率）。</summary>
    public static double Pow(double b, double y)
    {
        if (double.IsNaN(b) || double.IsNaN(y))
            return double.NaN;
        if (y == 0 || b == 1)
            return 1;
        if (b <= 0)
            return y > 0 ? 0 : double.PositiveInfinity;
        return Exp(y * Log(b));
    }
}
