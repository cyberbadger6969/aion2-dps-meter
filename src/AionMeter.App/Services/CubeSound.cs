using NAudio.Dsp;
using NAudio.Wave;

namespace AionMeter.App.Services;

/// <summary>What the hidden cube's sound says right now: which side it comes from and whether it grows louder.</summary>
/// <param name="Hearing">The game is playing sound (it mutes itself in the background).</param>
/// <param name="BalanceDb">Left minus right, dB, over the last second: positive = the cube is to the left.</param>
/// <param name="LevelDb">How loud it is (relative, only for comparing with itself).</param>
/// <param name="TrendDb">Loudness now minus a few seconds ago: positive = getting closer.</param>
/// <param name="RiseDb">Loudness now above the quietest it has been since the cube came into view.</param>
public readonly record struct CubeHearing(bool Hearing, double BalanceDb, double LevelDb, double TrendDb, double RiseDb = 0);

/// <summary>
/// Listens to what the PC plays (WASAPI loopback: only reads the sound going to the speakers, the game is not touched)
/// while a hidden cube is in view. A cube hums: a steady, broadband 3D sound that stops the moment it is picked up.
/// With the game's music and ambience off it is most of what is left, and flying round a cube swings it 10–15 dB from
/// one ear to the other (recording of 2026-10-24) — so the channels' balance says which side the cube is on, and the
/// loudness how near. With music on, the music drowns it (equal in both ears) and the balance stays near zero.
/// </summary>
public sealed class CubeSound : IDisposable
{
    private const int N = 2048, M = 11;          // FFT frame (≈43 ms at 48 kHz)
    private const double LowHz = 1000, HighHz = 13000; // above the bass of music and footsteps
    private const long WindowMs = 1_200;         // balance and loudness over about a second
    private const long TrendMs = 4_000;
    private const double SilentEnergy = 1e-11;   // ≈ −110 dB: the game is muted (not focused)

    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private float[] _left = new float[N], _right = new float[N];
    private int _fill;
    private int _channels, _bytesPerSample;
    private bool _float;
    private int _sampleRate;
    private readonly Queue<(long T, double L, double R)> _frames = new(); // the last second, sounding frames only
    private readonly Queue<(long T, double Db)> _levels = new();          // loudness history for the trend
    private double _quietest = double.MaxValue;
    private long _startedAt;

    /// <summary>Listens while <paramref name="on"/>; stops (and forgets) otherwise.</summary>
    public void Run(bool on)
    {
        if (on && _capture is null) Start();
        else if (!on && _capture is not null) Stop();
    }

    public CubeHearing Current
    {
        get
        {
            lock (_gate)
            {
                var now = Environment.TickCount64;
                while (_frames.Count > 0 && now - _frames.Peek().T > WindowMs) _frames.Dequeue();
                // A whole second of sound, or the game is muting / unmuting (alt-tab) and the fade would read as far.
                if (_sampleRate == 0 || _frames.Count < WindowMs * _sampleRate / N / 1000 * 4 / 5) return default;
                double l = 0, r = 0;
                foreach (var f in _frames)
                {
                    l += f.L;
                    r += f.R;
                }
                var level = 10 * Math.Log10((l + r) / _frames.Count + 1e-15);
                while (_levels.Count > 0 && now - _levels.Peek().T > TrendMs + 3_000) _levels.Dequeue();
                if (_levels.Count == 0 || now - _levels.Last().T > 1_000) _levels.Enqueue((now, level));
                var then = _levels.FirstOrDefault(x => now - x.T >= TrendMs);
                var trend = then.T != 0 ? level - then.Db : 0;
                // The quietest second so far is where the cube was farthest; the first two seconds only settle it.
                if (now - _startedAt > 2_000) _quietest = Math.Min(_quietest, level);
                var rise = _quietest == double.MaxValue ? 0 : level - _quietest;
                return new CubeHearing(true, 10 * Math.Log10((l + 1e-15) / (r + 1e-15)), level, trend, rise);
            }
        }
    }

    private void Start()
    {
        try
        {
            var capture = new WasapiLoopbackCapture();
            var f = capture.WaveFormat;
            _channels = f.Channels;
            _sampleRate = f.SampleRate;
            _float = f.Encoding == WaveFormatEncoding.IeeeFloat ||
                     (f is WaveFormatExtensible x && x.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
            _bytesPerSample = f.BitsPerSample / 8;
            if (_channels < 2 || (_float ? _bytesPerSample != 4 : _bytesPerSample != 2))
            {
                capture.Dispose();
                Log.Info($"Cube sound: unsupported output format {f}");
                return;
            }
            capture.DataAvailable += (_, e) => Feed(e.Buffer, e.BytesRecorded);
            _startedAt = Environment.TickCount64;
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info($"Cube sound: cannot listen to the sound output ({ex.Message})");
        }
    }

    private void Stop()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
        _capture?.Dispose();
        _capture = null;
        lock (_gate)
        {
            _frames.Clear();
            _levels.Clear();
            _quietest = double.MaxValue;
            _fill = 0;
        }
    }

    private void Feed(byte[] buffer, int bytes)
    {
        var frame = _channels * _bytesPerSample;
        for (var o = 0; o + frame <= bytes; o += frame)
        {
            _left[_fill] = Sample(buffer, o);
            _right[_fill] = Sample(buffer, o + _bytesPerSample);
            if (++_fill == N)
            {
                Analyse();
                _fill = 0;
            }
        }
    }

    private float Sample(byte[] b, int o) => _float ? BitConverter.ToSingle(b, o) : BitConverter.ToInt16(b, o) / 32768f;

    private void Analyse()
    {
        var cl = new Complex[N];
        var cr = new Complex[N];
        for (var i = 0; i < N; i++)
        {
            var w = (float)FastFourierTransform.HannWindow(i, N);
            cl[i].X = _left[i] * w;
            cr[i].X = _right[i] * w;
        }
        FastFourierTransform.FFT(true, M, cl);
        FastFourierTransform.FFT(true, M, cr);
        double l = 0, r = 0;
        for (var k = (int)(LowHz * N / _sampleRate); k <= (int)(HighHz * N / _sampleRate); k++)
        {
            l += cl[k].X * cl[k].X + cl[k].Y * cl[k].Y;
            r += cr[k].X * cr[k].X + cr[k].Y * cr[k].Y;
        }
        if (l + r < SilentEnergy) return; // muted: says nothing about the cube
        var now = Environment.TickCount64;
        lock (_gate)
        {
            _frames.Enqueue((now, l, r));
            while (_frames.Count > 0 && now - _frames.Peek().T > WindowMs) _frames.Dequeue();
        }
    }

    public void Dispose() => Stop();
}
