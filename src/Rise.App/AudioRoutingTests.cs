using NAudio.Wave;
using Rise.Core;

namespace Rise.App;

internal static class AudioRoutingTests
{
    internal static void Run(Action<bool, string> assert)
    {
        var headphones = new AudioEndpoint("headphones", "Headphones (Crusher)", OutputKind.Headphones, true, .5f, false);
        var speakers = new AudioEndpoint("speakers", "Speakers (Realtek)", OutputKind.Speakers, true, .24f, true);
        FakeDevices NewDevices() => new() { Snapshot = new(headphones.Id, [headphones, speakers]) };
        assert(WindowsAudioDevices.Classify(1, "Headphones (Crusher)") == OutputKind.Headphones
            && WindowsAudioDevices.Classify(5, "Casque") == OutputKind.Headphones
            && WindowsAudioDevices.Classify(1, "Haut-parleurs") == OutputKind.Speakers,
            "audio classification honors headsets and localized Windows form factors");
        {
            var fake = NewDevices(); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.DigitalHell, .35, .7, 0, true);
            assert(fake.LastId == headphones.Id && fake.Writes.Count == 0 && audio.AlarmPlaying,
                "audible headphones retain their volume and play normally");
            var samples = fake.LastSamples;
            fake.Snapshot = new(speakers.Id, [speakers]); audio.Monitor(true);
            assert(fake.LastId == speakers.Id && audio.SpeakerFallback && fake.Endpoint(speakers.Id) is { Volume: 1, Muted: false }
                && samples == fake.LastSamples && fake.Outputs.Count(o => o.PlaybackState == PlaybackState.Playing) == 1,
                "headphone disconnect with simultaneous default change reroutes the existing stream to unmuted speakers at 100 percent");
            fake.LastSamples!.Read(new float[4096], 0, 4096);
            assert(((SynthProvider)fake.LastSamples).Volume == 1 && fake.UnmutedSessions.Contains(speakers.Id),
                "speaker fallback overrides app volume and unmutes only its own speaker session");
            fake.Snapshot = new(headphones.Id, [headphones, fake.Endpoint(speakers.Id)]); audio.Monitor(true);
            audio.Play(SoundPreset.CursedMidi, .1, .7, 3, true);
            assert(fake.LastId == speakers.Id && audio.SpeakerFallback && ((SynthProvider)fake.LastSamples).Volume == 1,
                "headphone reconnect and escape fanfare cannot clear latched speaker fallback");
            fake.SetEndpoint(speakers.Id, 0, true); audio.Monitor(true);
            assert(fake.Endpoint(speakers.Id) is { Volume: 1, Muted: false }, "speaker mute or zero volume is corrected while fallback alarm rings");
            audio.Chime(3); audio.StopAlarm();
            assert(fake.Endpoint(speakers.Id) is { Volume: .24f, Muted: true } && !audio.AlarmPlaying && !audio.SpeakerFallback,
                "dismissal restores original speaker level and mute without touching headphones");
            int writes = fake.Writes.Count; audio.Monitor(true);
            assert(fake.Writes.Count == writes && fake.Writes.All(w => w == speakers.Id), "idle monitor never raises speaker or headphone volume");
        }
        foreach (int silentMode in Enumerable.Range(0, 3))
        {
            var fake = NewDevices();
            fake.Snapshot = new(headphones.Id, [headphones with { Volume = silentMode == 0 ? 0 : .5f, Muted = silentMode == 1 }, speakers]);
            fake.SilentSession = silentMode == 2;
            using var audio = new AudioEngine(fake); audio.Play(SoundPreset.Industrial, .35, .7, 0, true);
            assert(audio.SpeakerFallback && fake.LastId == speakers.Id && audio.Error == null,
                new[] { "zero headphone endpoint volume at alarm start triggers speaker fallback", "muted headphones at alarm start trigger speaker fallback", "muted RISE headphone mixer session triggers speaker fallback" }[silentMode]);
        }
        {
            var fake = NewDevices(); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.Industrial, .35, .7, 0, true);
            fake.Snapshot = new(headphones.Id, [headphones with { Volume = 0 }, speakers]); audio.Monitor(true);
            assert(audio.SpeakerFallback && fake.LastId == speakers.Id, "turning headphones down to zero during playback triggers fallback");
        }
        {
            var fake = NewDevices(); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.Industrial, .35, .7, 0, true);
            fake.Snapshot = new(null, []); audio.Monitor(true);
            assert(audio.SpeakerFallback && audio.Error!.Contains("No enabled speaker") && fake.Writes.Count == 0,
                "disconnect without speakers reports unavailable fallback and does not boost another device");
            fake.Snapshot = new(headphones.Id, [headphones, speakers]); audio.Monitor(true);
            assert(fake.LastId == speakers.Id && audio.Error == null, "waiting fallback recovers when speakers become available");
        }
        {
            var fake = NewDevices(); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.Industrial, .35, .7, 0);
            fake.Snapshot = new(headphones.Id, [headphones with { Volume = 0 }, speakers]); audio.Monitor(true);
            assert(!audio.SpeakerFallback && fake.Writes.Count == 0, "sound preview never invokes speaker volume enforcement");
        }
        {
            var fake = NewDevices(); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.Industrial, .35, .7, 0, true);
            fake.FailOpen = true; fake.Snapshot = new(speakers.Id, [speakers]); audio.Monitor(true);
            assert(audio.SpeakerFallback && audio.Error != null, "failed speaker stream keeps fallback state and reports an error");
            fake.FailOpen = false; audio.Monitor(true);
            assert(audio.Playing && fake.LastId == speakers.Id && audio.Error == null, "speaker stream initialization retries successfully");
            audio.Stop();
            assert(fake.Endpoint(speakers.Id) is { Volume: .24f, Muted: true } && !audio.Playing,
                "stopping audio restores speaker volume even after a route failure");
        }
        {
            var fake = NewDevices(); fake.Snapshot = new(speakers.Id, [headphones, speakers]); using var audio = new AudioEngine(fake);
            audio.Play(SoundPreset.Industrial, .35, .7, 0, true);
            assert(!audio.SpeakerFallback && fake.Writes.Count == 0, "alarms starting on speakers do not unnecessarily change Windows volume");
        }
    }
    private sealed class FakeDevices : IAudioDevices
    {
        public AudioDevices Snapshot = new(null, []);
        public readonly List<string> Writes = [], UnmutedSessions = [];
        public readonly List<FakeOutput> Outputs = [];
        public string? LastId;
        public ISampleProvider? LastSamples;
        public bool SilentSession, FailOpen;
        public AudioEndpoint Endpoint(string id) => Snapshot.Endpoints.Single(e => e.Id == id);
        public AudioDevices Read() => Snapshot;
        public bool SessionSilent(string id) => SilentSession;
        public void SetEndpoint(string id, float volume, bool muted)
        {
            Writes.Add(id); Snapshot = Snapshot with { Endpoints = Snapshot.Endpoints.Select(e => e.Id == id ? e with { Volume = volume, Muted = muted } : e).ToArray() };
        }
        public void UnmuteSession(string id) => UnmutedSessions.Add(id);
        public IWavePlayer Open(string id, ISampleProvider samples)
        {
            if (FailOpen) throw new InvalidOperationException("Simulated unplugged device");
            LastId = id; LastSamples = samples; var result = new FakeOutput(); Outputs.Add(result); return result;
        }
        public void Dispose() { }
    }
    private sealed class FakeOutput : IWavePlayer
    {
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(Synthesizer.SampleRate, 1);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) { }
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new StoppedEventArgs()); }
        public void Dispose() => Stop();
    }
}
