using System.Text;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    // Reused across invocations while the Lambda container stays warm.
    private static readonly HttpClient Client = new();

    /// <summary>
    /// Receives GitHub's issue webhook JSON (via API Gateway, non-proxy integration),
    /// reads issue.html_url and posts "Issue Created: <link>" to Slack.
    /// The Slack URL comes ONLY from the SLACK_URL environment variable.
    /// </summary>
    public string FunctionHandler(object input, ILambdaContext context)
    {
        context.Logger.LogLine($"Received: {input}");

        var json = JObject.Parse(input.ToString()!);

        // GitHub's initial "ping" (and any non-issue payload) has no "issue" object.
        string? issueUrl = json["issue"]?["html_url"]?.ToString();
        if (string.IsNullOrEmpty(issueUrl))
        {
            context.Logger.LogLine("No issue.html_url in payload (probably a ping); nothing to post.");
            return "ignored: no issue in payload";
        }

        string? slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        if (string.IsNullOrEmpty(slackUrl))
        {
            throw new InvalidOperationException("SLACK_URL environment variable is not set.");
        }

        string payload = JsonConvert.SerializeObject(new { text = $"Issue Created: {issueUrl}" });

        var webRequest = new HttpRequestMessage(HttpMethod.Post, slackUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = Client.Send(webRequest);
        using var reader = new StreamReader(response.Content.ReadAsStream());
        string body = reader.ReadToEnd();

        // Slack answers "ok" on success; other bodies (invalid_payload, no_service,
        // channel_not_found) mean the SLACK_URL value is wrong or revoked.
        context.Logger.LogLine($"Slack responded {(int)response.StatusCode}: {body}");
        return body;
    }
}