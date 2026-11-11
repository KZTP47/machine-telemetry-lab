using System.Globalization;
using MachineTelemetry;

var builder = WebApplication.CreateBuilder(args);
var threshold = Reading.AlarmThresholdC;
if (builder.Configuration["ALARM_THRESHOLD_C"] is { } setting &&
    (!double.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold) ||
     !double.IsFinite(threshold) || threshold is < -100 or > 500))
{
    throw new InvalidOperationException("ALARM_THRESHOLD_C must be between -100 and 500.");
}
ReadingSubscriber.Register(builder, "alerts", threshold);

var app = builder.Build();
ReadingSubscriber.MapReadiness(app);
app.MapGet("/alerts", (ReadingStore store) =>
    store.Latest().Select(entry => new TemperatureAlert(entry.Reading, entry.ThresholdC ?? Reading.AlarmThresholdC)));
app.Run();
