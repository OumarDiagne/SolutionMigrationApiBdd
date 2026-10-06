using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MigrationApiBdd.Helpers
{
    public static class RequestHashHelper
    {

        public static string Compute<T>(T request)
        {
            var json = JsonSerializer.Serialize(request);

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
