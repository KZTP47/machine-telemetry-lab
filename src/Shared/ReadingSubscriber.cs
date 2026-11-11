using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MachineTelemetry;

public sealed class ReadingSubscriber(
    string host, string clientId, ReadingStore store, double? thresholdC,
    ILogger<ReadingSubscriber> logger) : BackgroundService
{
    private volatile bool ready;

    public static void Register(WebApplicationBuilder builder, string name, double? thresholdC = null)
    {
        var host = builder.Configuration["MQTT_HOST"] ?? "localhost";
        var database = builder.Configuration["DATABASE_PATH"] ?? $"data/{name}.db";
        builder.Services.AddSingleton(_ => new ReadingStore(database));
        builder.Services.AddSingleton(services => new ReadingSubscriber(
            host, $"machine-lab-{name}", services.GetRequiredService<ReadingStore>(), thresholdC,
            services.GetRequiredService<ILogger<ReadingSubscriber>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<ReadingSubscriber>());
    }

    public static void MapReadiness(WebApplication app)
    {
        app.MapGet("/health/ready", (ReadingSubscriber subscriber) =>
            subscriber.ready ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        var connection = new MqttClientOptionsBuilder()
            .WithTcpServer(host)
            .WithClientId(clientId)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession(false)
            .Build();
        var subscription = factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic(Reading.TopicFilter)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = factory.CreateMqttClient();
            var reconnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DisconnectedAsync += _ =>
            {
                ready = false;
                reconnect.TrySetResult();
                return Task.CompletedTask;
            };
            client.ApplicationMessageReceivedAsync += async delivery =>
            {
                delivery.AutoAcknowledge = false;
                if (reconnect.Task.IsCompleted)
                {
                    return;
                }
                try
                {
                    var message = delivery.ApplicationMessage;
                    var reading = Reading.Parse(message.Topic, message.ConvertPayloadToString());
                    if (reading is null)
                    {
                        logger.LogWarning("Discarded an invalid machine reading");
                    }
                    else if (thresholdC is null || reading.TemperatureC > thresholdC.Value)
                    {
                        store.Save(reading, thresholdC);
                    }
                    await delivery.AcknowledgeAsync(stoppingToken);
                }
                catch (SqliteException error)
                {
                    logger.LogError(error, "Could not persist a reading. Reconnecting to retry delivery");
                    reconnect.TrySetResult();
                }
            };

            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(connection, connectTimeout.Token);
                var result = await client.SubscribeAsync(subscription, connectTimeout.Token);
                if (result.Items.Any(item => item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS1))
                {
                    throw new InvalidOperationException("The broker did not grant the QoS 1 subscription.");
                }
                ready = !reconnect.Task.IsCompleted;
                logger.LogInformation("Subscribed to machine telemetry");
                await reconnect.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "MQTT connection unavailable. Retrying");
            }
            finally
            {
                ready = false;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
