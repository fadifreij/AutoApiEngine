using System;
using System.Security.Cryptography;
using System.Text;

namespace AutoApiEngine.ServiceAbstraction.Common
{
    /// <summary>
    /// The single source of truth for the API key hash convention. Shared by ApiKeyController (hash on
    /// create) and the API key auth filter (hash the presented X-Api-Key header) so the two can never
    /// drift — if they did, every existing key would silently stop matching.
    /// <para>
    /// The output format is load-bearing: SHA-256 rendered as lowercase hex (64 chars). Do not change it;
    /// changing it invalidates every key already stored in ApiKeys.Key.
    /// </para>
    /// </summary>
    public static class ApiKeyHasher
    {
        public static string Hash(string plainKey)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainKey));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
