using System.Net;
using System.Net.Sockets;

namespace BudgetyTzar.Tests;

internal static class ProjectionTestHelpers
{
    public static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Condition was not met before the timeout.");
    }

    public static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
