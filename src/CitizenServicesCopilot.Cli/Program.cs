using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Minimal citizen-workflow CLI: authenticates against the running API, submits
// a question through the citizen-response path, then polls the run trace until
// it reaches a terminal state and prints it. Exercises auth (Checkpoint D),
// the background workflow (D1-D3) and the trace endpoint (D4) end-to-end.
//
// Usage: dotnet run --project src/CitizenServicesCopilot.Cli -- [baseUrl] [userId] [question]
//   baseUrl  API base URL                  (default http://localhost:5177)
//   userId   citizen id to log in and ask  (default "cli-user")
//   question question text                 (prompted on stdin if omitted)

const string defaultBaseUrl = "http://localhost:5177";
const string defaultUserId = "cli-user";

var baseUrl = Arg(args, 0, defaultBaseUrl).TrimEnd('/');
var userId = Arg(args, 1, defaultUserId);
var question = Arg(args, 2) ?? await ReadQuestionAsync();

using var http = new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };

// 1. Authenticate (POST /api/auth/login, role Citizen).
string token;
using (var login = await http.PostAsync(
           "/api/auth/login",
           new StringContent(JsonSerializer.Serialize(new { userId, role = "Citizen" }),
               Encoding.UTF8, "application/json")))
{
    var body = await login.Content.ReadAsStringAsync();
    if (!login.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"login failed: {(int)login.StatusCode} {body}");
        return 1;
    }

    token = JsonDocument.Parse(body).RootElement.GetProperty("token").GetString()!;
}

// 2. Submit the question with a fresh correlation id.
var correlationId = Guid.NewGuid();
Console.WriteLine($"submitting run with correlation id {correlationId}");

using (var submit = await http.SendAsync(Submit()))
{
    var body = await submit.Content.ReadAsStringAsync();
    if (!submit.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"submit failed: {(int)submit.StatusCode} {body}");
        return 1;
    }

    var runId = JsonDocument.Parse(body).RootElement.GetProperty("runId").GetString()!;

    // 3. Poll the trace until the run reaches a terminal state.
    for (var attempt = 0; attempt < 120; attempt++)
    {
        using var trace = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/trace")
        {
            Headers = { Authorization = Bearer(token) }
        };

        using var traceResp = await http.SendAsync(trace);
        var traceBody = await traceResp.Content.ReadAsStringAsync();

        if (traceResp.IsSuccessStatusCode && TryStatus(traceBody, out var status) && IsTerminal(status))
        {
            Console.WriteLine();
            Console.WriteLine(FormatJson(traceBody));
            return status is "Approved" or "Rejected" ? 0 : 1;
        }

        await Task.Delay(500);
    }
}

Console.Error.WriteLine("run did not reach a terminal state within the timeout");
return 1;

HttpRequestMessage Submit()
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/workflows/citizen-response")
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { question }), Encoding.UTF8, "application/json"),
        Headers = { Authorization = Bearer(token) }
    };
    request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());
    return request;
}

static string Arg(string[] args, int index, string? fallback = null)
    => index < args.Length && !string.IsNullOrWhiteSpace(args[index]) ? args[index] : fallback ?? string.Empty;

static AuthenticationHeaderValue Bearer(string token) => AuthenticationHeaderValue.Parse($"Bearer {token}");

static bool IsTerminal(string? status)
    => status is "Approved" or "Rejected" or "Failed" or "Cancelled";

static bool TryStatus(string json, out string? status)
{
    status = null;
    try
    {
        if (JsonDocument.Parse(json).RootElement.TryGetProperty("status", out var el))
        {
            status = el.GetString();
            return true;
        }
    }
    catch (JsonException)
    {
    }

    return false;
}

static string FormatJson(string json)
    => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, new JsonSerializerOptions { WriteIndented = true });

static async Task<string> ReadQuestionAsync()
{
    Console.Write("question: ");
    var line = await Console.In.ReadLineAsync();
    return string.IsNullOrWhiteSpace(line) ? throw new InvalidOperationException("no question provided") : line;
}