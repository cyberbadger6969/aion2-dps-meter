using AionMeter.Core.Events;
using AionMeter.Core.Game;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AionMeter.Core.Capture;

/// <summary>Feeds a recorded .pcap through the same pipeline as live capture — for testing parsers on real fights.</summary>
public sealed class PcapReplaySource : IEventSource
{
    private readonly string _path;
    private readonly bool _realtime;
    private readonly PacketPipeline _pipeline;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private CaptureStatus _status;

    public PcapReplaySource(GameData data, string path, bool realtime)
    {
        _path = path;
        _realtime = realtime;
        _pipeline = new PacketPipeline(data, e => EventDecoded?.Invoke(e));
        _status = new CaptureStatus(CaptureState.Stopped, "Replay ready");
    }

    public event Action<GameEvent>? EventDecoded;
    public event Action<CaptureStatus>? StatusChanged;
    public CaptureStatus Status => _status;
    public PacketPipeline Pipeline => _pipeline;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => Run(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    /// <summary>Synchronous replay (CLI / tests). Returns the number of packets read.</summary>
    public long RunToEnd() => Run(CancellationToken.None);

    private long Run(CancellationToken ct)
    {
        long count = 0;
        SetStatus(CaptureState.Capturing, $"Replaying {Path.GetFileName(_path)}");
        using var reader = new CaptureFileReaderDevice(_path);
        reader.Open();
        long? firstPacketMs = null;
        var started = DateTime.UtcNow;
        while (!ct.IsCancellationRequested && reader.GetNextPacket(out var e) == GetPacketStatus.PacketRead)
        {
            count++;
            var raw = e.GetPacket();
            if (!SegmentReader.TryRead(raw, out var seg)) continue;
            if (_realtime)
            {
                firstPacketMs ??= seg.TimeMs;
                var due = started.AddMilliseconds(seg.TimeMs - firstPacketMs.Value);
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            }
            _pipeline.OnSegment(seg.TimeMs, seg.Key, seg.Seq, seg.Syn, seg.FinOrRst, seg.Payload);
        }
        SetStatus(CaptureState.Stopped, $"Replay finished · {count} packets");
        return count;
    }

    private void SetStatus(CaptureState state, string message)
    {
        _status = new CaptureStatus(state, message);
        StatusChanged?.Invoke(_status);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _task?.Wait(1_000); } catch (AggregateException) { }
        _cts?.Dispose();
    }
}
