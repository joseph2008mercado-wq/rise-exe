using NAudio.Wave;
using System.Collections.Concurrent;
using Rise.Core;

namespace Rise.App;

public sealed class SynthProvider : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synthesizer.SampleRate, 1);
    public SoundPreset Preset { get; set; }
    public volatile float Volume = .35f;
    public volatile float Intensity = .65f;
    public volatile int Embarrassment;
    public volatile bool AlarmEnabled = true;
    public readonly float[] Bands = new float[28];
    private readonly ConcurrentQueue<int> chimes = new();
    private readonly List<ChimeVoice> voices = [];
    private sealed class ChimeVoice(int tile) { public int Tile { get; } = tile; public int Frame; }
    private long frame;
    private double alarmGain = 1;
    public void Chime(int tile) { if (tile is >= 0 and <= 3) chimes.Enqueue(tile); }
    public int Read(float[] buffer, int offset, int count)
    {
        float volume = Volume, intensity = Intensity; int embarrassment = Embarrassment;
        while (chimes.TryDequeue(out int tile))
        {
            if (voices.Count == 8) voices.RemoveAt(0);
            voices.Add(new ChimeVoice(tile));
        }
        if (!AlarmEnabled && voices.Count == 0) return 0;
        for (int i = 0; i < count; i++)
        {
            double bell = 0;
            for (int voice = voices.Count - 1; voice >= 0; voice--)
            {
                var note = voices[voice];
                bell += TileChime.Sample(note.Tile, note.Frame++);
                if (note.Frame >= TileChime.Frames) voices.RemoveAt(voice);
            }
            // Briefly duck the alarm so the melody stays clear over bass and distortion.
            // Both channels share one output, one volume, and the same fixed peak limit.
            alarmGain += ((voices.Count > 0 ? .22 : 1) - alarmGain) * .002;
            double alarm = AlarmEnabled ? Synthesizer.Sample(Preset, frame, intensity, embarrassment) * alarmGain : 0;
            frame++;
            buffer[offset + i] = (float)Math.Clamp(alarm + bell * 1.15, -.94, .94) * volume;
        }
        int n = Math.Min(512, count);
        for (int band = 0; band < Bands.Length; band++)
        {
            double freq = 30 * Math.Pow(400, band / 27.0), coefficient = 2 * Math.Cos(2 * Math.PI * freq / Synthesizer.SampleRate);
            double q1 = 0, q2 = 0;
            for (int i = count - n; i < count; i++) { double q0 = coefficient * q1 - q2 + buffer[offset + i]; q2 = q1; q1 = q0; }
            Bands[band] = (float)Math.Clamp(Math.Sqrt(Math.Max(0, q1 * q1 + q2 * q2 - coefficient * q1 * q2)) / n * 9, 0, 1);
        }
        return count; // Alarm loops indefinitely; after dismissal only queued chimes decay.
    }
}

public sealed class AudioEngine : IDisposable
{
    private WaveOutEvent? output;
    private SynthProvider? provider;
    public bool Playing => output?.PlaybackState == PlaybackState.Playing;
    public bool AlarmPlaying => provider?.AlarmEnabled == true && Playing;
    public float[]? Bands => provider?.Bands;
    public string? Error { get; private set; }
    public void Play(SoundPreset preset, double volume, double intensity, int embarrassment)
    {
        if (provider?.Preset == preset && AlarmPlaying) { Configure(volume, intensity, embarrassment); return; }
        Stop();
        try
        {
            provider = new SynthProvider { Preset = preset };
            Configure(volume, intensity, embarrassment);
            output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            output.Init(provider);
            output.Play(); Error = null;
        }
        catch (Exception ex) { Error = "AUDIO ERROR // " + ex.Message + " Check the Windows output device."; Stop(); }
    }
    public void Chime(int tile) => provider?.Chime(tile);
    public void StopAlarm()
    {
        // Stop the alarm at once, allowing the final tile's note to finish in the same
        // stream. The provider returns EOF after the short tail; a new alarm replaces it.
        if (provider != null) provider.AlarmEnabled = false;
    }
    public void Configure(double volume, double intensity, int embarrassment)
    {
        if (provider == null) return;
        provider.Volume = (float)Math.Clamp(volume, .05, 1);
        provider.Intensity = (float)Math.Clamp(intensity, 0, 1);
        provider.Embarrassment = embarrassment;
    }
    public void Stop() { output?.Stop(); output?.Dispose(); output = null; provider = null; }
    public void Dispose() => Stop();
}
