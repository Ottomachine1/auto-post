using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MySqlConnector;
using System.Collections.Concurrent;

namespace AutoPost.Infrastructure;

public static class MySqlIdentity
{
    private static readonly ConcurrentDictionary<string, MySqlDataSource> Sources = new();
    public static MySqlConnection Create(string connectionString)
    {
        var config = new MySqlConnectionStringBuilder(connectionString);
        var pin = Environment.GetEnvironmentVariable("MYSQL_CERT_SHA256");
        if (!string.IsNullOrWhiteSpace(pin))
        {
            var expected = Convert.FromHexString(pin.Replace(":", "").Trim());
            if (expected.Length != 32) throw new InvalidOperationException("MYSQL_CERT_SHA256 must contain 32 bytes");
            if (config.SslMode != MySqlSslMode.Required || !string.IsNullOrEmpty(config.SslCa))
                throw new InvalidOperationException("Pinned MySQL requires mandatory TLS and the handshake identity callback");
            // Separate data-source pools by trusted identity; changed pins cannot reuse old TLS sessions.
            config.Pooling = true;
            config.MaximumPoolSize = 10;
            config.ConnectionLifeTime = 60;
            return Sources.GetOrAdd(config.ConnectionString + "|" + Convert.ToHexString(expected), _ =>
                new MySqlDataSourceBuilder(config.ConnectionString)
                    .UseRemoteCertificateValidationCallback((_, certificate, _, _) => Matches(certificate, expected, DateTime.UtcNow))
                    .Build()).CreateConnection();
        }
        if (config.Server is not ("localhost" or "127.0.0.1" or "::1") && config.SslMode is not (MySqlSslMode.VerifyCA or MySqlSslMode.VerifyFull))
            throw new InvalidOperationException("Remote MySQL requires a verified CA or an explicitly trusted certificate fingerprint");
        return new MySqlConnection(config.ConnectionString);
    }

    public static bool Matches(X509Certificate? certificate, byte[] expected, DateTime utcNow)
    {
        if (certificate is null || expected.Length != 32) return false;
        using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return utcNow >= leaf.NotBefore.ToUniversalTime() && utcNow <= leaf.NotAfter.ToUniversalTime()
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(leaf.RawData), expected);
    }
}
