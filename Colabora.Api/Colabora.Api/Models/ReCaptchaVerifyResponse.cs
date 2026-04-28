using System.Text.Json.Serialization;

namespace Colabora.Api.Models
{
    public class ReCaptchaVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("challenge_ts")]
        public string? ChallengeTs { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        // Campos típicos de v3 (por si migras algún día)
        [JsonPropertyName("score")]
        public float? Score { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
