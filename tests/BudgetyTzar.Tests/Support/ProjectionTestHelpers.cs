using System.Net;
using System.Net.Sockets;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BudgetyTzar.Tests;

internal static class ProjectionTestHelpers
{
    public static async Task<IContainer> StartRedpandaAsync(int kafkaPort)
    {
        var kafka = new ContainerBuilder("docker.redpanda.com/redpandadata/redpanda:v24.3.7")
            .WithPortBinding(kafkaPort, 19092)
            .WithCommand(
                "redpanda",
                "start",
                "--mode", "dev-container",
                "--smp", "1",
                "--memory", "512M",
                "--overprovisioned",
                "--node-id", "0",
                "--check=false",
                "--kafka-addr", "internal://0.0.0.0:9092,external://0.0.0.0:19092",
                "--advertise-kafka-addr", $"internal://127.0.0.1:9092,external://127.0.0.1:{kafkaPort}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(19092))
            .Build();

        try
        {
            await kafka.StartAsync();
        }
        catch (Exception ex)
        {
            await kafka.DisposeAsync();
            throw new InvalidOperationException(
                "Kafka integration tests require Docker/Testcontainers to start a Redpanda container. "
                + "Start Docker and rerun the test; the API application does not need to be running separately.",
                ex);
        }

        return kafka;
    }

    public static async Task CreateKafkaTopicsAsync(string bootstrapServers)
    {
        var topics = new[]
        {
            "budgetytzar.budgeting.events",
            "budgetytzar.transactions.events",
            "budgetytzar.reporting.events",
            "budgetytzar.reporting.dead-letter-events",
            "budgetytzar.audit.dead-letter-events"
        };

        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        try
        {
            await admin.CreateTopicsAsync(
                topics.Select(x => new TopicSpecification
                {
                    Name = x,
                    NumPartitions = 1,
                    ReplicationFactor = 1
                }),
                new CreateTopicsOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(10),
                    RequestTimeout = TimeSpan.FromSeconds(10)
                });
        }
        catch (CreateTopicsException ex) when (ex.Results.All(x => x.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Kafka integration tests could not create the required Redpanda topics. "
                + "The test owns this setup and should not require the API application to run first.",
                ex);
        }
    }

    public static async Task WaitUntil(
        Func<Task<bool>> condition,
        Func<Task<string>>? describeFailure = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(20));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        var details = describeFailure is null ? null : await describeFailure();
        Assert.Fail(details is null
            ? "Condition was not met before the timeout."
            : $"Condition was not met before the timeout. {details}");
    }

    public static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
