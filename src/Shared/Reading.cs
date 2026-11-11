using System.Text.Json;
using System.Text.Json.Serialization;

namespace MachineTelemetry;

public sealed record Reading(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string MachineId,
    [property: JsonRequired] DateTimeOffset RecordedAt,
    [property: JsonRequired] double TemperatureC)
{
    public const string TopicFilter = "machines/+/telemetry";
    public const double AlarmThresholdC = 75;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [JsonIgnore]
    public string Topic => $"machines/{MachineId}/telemetry";

    public static Reading? Parse(string topic, string payload)
    {
        try
        {
            var reading = JsonSerializer.Deserialize<Reading>(payload, Json);
            if (reading is null || reading.Id == Guid.Empty ||
                reading.MachineId is not { Length: > 0 and <= 32 } ||
                !reading.MachineId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
                reading.Topic != topic || reading.RecordedAt == default ||
                !double.IsFinite(reading.TemperatureC) || reading.TemperatureC is < -100 or > 500)
            {
                return null;
            }

            return reading;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record TemperatureAlert(Reading Reading, double ThresholdC);
