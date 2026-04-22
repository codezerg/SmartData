using System.Numerics;
using System.Text;

namespace SmartData.Core;

/// <summary>
/// Generates 32-character Base62-encoded IDs using timestamp + GUID.
/// Format: (DateTime bytes + Guid bytes) → Base62 encode → 32 character string
/// </summary>
public static class IdGenerator
{
    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string NewId()
    {
        var timestampBytes = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
        var guidBytes = Guid.NewGuid().ToByteArray();

        var combined = new byte[24];
        Array.Copy(timestampBytes, 0, combined, 0, 8);
        Array.Copy(guidBytes, 0, combined, 8, 16);

        return ToBase62(combined);
    }

    private static string ToBase62(byte[] bytes)
    {
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

        if (value == 0)
            return new string('0', 32);

        var result = new StringBuilder();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, 62, out var remainder);
            result.Insert(0, Base62Chars[(int)remainder]);
        }

        return result.ToString().PadLeft(32, '0');
    }
}
