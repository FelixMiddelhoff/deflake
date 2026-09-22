using System;

namespace Deflake.Runtime;

/// <summary>
/// A <see cref="Random"/> that hands back exactly the values a <see cref="RecordingRandom"/> recorded
/// for the same named stream, in the order they were recorded, instead of generating new ones. Any call
/// whose kind, argument shape, or position does not match what was recorded throws
/// <see cref="DeflakeReplayDivergenceException"/> immediately.
/// </summary>
public sealed class ReplayRandom : Random
{
    private readonly RandomStreamState _stream;
    private readonly string _streamId;

    public ReplayRandom(RuntimeSession session, string streamId)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentException("A stream id is required.", nameof(streamId));
        }

        _streamId = streamId;
        _stream = session.GetOrAddRandomStream(streamId);
    }

    public override int Next()
    {
        var evt = PopNext(EventKinds.Next, "Next()");
        return RequireInt(evt, "Next()");
    }

    public override int Next(int maxValue)
    {
        var evt = PopNext(EventKinds.NextMax, $"Next({maxValue})");
        if (evt.MaxValue != maxValue)
        {
            throw ArgumentDivergence(evt, $"Next({maxValue})");
        }

        return RequireInt(evt, $"Next({maxValue})");
    }

    public override int Next(int minValue, int maxValue)
    {
        var evt = PopNext(EventKinds.NextMinMax, $"Next({minValue}, {maxValue})");
        if (evt.MinValue != minValue || evt.MaxValue != maxValue)
        {
            throw ArgumentDivergence(evt, $"Next({minValue}, {maxValue})");
        }

        return RequireInt(evt, $"Next({minValue}, {maxValue})");
    }

    public override long NextInt64()
    {
        var evt = PopNext(EventKinds.NextInt64, "NextInt64()");
        return RequireInt64(evt, "NextInt64()");
    }

    public override long NextInt64(long maxValue)
    {
        var evt = PopNext(EventKinds.NextInt64Max, $"NextInt64({maxValue})");
        if (evt.MaxValueInt64 != maxValue)
        {
            throw ArgumentDivergence(evt, $"NextInt64({maxValue})");
        }

        return RequireInt64(evt, $"NextInt64({maxValue})");
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        var evt = PopNext(EventKinds.NextInt64MinMax, $"NextInt64({minValue}, {maxValue})");
        if (evt.MinValueInt64 != minValue || evt.MaxValueInt64 != maxValue)
        {
            throw ArgumentDivergence(evt, $"NextInt64({minValue}, {maxValue})");
        }

        return RequireInt64(evt, $"NextInt64({minValue}, {maxValue})");
    }

    public override double NextDouble()
    {
        var evt = PopNext(EventKinds.NextDouble, "NextDouble()");
        if (evt.DoubleValue is not { } value)
        {
            throw MissingValue(evt, "NextDouble()");
        }

        return value;
    }

    public override float NextSingle()
    {
        var evt = PopNext(EventKinds.NextSingle, "NextSingle()");
        if (evt.SingleValue is not { } value)
        {
            throw MissingValue(evt, "NextSingle()");
        }

        return value;
    }

    public override void NextBytes(byte[] buffer)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        // The base Random.NextBytes(byte[]) implementation does not dispatch to the virtual
        // NextBytes(Span<byte>) override, so both overloads are overridden explicitly here and both
        // funnel through the same replay logic below.
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        var requested = $"NextBytes(length {buffer.Length})";
        var evt = PopNext(EventKinds.NextBytes, requested);
        if (evt.BytesValue is not { } bytes || bytes.Length != buffer.Length)
        {
            throw ArgumentDivergence(evt, requested);
        }

        bytes.CopyTo(buffer);
    }

    private int RequireInt(RandomEvent evt, string requested)
    {
        if (evt.IntValue is not { } value)
        {
            throw MissingValue(evt, requested);
        }

        return value;
    }

    private long RequireInt64(RandomEvent evt, string requested)
    {
        if (evt.Int64Value is not { } value)
        {
            throw MissingValue(evt, requested);
        }

        return value;
    }

    private RandomEvent PopNext(string expectedKind, string requested)
    {
        var evt = _stream.PopNext();
        if (evt is null)
        {
            throw new DeflakeReplayDivergenceException(_streamId, expected: "no more calls (stream exhausted)", actual: requested);
        }

        if (evt.Kind != expectedKind)
        {
            throw new DeflakeReplayDivergenceException(_streamId, expected: $"{evt.Kind} (seq {evt.Seq})", actual: requested);
        }

        return evt;
    }

    private DeflakeReplayDivergenceException ArgumentDivergence(RandomEvent evt, string requested)
    {
        return new DeflakeReplayDivergenceException(_streamId, expected: $"{evt.Kind} with different arguments (seq {evt.Seq})", actual: requested);
    }

    private DeflakeReplayDivergenceException MissingValue(RandomEvent evt, string requested)
    {
        return new DeflakeReplayDivergenceException(
            _streamId,
            expected: $"{evt.Kind} recorded without the expected payload (seq {evt.Seq})",
            actual: requested);
    }
}
