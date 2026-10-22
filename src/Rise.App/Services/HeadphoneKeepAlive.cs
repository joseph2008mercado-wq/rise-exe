using NAudio.Wave;
using Rise.Core;

namespace Rise.App;

internal sealed class SilenceProvider : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Synthesizer.SampleRate, 1);
    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }
}

internal sealed class HeadphoneKeepAlive : IDisposable
{
    internal static readonly TimeSpan PulseDuration = TimeSpan.FromSeconds(2);
    private readonly IAudioDevices devices;
    private readonly TimeProvider time;
    private IWavePlayer? output;
    private long? nextPulse, pulseStarted;
    private int interval = 5;
    public string Status { get; private set; } = "No enabled alarms or timers";
    internal bool Pulsing => output?.PlaybackState == PlaybackState.Playing;
    public HeadphoneKeepAlive() : this(new WindowsAudioDevices(), TimeProvider.System) { }
    internal HeadphoneKeepAlive(IAudioDevices devices, TimeProvider time) { this.devices = devices; this.time = time; }
    private long After(long now, TimeSpan duration) => now + (long)(duration.TotalSeconds * time.TimestampFrequency);
    public void Tick(bool armed, bool busy, int minutes)
    {
        int selected = minutes == 10 ? 10 : 5;
        if (selected != interval) { StopPulse(); nextPulse = null; interval = selected; }
        if (!armed) { Stop(); return; }
        if (busy) { Pause(); return; }
        long now = time.GetTimestamp();
        if (output != null)
        {
            if (pulseStarted != null && time.GetElapsedTime(pulseStarted.Value, now) >= PulseDuration)
            {
                StopPulse(); Status = $"Silent audio every {interval} minutes";
            }
            else if (Pulsing) return;
            else { StopPulse(); nextPulse = After(now, TimeSpan.FromSeconds(30)); Status = "Headphone stream stopped; retrying"; }
        }
        if (nextPulse != null && now < nextPulse) return;
        try
        {
            var snapshot = devices.Read();
            var headphones = snapshot.Endpoints.Where(e => e.Active && e.Kind == OutputKind.Headphones)
                .OrderByDescending(e => e.Id == snapshot.DefaultId)
                .ThenByDescending(e => e.Name.Contains("crusher", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (headphones == null)
            {
                nextPulse = After(now, TimeSpan.FromSeconds(30)); Status = "Waiting for connected headphones"; return;
            }
            // Real zero samples, sent only to headphones. No tone/noise, speaker
            // fallback, session unmute, or endpoint volume changes in this service.
            output = devices.Open(headphones.Id, new SilenceProvider());
            output.Play(); pulseStarted = now;
            nextPulse = After(now, TimeSpan.FromMinutes(interval));
            Status = $"Sending silent audio / {headphones.Name}";
        }
        catch (Exception ex)
        {
            StopPulse(); nextPulse = After(now, TimeSpan.FromSeconds(30));
            Status = "Keep-alive unavailable: " + ex.Message;
        }
    }
    public void Pause()
    {
        StopPulse(); nextPulse = After(time.GetTimestamp(), TimeSpan.FromMinutes(interval));
        Status = "Paused during alarm or preview";
    }
    private void StopPulse()
    {
        var old = output; output = null; pulseStarted = null;
        try { old?.Stop(); } catch (Exception) { /* Keep-alive must never prevent an actual alarm. */ }
        try { old?.Dispose(); } catch (Exception) { }
    }
    public void Stop() { StopPulse(); nextPulse = null; Status = "No enabled alarms or timers"; }
    public void Dispose() { Stop(); devices.Dispose(); }
}
