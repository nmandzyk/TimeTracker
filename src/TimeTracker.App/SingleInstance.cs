using System.IO.Pipes;

namespace TimeTracker.App;

public static class SingleInstance
{
    private const string PipeName = "TimeTracker.Activate";

    public static Action? ActivateRequested { get; set; }

    // Asks the already-running instance to show its window; returns false if it could not be reached.
    public static bool SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void StartListening(CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    var buffer = new byte[1];
                    if (await server.ReadAsync(buffer, token) > 0)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActivateRequested?.Invoke());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, token);
    }
}
