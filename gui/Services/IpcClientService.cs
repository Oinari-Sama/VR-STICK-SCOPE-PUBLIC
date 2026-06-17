using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InariKontroller.Models;

namespace InariKontroller.Services;

public class StateUpdatedEventArgs(EngineStateMessage state) : EventArgs
{
    public EngineStateMessage State { get; } = state;
}

public class IpcClientService
{
    public const string PipeName = "InariKontrollerEngine";

    public event EventHandler<StateUpdatedEventArgs>? StateUpdated;
    public event EventHandler<bool>? ConnectionChanged;

    private NamedPipeClientStream? _pipe;
    private BinaryWriter? _writer;
    private CancellationTokenSource? _cts;
    private bool _connected;
    private readonly object _writeLock = new();

    public bool IsConnected => _connected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ConnectLoopAsync(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _pipe?.Dispose();
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(3000, ct);
                _writer = new BinaryWriter(_pipe, Encoding.UTF8, leaveOpen: true);
                SetConnected(true);
                await ReadLoopAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* engine not running */ }
            finally
            {
                SetConnected(false);
                _writer?.Dispose();
                _writer = null;
                _pipe?.Dispose();
                _pipe = null;
            }
            if (!ct.IsCancellationRequested)
                await Task.Delay(2000, ct).ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _pipe?.IsConnected == true)
        {
            byte[] lenBuf = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = await _pipe.ReadAsync(lenBuf.AsMemory(read, 4 - read), ct);
                if (n == 0) return;
                read += n;
            }
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0 || len > 1024 * 1024) continue;

            byte[] data = new byte[len];
            read = 0;
            while (read < len)
            {
                int n = await _pipe.ReadAsync(data.AsMemory(read, len - read), ct);
                if (n == 0) return;
                read += n;
            }
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var msg = JsonSerializer.Deserialize<EngineStateMessage>(data, opts);
                if (msg != null) StateUpdated?.Invoke(this, new StateUpdatedEventArgs(msg));
            }
            catch { }
        }
    }

    public void SendCommand(object command)
    {
        if (_pipe?.IsConnected != true) return;
        try
        {
            var json = JsonSerializer.Serialize(command);
            SendRaw(Encoding.UTF8.GetBytes(json));
        }
        catch { }
    }

    public async Task<bool> WaitForConnectionAsync(TimeSpan timeout)
        => await WaitForConnectionStateAsync(true, timeout).ConfigureAwait(false);

    public async Task<bool> WaitForDisconnectionAsync(TimeSpan timeout)
        => await WaitForConnectionStateAsync(false, timeout).ConfigureAwait(false);

    private async Task<bool> WaitForConnectionStateAsync(bool connected, TimeSpan timeout)
    {
        if (_connected == connected) return true;

        DateTime until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (_connected == connected) return true;
            await Task.Delay(100).ConfigureAwait(false);
        }

        return _connected == connected;
    }

    // LUT をエンジン形式 (rs/ao/xc/yc) で直接送信
    public void SendLut(string side, CorrectionLUT lut)
    {
        if (_pipe?.IsConnected != true) return;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"update_lut\",\"side\":\"");
        sb.Append(side);
        sb.Append("\",\"lut\":{\"strength\":");
        sb.Append(lut.Strength.ToString("G6", ic));
        sb.Append(",\"entries\":[");
        for (int i = 0; i < CorrectionLUT.Bins; i++)
        {
            var e = lut.Entries[i];
            sb.Append("{\"rs\":"); sb.Append(e.RadiusScale.ToString("G6", ic));
            sb.Append(",\"ao\":"); sb.Append(e.AngleOffset.ToString("G6", ic));
            sb.Append(",\"xc\":"); sb.Append(e.XCross.ToString("G6", ic));
            sb.Append(",\"yc\":"); sb.Append(e.YCross.ToString("G6", ic));
            sb.Append(i < CorrectionLUT.Bins - 1 ? "}," : "}");
        }
        sb.Append("]}}");
        SendRaw(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public void SendLoadProfile(string profileId)
        => SendCommand(new { type = "load_profile", profile_id = profileId });

    private void SendRaw(byte[] bytes)
    {
        lock (_writeLock)
        {
            if (_writer == null || _pipe?.IsConnected != true) return;
            try
            {
                _writer.Write((uint)bytes.Length);
                _writer.Write(bytes);
                _writer.Flush();
            }
            catch { }
        }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        ConnectionChanged?.Invoke(this, value);
    }
}
