using AionMeter.Core.Events;
using AionMeter.Core.Game;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AionMeter.Core.Capture;

public sealed class LiveCaptureOptions
{
    /// <summary>Npcap device name to force (e.g. a VPN adapter). Null = listen on every adapter.</summary>
    public string? DeviceName { get; set; }

    /// <summary>When set, every captured game packet is also written to a .pcap file in this folder.</summary>
    public string? RecordDirectory { get; set; }
}

/// <summary>
/// Passive capture of the AION 2 connection through Npcap. It listens on every network adapter (the loopback one
/// included: a ping accelerator relays the game locally) and recognises the game's stream by the game's own
/// heartbeats, like any other meter does; once found, the kernel filter narrows to that server, and widens again when
/// the stream goes quiet (a loading screen can move the game to another server). It never looks at the game process,
/// its memory or the system's connection table, and nothing is ever sent or injected.
/// </summary>
public sealed class LiveCapture : IEventSource
{
    private const string Broad = "tcp";
    private const long QuietMs = 5_000;        // no game data for this long: the stream may have moved, listen wide again
    private const long RetryFailedMs = 60_000; // an adapter that would not open is tried again this often
    private const int KernelBufferBytes = 16 * 1024 * 1024;

    private readonly LiveCaptureOptions _options;
    private readonly PacketPipeline _pipeline;
    private readonly object _gate = new();     // the adapters and the recorder
    private readonly object _pipeGate = new(); // the pipeline: every open adapter delivers on its own thread
    private readonly List<LibPcapLiveDevice> _devices = new();
    private readonly Dictionary<string, long> _failed = new(); // adapter name → when it last refused to open
    private CancellationTokenSource? _cts;
    private Task? _monitor;
    private string _filter = Broad;
    private CaptureFileWriterDevice? _writer;
    private LinkLayers _writerLink;
    private long _packets;
    private long _events;
    private CaptureStatus _status = new(CaptureState.Stopped, "Capture stopped");

    public LiveCapture(GameData data, LiveCaptureOptions options)
    {
        _options = options;
        _pipeline = new PacketPipeline(data, OnEvent) { LockThreshold = 3 };
    }

    public event Action<GameEvent>? EventDecoded;
    public event Action<CaptureStatus>? StatusChanged;

    public CaptureStatus Status => _status;
    public PacketPipeline Pipeline => _pipeline;
    public string? RecordingPath { get; private set; }

    /// <summary>Npcap's capture library is on this PC (its own folder, or System32 in WinPcap-compatible mode).</summary>
    public static bool IsNpcapInstalled() =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "Npcap", "wpcap.dll")) ||
        File.Exists(Path.Combine(Environment.SystemDirectory, "wpcap.dll"));

    /// <summary>Packets Npcap delivered and dropped on the open adapters, and holes in the game streams; null while no
    /// adapter is open. The adapters' counters restart when they are reopened.</summary>
    public CaptureHealth? Health()
    {
        lock (_gate)
        {
            if (_devices.Count == 0) return null;
            long received = 0, dropped = 0;
            foreach (var d in _devices)
            {
                try
                {
                    var s = d.Statistics;
                    received += s.ReceivedPackets;
                    dropped += s.DroppedPackets + s.InterfaceDroppedPackets;
                }
                catch (PcapException)
                {
                }
            }
            return new CaptureHealth(received, dropped, _pipeline.Gaps);
        }
    }

    public static IReadOnlyList<(string Name, string Description)> ListDevices()
    {
        return CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>()
            .Select(d => (d.Name, Describe(d)))
            .ToList();
    }

    public void Start()
    {
        if (_monitor is not null) return;
        _cts = new CancellationTokenSource();
        SetStatus(CaptureState.Starting, "Starting capture…");
        _monitor = Task.Run(() => MonitorLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _monitor?.Wait(2_000); } catch (AggregateException) { }
        _monitor = null;
        CloseDevices();
        SetStatus(CaptureState.Stopped, "Capture stopped");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private async Task MonitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Refresh();
            }
            catch (DllNotFoundException)
            {
                SetStatus(CaptureState.Error, "Npcap is not installed — get it from npcap.com");
            }
            catch (TypeInitializationException)
            {
                SetStatus(CaptureState.Error, "Npcap is not installed — get it from npcap.com");
            }
            catch (PcapException ex)
            {
                SetStatus(CaptureState.Error, "Capture error: " + ex.Message);
                CloseDevices();
            }
            try
            {
                await Task.Delay(2_000, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>The adapters to listen on: the forced one, or every adapter with an address plus the loopback one.</summary>
    private List<LibPcapLiveDevice> Wanted()
    {
        var all = CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>();
        if (_options.DeviceName is { Length: > 0 } forced) return all.Where(d => d.Name == forced).ToList();
        return all.Where(d => d.Addresses.Count > 0 || IsLoopback(d)).ToList();
    }

    private void Refresh()
    {
        var wanted = Wanted();
        if (wanted.Count == 0)
        {
            CloseDevices();
            SetStatus(CaptureState.Error, _options.DeviceName is { Length: > 0 } ? "Selected network adapter not found" : "No Npcap adapter found");
            return;
        }
        lock (_gate)
        {
            // Adapters that are gone (or no longer wanted) close; new ones open; the filter follows the game stream.
            foreach (var d in _devices.Where(d => wanted.All(w => w.Name != d.Name)).ToList()) CloseDeviceLocked(d);
            var now = Environment.TickCount64;
            foreach (var w in wanted)
            {
                if (_devices.Any(d => d.Name == w.Name)) continue;
                if (_failed.TryGetValue(w.Name, out var at) && now - at < RetryFailedMs) continue;
                try
                {
                    OpenLocked(w);
                    _failed.Remove(w.Name);
                }
                catch (PcapException)
                {
                    _failed[w.Name] = now; // a VPN or virtual adapter that will not capture: the others carry on
                }
            }
            if (_devices.Count == 0)
            {
                SetStatus(CaptureState.Error, "No Npcap adapter could be opened");
                return;
            }
            ApplyFilterLocked(WantedFilter());
        }
        ReportFlowStatus();
    }

    private void OpenLocked(LibPcapLiveDevice device)
    {
        device.OnPacketArrival += OnPacketArrival;
        try
        {
            // A world boss with a crowd floods the game connection: Npcap's default 1 MB kernel buffer overflows
            // in the bursts, and every packet it drops is a hole in the stream.
            device.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100, BufferSize = KernelBufferBytes });
            device.Filter = _filter;
            device.StartCapture();
        }
        catch
        {
            device.OnPacketArrival -= OnPacketArrival;
            try { device.Close(); } catch (PcapException) { }
            throw;
        }
        _devices.Add(device);
    }

    /// <summary>Everything TCP until the game's stream is found, then that server alone; wide again once the stream
    /// has been quiet for a moment, so a move to another server is picked up from its first packets.</summary>
    private string WantedFilter()
    {
        List<FlowKey> locked;
        long lastData;
        lock (_pipeGate)
        {
            locked = _pipeline.LockedFlows.ToList();
            lastData = _pipeline.LastGameDataMs;
        }
        if (locked.Count == 0 || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastData > QuietMs) return Broad;
        var servers = locked.Select(k => $"(host {k.Src} and port {k.SrcPort})").Distinct().OrderBy(s => s);
        return Broad + " and (" + string.Join(" or ", servers) + ")";
    }

    private void ApplyFilterLocked(string filter)
    {
        if (filter == _filter) return;
        foreach (var d in _devices)
        {
            try
            {
                d.Filter = filter;
            }
            catch (PcapException)
            {
                // Stays on its old filter: wider is only slower, narrower is at worst a few seconds behind.
            }
        }
        _filter = filter;
    }

    private void ReportFlowStatus()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int flows;
        string? server;
        long lastData;
        lock (_pipeGate)
        {
            flows = _pipeline.GameFlows;
            server = _pipeline.LockedFlows.Select(f => f.Src.ToString()).FirstOrDefault();
            lastData = _pipeline.LastGameDataMs;
        }
        if (flows > 0 && now - lastData < 10_000)
            SetStatus(CaptureState.Capturing, $"Capturing · {server}" + (RecordingPath is null ? "" : " · REC"));
        else if (flows > 0)
            SetStatus(CaptureState.WaitingForGame, "Connected · no game traffic for a while");
        else
        {
            string where;
            lock (_gate) where = _devices.Count == 1 ? Describe(_devices[0]) : $"{_devices.Count} adapters";
            SetStatus(CaptureState.WaitingForGame, $"Listening on {where}…");
        }
    }

    private void CloseDevices()
    {
        lock (_gate)
        {
            foreach (var d in _devices.ToList()) CloseDeviceLocked(d);
            _writer?.Close();
            _writer = null;
            RecordingPath = null;
            _filter = Broad;
        }
    }

    private void CloseDeviceLocked(LibPcapLiveDevice device)
    {
        try
        {
            device.StopCapture();
        }
        catch (PcapException)
        {
        }
        device.OnPacketArrival -= OnPacketArrival;
        try
        {
            device.Close();
        }
        catch (PcapException)
        {
        }
        _devices.Remove(device);
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        var raw = e.GetPacket();
        Interlocked.Increment(ref _packets);
        try
        {
            if (_options.RecordDirectory is not null) Record(raw);
            if (SegmentReader.TryRead(raw, out var seg))
                lock (_pipeGate) _pipeline.OnSegment(seg.TimeMs, seg.Key, seg.Seq, seg.Syn, seg.FinOrRst, seg.Payload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A malformed packet must never take the capture thread down.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>The recording takes the link type of the first packet; adapters of another kind are left out of it.</summary>
    private void Record(RawCapture raw)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                if (_options.RecordDirectory is not { Length: > 0 } dir) return;
                Directory.CreateDirectory(dir);
                RecordingPath = Path.Combine(dir, $"aion2_{DateTime.Now:yyyyMMdd_HHmmss}.pcap");
                _writer = new CaptureFileWriterDevice(RecordingPath);
                _writer.Open(new DeviceConfiguration { LinkLayerType = raw.LinkLayerType });
                _writerLink = raw.LinkLayerType;
            }
            if (raw.LinkLayerType == _writerLink) _writer.Write(raw);
        }
    }

    private void OnEvent(GameEvent e)
    {
        _events++;
        EventDecoded?.Invoke(e);
    }

    private void SetStatus(CaptureState state, string message)
    {
        var s = new CaptureStatus(state, message, Interlocked.Read(ref _packets), _events);
        _status = s;
        StatusChanged?.Invoke(s);
    }

    private static bool IsLoopback(LibPcapLiveDevice d) =>
        d.Loopback ||
        d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
        (d.Description?.Contains("loopback", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string Describe(LibPcapLiveDevice d) =>
        IsLoopback(d) ? "Loopback" : string.IsNullOrWhiteSpace(d.Description) ? d.Name : d.Description!;
}

public readonly record struct CaptureHealth(long Received, long Dropped, long Gaps);

/// <summary>Link layer → TCP segment, shared by live capture and pcap replay.</summary>
public static class SegmentReader
{
    public readonly record struct Segment(long TimeMs, FlowKey Key, uint Seq, bool Syn, bool FinOrRst, byte[] Payload);

    public static bool TryRead(RawCapture raw, out Segment segment)
    {
        segment = default;
        Packet packet;
        try
        {
            packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or NotImplementedException)
        {
            return false;
        }
        var ip = packet.Extract<IPPacket>();
        var tcp = packet.Extract<TcpPacket>();
        if (ip is null || tcp is null) return false;

        var t = raw.Timeval;
        var ms = (long)t.Seconds * 1000 + (long)t.MicroSeconds / 1000;
        var key = new FlowKey(ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort);
        segment = new Segment(ms, key, tcp.SequenceNumber, tcp.Synchronize, tcp.Finished || tcp.Reset, tcp.PayloadData ?? []);
        return true;
    }
}
