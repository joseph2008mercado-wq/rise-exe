namespace Rise.Core;

// Four original GM-style bell notes: C5, E5, G5, C6. Procedural PCM, no MIDI device
// or sample files required. Each note has a fast attack and a short, bright decay.
public static class TileChime
{
    private static readonly double[] Frequencies = [523.2511306, 659.2551138, 783.9908720, 1046.5022612];
    public const double Duration = .6;
    public static int Frames => (int)(Duration * Synthesizer.SampleRate);
    public static double Frequency(int tile) => Frequencies[tile];
    public static float Sample(int tile, int frame)
    {
        if (tile is < 0 or > 3 || frame < 0 || frame >= Frames) return 0;
        double time = frame / (double)Synthesizer.SampleRate;
        double phase = 2 * Math.PI * Frequencies[tile] * time;
        double attack = Math.Min(1, time / .004);
        double release = Math.Min(1, (Duration - time) / .035);
        double body = .67 * Math.Sin(phase) * Math.Exp(-time * 3.2)
            + .22 * Math.Sin(phase * 2.005) * Math.Exp(-time * 6)
            + .11 * Math.Sin(phase * 3) * Math.Exp(-time * 9);
        return (float)(.94 * body * attack * release);
    }
}
