using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace Rise.App;

internal enum OutputKind { Other, Headphones, Speakers }
internal sealed record AudioEndpoint(string Id, string Name, OutputKind Kind, bool Active, float Volume, bool Muted);
internal sealed record AudioDevices(string? DefaultId, IReadOnlyList<AudioEndpoint> Endpoints);
internal interface IAudioDevices : IDisposable
{
    AudioDevices Read();
    IWavePlayer Open(string id, ISampleProvider samples);
    bool SessionSilent(string id);
    void SetEndpoint(string id, float volume, bool muted);
    void UnmuteSession(string id);
}

internal sealed class WindowsAudioDevices : IAudioDevices
{
    // Created lazily on the WPF thread; hardware-free self-tests never initialize COM audio.
    private MMDeviceEnumerator? enumerator;
    private MMDeviceEnumerator Enumerator => enumerator ??= new MMDeviceEnumerator();
    internal static OutputKind Classify(int formFactor, string name)
    {
        // Some Bluetooth/USB drivers report a headset as Speakers. Prefer an explicit
        // headphone name, then the language-independent Windows form-factor property.
        if (formFactor is 3 or 5 || name.Contains("headphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("headset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("crusher", StringComparison.OrdinalIgnoreCase)) return OutputKind.Headphones;
        return formFactor == 1 || name.Contains("speakers", StringComparison.OrdinalIgnoreCase) ? OutputKind.Speakers : OutputKind.Other;
    }
    public AudioDevices Read()
    {
        string? defaultId = null;
        if (Enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = device.ID;
        }
        List<AudioEndpoint> endpoints = [];
        foreach (var device in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    string name = device.FriendlyName;
                    var key = PropertyKeys.PKEY_AudioEndpoint_FormFactor;
                    int factor = device.Properties.Contains(key) ? Convert.ToInt32(device.Properties[key].Value) : -1;
                    var volume = device.AudioEndpointVolume;
                    endpoints.Add(new(device.ID, name, Classify(factor, name), true, volume.MasterVolumeLevelScalar, volume.Mute));
                }
                catch (COMException) { /* The endpoint can disappear during enumeration. */ }
            }
        }
        return new(defaultId, endpoints);
    }
    public IWavePlayer Open(string id, ISampleProvider samples)
    {
        using var device = Enumerator.GetDevice(id);
        var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
        try { output.Init(samples); return output; }
        catch { output.Dispose(); throw; }
    }
    public bool SessionSilent(string id)
    {
        using var device = Enumerator.GetDevice(id);
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.GetProcessID == (uint)Environment.ProcessId
                && (session.SimpleAudioVolume.Mute || session.SimpleAudioVolume.Volume <= .0001f)) return true;
        }
        return false;
    }
    public void SetEndpoint(string id, float volume, bool muted)
    {
        using var device = Enumerator.GetDevice(id);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
        device.AudioEndpointVolume.Mute = muted;
    }
    public void UnmuteSession(string id)
    {
        using var device = Enumerator.GetDevice(id);
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.GetProcessID != (uint)Environment.ProcessId) continue;
            session.SimpleAudioVolume.Volume = 1;
            session.SimpleAudioVolume.Mute = false;
        }
    }
    public void Dispose() { enumerator?.Dispose(); enumerator = null; }
}
