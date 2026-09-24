using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tatkal.Authentication;

namespace BookingService.Admin;

public record DeadLetterMessage(string Queue, string Payload, int RedeliveryCount);

/// <summary>
/// Spec section 16: inspect and retry messages MassTransit has moved to a
/// `&lt;queue&gt;_error` queue after exhausting AddTatkalMessaging's retry
/// policy. Talks directly to the RabbitMQ Management HTTP API (enabled by
/// the `rabbitmq:3.13-management-alpine` image in docker-compose) rather
/// than reinventing a dead-letter store — RabbitMQ already IS one.
///
/// Simplification: "retry" here re-publishes the raw message body to the
/// original queue via the management API's basic-publish endpoint. A
/// production version would deserialize the MassTransit envelope and
/// re-publish through IPublishEndpoint so headers/message type are
/// preserved exactly; this is enough to demonstrate the admin workflow.
/// </summary>
[ApiController]
[Route("api/admin/dead-letters")]
[Authorize(Policy = Policies.AdminOnly)]
public class DeadLetterAdminController(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<DeadLetterAdminController> logger) : ControllerBase
{
    private static readonly string[] KnownErrorQueues =
    [
        "booking-created_error", "payment-succeeded_error", "payment-failed_error"
    ];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeadLetterMessage>>> List(CancellationToken ct)
    {
        var client = ManagementClient();
        var results = new List<DeadLetterMessage>();

        foreach (var queue in KnownErrorQueues)
        {
            try
            {
                var resp = await client.PostAsJsonAsync($"/api/queues/%2F/{queue}/get",
                    new { count = 10, ackmode = "ack_requeue_true", encoding = "auto" }, ct);

                if (!resp.IsSuccessStatusCode) continue;

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var payload = item.GetProperty("payload").GetString() ?? "";
                    var redelivered = item.TryGetProperty("redelivered", out var r) && r.GetBoolean() ? 1 : 0;
                    results.Add(new DeadLetterMessage(queue, payload, redelivered));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read dead-letter queue {Queue} from RabbitMQ management API", queue);
            }
        }

        return Ok(results);
    }

    [HttpPost("{queue}/retry")]
    public async Task<IActionResult> Retry(string queue, CancellationToken ct)
    {
        if (!KnownErrorQueues.Contains(queue)) return NotFound();

        var originalQueue = queue.Replace("_error", "");
        var client = ManagementClient();

        var getResp = await client.PostAsJsonAsync($"/api/queues/%2F/{queue}/get",
            new { count = 1, ackmode = "ack_requeue_false", encoding = "auto" }, ct);
        if (!getResp.IsSuccessStatusCode) return Problem("Could not read from dead-letter queue.");

        var body = await getResp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.EnumerateArray().ToList();
        if (messages.Count == 0) return Ok(new { message = "No messages to retry." });

        var payload = messages[0].GetProperty("payload").GetString() ?? "";
        var publishResp = await client.PostAsync($"/api/exchanges/%2F/{originalQueue}/publish",
            new StringContent(JsonSerializer.Serialize(new
            {
                properties = new { },
                routing_key = originalQueue,
                payload,
                payload_encoding = "string"
            }), Encoding.UTF8, "application/json"), ct);

        return publishResp.IsSuccessStatusCode
            ? Ok(new { message = "Message requeued to original exchange." })
            : Problem("Could not republish message.");
    }

    private HttpClient ManagementClient()
    {
        var client = httpClientFactory.CreateClient();
        var host = config["RabbitMq:Host"] ?? "rabbitmq";
        var user = config["RabbitMq:Username"] ?? "tatkal";
        var pass = config["RabbitMq:Password"] ?? "change_me";
        client.BaseAddress = new Uri($"http://{host}:15672");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}")));
        return client;
    }
}
