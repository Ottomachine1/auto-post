using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AutoPost.Infrastructure;
using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;

public sealed class MySqlIdentityTests
{
    [Fact]
    public async Task ChangedCertificateStopsBeforeAuthentication()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=database", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () => {
            using var client = await listener.AcceptTcpClientAsync();
            using var socket = client.GetStream();
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload, Encoding.UTF8, true);
            writer.Write((byte)10); writer.Write(Encoding.ASCII.GetBytes("8.4.6\0")); writer.Write(1);
            writer.Write(Encoding.ASCII.GetBytes("abcdefgh")); writer.Write((byte)0);
            writer.Write((ushort)0xffff); writer.Write((byte)45); writer.Write((ushort)2);
            writer.Write((ushort)8); writer.Write((byte)21); writer.Write(new byte[10]);
            writer.Write(Encoding.ASCII.GetBytes("ijklmnopqrst\0mysql_native_password\0"));
            var data = payload.ToArray();
            await socket.WriteAsync(new byte[] {(byte)data.Length,0,0,0}); await socket.WriteAsync(data);
            var header = new byte[4]; await socket.ReadExactlyAsync(header);
            var length = header[0] + (header[1]<<8) + (header[2]<<16);
            var sslRequest = new byte[length]; await socket.ReadExactlyAsync(sslRequest);
            Assert.Equal(32, length); // The pre-TLS SSLRequest has no username or authentication response.
            using var tls = new SslStream(socket, false);
            try {
                await tls.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls12, false);
                var authentication = new byte[1];
                Assert.Equal(0, await tls.ReadAsync(authentication));
            } catch (System.Security.Authentication.AuthenticationException) { }
              catch (IOException) { }
        });
        var old = Environment.GetEnvironmentVariable("MYSQL_CERT_SHA256");
        try {
            Environment.SetEnvironmentVariable("MYSQL_CERT_SHA256", new string('0',64));
            await using var connection = MySqlIdentity.Create($"Server=127.0.0.1;Port={port};User ID=must-not-be-sent;Password=test-only;SslMode=Required;Connection Timeout=5");
            await Assert.ThrowsAsync<MySqlConnector.MySqlException>(() => connection.OpenAsync());
            await server.WaitAsync(TimeSpan.FromSeconds(10));
        } finally { Environment.SetEnvironmentVariable("MYSQL_CERT_SHA256", old); }
    }
    [Fact]
    public void PinRequiresExactCertificateAndValidDates()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=database", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var pin = SHA256.HashData(certificate.RawData);
        Assert.True(MySqlIdentity.Matches(certificate, pin, DateTime.UtcNow));
        Assert.False(MySqlIdentity.Matches(certificate, new byte[32], DateTime.UtcNow));
        Assert.False(MySqlIdentity.Matches(null, pin, DateTime.UtcNow));
        Assert.False(MySqlIdentity.Matches(certificate, pin, DateTime.UtcNow.AddDays(2)));
        Assert.False(MySqlIdentity.Matches(certificate, pin, DateTime.UtcNow.AddDays(-1)));
    }
}
