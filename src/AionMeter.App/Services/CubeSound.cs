using NAudio.Dsp;
using NAudio.Wave;

namespace AionMeter.App.Services;

/// <summary>What the hidden cube's chime says right now: which side it comes from and whether it grows louder.</summary>
/// <param name="Hearing">A chime was heard in the last few seconds.</param>
/// <param name="BalanceDb">Left minus right, dB, over the last chimes: positive = the cube is to the left.</param>
/// <param name="LevelDb">How loud the chime is (relative, only for comparing with itself).</param>
/// <param name="TrendDb">Loudness now minus a few seconds ago: positive = getting closer.</param>
public readonly record struct CubeHearing(bool Hearing, double BalanceDb, double LevelDb, double TrendDb);

/// <summary>
/// Listens to what the PC plays (WASAPI loopback: only reads the sound going to the speakers, the game is not touched)
/// while a hidden cube is in view, and picks out its chime: a ring of short pulses around 4.6–5.9 kHz, about every
/// 6 s, louder the nearer the cube, panned like any 3D sound (recording of 2026-10-24: right louder by 2–9 dB with
/// the cube on the right). The game mutes itself in the background, so this only hears it while the game has focus.
/// </summary>
public sealed class CubeSound : IDisposable
{
    private const int N = 2048, M = 11;          // FFT frame (≈43 ms at 48 kHz)
    private const double LowHz = 4600, HighHz = 5900;
    private const long WindowMs = 7_000;         // one ring of the chime and a bit
    private const long TrendMs = 5_000;

    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private float[] _left = new float[N], _right = new float[N];
    private int _fill;
    private int _channels, _bytesPerSample;
    private bool _float;
    private int _sampleRate;
    private readonly Queue<double> _floor = new();                       // recent band energies
    private readonly Queue<(long T, double L, double R)> _pulses = new(); // chime pulses
    private readonly Queue<(long T, double Db)> _levels = new();          // loudness history for the trend

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
                while (_pulses.Count > 0 && now - _pulses.Peek().T > WindowMs) _pulses.Dequeue();
                if (_pulses.Count < 2) return default;
                double l = 0, r = 0, peak = 0;
                foreach (var p in _pulses)
                {
                    l += p.L;
                    r += p.R;
                    peak = Math.Max(peak, p.L + p.R);
                }
                var level = 10 * Math.Log10(peak + 1e-15);
                while (_levels.Count > 0 && now - _levels.Peek().T > TrendMs + 3_000) _levels.Dequeue();
                if (_levels.Count == 0 || now - _levels.Last().T > 1_000) _levels.Enqueue((now, level));
                var then = _levels.FirstOrDefault(x => now - x.T >= TrendMs);
                var trend = then.T != 0 ? level - then.Db : 0;
                return new CubeHearing(true, 10 * Math.Log10((l + 1e-15) / (r + 1e-15)), level, trend);
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
                Log.Info($"Cube chime: unsupported output format {f}");
                return;
            }
            capture.DataAvailable += (_, e) => Feed(e.Buffer, e.BytesRecorded);
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info($"Cube chime: cannot listen to the sound output ({ex.Message})");
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
            _pulses.Clear();
            _levels.Clear();
            _floor.Clear();
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
        var e = l + r;
        var now = Environment.TickCount64;
        lock (_gate)
        {
            // The floor: a low percentile of the last ~4 s of this band. A pulse stands well above it (≈ +8 dB).
            _floor.Enqueue(e);
            while (_floor.Count > 96) _floor.Dequeue();
            if (_floor.Count < 24) return;
            var sorted = _floor.OrderBy(x => x).ToArray();
            var floor = sorted[sorted.Length / 3];
            if (e > floor * 6 && e > 1e-9) _pulses.Enqueue((now, l, r));
            while (_pulses.Count > 0 && now - _pulses.Peek().T > WindowMs) _pulses.Dequeue();
        }
    }

    public void Dispose() => Stop();
}
