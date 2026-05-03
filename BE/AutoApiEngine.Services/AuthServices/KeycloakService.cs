using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoApiEngine.Services.AuthServices
{
    public class KeycloakService
    {
        private readonly HttpClient _httpClient;
        private readonly string _realm;
        private readonly string _baseUrl;
        private readonly string _clientId = "api-engine-service";
        private readonly string _clientSecret = "api-engine-service-secret";
        private string? _adminToken;
        private DateTime _adminTokenExpiry;

        public KeycloakService(
            string baseUrl = "http://localhost:8081", 
            string realm = "ApiEngineRealm")
        {
            _httpClient = new HttpClient();
            _realm = realm;
            _baseUrl = baseUrl;
        }

        private async Task EnsureAdminToken()
        {
            if (!string.IsNullOrEmpty(_adminToken) && _adminTokenExpiry > DateTime.UtcNow)
                return;

            var tokenRequest = new Dictionary<string, string>
            {
                {"grant_type", "client_credentials"},
                {"client_id", _clientId},
                {"client_secret", _clientSecret}
            };

            var content = new FormUrlEncodedContent(tokenRequest);
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/realms/{_realm}/protocol/openid-connect/token",
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/admin/realms/{_realm}/users")
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

            return $"{_baseUrl}/realms/{_realm}/protocol/openid-connect/auth?{string.Join("&", parameters)}";
        }
       

        public async Task<KeycloakUserInfo> GetUserInfo(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/realms/{_realm}/protocol/openid-connect/userinfo");
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
                ? $"{_baseUrl}/admin/realms/{_realm}/groups"
                : $"{_baseUrl}/admin/realms/{_realm}/groups/{parentId}/children";
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

            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/admin/realms/{_realm}/groups");
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

            var url = $"{_baseUrl}/admin/realms/{_realm}/users/{userId}/groups/{groupId}";
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

            var url = $"{_baseUrl}/admin/realms/{_realm}/users/{userId}/groups/{groupId}";
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

            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/admin/realms/{_realm}/users/{userId}/groups");
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
                $"{_baseUrl}/admin/realms/{_realm}/users?username={username}");
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
                $"{_baseUrl}/admin/realms/{_realm}/users/{userId}/send-verify-email");
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to send verification email: {error}");
            }
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
}