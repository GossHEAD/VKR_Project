using System.Security.Cryptography;
using System.Text;

namespace VKR_Node.Utilities;

public static class Hashing
{
    public static Guid ComputeSha1Guid(string input)
    {
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(hash.Take(16).ToArray());
        }
    }

    public static string ComputeSha1Hex(string input)
    {
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}