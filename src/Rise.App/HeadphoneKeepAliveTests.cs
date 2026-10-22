namespace Rise.App;

internal static class HeadphoneKeepAliveTests
{
    internal static void Run(Action<bool, string> assert)
    {
        var headphones = new AudioEndpoint("anc2", "Headphones (Crusher ANC 2)", OutputKind.Headphones, true, 0, true);
        var speakers = new AudioEndpoint("speakers", "Speakers", OutputKind.Speakers, true, .2f, true);
        var fake = new AudioRoutingTests.FakeDevices { Snapshot = new(speakers.Id, [headphones, speakers]) };
        var clock = new ManualTime();
        using var keepAlive = new HeadphoneKeepAlive(fake, clock);
        keepAlive.Tick(false, false, 5);
        assert(fake.Outputs.Count == 0, "keep-alive opens no stream without an enabled alarm or timer");
        keepAlive.Tick(true, false, 5);
        assert(keepAlive.Pulsing && fake.LastId == headphones.Id && fake.Writes.Count == 0 && fake.UnmutedSessions.Count == 0,
            "keep-alive targets connected ANC 2 headphones even when speakers are default and never changes volume or mute");
        var buffer = Enumerable.Repeat(.99f, 8192).ToArray();
        int count = fake.LastSamples!.Read(buffer, 5, 8180);
        assert(count == 8180 && buffer.Skip(5).Take(8180).All(x => x == 0) && buffer[0] == .99f && buffer[^1] == .99f,
            "keep-alive produces exact digital silence and respects buffer boundaries");
        clock.Advance(TimeSpan.FromSeconds(2)); keepAlive.Tick(true, false, 5);
        assert(!keepAlive.Pulsing && fake.Outputs.Count == 1, "silent headphone pulse stops after two seconds");
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(3)); keepAlive.Tick(true, false, 5);
        assert(fake.Outputs.Count == 1, "five-minute keep-alive does not send an early pulse");
        clock.Advance(TimeSpan.FromSeconds(1)); keepAlive.Tick(true, false, 5);
        assert(keepAlive.Pulsing && fake.Outputs.Count == 2, "five-minute interval sends the next silent pulse on time");
        keepAlive.Tick(true, true, 5);
        assert(!keepAlive.Pulsing, "alarm or preview interrupts the silent stream immediately");
        clock.Advance(TimeSpan.FromMinutes(10)); keepAlive.Tick(true, true, 5);
        assert(fake.Outputs.Count == 2, "keep-alive never competes with a busy alarm or preview");
        keepAlive.Tick(true, false, 5);
        assert(!keepAlive.Pulsing, "keep-alive waits a full interval after audible playback");
        keepAlive.Tick(true, false, 10); int tenMinuteStart = fake.Outputs.Count;
        clock.Advance(TimeSpan.FromSeconds(2)); keepAlive.Tick(true, false, 10);
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(3)); keepAlive.Tick(true, false, 10);
        assert(fake.Outputs.Count == tenMinuteStart, "ten-minute option does not send a pulse early");
        clock.Advance(TimeSpan.FromSeconds(1)); keepAlive.Tick(true, false, 10);
        assert(keepAlive.Pulsing && fake.Outputs.Count == tenMinuteStart + 1, "ten-minute option sends the next pulse on time");
        keepAlive.Tick(false, false, 10);
        assert(!keepAlive.Pulsing, "disabling keep-alive or the final alarm stops an active pulse");
        fake.Snapshot = new(speakers.Id, [speakers]); int previous = fake.Outputs.Count;
        keepAlive.Tick(true, false, 5);
        assert(!keepAlive.Pulsing && fake.Outputs.Count == previous && keepAlive.Status.Contains("Waiting"),
            "missing headphones never redirect silent keep-alive to speakers");
        clock.Advance(TimeSpan.FromSeconds(30)); fake.Snapshot = new(headphones.Id, [headphones, speakers]); fake.FailOpen = true;
        keepAlive.Tick(true, false, 5);
        assert(!keepAlive.Pulsing && keepAlive.Status.Contains("unavailable"), "keep-alive output failures are reported without interrupting alarm scheduling");
        clock.Advance(TimeSpan.FromSeconds(30)); fake.FailOpen = false; keepAlive.Tick(true, false, 5);
        assert(keepAlive.Pulsing, "keep-alive recovers after headphones reconnect or output initialization recovers");
        keepAlive.Stop();
        assert(!keepAlive.Pulsing && fake.Writes.Count == 0 && fake.UnmutedSessions.Count == 0,
            "stopping keep-alive releases playback without touching any Windows volume");
        assert(WakeScheduler.ExecutionFlags(true, false) == 0x80000001u
            && WakeScheduler.ExecutionFlags(true, true) == 0x80000003u
            && WakeScheduler.ExecutionFlags(false, false) == 0x80000000u,
            "armed keep-alive prevents idle sleep while allowing display sleep; ringing also requests display wake");
    }
    private sealed class ManualTime : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }
}
