using System.Net.Http.Json;
using System.Text.Json;
using MachineTelemetry;
using MQTTnet;
using MQTTnet.Protocol;

var host = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
var telemetryUrl = Environment.GetEnvironmentVariable("TELEMETRY_URL") ?? "http://localhost:5181";
var alertsUrl = Environment.GetEnvironmentVariable("ALERTS_URL") ?? "http://localhost:5182";

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var cancellation = deadline.Token;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
await WaitUntilReady(telemetryUrl);
await WaitUntilReady(alertsUrl);

using var client = new MqttClientFactory().CreateMqttClient();
await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(host).Build(), cancellation);

var now = DateTimeOffset.UtcNow;
var normal = new Reading(Guid.NewGuid(), "pump-01", now, 62);
var boundary = normal with { Id = Guid.NewGuid(), TemperatureC = 75 };
var hot = normal with { Id = Guid.NewGuid(), TemperatureC = 86 };
var mismatched = hot with { Id = Guid.NewGuid(), MachineId = "pump-02" };
var marker = hot with { Id = Guid.NewGuid(), TemperatureC = 81 };
await Publish(normal);
await Publish(boundary);
await Publish(hot);
await Publish(hot);
await Publish(mismatched, hot.Topic);
await Publish(marker);
await client.DisconnectAsync();

Reading[] readings;
TemperatureAlert[] alerts;
do
{
    await Task.Delay(250, cancellation);
    readings = await http.GetFromJsonAsync<Reading[]>(telemetryUrl + "/readings", cancellation) ?? [];
    alerts = await http.GetFromJsonAsync<TemperatureAlert[]>(alertsUrl + "/alerts", cancellation) ?? [];
}
while (!readings.Any(r => r.Id == marker.Id) || !alerts.Any(a => a.Reading.Id == marker.Id));

foreach (var expected in new[] { normal, boundary, hot, marker })
{
    Verify(readings.Count(r => r.Id == expected.Id && r.TemperatureC == expected.TemperatureC) == 1,
        $"Expected exactly one stored reading at {expected.TemperatureC} °C.");
}
foreach (var expected in new[] { hot, marker })
{
    Verify(alerts.Count(a => a.Reading.Id == expected.Id && a.ThresholdC == 75) == 1,
        $"Expected exactly one alert at {expected.TemperatureC} °C.");
}
Verify(!alerts.Any(a => a.Reading.Id == normal.Id || a.Reading.Id == boundary.Id),
    "A reading at or below 75 °C raised an alert.");
Verify(!readings.Any(r => r.Id == mismatched.Id) && !alerts.Any(a => a.Reading.Id == mismatched.Id),
    "A reading with the wrong machine topic was accepted.");

Console.WriteLine("PASS: four readings, two alerts, duplicate ignored, mismatched topic rejected.");
Console.WriteLine($"Follow the 86 °C reading in both APIs using event ID {hot.Id}.");

async Task Publish(Reading reading, string? topic = null)
{
    var result = await client.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic(topic ?? reading.Topic)
        .WithPayload(JsonSerializer.Serialize(reading, Reading.Json))
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build(), cancellation);
    Verify(result.IsSuccess, "The broker did not accept the reading.");
}

async Task WaitUntilReady(string url)
{
    while (true)
    {
        try
        {
            using var response = await http.GetAsync(url + "/health/ready", cancellation);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
            // The service may still be starting.
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // The readiness request timed out before the overall deadline.
        }
        await Task.Delay(500, cancellation);
    }
}

static void Verify(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
