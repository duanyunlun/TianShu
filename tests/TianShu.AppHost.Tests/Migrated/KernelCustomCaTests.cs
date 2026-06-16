using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Net;
using System.Net.WebSockets;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelCustomCaTests : IDisposable
{
    private readonly string? originalTianShuCa = Environment.GetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv);
    private readonly string? originalSslCertFile = Environment.GetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv);
    private readonly string? originalDisableSystemProxy = Environment.GetEnvironmentVariable(KernelCustomCaSupport.DisableSystemProxyEnv);

    [Fact]
    public void ResolveConfiguredCaPathForTest_ShouldPreferTianShuEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, @"C:\tianshu.pem");
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, @"C:\fallback.pem");

        var path = KernelCustomCaSupport.ResolveConfiguredCaPathForTest();

        Assert.Equal(@"C:\tianshu.pem", path);
    }

    [Fact]
    public void ResolveConfiguredCaPathForTest_ShouldFallBackToSslCertFile()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, null);
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, @"C:\fallback.pem");

        var path = KernelCustomCaSupport.ResolveConfiguredCaPathForTest();

        Assert.Equal(@"C:\fallback.pem", path);
    }

    [Fact]
    public void ResolveConfiguredCaPathForTest_ShouldIgnoreEmptyValues()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, string.Empty);
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, @"C:\fallback.pem");

        var path = KernelCustomCaSupport.ResolveConfiguredCaPathForTest();

        Assert.Equal(@"C:\fallback.pem", path);
    }

    [Fact]
    public void LoadConfiguredCertificatesForTest_ShouldAcceptTrustedCertificateAndIgnoreCrl()
    {
        var root = CreateTempDirectory();

        try
        {
            var pemPath = Path.Combine(root, "trusted-ca.pem");
            File.WriteAllText(pemPath, BuildTestPem(trustedLabel: true, includeCrl: true));
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, pemPath);
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, null);

            var certificates = KernelCustomCaSupport.LoadConfiguredCertificatesForTest();

            Assert.Single(certificates);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateHttpHandler_ShouldReportReadableErrorForInvalidPem()
    {
        var root = CreateTempDirectory();

        try
        {
            var pemPath = Path.Combine(root, "invalid.pem");
            File.WriteAllText(pemPath, string.Empty);
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, pemPath);
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, null);

            var error = Assert.Throws<InvalidOperationException>(() => KernelCustomCaSupport.CreateHttpHandler());
            Assert.Contains(KernelCustomCaSupport.TianShuCaCertificateEnv, error.Message, StringComparison.Ordinal);
            Assert.Contains(pemPath, error.Message, StringComparison.Ordinal);
            Assert.Contains("no certificates found in PEM file", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ConfigureClientWebSocketOptions_ShouldInstallValidationCallbackWhenCustomCaConfigured()
    {
        var root = CreateTempDirectory();

        try
        {
            var pemPath = Path.Combine(root, "trusted-ca.pem");
            File.WriteAllText(pemPath, BuildTestPem(trustedLabel: false, includeCrl: false));
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, pemPath);
            Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, null);

            using var client = new ClientWebSocket();
            var options = client.Options;
            KernelCustomCaSupport.ConfigureClientWebSocketOptions(options);

            var callbackProperty = options.GetType().GetProperty(
                "RemoteCertificateValidationCallback",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(callbackProperty);
            Assert.NotNull(callbackProperty!.GetValue(options));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateHttpHandler_WhenDisableSystemProxyEnabled_ShouldDisableProxy()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.DisableSystemProxyEnv, "1");

        var handler = Assert.IsType<HttpClientHandler>(KernelCustomCaSupport.CreateHttpHandler());

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void ConfigureClientWebSocketOptions_WhenDisableSystemProxyEnabled_ShouldClearProxy()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.DisableSystemProxyEnv, "1");

        using var client = new ClientWebSocket();
        client.Options.Proxy = new WebProxy("http://127.0.0.1:8899");

        KernelCustomCaSupport.ConfigureClientWebSocketOptions(client.Options);

        Assert.Null(client.Options.Proxy);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.TianShuCaCertificateEnv, originalTianShuCa);
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.SslCertFileEnv, originalSslCertFile);
        Environment.SetEnvironmentVariable(KernelCustomCaSupport.DisableSystemProxyEnv, originalDisableSystemProxy);
    }

    private static string BuildTestPem(bool trustedLabel, bool includeCrl)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=TianShu Test CA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7));
        var pem = certificate.ExportCertificatePem();
        if (trustedLabel)
        {
            pem = pem
                .Replace("BEGIN CERTIFICATE", "BEGIN TRUSTED CERTIFICATE", StringComparison.Ordinal)
                .Replace("END CERTIFICATE", "END TRUSTED CERTIFICATE", StringComparison.Ordinal);
        }

        if (!includeCrl)
        {
            return pem;
        }

        return pem + Environment.NewLine +
               "-----BEGIN X509 CRL-----" + Environment.NewLine +
               "AQID" + Environment.NewLine +
               "-----END X509 CRL-----" + Environment.NewLine;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
