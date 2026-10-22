using System.Security.Cryptography;

namespace Rise.Core;

public enum PuzzleResult { Ignored, Correct, Retry, Complete }

public sealed class Puzzle
{
    private readonly Func<int> random;
    public Puzzle(Func<int>? random = null) { this.random = random ?? (() => RandomNumberGenerator.GetInt32(4)); Reset(); }
    public const int SequenceLength = 5;
    public int Position { get; private set; }
    public bool Accepting { get; private set; }
    public bool Completed { get; private set; }
    public int[] Sequence { get; private set; } = [];
    public void Reset() { Completed = false; NewSequence(); }
    private void NewSequence()
    {
        Position = 0; Accepting = false;
        Sequence = Enumerable.Range(0, SequenceLength).Select(_ => Math.Clamp(random(), 0, 3)).ToArray();
    }
    public void Replay() { if (!Completed) NewSequence(); }
    public void BeginInput() { if (!Completed) Accepting = true; }
    public PuzzleResult Press(int tile)
    {
        if (!Accepting || Completed || tile is < 0 or > 3) return PuzzleResult.Ignored;
        if (tile != Sequence[Position]) { NewSequence(); return PuzzleResult.Retry; }
        Position++;
        if (Position != Sequence.Length) return PuzzleResult.Correct;
        Accepting = false;
        Completed = true;
        return PuzzleResult.Complete;
    }
}
