using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;

namespace SmileApi.Infrastructure.AI;

public class OpenAIAnalysisService : IAIAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIAnalysisService> _logger;

    public OpenAIAnalysisService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAIAnalysisService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SmileAnalysisResultDto> AnalyzeAsync(byte[] imageBytes, string? modelSelection = null, Dictionary<string, string>? userApiKeys = null)
    {
        var (provider, modelId, apiUrl, apiKey) = ResolveModel(modelSelection, userApiKeys);
        _logger.LogInformation("Resolved model: provider={Provider}, model={Model}, url={Url}", provider, modelId, apiUrl);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key configured for provider '{provider}'. Please add your API key in Settings.");

        var base64Image = Convert.ToBase64String(imageBytes);
        var overallStopwatch = Stopwatch.StartNew();
        const int maxRetries = 2;

        bool isSmileWithVisibleTeeth = false;
        double gateConfidence = 0;
        string gateReason = "";
        Exception? gateException = null;

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            try
            {
                _logger.LogInformation("[STEP 1/2] Smile detection attempt {Attempt} with model {Model}...", attempt, modelSelection ?? "nvidia");
                (isSmileWithVisibleTeeth, gateConfidence, gateReason) = await RunSmileDetectionAsync(apiKey, base64Image, apiUrl, modelId);
                break;
            }
            catch (HttpRequestException) { throw; }
            catch (Exception ex) when (attempt <= maxRetries)
            {
                gateException = ex;
                _logger.LogWarning(ex, "[STEP 1/2] Gate attempt {Attempt} failed. Retrying...", attempt);
                await Task.Delay(1000 * attempt);
            }
        }

        if (!isSmileWithVisibleTeeth)
        {
            _logger.LogWarning("[STEP 1/2] GATE REJECTED: reason={Reason}", gateReason);
            throw new ArgumentException($"No smile or teeth detected in the image. Please capture or upload a clear dental photo. (AI Reason: {gateReason})", gateException);
        }

        SmileAnalysisResultDto? finalResult = null;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            try
            {
                _logger.LogInformation("[STEP 2/2] Scoring attempt {Attempt} with model {Model}...", attempt, modelSelection ?? "nvidia");
                var scoringResponse = await RunDentalScoringAsync(apiKey, base64Image, apiUrl, modelId);
                var tokensUsed = ExtractTokensUsed(scoringResponse);
                overallStopwatch.Stop();
                finalResult = ParseValidResponse(scoringResponse, tokensUsed, overallStopwatch.ElapsedMilliseconds);
                break;
            }
            catch (HttpRequestException) { throw; }
            catch (Exception ex) when (ex.GetType().Name == "BrokenCircuitException")
            {
                _logger.LogWarning("[CIRCUIT TRIPPED] Primary LLM is down. Executing instant failover to GPT-4o...");
                var fallbackApiKey = _configuration["OPENROUTER_API_KEY"];
                if (string.IsNullOrEmpty(fallbackApiKey)) throw new InvalidOperationException("Failover triggered, but OPENROUTER_API_KEY is missing.");
                var scoringResponse = await RunDentalScoringAsync(fallbackApiKey, base64Image, "https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4o-mini");
                var tokensUsed = ExtractTokensUsed(scoringResponse);
                overallStopwatch.Stop();
                finalResult = ParseValidResponse(scoringResponse, tokensUsed, overallStopwatch.ElapsedMilliseconds);
                break;
            }
            catch (Exception ex) when (attempt <= maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "[STEP 2/2] Scoring attempt {Attempt} failed. Retrying...", attempt);
                await Task.Delay(1000 * attempt);
                overallStopwatch.Restart();
            }
        }

        if (finalResult == null)
        {
            _logger.LogError(lastException, "[STEP 2/2] All scoring attempts failed.");
            throw new ArgumentException("No smile or teeth detected in the image. Please capture or upload a clear dental photo.", lastException);
        }

        return finalResult;
    }

    private async Task<(bool IsSmileWithVisibleTeeth, double Confidence, string Reason)> RunSmileDetectionAsync(string apiKey, string base64Image, string apiUrl, string modelName)
    {
        var gateBody = new
        {
            model = modelName,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = @"You are a strict visual pre-screening AI. Your ONLY job is to determine if an image contains human teeth that are clearly visible and prominent enough for clinical evaluation.

RULES:
1. Output ONLY raw JSON. No markdown, no commentary.
2. You must return exactly this JSON structure, ensuring 'step_by_step_analysis' comes FIRST:
{
  ""step_by_step_analysis"": ""Describe the state of the mouth (open/closed) and if teeth are visible"",
  ""isSmileWithVisibleTeeth"": true/false,
  ""confidence"": 0.0-1.0,
  ""reason"": ""one sentence summary""
}

DECISION CRITERIA:
- isSmileWithVisibleTeeth = TRUE ONLY IF: The image shows a person clearly smiling with their TEETH EXPOSED, and the teeth are large/clear enough in the photo to evaluate their alignment, whiteness, and gum health.
- isSmileWithVisibleTeeth = FALSE IF: The mouth is closed, teeth are not visible, or it is a non-human image.

Output ONLY the JSON. Nothing else."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Are human teeth clearly visible in this image? Return only the JSON." },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                    }
                }
            },
            max_tokens = 100,
            temperature = 0.0,
            seed = 42
        };

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(gateBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (apiUrl.Contains("openrouter"))
        {
            request.Headers.Add("HTTP-Referer", "https://smileintelligence.app");
            request.Headers.Add("X-Title", "SmileAI");
        }

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Smile detection call failed: {StatusCode} {Response}", response.StatusCode, content);
            throw new HttpRequestException($"AI smile detection API error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(content);
        var rawGateJson = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        try
        {
            var sanitized = SanitizePythonJson(rawGateJson.Trim());
            int firstBrace = sanitized.IndexOf('{');
            int lastBrace = sanitized.LastIndexOf('}');
            if (firstBrace != -1 && lastBrace > firstBrace)
                sanitized = sanitized.Substring(firstBrace, lastBrace - firstBrace + 1);

            using var gateDoc = JsonDocument.Parse(sanitized);
            var root = gateDoc.RootElement;
            bool isVisible = false;
            if (root.TryGetProperty("isSmileWithVisibleTeeth", out var visibleProp) || root.TryGetProperty("IsSmileWithVisibleTeeth", out visibleProp))
            {
                if (visibleProp.ValueKind == JsonValueKind.True || visibleProp.ValueKind == JsonValueKind.False)
                    isVisible = visibleProp.GetBoolean();
                else if (visibleProp.ValueKind == JsonValueKind.String)
                    isVisible = visibleProp.GetString()?.ToLowerInvariant() == "true";
            }
            double confidence = 0.0;
            if (root.TryGetProperty("confidence", out var confProp) || root.TryGetProperty("Confidence", out confProp))
            {
                if (confProp.ValueKind == JsonValueKind.Number) confidence = confProp.GetDouble();
                else if (confProp.ValueKind == JsonValueKind.String) double.TryParse(confProp.GetString(), out confidence);
            }
            string reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() ?? "" : "";
            return (isVisible, confidence, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[STEP 1/2] Failed to parse gate JSON. Rejecting image.");
            throw new ArgumentException("No smile or teeth detected in the image. Please capture or upload a clear dental photo.");
        }
    }

    private async Task<string> RunDentalScoringAsync(string apiKey, string base64Image, string apiUrl, string modelName)
    {
        var scoringBody = new
        {
            model = modelName,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = @"You are an expert cosmetic dental AI. Your job is to score the visible teeth in the provided image.
As long as human teeth are visible, you MUST score them. Set IsDentalImage to true. ONLY set IsDentalImage to false if there are genuinely NO TEETH visible.

RULES:
1. Output ONLY raw JSON. No markdown, no code fences.
2. SCORING RANGE: Use the FULL 0-100 range.
3. Required JSON fields:
- Reasoning: string (1-2 sentences)
- IsDentalImage: boolean
- AlignmentScore: integer 0-100
- GumHealthScore: integer 0-100
- WhitenessScore: integer 0-100
- SymmetryScore: integer 0-100
- PlaqueRiskLevel: exactly ""low"" or ""medium"" or ""high""
- AiSelfConfidence: decimal 0.0-1.0
- CarePlanActions: array of 3-5 objects with: Category, Title, Description, Impact"
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Score the teeth visible in this dental image. Output only the raw JSON." },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                    }
                }
            },
            max_tokens = 600,
            temperature = 0.0,
            seed = 42
        };

        var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(scoringBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (apiUrl.Contains("openrouter"))
        {
            request.Headers.Add("HTTP-Referer", "https://smileintelligence.app");
            request.Headers.Add("X-Title", "SmileAI");
        }

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Dental scoring call failed: {StatusCode} {Response}", response.StatusCode, content);
            throw new HttpRequestException($"AI scoring API error: {response.StatusCode}");
        }
        return content;
    }

    private (string Provider, string ModelId, string ApiUrl, string? ApiKey) ResolveModel(string? modelSelection, Dictionary<string, string>? userApiKeys)
    {
        string provider;
        string modelId;
        if (string.IsNullOrWhiteSpace(modelSelection))
        {
            provider = "nvidia";
            modelId = "meta/llama-3.2-11b-vision-instruct";
        }
        else if (modelSelection == "gpt4o")
        {
            provider = "openrouter";
            modelId = "openai/gpt-4o-mini";
        }
        else if (modelSelection == "nvidia")
        {
            provider = "nvidia";
            modelId = "meta/llama-3.2-11b-vision-instruct";
        }
        else
        {
            var parts = modelSelection.Split(':', 2);
            provider = parts[0];
            modelId = parts.Length > 1 ? parts[1] : "";
        }

        string apiUrl = provider switch
        {
            "nvidia" => "https://integrate.api.nvidia.com/v1/chat/completions",
            "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
            "openai" => "https://api.openai.com/v1/chat/completions",
            "anthropic" => "https://api.anthropic.com/v1/messages",
            "google" => $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent",
            _ => throw new ArgumentException($"Unknown AI provider: {provider}")
        };

        string? apiKey = null;
        if (userApiKeys != null && userApiKeys.TryGetValue(provider, out var userKey))
            apiKey = userKey;
        else
        {
            string configKeyLabel = provider switch
            {
                "nvidia" => "NVIDIA_API_KEY",
                "openrouter" => "OPENROUTER_API_KEY",
                "openai" => "OPENAI_API_KEY",
                "anthropic" => "ANTHROPIC_API_KEY",
                "google" => "GOOGLE_API_KEY",
                _ => ""
            };
            if (!string.IsNullOrEmpty(configKeyLabel))
                apiKey = _configuration[configKeyLabel];
        }

        return (provider, modelId, apiUrl, apiKey);
    }

    private SmileAnalysisResultDto ParseValidResponse(string jsonResponse, int tokensUsed, long processingTimeMs)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            var responseJsonStr = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(responseJsonStr))
                throw new InvalidOperationException("Response content from AI is empty.");

            responseJsonStr = responseJsonStr.Trim();
            if (responseJsonStr.Contains("```"))
            {
                var firstNewline = responseJsonStr.IndexOf('\n', responseJsonStr.IndexOf("```"));
                var lastFence = responseJsonStr.LastIndexOf("```");
                if (firstNewline != -1 && lastFence != -1 && lastFence > firstNewline)
                    responseJsonStr = responseJsonStr.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            int firstBrace = responseJsonStr.IndexOf('{');
            int lastBrace = responseJsonStr.LastIndexOf('}');
            if (firstBrace != -1 && lastBrace >= firstBrace)
                responseJsonStr = responseJsonStr.Substring(firstBrace, lastBrace - firstBrace + 1);

            responseJsonStr = SanitizePythonJson(responseJsonStr);
            responseJsonStr = Regex.Replace(responseJsonStr, @"""IsDentalImage""\s*:\s*""true""", @"""IsDentalImage"": true");
            responseJsonStr = Regex.Replace(responseJsonStr, @"""IsDentalImage""\s*:\s*""false""", @"""IsDentalImage"": false");

            var jsonOpts = new JsonDocumentOptions { AllowTrailingCommas = true };
            using var rawJsonDoc = JsonDocument.Parse(responseJsonStr, jsonOpts);
            if (rawJsonDoc.RootElement.TryGetProperty("IsDentalImage", out var isDentalElement) ||
                rawJsonDoc.RootElement.TryGetProperty("isDentalImage", out isDentalElement))
            {
                bool isDental = isDentalElement.ValueKind == JsonValueKind.True || isDentalElement.ValueKind == JsonValueKind.False
                    ? isDentalElement.GetBoolean()
                    : isDentalElement.GetString()?.Trim().ToLowerInvariant() == "true";
                if (!isDental)
                {
                    string rejectReason = rawJsonDoc.RootElement.TryGetProperty("Reasoning", out var reasonElement) ? reasonElement.GetString() ?? "Unknown" : "Unknown";
                    throw new ArgumentException($"No smile or teeth detected in the image. Please capture or upload a clear dental photo. (Step 2 Parse Reason: {rejectReason})");
                }
            }
            else
                throw new ArgumentException("No smile or teeth detected in the image. Please capture or upload a clear dental photo. (Missing IsDentalImage property)");

            var result = JsonSerializer.Deserialize<SmileAnalysisResultDto>(responseJsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
                throw new InvalidOperationException("Failed to deserialize AI JSON into SmileAnalysisResultDto.");

            result.TokensUsed = tokensUsed;
            result.ProcessingTimeMs = processingTimeMs;

            if (!result.IsDentalImage)
                throw new ArgumentException($"No smile or teeth detected in the image. Please capture or upload a clear dental photo. (Step 2 Reason: {result.Reasoning})");

            ValidateScore(result.AlignmentScore, nameof(result.AlignmentScore));
            ValidateScore(result.GumHealthScore, nameof(result.GumHealthScore));
            ValidateScore(result.WhitenessScore, nameof(result.WhitenessScore));
            ValidateScore(result.SymmetryScore, nameof(result.SymmetryScore));

            if (result.PlaqueRiskLevel != "low" && result.PlaqueRiskLevel != "medium" && result.PlaqueRiskLevel != "high")
                throw new ArgumentException($"Invalid PlaqueRiskLevel: {result.PlaqueRiskLevel}. Expected low, medium, or high.");

            if (result.AiSelfConfidence < 0 || result.AiSelfConfidence > 1.0)
                throw new ArgumentException("AiSelfConfidence must be between 0.0 and 1.0.");

            if (result.AiSelfConfidence < 0.20)
            {
                _logger.LogWarning("AI returned critically low confidence ({Confidence}). Rejecting as non-dental.", result.AiSelfConfidence);
                throw new ArgumentException("No smile or teeth detected in the image. Please capture or upload a clear dental photo.");
            }

            return result;
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI JSON response.");
            throw new ArgumentException("No smile or teeth detected in the image. Please capture or upload a clear dental photo.", ex);
        }
    }

    private static void ValidateScore(int score, string name)
    {
        if (score < 0 || score > 100)
            throw new ArgumentException($"{name} must be between 0 and 100.");
    }

    private static int ExtractTokensUsed(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.TryGetProperty("usage", out var usageStr) && usageStr.TryGetProperty("total_tokens", out var totalTokens))
                return totalTokens.GetInt32();
        }
        catch { }
        return 0;
    }

    private static string SanitizePythonJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        input = Regex.Replace(input, @"\bTrue\b", "true");
        input = Regex.Replace(input, @"\bFalse\b", "false");
        input = Regex.Replace(input, @"\bNone\b", "null");
        if (!input.Contains('\'')) return input;

        var sb = new StringBuilder(input.Length);
        bool inSingleQuotedString = false;
        bool inDoubleQuotedString = false;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            char prev = i > 0 ? input[i - 1] : '\0';
            char next = i < input.Length - 1 ? input[i + 1] : '\0';

            if (c == '\'' && !inDoubleQuotedString && prev != '\\')
            {
                if (inSingleQuotedString)
                {
                    bool isClosingQuote = (next == '\0') || next == ':' || next == ',' || next == '}' || next == ']' || next == ' ' || next == '\t' || next == '\n' || next == '\r';
                    if (isClosingQuote) { inSingleQuotedString = false; sb.Append('"'); }
                    else sb.Append('\'');
                }
                else { inSingleQuotedString = true; sb.Append('"'); }
            }
            else if (c == '"' && !inSingleQuotedString && prev != '\\')
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                sb.Append(c);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
