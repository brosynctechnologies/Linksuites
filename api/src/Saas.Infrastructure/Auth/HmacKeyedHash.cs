using System.Security.Cryptography;
using System.Text;
using Saas.Application.Common;

namespace Saas.Infrastructure.Auth;

/// <summary>HMAC-SHA256 keyed with the server's signing secret, separated by purpose so one value hashes differently per use.</summary>
public sealed class HmacKeyedHash(string secret) : IKeyedHash
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes("keyed-hash:" + secret);

    public string Hash(string purpose, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(purpose + "\n" + value)));
}
