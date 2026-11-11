using MachineTelemetry;

var builder = WebApplication.CreateBuilder(args);
ReadingSubscriber.Register(builder, "alerts", reading => reading.TemperatureC > Reading.AlarmThresholdC);

var app = builder.Build();
ReadingSubscriber.MapReadiness(app);
app.MapGet("/alerts", (ReadingStore store) =>
    store.Latest().Select(reading => new TemperatureAlert(reading, Reading.AlarmThresholdC)));
app.Run();
