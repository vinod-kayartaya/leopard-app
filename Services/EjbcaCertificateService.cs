using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace admin_web.Services
{
    public class EjbcaCertificateService : ICertificateService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EjbcaCertificateService> _logger;

        public EjbcaCertificateService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<EjbcaCertificateService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            // Create a new HttpClientHandler with certificate configuration
            var handler = new HttpClientHandler();
            
            // Configure client certificate
            var certPath = _configuration["Ejbca:SuperAdminP12Path"];
            var certPassword = _configuration["Ejbca:SuperAdminP12Password"];
            
            if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(certPassword))
            {
                var cert = new X509Certificate2(certPath, certPassword);
                handler.ClientCertificates.Add(cert);
            }

            // Accept all server certificates (only for development)
            handler.ServerCertificateCustomValidationCallback = 
                (sender, certificate, chain, sslPolicyErrors) => true;

            // Create HttpClient with the configured handler
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_configuration.GetValue<string>("Ejbca:BaseUrl") ?? throw new InvalidOperationException("EJBCA BaseUrl is not configured"))
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        private string GenerateSecurePassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string GeneratePkcs10Request(string firstName, string lastName, string email)
        {
            using (var rsa = RSA.Create(2048))
            {
                // Format the subject name with proper order and format
                var subjectName = new X500DistinguishedName(
                    $"CN={firstName} {lastName}," +  // Common Name first
                    $"E={email}," +                 // Email
                    $"O=Leopard App," +            // Organization
                    $"GN={firstName}," +           // Given Name
                    $"SN={lastName}"               // Surname (no trailing comma)
                );

                var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                
                // Add enhanced key usage for client authentication and browser usage
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection { 
                            new Oid("1.3.6.1.5.5.7.3.2"),  // Client Authentication
                            new Oid("1.3.6.1.5.5.7.3.4"),  // Email Protection
                            new Oid("1.3.6.1.5.5.7.3.1")   // Server Authentication - add for browser compatibility
                        }, 
                        true));

                // Add key usage for broader compatibility
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | 
                        X509KeyUsageFlags.KeyEncipherment |
                        X509KeyUsageFlags.DataEncipherment |
                        X509KeyUsageFlags.NonRepudiation,
                        true));

                // Add Subject Alternative Name for email and CN
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddEmailAddress(email);
                sanBuilder.AddDnsName($"{firstName}.{lastName}".ToLower());  // Add DNS name for browser compatibility
                request.CertificateExtensions.Add(sanBuilder.Build());

                var csr = request.CreateSigningRequest();
                return Convert.ToBase64String(csr);
            }
        }

        public async Task<string?> IssueCertificateAsync(string firstName, string lastName, string email)
        {
            try
            {
                _logger.LogInformation("Attempting to issue certificate for {Email}", email);

                var password = GenerateSecurePassword();
                var certificateRequest = GeneratePkcs10Request(firstName, lastName, email);

                // Calculate dates for 1 year validity
                var validityStart = DateTime.UtcNow;
                var validityEnd = validityStart.AddYears(1);

                var request = new
                {
                    certificate_request = $"-----BEGIN CERTIFICATE REQUEST-----\n{certificateRequest}\n-----END CERTIFICATE REQUEST-----",
                    certificate_profile_name = "ENDUSER",
                    end_entity_profile_name = "EMPTY",
                    certificate_authority_name = "ManagementCA",
                    username = email.Replace("@", "_"),
                    password = password,
                    include_chain = true,
                    validity_start = validityStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    validity_end = validityEnd.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var requestUrl = new Uri(_httpClient.BaseAddress, "/ejbca/ejbca-rest-api/v1/certificate/pkcs10enroll");
                _logger.LogInformation("Full request URL: {Url}", requestUrl);
                
                _logger.LogInformation("Sending request to EJBCA: {RequestBody}", 
                    JsonSerializer.Serialize(request));

                var response = await _httpClient.PostAsync("/ejbca/ejbca-rest-api/v1/certificate/pkcs10enroll", content);
                
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("EJBCA Response: {StatusCode} - {Content}", 
                    response.StatusCode, responseContent);

                _logger.LogInformation("Response Headers: {Headers}", 
                    string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")));

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to issue certificate: {Error}", responseContent);
                    return null;
                }

                // Parse the certificate serial number from the response
                try
                {
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    if (jsonResponse.TryGetProperty("certificate", out var certElement))
                    {
                        // Convert the base64 certificate to X509Certificate2 to get serial number
                        var certBytes = Convert.FromBase64String(certElement.GetString() ?? string.Empty);
                        var cert = new X509Certificate2(certBytes);
                        return cert.SerialNumber;  // Store the serial number instead of the full certificate
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse certificate response");
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error issuing certificate for {Email}", email);
                return null;
            }
        }

        public async Task<byte[]?> DownloadCertificateAsync(string serialNumber)
        {
            try
            {
                _logger.LogInformation("Attempting to download certificate with serial number: {SerialNumber}", serialNumber);

                // Try different URL formats with proper encoding and formats
                var urls = new[]
                {
                    // Format 1: Using issuer DN
                    $"/ejbca/ejbca-rest-api/v1/ca/{Uri.EscapeDataString("UID=c-0qco52x48o5g8mjat,CN=ManagementCA,O=EJBCA Container Quickstart")}/certificate/download",
                    
                    // Format 2: Using CA name and serial number in hex
                    $"/ejbca/ejbca-rest-api/v1/ca/ManagementCA/certificate/0x{serialNumber}/download",
                    
                    // Format 3: Using full path with issuer DN and serial
                    $"/ejbca/ejbca-rest-api/v1/ca/{Uri.EscapeDataString("CN=ManagementCA")}/certificate/0x{serialNumber}/download",
                    
                    // Format 4: Direct certificate endpoint
                    $"/ejbca/ejbca-rest-api/v1/certificate/0x{serialNumber}/download"
                };

                foreach (var url in urls)
                {
                    _logger.LogInformation("Trying URL: {Url}", url);
                    var response = await _httpClient.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var certBytes = await response.Content.ReadAsByteArrayAsync();
                        
                        // Create certificate with specific store location and flags
                        var cert = new X509Certificate2(certBytes, (string?)null, 
                            X509KeyStorageFlags.Exportable | 
                            X509KeyStorageFlags.PersistKeySet | 
                            X509KeyStorageFlags.UserKeySet);

                        _logger.LogInformation("Certificate Subject: {Subject}", cert.Subject);
                        _logger.LogInformation("Certificate Issuer: {Issuer}", cert.Issuer);

                        // Extract the full name from the certificate subject
                        var friendlyName = cert.Subject
                            .Split(',')
                            .FirstOrDefault(x => x.Trim().StartsWith("CN="))
                            ?.Substring(3)
                            .Trim();

                        if (string.IsNullOrEmpty(friendlyName))
                        {
                            // Fallback to email if CN not found
                            friendlyName = cert.Subject
                                .Split(',')
                                .FirstOrDefault(x => x.Trim().StartsWith("E="))
                                ?.Substring(2)
                                .Trim();
                        }

                        // Create a new certificate with the friendly name
                        var exportCert = new X509Certificate2(certBytes, (string?)null,
                            X509KeyStorageFlags.Exportable | 
                            X509KeyStorageFlags.PersistKeySet);

                        if (!string.IsNullOrEmpty(friendlyName))
                        {
                            exportCert.FriendlyName = friendlyName;
                            _logger.LogInformation("Setting friendly name to: {FriendlyName}", friendlyName);
                        }

                        // Store in CurrentUser Personal store with friendly name
                        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
                        {
                            store.Open(OpenFlags.ReadWrite);
                            store.Add(exportCert);  // Add the certificate with friendly name
                            store.Close();
                        }

                        return exportCert.Export(X509ContentType.Pkcs12, "changeit");
                    }
                    
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Failed to download certificate using {Url}: Status: {Status}, Error: {Error}", 
                        url, 
                        response.StatusCode,
                        errorContent);
                }

                _logger.LogError("All download attempts failed for serial number {SerialNumber}", serialNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading certificate with serial number {SerialNumber}", serialNumber);
                return null;
            }
        }
    }
} 