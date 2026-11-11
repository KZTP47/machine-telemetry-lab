using MachineTelemetry;

var builder = WebApplication.CreateBuilder(args);
ReadingSubscriber.Register(builder, "telemetry", _ => true);

var app = builder.Build();
ReadingSubscriber.MapReadiness(app);
app.MapGet("/readings", (ReadingStore store) => store.Latest());
app.Run();
