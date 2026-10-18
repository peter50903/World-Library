using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WorldSimulation.Core.Config;

/// <summary>
/// 第 02 章 b 節（40F VER_CONFIG）：設定雜湊。SHA-256；檔案依檔名 Ordinal 排序，每檔依序寫入
/// UTF8(檔名)、0x00、UInt64LE(正規化後長度)、正規化內容（去開頭 UTF-8 BOM、CRLF→LF）。
/// 輸出 64 個小寫十六進位字元。換行設定或檔案讀取順序不會改變雜湊。
/// </summary>
public static class ConfigHash
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    public static string Compute(IEnumerable<(string Name, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[8];
        foreach (var (name, content) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var normalized = Normalize(content);
            sha.AppendData(Encoding.UTF8.GetBytes(name));
            sha.AppendData(new byte[] { 0 });
            BinaryPrimitives.WriteUInt64LittleEndian(length, (ulong)normalized.Length);
            sha.AppendData(length);
            sha.AppendData(normalized);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>去開頭 BOM、CRLF→LF（單獨的 CR 保留）。</summary>
    internal static byte[] Normalize(byte[] content)
    {
        var start = content.AsSpan().StartsWith(Bom) ? Bom.Length : 0;
        var output = new List<byte>(content.Length - start);
        for (var i = start; i < content.Length; i++)
        {
            if (content[i] == (byte)'\r' && i + 1 < content.Length && content[i + 1] == (byte)'\n')
                continue;
            output.Add(content[i]);
        }

        return output.ToArray();
    }
}
