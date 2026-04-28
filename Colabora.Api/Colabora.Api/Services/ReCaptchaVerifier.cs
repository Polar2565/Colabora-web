using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Colabora.Api.Models;

namespace Colabora.Api.Services
{
    public interface IReCaptchaVerifier
    {
        Task<bool> VerifyAsync(string token, string? remoteIp = null);
    }

    public class ReCaptchaVerifier : IReCaptchaVerifier
    {
        private readonly HttpClient _http;
        private readonly GoogleReCaptchaSettings _settings;

        public ReCaptchaVerifier(HttpClient http, IOptions<GoogleReCaptchaSettings> settings)
        {
            _http = http;
            _settings = settings.Value;
        }

        public async Task<bool> VerifyAsync(string token, string? remoteIp = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            using var form = new MultipartFormDataContent
            {
                { new StringContent(_settings.SecretKey), "secret" },
                { new StringContent(token), "response" }
            };

            if (!string.IsNullOrWhiteSpace(remoteIp))
                form.Add(new StringContent(remoteIp), "remoteip");

            var resp = await _http.PostAsync("https://www.google.com/recaptcha/api/siteverify", form);
            if (!resp.IsSuccessStatusCode) return false;

            var payload = await resp.Content.ReadFromJsonAsync<ReCaptchaVerifyResponse>();
            return payload?.Success == true;
        }
    }
}
