using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NCS.CBT.Services;

public class GradingResult
{
    public bool Pass { get; set; }
    public double Score { get; set; }
    public int Marks { get; set; }
    public string Feedback { get; set; } = string.Empty;
}

public class AIGradingService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<AIGradingService> _logger;

    public AIGradingService(HttpClient http, IConfiguration config, ILogger<AIGradingService> logger)
    {
        _http = http;
        _apiKey = config["Anthropic:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    // Fuzzy mark scale applied to AI correlation score (0–1)
    // ≥ 0.70 → 20 marks | 0.60–0.69 → 15 | 0.50–0.59 → 10 | 0.40–0.49 → 5 | < 0.40 → 1
    public static int CorrelationToMarks(double correlation) => correlation switch
    {
        >= 0.70 => 20,
        >= 0.60 => 15,
        >= 0.50 => 10,
        >= 0.40 => 5,
        _       => 1
    };

    public async Task<GradingResult> GradeAsync(string questionText, string modelAnswer, string studentAnswer)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Anthropic API key not configured — awarding minimum theory marks.");
            return new GradingResult { Pass = true, Score = 1, Marks = 1, Feedback = "AI grading unavailable — minimum marks awarded." };
        }

        var prompt = $"You are grading a theory question in a professional examination.\n\n" +
            $"Question: {questionText}\n" +
            $"Model Answer: {modelAnswer}\n" +
            $"Student's Answer: {studentAnswer}\n\n" +
            "Rate how closely the student's answer matches the meaning of the model answer. " +
            "Score 1.0 = perfect match in meaning, 0.0 = completely wrong or blank. " +
            "Be fair: correct ideas worded differently still score high. Minor inaccuracies score moderately.\n\n" +
            "Respond with ONLY valid JSON, no other text:\n" +
            "{\"correlation\": 0.85, \"feedback\": \"One sentence explanation.\"}";

        var body = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 200,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Anthropic API error {Status}: {Body}", resp.StatusCode, json);
                return Fallback("AI grading error — minimum marks awarded.");
            }

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            var start = text.IndexOf('{');
            var end   = text.LastIndexOf('}');
            if (start < 0 || end < 0)
                return Fallback("Could not parse AI response.");

            using var result = JsonDocument.Parse(text[start..(end + 1)]);
            var root = result.RootElement;

            var correlation = root.TryGetProperty("correlation", out var c) ? c.GetDouble() : 0;
            var feedback    = root.TryGetProperty("feedback",    out var f) ? f.GetString() ?? "" : "";
            var marks       = CorrelationToMarks(correlation);

            return new GradingResult
            {
                Pass     = true,       // always pass — minimum is 1 mark for any attempt
                Score    = correlation,
                Marks    = marks,
                Feedback = $"{feedback} [{marks} mark(s) awarded]"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI grading exception");
            return Fallback("AI grading failed — minimum marks awarded.");
        }
    }

    private static GradingResult Fallback(string feedback) =>
        new() { Pass = true, Score = 0, Marks = 1, Feedback = feedback };
}
