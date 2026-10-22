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
    private readonly IAudioDevices devices;
    private IWavePlayer? output;
    private SynthProvider? provider;
    private readonly Dictionary<string, AudioEndpoint> originalSpeakerLevels = [];
    private string? endpointId;
    private OutputKind endpointKind;
    private bool protectHeadphones, speakerFallback;
    private DateTimeOffset nextCheck;
    private float requestedVolume = .35f;
    internal AudioEngine(IAudioDevices devices) => this.devices = devices;
    public AudioEngine() : this(new WindowsAudioDevices()) { }
    public bool Playing => output?.PlaybackState == PlaybackState.Playing;
    public bool AlarmPlaying => provider?.AlarmEnabled == true && Playing;
    public float[]? Bands => provider?.Bands;
    public string? Error { get; private set; }
    public string? RouteStatus { get; private set; }
    internal bool SpeakerFallback => speakerFallback;
    public void Play(SoundPreset preset, double volume, double intensity, int embarrassment, bool protectHeadphones = false)
    {
        if (provider?.AlarmEnabled == true && this.protectHeadphones == protectHeadphones)
        {
            provider.Preset = preset; Configure(volume, intensity, embarrassment); Monitor(true); return;
        }
        Stop();
        this.protectHeadphones = protectHeadphones;
        provider = new SynthProvider { Preset = preset };
        Configure(volume, intensity, embarrassment);
        Monitor(true);
    }
    public void Monitor(bool force = false)
    {
        if (provider?.AlarmEnabled != true || (!protectHeadphones && !force)) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now < nextCheck) return;
        nextCheck = now.AddMilliseconds(250);
        try
        {
            var snapshot = devices.Read();
            var current = snapshot.Endpoints.FirstOrDefault(e => e.Id == endpointId && e.Active);
            var preferred = snapshot.Endpoints.FirstOrDefault(e => e.Id == snapshot.DefaultId && e.Active);
            // Inspect the old route before following a changed Windows default: an
            // unplug event often changes the default and invalidates playback together.
            if (protectHeadphones && !speakerFallback)
            {
                bool lostHeadphones = endpointKind == OutputKind.Headphones && current == null;
                var headphones = endpointId == null ? preferred : current;
                bool silentHeadphones = headphones?.Kind == OutputKind.Headphones
                    && (headphones.Muted || headphones.Volume <= .0001f || devices.SessionSilent(headphones.Id));
                if (lostHeadphones || silentHeadphones) speakerFallback = true;
            }
            AudioEndpoint? target;
            if (speakerFallback)
            {
                // Latch until dismissal, including when headphones reconnect. Never
                // raise a headphone endpoint, even if it has become the new default.
                target = current?.Kind == OutputKind.Speakers ? current
                    : snapshot.Endpoints.Where(e => e.Active && e.Kind == OutputKind.Speakers)
                        .OrderByDescending(e => e.Id == snapshot.DefaultId)
                        .ThenByDescending(e => e.Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase)
                            || e.Name.Contains("internal", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (target == null)
                {
                    // Keep any surviving route audible; retry when speakers appear.
                    Error = "SPEAKER FALLBACK // No enabled speaker output is available. Connect or enable speakers.";
                    RouteStatus = "Speaker fallback waiting for speakers";
                    provider.Volume = requestedVolume;
                    return;
                }
                if (!originalSpeakerLevels.ContainsKey(target.Id)) originalSpeakerLevels.Add(target.Id, target);
                if (target.Volume < 1 || target.Muted) devices.SetEndpoint(target.Id, 1, false);
            }
            else target = preferred ?? current;
            if (target == null) throw new InvalidOperationException("No enabled Windows audio output is available.");
            if (endpointId != target.Id || !Playing)
            {
                ReleaseOutput();
                endpointId = target.Id; endpointKind = target.Kind;
                output = devices.Open(target.Id, provider);
                if (speakerFallback) devices.UnmuteSession(target.Id);
                provider.Volume = speakerFallback ? 1 : requestedVolume;
                output.Play();
            }
            provider.Volume = speakerFallback ? 1 : requestedVolume;
            if (speakerFallback) devices.UnmuteSession(target.Id);
            RouteStatus = speakerFallback ? $"SPEAKER FALLBACK / {target.Name} / 100%" : $"OUTPUT / {target.Name}";
            Error = null;
        }
        catch (Exception ex) { Error = "AUDIO ERROR // " + ex.Message + " Check the Windows output device."; }
    }
    public void Chime(int tile) => provider?.Chime(tile);
    public void StopAlarm()
    {
        // Stop the alarm at once, allowing the final tile's note to finish in the same
        // stream. The provider returns EOF after the short tail; a new alarm replaces it.
        if (provider != null) provider.AlarmEnabled = false;
        protectHeadphones = false;
        RestoreSpeakerLevels();
    }
    public void Configure(double volume, double intensity, int embarrassment)
    {
        if (provider == null) return;
        requestedVolume = (float)Math.Clamp(volume, .05, 1);
        provider.Volume = speakerFallback && endpointKind == OutputKind.Speakers ? 1 : requestedVolume;
        provider.Intensity = (float)Math.Clamp(intensity, 0, 1);
        provider.Embarrassment = embarrassment;
    }
    private void ReleaseOutput()
    {
        var old = output; output = null;
        try { old?.Stop(); } finally { old?.Dispose(); }
    }
    private void RestoreSpeakerLevels()
    {
        foreach (var entry in originalSpeakerLevels.ToArray())
        {
            try { devices.SetEndpoint(entry.Key, entry.Value.Volume, entry.Value.Muted); originalSpeakerLevels.Remove(entry.Key); }
            catch (Exception ex) { Error = "Could not restore speaker volume: " + ex.Message; }
        }
        speakerFallback = false;
        if (provider != null) provider.Volume = requestedVolume;
        RouteStatus = null;
    }
    public void Stop()
    {
        try { ReleaseOutput(); }
        finally { provider = null; protectHeadphones = false; endpointId = null; endpointKind = OutputKind.Other; RestoreSpeakerLevels(); }
    }
    public void Dispose() { try { Stop(); } finally { devices.Dispose(); } }
}
