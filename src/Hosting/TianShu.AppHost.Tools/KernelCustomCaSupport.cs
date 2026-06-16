using System.Formats.Asn1;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace TianShu.AppHost.Tools;

internal static class KernelCustomCaSupport
{
    internal const string TianShuCaCertificateEnv = "TIANSHU_CA_CERTIFICATE";
    internal const string SslCertFileEnv = "SSL_CERT_FILE";
    internal const string DisableSystemProxyEnv = "TIANSHU_DISABLE_SYSTEM_PROXY";
    internal const string LegacyDisableSystemProxyEnv = "TIANSHU_DISABLE_SYSTEM_PROXY";

    private const string CaCertificateHint =
        "If you set TIANSHU_CA_CERTIFICATE or SSL_CERT_FILE, ensure it points to a PEM file containing one or more CERTIFICATE blocks, or unset it to use system roots.";

    private static readonly Regex PemBlockRegex = new(
        "-----BEGIN (?<label>[A-Z0-9 ]+)-----(?<body>.*?)-----END \\k<label>-----",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    internal static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var handler = CreateHttpHandler();
        return new HttpClient(handler)
        {
            Timeout = timeout,
        };
    }

    internal static HttpMessageHandler CreateHttpHandler()
    {
        var handler = new HttpClientHandler();
        if (ShouldDisableSystemProxy())
        {
            handler.UseProxy = false;
        }

        var bundle = TryLoadConfiguredBundle();
        if (bundle is null)
        {
            return handler;
        }

        handler.ServerCertificateCustomValidationCallback = bundle.ValidateForHttp;
        return handler;
    }

    internal static void ConfigureClientWebSocketOptions(ClientWebSocketOptions options)
    {
        if (ShouldDisableSystemProxy())
        {
            options.Proxy = null;
        }

        var bundle = TryLoadConfiguredBundle();
        if (bundle is null)
        {
            return;
        }

        var callbackProperty = options.GetType().GetProperty(
            "RemoteCertificateValidationCallback",
            BindingFlags.Instance | BindingFlags.Public);
        if (callbackProperty is null)
        {
            throw CreateConfigurationException(
                bundle.SourceEnv,
                bundle.Path,
                "current runtime does not expose websocket custom CA hooks");
        }

        var callbackMethod = typeof(ConfiguredCaBundle).GetMethod(
            "ValidateForWebSocket",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (callbackMethod is null)
        {
            throw CreateConfigurationException(
                bundle.SourceEnv,
                bundle.Path,
                "failed to bind websocket custom CA validation callback");
        }

        var callback = Delegate.CreateDelegate(callbackProperty.PropertyType, bundle, callbackMethod);
        callbackProperty.SetValue(options, callback);
    }

    internal static string? ResolveConfiguredCaPathForTest()
        => TryGetConfiguredBundle()?.Path;

    internal static IReadOnlyList<byte[]> LoadConfiguredCertificatesForTest()
        => TryLoadConfiguredBundle()?.CertificateDerList ?? Array.Empty<byte[]>();

    private static ConfiguredCaBundle? TryLoadConfiguredBundle()
    {
        var configured = TryGetConfiguredBundle();
        if (configured is null)
        {
            return null;
        }

        var certificateDerList = LoadCertificates(configured.SourceEnv, configured.Path);
        return new ConfiguredCaBundle(configured.SourceEnv, configured.Path, certificateDerList);
    }

    private static ConfiguredCaLocation? TryGetConfiguredBundle()
    {
        var tianShuPath = NormalizeEnvironmentPath(Environment.GetEnvironmentVariable(TianShuCaCertificateEnv));
        if (!string.IsNullOrWhiteSpace(tianShuPath))
        {
            return new ConfiguredCaLocation(TianShuCaCertificateEnv, tianShuPath!);
        }

        var sslCertPath = NormalizeEnvironmentPath(Environment.GetEnvironmentVariable(SslCertFileEnv));
        return string.IsNullOrWhiteSpace(sslCertPath)
            ? null
            : new ConfiguredCaLocation(SslCertFileEnv, sslCertPath!);
    }

    private static string? NormalizeEnvironmentPath(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static bool ShouldDisableSystemProxy()
    {
        var raw = NormalizeEnvironmentPath(Environment.GetEnvironmentVariable(DisableSystemProxyEnv))
                  ?? NormalizeEnvironmentPath(Environment.GetEnvironmentVariable(LegacyDisableSystemProxyEnv));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim() switch
        {
            "1" => true,
            _ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
            _ when raw.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
            _ when raw.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };
    }

    private static IReadOnlyList<byte[]> LoadCertificates(string sourceEnv, string path)
    {
        string pemText;
        try
        {
            pemText = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateConfigurationException(sourceEnv, path, $"failed to read CA certificate file: {ex.Message}");
        }

        var certificates = new List<byte[]>();
        foreach (Match match in PemBlockRegex.Matches(pemText))
        {
            var label = match.Groups["label"].Value.Trim();
            if (string.Equals(label, "X509 CRL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(label, "CERTIFICATE", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "TRUSTED CERTIFICATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var body = Regex.Replace(match.Groups["body"].Value, "\\s+", string.Empty, RegexOptions.CultureInvariant);
            byte[] pemBytes;
            try
            {
                pemBytes = Convert.FromBase64String(body);
            }
            catch (FormatException ex)
            {
                throw CreateConfigurationException(sourceEnv, path, $"failed to parse PEM file: {ex.Message}");
            }

            byte[] certificateDer;
            try
            {
                var reader = new AsnReader(pemBytes, AsnEncodingRules.BER);
                certificateDer = reader.ReadEncodedValue().ToArray();
            }
            catch (AsnContentException ex)
            {
                throw CreateConfigurationException(sourceEnv, path, $"failed to parse PEM file: {ex.Message}");
            }

            try
            {
                using var certificate = X509CertificateLoader.LoadCertificate(certificateDer);
                certificates.Add(certificate.Export(X509ContentType.Cert));
            }
            catch (CryptographicException ex)
            {
                throw CreateConfigurationException(sourceEnv, path, $"failed to parse certificate: {ex.Message}");
            }
        }

        if (certificates.Count == 0)
        {
            throw CreateConfigurationException(sourceEnv, path, "no certificates found in PEM file");
        }

        return certificates;
    }

    private static InvalidOperationException CreateConfigurationException(string sourceEnv, string path, string detail)
        => new(
            $"Failed to load CA certificates from {path} selected by {sourceEnv}: {detail}. {CaCertificateHint}");

    private sealed record ConfiguredCaLocation(string SourceEnv, string Path);

    private sealed record ConfiguredCaBundle(string SourceEnv, string Path, IReadOnlyList<byte[]> CertificateDerList)
    {
        public bool ValidateForHttp(
            HttpRequestMessage _,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
            => ValidateCertificate(certificate, chain, sslPolicyErrors);

        private bool ValidateForWebSocket(
            object _,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            using var certificate2 = certificate switch
            {
                null => null,
                X509Certificate2 alreadyTyped => X509CertificateLoader.LoadCertificate(alreadyTyped.Export(X509ContentType.Cert)),
                _ => X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert)),
            };
            return ValidateCertificate(certificate2, chain, sslPolicyErrors);
        }

        private bool ValidateCertificate(
            X509Certificate2? certificate,
            X509Chain? _,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            if (certificate is null)
            {
                return false;
            }

            using var customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

            var roots = new List<X509Certificate2>(CertificateDerList.Count);
            try
            {
                foreach (var der in CertificateDerList)
                {
                    var root = X509CertificateLoader.LoadCertificate(der);
                    roots.Add(root);
                    customChain.ChainPolicy.CustomTrustStore.Add(root);
                }

                return customChain.Build(certificate);
            }
            finally
            {
                foreach (var root in roots)
                {
                    root.Dispose();
                }
            }
        }
    }
}
