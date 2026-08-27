using System.Net.Http.Headers;
using System.Text.Json;

namespace NCS.CBT.Services;

public class FaceVerificationService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FaceVerificationService> _logger;

    public FaceVerificationService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<FaceVerificationService> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<(bool match, int confidence, string message)> VerifyAsync(string passportPath, Stream livePhotoStream)
    {
        var endpoint = _config["AzureFace:Endpoint"]?.TrimEnd('/');
        var key      = _config["AzureFace:Key"];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("Azure Face API not configured — skipping comparison.");
            return (true, 0, "Verification skipped (not configured).");
        }

        try
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", key);

            // Step 1: detect face in passport
            var passportBytes = await File.ReadAllBytesAsync(passportPath);
            var passportFaceId = await DetectFaceIdAsync(client, endpoint, passportBytes);
            if (passportFaceId == null)
                return (false, 0, "No face detected in your passport photo. Please contact the administrator.");

            // Step 2: detect face in live photo
            using var ms = new MemoryStream();
            await livePhotoStream.CopyToAsync(ms);
            var liveFaceId = await DetectFaceIdAsync(client, endpoint, ms.ToArray());
            if (liveFaceId == null)
                return (false, 0, "No face detected in camera photo. Ensure your face is clearly visible and well-lit, then try again.");

            // Step 3: verify
            var verifyBody = JsonSerializer.Serialize(new { faceId1 = passportFaceId, faceId2 = liveFaceId });
            var verifyResp = await client.PostAsync(
                $"{endpoint}/face/v1.0/verify",
                new StringContent(verifyBody, System.Text.Encoding.UTF8, "application/json"));

            verifyResp.EnsureSuccessStatusCode();
            var verifyJson = await verifyResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(verifyJson);
            var root = doc.RootElement;

            bool isIdentical = root.GetProperty("isIdentical").GetBoolean();
            double confidence = root.GetProperty("confidence").GetDouble();
            int pct = (int)Math.Round(confidence * 100);

            if (isIdentical)
                return (true, pct, $"Identity verified ({pct}% match).");
            else
                return (false, pct, $"Face does not match ({pct}%). Ensure good lighting, remove glasses/hat, and face the camera directly, then try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Face API error");
            // Fail open — don't block students if the API is down
            return (true, 0, "Verification service unavailable — proceeding.");
        }
    }

    private static async Task<string?> DetectFaceIdAsync(HttpClient client, string endpoint, byte[] imageBytes)
    {
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PostAsync(
            $"{endpoint}/face/v1.0/detect?returnFaceId=true&detectionModel=detection_03",
            content);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        if (arr.GetArrayLength() == 0) return null;

        return arr[0].GetProperty("faceId").GetString();
    }
}
