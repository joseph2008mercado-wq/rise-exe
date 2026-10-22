namespace Rise.Core;

// Pure, deterministic PCM composition. No recordings, MIDI files, or copyrighted samples.
public static class Synthesizer
{
    public const int SampleRate = 44100;
    private static readonly int[] Melody = [76, 79, 84, 83, 79, 76, 74, 79, 77, 81, 86, 84, 81, 77, 76, 72, 76, 79, 88, 86, 84, 79, 83, 86, 84, 83, 79, 74, 76, 79, 84, 91];
    private static readonly int[] Roots = [48, 53, 55, 48];
    private static readonly int[] Arpeggio = [12, 16, 19, 24];
    private static readonly int[] Fanfare = [72, 72, 79, 84, 82, 79, 76, 79, 84, 88, 86, 83, 84, 79, 72, 67];
    public static double Duration(SoundPreset preset) => preset == SoundPreset.CursedMidi ? 32 * 60.0 / 105 : 8;
    private static double Sin(double phase) => Math.Sin(2 * Math.PI * phase);
    private static double Saw(double phase) => 2 * (phase - Math.Floor(phase + .5));
    private static double Pulse(double phase, double width = .5) => phase - Math.Floor(phase) < width ? 1 : -1;
    private static double Note(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);
    private static double Noise(long i)
    {
        uint x = unchecked((uint)i * 747796405u + 2891336453u);
        x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
        return ((x >> 22) ^ x) / (double)uint.MaxValue * 2 - 1;
    }
    private static double Envelope(double t, double duration) => Math.Min(1, t / .004) * Math.Min(1, Math.Max(0, duration - t) / .012);
    public static float Sample(SoundPreset preset, long frame, double intensity, int embarrassment = 0)
    {
        double duration = Duration(preset);
        double t = frame / (double)SampleRate % duration;
        double value;
        if (preset == SoundPreset.Industrial)
        {
            // Two conflicting subdivisions produce a lurching, pneumatic 7-against-8 rhythm.
            double p = t % .125, f = ((int)(t * 4) % 8) switch { 0 => 440, 1 => 622.25, 2 => 880, 3 => 466.16, 4 => 740, 5 => 523.25, 6 => 932.33, _ => 415.3 };
            double motor = .34 * Saw(t * 110) + .28 * Pulse(t * f, .27) + .24 * Saw(t * f * 1.05946);
            double siren = .28 * Sin(1700 * t + 48 * Sin(t * 3.5));
            double metal = .35 * Sin(2100 * t + 8 * Sin(337 * t)) * Math.Exp(-p * 21);
            double drill = .19 * Noise(frame / 3) * (.25 + .75 * Math.Exp(-(t % (1.0 / 7)) * 35));
            double kick = .4 * Sin(67 * t + 12 * (1 - Math.Exp(-(t % .25) * 35))) * Math.Exp(-(t % .25) * 20);
            double gate = p < .092 ? 1 : .28;
            value = (motor * gate + siren + metal + drill + kick) * Envelope(p, .125);
        }
        else if (preset == SoundPreset.DigitalHell)
        {
            int section = (int)(t * 2) % 8;
            double p = t % .5;
            double sweepPhase = 500 * p + (section % 2 == 0 ? 2600 : -400) * p * p;
            double modem = Sin(t * 1200) * Sin(t * 1700) + .6 * Sin(sweepPhase);
            double crushed = Math.Round(Noise((long)(t * (section % 2 == 0 ? 6000 : 2200))) * 5) / 5;
            double glitch = Pulse(t * (section % 3 == 0 ? 880 : 1760), .18) * ((int)(t * 32) % 3 == 0 ? 1 : .15);
            value = (.27 * modem + .27 * crushed + .25 * glitch) * Envelope(p, .5);
        }
        else
        {
            double beat = t * 105 / 60, step = beat * 2, local = step % 1;
            int n = Melody[(int)step % Melody.Length] + ((int)(beat / 16) % 2 == 1 ? 12 : 0);
            double lead = .28 * Pulse(t * Note(n), .3) * Math.Exp(-local * 2.5) * Envelope(local, 1);
            int root = Roots[(int)(beat / 8) % 4];
            double bass = .2 * Saw(t * Note(root + ((int)beat % 2 == 0 ? 0 : 7))) * Math.Exp(-(beat % 1) * 4);
            double arp = .13 * Sin(t * Note(root + Arpeggio[(int)(beat * 4) % 4]));
            double drums = .22 * Sin(55 * t + 6 * (1 - Math.Exp(-(beat % 1) * 20))) * Math.Exp(-(beat % 1) * 12)
                + .12 * Noise(frame) * Math.Exp(-(step % 1) * 20);
            double sourBrass = .23 * Saw(t * Note(n + 6)) * Math.Exp(-local * 4) * Envelope(local, 1);
            double panicTrill = .16 * Pulse(t * Note(n + ((int)(beat * 8) % 2 == 0 ? 12 : 13)), .15) * ((int)(beat * 2) % 4 == 3 ? 1 : .2);
            value = 1.25 * lead + 1.25 * bass + arp + 1.4 * drums + sourBrass + panicTrill;
        }
        if (embarrassment > 0)
        {
            double beat = t * 2.8, p = beat % .5;
            double freq = Note(Fanfare[(int)(beat * 2) % Fanfare.Length]);
            double brass = .25 * (Saw(t * freq) + .45 * Saw(t * freq * 1.005)) * Math.Exp(-p * 5);
            double kazoo = .13 * Sin(t * freq * .5 + 3 * Sin(t * 7));
            value = .65 * value + 1.5 * brass + 1.5 * kazoo + .17 * Noise(frame) * Math.Exp(-(beat % 1) * 25);
        }
        // A separate bass bus reserves low-frequency energy under even the busiest noise.
        // The modem's upper arrangement stays unchanged; all presets now have 40–80 Hz bass.
        double bassFrequency = preset switch { SoundPreset.Industrial => 48, SoundPreset.DigitalHell => 55, _ => Note(Roots[(int)(t * 105 / 60 / 8) % 4] - 12) };
        double beatTime = preset == SoundPreset.CursedMidi ? t * 105 / 60 : t * 2;
        double subBass = .92 * Sin(t * bassFrequency) + .08 * Sin(t * bassFrequency * 2);
        double kickTime = beatTime % 1;
        subBass = .90 * subBass + .10 * Sin(t * 43 + 8 * (1 - Math.Exp(-kickTime * 30))) * Math.Exp(-kickTime * 12);
        // Bounded bus compression preserves composition and bass without changing Windows gain.
        double drive = preset == SoundPreset.DigitalHell ? 1.2 + Math.Clamp(intensity, 0, 1) * 2.8 : 3.8 + Math.Clamp(intensity, 0, 1) * 6.2;
        double edge = Math.Min(1, t / .008) * Math.Min(1, (duration - t) / .008);
        double mix = .32 * Math.Tanh(value * drive) + .95 * subBass;
        return (float)Math.Clamp(.94 * Math.Tanh(mix * 1.6) * edge, -.94, .94);
    }
}
