using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvalonClient.Services;

public sealed class ClientSession : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public bool IsConnected => _client is { Connected: true };

    public event Action<string>? LineReceived;
    public event Action<string>? Info; // outgoing log + internal info
    public event Action? Disconnected;

    public async Task ConnectAsync(string host, int port, string nick)
    {
        if (_client != null) throw new InvalidOperationException("Already connected.");

        _cts = new CancellationTokenSource();
        _client = new TcpClient();

        Info?.Invoke($"Connecting to {host}:{port}...");
        await _client.ConnectAsync(host, port);

        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };

        Info?.Invoke("Connected.");
        await SendLineAsync($"HELLO {nick}");

        _ = Task.Run(() => RxLoopAsync(_cts.Token));
    }

    public async Task SendLineAsync(string line)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected.");
        Info?.Invoke($"> {line}");
        await _writer.WriteLineAsync(line);
    }

    private async Task RxLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync();
                if (line == null) break;

                LineReceived?.Invoke(line);
            }
        }
        catch (Exception ex)
        {
            Info?.Invoke($"RX error: {ex.Message}");
        }
        finally
        {
            DisconnectInternal("Disconnected.");
        }
    }

    public void Disconnect()
    {
        DisconnectInternal("Disconnected.");
    }

    private void DisconnectInternal(string msg)
    {
        try { _cts?.Cancel(); } catch { }

        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }

        _reader = null;
        _writer = null;
        _stream = null;
        _client = null;

        try { _cts?.Dispose(); } catch { }
        _cts = null;

        Info?.Invoke(msg);
        Disconnected?.Invoke();
    }

    public void Dispose() => Disconnect();
}
