using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Rise.App;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string name;
    private readonly CancellationTokenSource stop = new();
    public bool IsPrimary { get; }
    public static string UserKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..12];
    public SingleInstance(string? isolated)
    {
        name = "RISE-" + UserKey + (isolated == null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(isolated)))[..8]);
        mutex = new Mutex(true, @"Local\" + name, out bool created);
        IsPrimary = created;
    }
    public void Signal()
    {
        for (int i = 0; i < 3; i++)
        {
            try { using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out); pipe.Connect(1500); pipe.WriteByte(1); return; }
            catch (Exception ex) when (ex is TimeoutException or IOException) { }
        }
    }
    public void Listen(Action wake) => _ = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                var data = new byte[1];
                if (await pipe.ReadAsync(data, stop.Token) > 0) wake();
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    });
    public void Dispose() { stop.Cancel(); if (IsPrimary) mutex.ReleaseMutex(); mutex.Dispose(); stop.Dispose(); }
}
