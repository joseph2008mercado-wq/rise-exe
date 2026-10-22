using NAudio.Wave;
using Rise.Core;

namespace Rise.App;

public sealed class SynthProvider : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synthesizer.SampleRate, 1);
    public SoundPreset Preset { get; set; }
    public volatile float Volume = .35f;
    public volatile float Intensity = .65f;
    public volatile int Embarrassment;
    public readonly float[] Bands = new float[28];
    private long frame;
    public int Read(float[] buffer, int offset, int count)
    {
        float volume = Volume, intensity = Intensity; int embarrassment = Embarrassment;
        for (int i = 0; i < count; i++) buffer[offset + i] = Synthesizer.Sample(Preset, frame++, intensity, embarrassment) * volume;
        int n = Math.Min(512, count);
        for (int band = 0; band < Bands.Length; band++)
        {
            double freq = 30 * Math.Pow(400, band / 27.0), coefficient = 2 * Math.Cos(2 * Math.PI * freq / Synthesizer.SampleRate);
            double q1 = 0, q2 = 0;
            for (int i = count - n; i < count; i++) { double q0 = coefficient * q1 - q2 + buffer[offset + i]; q2 = q1; q1 = q0; }
            Bands[band] = (float)Math.Clamp(Math.Sqrt(Math.Max(0, q1 * q1 + q2 * q2 - coefficient * q1 * q2)) / n * 9, 0, 1);
        }
        return count; // Infinite stream: output never ends between composition cycles.
    }
}

public sealed class AudioEngine : IDisposable
{
    private WaveOutEvent? output;
    private SynthProvider? provider;
    public bool Playing => output?.PlaybackState == PlaybackState.Playing;
    public float[]? Bands => provider?.Bands;
    public string? Error { get; private set; }
    public void Play(SoundPreset preset, double volume, double intensity, int embarrassment)
    {
        if (provider?.Preset == preset && Playing) { Configure(volume, intensity, embarrassment); return; }
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
