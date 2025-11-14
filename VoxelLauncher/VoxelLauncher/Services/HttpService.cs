using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoxelLauncher.Services
{
    public static class HttpService
    {
        private static readonly HttpClient _client = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static HttpClient Instance => _client;

        static HttpService()
        {
            _client.Timeout = TimeSpan.FromSeconds(15);
            _client.DefaultRequestHeaders.Add("User-Agent", "VoxelLauncher/1.0");
        }

        public static async Task<T?> GetFromJsonAsync<T>(string requestUri)
        {
            try
            {
                var response = await _client.GetAsync(requestUri);
                if (!response.IsSuccessStatusCode)
                    return default(T);

                var stream = await response.Content.ReadAsStreamAsync();
                return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions);
            }
            catch
            {
                return default(T);
            }
        }
    }
}