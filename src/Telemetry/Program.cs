using MachineTelemetry;

var builder = WebApplication.CreateBuilder(args);
ReadingSubscriber.Register(builder, "telemetry");

var app = builder.Build();
ReadingSubscriber.MapReadiness(app);
app.MapGet("/readings", (ReadingStore store) => store.Latest().Select(entry => entry.Reading));
app.Run();
