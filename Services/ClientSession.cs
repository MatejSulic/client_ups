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
    if (_stream == null)
    {
        DisconnectInternal("Disconnected.");
        return;
    }

    var buf = new byte[4096];
    var sb = new StringBuilder();

    try
    {
        while (!ct.IsCancellationRequested)
        {
            int n = await _stream.ReadAsync(buf, 0, buf.Length, ct);
            if (n == 0)
            {
                Info?.Invoke("RX ended: EOF (server closed connection).");
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buf, 0, n));

            while (true)
            {
                var s = sb.ToString();
                int nl = s.IndexOf('\n');
                if (nl < 0) break;

                var line = s.Substring(0, nl);
                // consume
                sb.Clear();
                sb.Append(s.Substring(nl + 1));

                line = line.TrimEnd('\r');
                if (line.Length > 0)
                    LineReceived?.Invoke(line);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // normal
    }
    catch (Exception ex)
    {
        Info?.Invoke($"RX error: {ex.GetType().Name}: {ex.Message}");
        if (ex is SocketException se)
            Info?.Invoke($"SocketErrorCode: {se.SocketErrorCode}");
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
