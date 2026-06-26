using System.Threading.Channels;

namespace BudgetyTzar.Api.Application.Reporting;

public sealed record ProjectionReadyNotification(
    Guid BudgetId,
    Guid EventId,
    string EventType,
    DateTimeOffset ProjectedAt,
    IReadOnlyList<string> ReadModels);

public sealed class ProjectionNotificationService
{
    private readonly object _gate = new();
    private readonly List<Channel<ProjectionReadyNotification>> _subscribers = [];

    public ChannelReader<ProjectionReadyNotification> Subscribe(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<ProjectionReadyNotification>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        cancellationToken.Register(() => Unsubscribe(channel));
        return channel.Reader;
    }

    public void Publish(ProjectionReadyNotification notification)
    {
        Channel<ProjectionReadyNotification>[] subscribers;
        lock (_gate)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(notification))
            {
                Unsubscribe(subscriber);
            }
        }
    }

    private void Unsubscribe(Channel<ProjectionReadyNotification> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }

        channel.Writer.TryComplete();
    }
}
