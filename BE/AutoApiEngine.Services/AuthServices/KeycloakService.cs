using AutoApiEngine.ServiceAbstraction.DTO;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoApiEngine.Services.AuthServices
{
    public class KeycloakService
    {
        private readonly HttpClient _httpClient;
        
        private string? _adminToken;
        private DateTime _adminTokenExpiry;

        private readonly KeyclockSettings _settings;

        //private string KeycloakTokenUrl =>
        //  $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/token";

        public KeycloakService(IOptions<KeyclockSettings> settings)
        {
            _httpClient = new HttpClient();
            _settings = settings.Value;
       
         }

        private async Task EnsureAdminToken()
        {
            if (!string.IsNullOrEmpty(_adminToken) && _adminTokenExpiry > DateTime.UtcNow)
                return;

            var tokenRequest = new Dictionary<string, string>
            {
                {"grant_type", "client_credentials"},
                {"client_id", _settings.ApiClientId},
                {"client_secret", _settings.ApiClientSecret}
            };

            var content = new FormUrlEncodedContent(tokenRequest);
            var response = await _httpClient.PostAsync(
                $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/token",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get admin token: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(json);

            _adminToken = tokenResponse.GetProperty("access_token").GetString();
            _adminTokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32() - 60);
        }

        public async Task<KeycloakUser> CreateUser(string email, string password, string organization, string? firstName = null, string? lastName = null )
        {
            await EnsureAdminToken();

            var user = new
            {
                username = email,
                email = email,
                firstName = firstName ?? email.Split('@')[0],
                lastName = lastName ?? "",
                enabled = true,
                attributes = new
                {
                    org = new[] { organization }
                },
                credentials = new[]
                {
                    new
                    {
                        type = "password",
                        value = password,
                        temporary = false
                    }
                }
            };

            var json = JsonSerializer.Serialize(user);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.Url}/admin/realms/{_settings.Realm}/users")
            {
                Content = content
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create Keycloak user: {error}");
            }

            var location = response.Headers.Location?.ToString();
            var userId = location?.Split('/')[^1] ?? await GetUserIdByUsername(email);

            return new KeycloakUser
            {
                Id = userId,
                Username = email,
                Email = email
            };
        }

        public string GetAuthorizationUrl(string redirectUri, string? state = null)
        {
            var parameters = new List<string>
            {
                $"client_id=api-engine-app",
                $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
                $"response_type=code",
                $"scope=openid"
            };

            if (!string.IsNullOrEmpty(state))
            {
                parameters.Add($"state={state}");
            }

            return $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/auth?{string.Join("&", parameters)}";
        }
       

        public async Task<KeycloakUserInfo> GetUserInfo(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get user info: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var userInfo = JsonSerializer.Deserialize<JsonElement>(json);

            return new KeycloakUserInfo
            {
                Sub = userInfo.GetProperty("sub").GetString() ?? "",
                Email = userInfo.TryGetProperty("email", out var email) ? email.GetString() : null,
                PreferredUsername = userInfo.TryGetProperty("preferred_username", out var username) ? username.GetString() : null,
                Name = userInfo.TryGetProperty("name", out var name) ? name.GetString() : null
            };
        }
                      

        public async Task<KeycloakGroup> CreateGroup(string name, string? parentId = null)
        {
            await EnsureAdminToken();

            var group = new
            {
                name = name
            };

            var json = JsonSerializer.Serialize(group);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = string.IsNullOrEmpty(parentId)
                ? $"{_settings.Url}/admin/realms/{_settings.Realm}/groups"
                : $"{_settings.Url}/admin/realms/{_settings.Realm}/groups/{parentId}/children";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create Keycloak group: {error}");
            }

            var location = response.Headers.Location?.ToString();
            var groupId = location?.Split('/')[^1] ?? await GetGroupIdByName(name, parentId);

            return new KeycloakGroup
            {
                Id = groupId,
                Name = name,
                ParentId = parentId
            };
        }

        public async Task<List<KeycloakGroup>> GetGroups()
        {
            await EnsureAdminToken();

            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.Url}/admin/realms/{_settings.Realm}/groups");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return new List<KeycloakGroup>();

            var json = await response.Content.ReadAsStringAsync();
            var groups = JsonSerializer.Deserialize<JsonElement>(json);

            var result = new List<KeycloakGroup>();
            foreach (var g in groups.EnumerateArray())
            {
                result.Add(new KeycloakGroup
                {
                    Id = g.GetProperty("id").GetString() ?? "",
                    Name = g.GetProperty("name").GetString() ?? ""
                });
            }

            return result;
        }

        public async Task AddUserToGroup(string userId, string groupId)
        {
            await EnsureAdminToken();

            var url = $"{_settings.Url}/admin/realms/{_settings.Realm}/users/{userId}/groups/{groupId}";
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to add user to group: {error}");
            }
        }

        public async Task RemoveUserFromGroup(string userId, string groupId)
        {
            await EnsureAdminToken();

            var url = $"{_settings.Url}/admin/realms/{_settings.Realm}/users/{userId}/groups/{groupId}";
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to remove user from group: {error}");
            }
        }

        public async Task<List<KeycloakGroup>> GetUserGroups(string userId)
        {
            await EnsureAdminToken();

            var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.Url}/admin/realms/{_settings.Realm}/users/{userId}/groups");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return new List<KeycloakGroup>();

            var json = await response.Content.ReadAsStringAsync();
            var groups = JsonSerializer.Deserialize<JsonElement>(json);

            var result = new List<KeycloakGroup>();
            foreach (var g in groups.EnumerateArray())
            {
                result.Add(new KeycloakGroup
                {
                    Id = g.GetProperty("id").GetString() ?? "",
                    Name = g.GetProperty("name").GetString() ?? ""
                });
            }

            return result;
        }

        public async Task<string> GetGroupIdByName(string name, string? parentId = null)
        {
            var groups = await GetGroups();
            foreach (var g in groups)
            {
                if (g.Name == name)
                    return g.Id;
            }
            return "";
        }

        public async Task<string> GetUserIdByUsername(string username)
        {
            await EnsureAdminToken();

            var request = new HttpRequestMessage(HttpMethod.Get, 
                $"{_settings.Url}/admin/realms/{_settings.Realm}/users?username={username}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return "";

            var json = await response.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<JsonElement>(json);

            if (users.GetArrayLength() > 0)
                return users[0].GetProperty("id").GetString() ?? "";

            return "";
        }

        public async Task SendVerificationEmail(string userId)
        {
            await EnsureAdminToken();

            var request = new HttpRequestMessage(HttpMethod.Put, 
                $"{_settings.Url}/admin/realms/{_settings.Realm}/users/{userId}/send-verify-email");
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to send verification email: {error}");
            }
        }


        // Token exchange and refresh methods can be implemented here as needed
        public async Task<TokenResponse> LoginAsync(LoginRequest request) 
        {
            string url = $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/token";
            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = request.Code,
                ["redirect_uri"] = request.RedirectUri,
                ["client_id"] = _settings.ClientId,
                ["scope"]= "openid organization"
            };
            AddClientAssertion(parameters);
            return await ExchangeTokenAsync(url, parameters);
        }
        public async Task<TokenResponse> GetRefreshToken(string refreshToken) {
            string url = $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/token";
            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _settings.ClientId,
            };
            AddClientAssertion(parameters);
            return await ExchangeTokenAsync(url, parameters);
        }

        public async Task<string> LogoutAsync(string postLogoutRedirectUri, string? idTokenHint = null)
        {
            string logoutUrl =
              $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/logout" +
              $"?client_id={_settings.ClientId}" +
              $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirectUri)}";

            if (!string.IsNullOrEmpty(idTokenHint))
                logoutUrl += $"&id_token_hint={Uri.EscapeDataString(idTokenHint)}";

            return await Task.FromResult(logoutUrl);
        }
        private async Task<TokenResponse> ExchangeTokenAsync(string url, Dictionary<string, string> parameters)
        {

            var body = new FormUrlEncodedContent(parameters);

            var response = await _httpClient.PostAsync(url, body);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new TokenResponse { Success = false,
                    Error = $"Keycloak token exchange failed: {error}"};
            }

            var content = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>();


            if (content == null) return new TokenResponse { Success = false, Error = "Failed to deserialize Keycloak response" };

            return new TokenResponse { Success = true, Response = content };
        }

        private void AddClientAssertion(Dictionary<string, string> parameters)
        {
            if (_settings.ClientJwtKey == null) return;

            var assertion = GenerateClientAssertion();
            parameters["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            parameters["client_assertion"] = assertion;
        }

        private string GenerateClientAssertion()
        {
            var key = _settings.ClientJwtKey!;
            var tokenUrl = $"{_settings.Url}/realms/{_settings.Realm}/protocol/openid-connect/token";

            // Build RSA key from JWK components
            var rsaParams = new RSAParameters
            {
                Modulus = Base64UrlDecode(key.N),
                Exponent = Base64UrlDecode(key.E),
                D = Base64UrlDecode(key.D),
                P = Base64UrlDecode(key.P),
                Q = Base64UrlDecode(key.Q),
                DP = Base64UrlDecode(key.DP),
                DQ = Base64UrlDecode(key.DQ),
                InverseQ = Base64UrlDecode(key.QI)
            };

            using var rsa = RSA.Create();
            rsa.ImportParameters(rsaParams);

            // JWT header
            var header = JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = key.Kid });
            var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));

            // JWT payload
            var now = DateTimeOffset.UtcNow;
            var payload = JsonSerializer.Serialize(new
            {
                iss = _settings.ClientId,
                sub = _settings.ClientId,
                aud = tokenUrl,
                jti = Guid.NewGuid().ToString(),
                exp = now.AddMinutes(5).ToUnixTimeSeconds(),
                iat = now.ToUnixTimeSeconds()
            });
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));

            // Sign
            var dataToSign = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
            var signature = rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signatureB64 = Base64UrlEncode(signature);

            return $"{headerB64}.{payloadB64}.{signatureB64}";
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
        
    
    }

    public class KeycloakUser
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class KeycloakUserInfo
    {
        public string Sub { get; set; } = "";
        public string? Email { get; set; }
        public string? PreferredUsername { get; set; }
        public string? Name { get; set; }
    }

    public class KeycloakToken
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = "Bearer";
    }

    public class KeycloakGroup
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }
    }

    public class TokenResponse
    {
        public Boolean Success { get; set; }
        public KeycloakTokenResponse? Response { get; set; }
        public string? Error { get; set; }
    }



    public class KeycloakTokenResponse
    {
        public string Access_Token { get; set; } = string.Empty;
        public string Refresh_Token { get; set; } = string.Empty;
        public string Id_Token { get; set; } = string.Empty;
        public int Expires_In { get; set; }
        public string Token_Type { get; set; } = string.Empty;
    }
}