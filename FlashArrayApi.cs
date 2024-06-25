using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Web;

namespace puredf;

internal class FlashArrayApi
{
    internal const string HttpAuthorizationHeaderName = "Authorization";
    internal const string HttpJsonMediaType = "application/json";
    internal const string ApiVersion = "2.30";

    internal const string GetArraySpaceInfoMethod = "arrays/space";
    internal const string ListDirectoryExportsMethod = "directory-exports";
    internal const string ListDirectorySpaceUtilizationMethod = "directories/space";
    internal const string ListQuotaPoliciesAttachedToDirectoryMethod = "directories/policies/quota";
    internal const string ListQuotaPolicyRulesMethod = "policies/quota/rules";

    internal string IpFqdn { get; init; }
    internal bool Insecure { get; init; }
    internal string? AccessToken { get; set; }

    internal FlashArrayApi(string ipFqdn, bool insecure = true)
    {
        IpFqdn = ipFqdn;
        Insecure = insecure;
    }

    private HttpClient CreateHttpClient()
    {
        if (Insecure)
        {
            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return true;
                }
            };

            return new HttpClient(httpClientHandler);
        }

        return new HttpClient();
    }

    private HttpClient CreateAuthHttpClient()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            throw new InvalidOperationException("Login required before using this method");
        }

        HttpClient client;

        if (Insecure)
        {
            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return true;
                }
            };

            client = new HttpClient(httpClientHandler);
            client.DefaultRequestHeaders.Add(HttpAuthorizationHeaderName, AccessToken);

            return client;
        }

        client = new HttpClient();
        client.DefaultRequestHeaders.Add(HttpAuthorizationHeaderName, AccessToken);

        return client;
    }

    private Uri CreateApiUri(string apiMethod, Dictionary<string, string>? queryParameters = null)
    {
        var builder = new UriBuilder($"https://{IpFqdn}/api/{ApiVersion}/{apiMethod}");

        if (queryParameters != null)
        {
            var query = HttpUtility.ParseQueryString(builder.Query);

            foreach (var parameter in queryParameters)
                query[parameter.Key] = parameter.Value;

            builder.Query = query.ToString();
        }

        return builder.Uri;
    }

    internal async Task<bool> Login(string clientId, string keyId, string issuer, string username, string privateKeyPath)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));

        var key = new RsaSecurityKey(rsa)
        {
            KeyId = keyId
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: clientId,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, username) },
            expires: DateTime.Now.AddMinutes(60),
            signingCredentials: creds);

        var encodedJwt = new JwtSecurityTokenHandler().WriteToken(token);

        var content = new FormUrlEncodedContent
            (
                new Dictionary<string, string>()
                {
                    { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
                    { "subject_token" , encodedJwt },
                    { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" }
                }
            );

        var httpClient = CreateHttpClient();

        var response = await httpClient.PostAsync($"https://{IpFqdn}/oauth2/1.0/token", content);

        if (response.IsSuccessStatusCode)
        {
            var responseJson = JObject.Parse(await response.Content.ReadAsStringAsync());
            AccessToken = "Bearer " + responseJson["access_token"];
        }

        return response.IsSuccessStatusCode;
    }

    internal async Task<List<Version>?> GetApiVersions()
    {
        var httpClient = CreateHttpClient();
        var response = await httpClient.GetAsync($"https://{IpFqdn}/api/api_version");

        if (response.IsSuccessStatusCode)
        {
            var responseJson = JObject.Parse(await response.Content.ReadAsStringAsync());
            return responseJson["versions"]!.Select(p => new Version(p.ToString())).ToList();
        }

        return null;
    }

    internal async Task<string> GetArraySpaceInfo()
    {
        var httpClient = CreateAuthHttpClient();
        var response = await httpClient.GetAsync(CreateApiUri(GetArraySpaceInfoMethod));
        return await response.Content.ReadAsStringAsync();
    }

    internal async Task<string> GetDirectoryExports()
    {
        var httpClient = CreateAuthHttpClient();
        var response = await httpClient.GetAsync(CreateApiUri(ListDirectoryExportsMethod));
        return await response.Content.ReadAsStringAsync();
    }

    internal async Task<string> GetDirectorySpaceUtilization(string directoryId)
    {
        var httpClient = CreateAuthHttpClient();

        var queryParameters = new Dictionary<string, string>()
        {
            { "ids", directoryId }
        };

        var response = await httpClient.GetAsync(CreateApiUri(ListDirectorySpaceUtilizationMethod, queryParameters));
        return await response.Content.ReadAsStringAsync();
    }

    internal async Task<bool> DoesQuotaPolicyExistsForDirectory(string directoryId)
    {
        var httpClient = CreateAuthHttpClient();

        var queryParameters = new Dictionary<string, string>()
        {
            { "member_ids", directoryId }
        };

        var response = await httpClient.GetAsync(CreateApiUri(ListQuotaPoliciesAttachedToDirectoryMethod, queryParameters));
        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
        return result["items"]?.Count() > 0;
    }

    internal async Task<string> GetQuotaPoliciesForDirectory(string directoryId)
    {
        var httpClient = CreateAuthHttpClient();

        var queryParameters = new Dictionary<string, string>()
        {
            { "member_ids", directoryId }
        };

        var response = await httpClient.GetAsync(CreateApiUri(ListQuotaPoliciesAttachedToDirectoryMethod, queryParameters));
        return await response.Content.ReadAsStringAsync();
    }

    internal async Task<string> GetQuotaPolicyRules(string policyId)
    {
        var httpClient = CreateAuthHttpClient();

        var queryParameters = new Dictionary<string, string>()
        {
            { "policy_ids", policyId }
        };

        var response = await httpClient.GetAsync(CreateApiUri(ListQuotaPolicyRulesMethod, queryParameters));
        return await response.Content.ReadAsStringAsync();
    }
}