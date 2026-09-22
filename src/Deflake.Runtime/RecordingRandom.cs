using System;

namespace Deflake.Runtime;

/// <summary>
/// A <see cref="Random"/> that behaves exactly like an inner source of randomness
/// (<see cref="Random.Shared"/> unless a different one is supplied) while recording every value it
/// hands out, under one named stream, into a <see cref="RuntimeSession"/>.
/// </summary>
public sealed class RecordingRandom : Random
{
    private readonly Random _inner;
    private readonly RuntimeSession _session;
    private readonly RandomStreamState _stream;

    public RecordingRandom(RuntimeSession session, string streamId)
        : this(session, streamId, Random.Shared)
    {
    }

    public RecordingRandom(RuntimeSession session, string streamId, Random inner)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentException("A stream id is required.", nameof(streamId));
        }

        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _stream = session.GetOrAddRandomStream(streamId);
    }

    public override int Next()
    {
        var value = _inner.Next();
        Record(seq => new RandomEvent(seq, EventKinds.Next) { IntValue = value });
        return value;
    }

    public override int Next(int maxValue)
    {
        var value = _inner.Next(maxValue);
        Record(seq => new RandomEvent(seq, EventKinds.NextMax) { IntValue = value, MaxValue = maxValue });
        return value;
    }

    public override int Next(int minValue, int maxValue)
    {
        var value = _inner.Next(minValue, maxValue);
        Record(seq => new RandomEvent(seq, EventKinds.NextMinMax) { IntValue = value, MinValue = minValue, MaxValue = maxValue });
        return value;
    }

    public override long NextInt64()
    {
        var value = _inner.NextInt64();
        Record(seq => new RandomEvent(seq, EventKinds.NextInt64) { Int64Value = value });
        return value;
    }

    public override long NextInt64(long maxValue)
    {
        var value = _inner.NextInt64(maxValue);
        Record(seq => new RandomEvent(seq, EventKinds.NextInt64Max) { Int64Value = value, MaxValueInt64 = maxValue });
        return value;
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        var value = _inner.NextInt64(minValue, maxValue);
        Record(seq => new RandomEvent(seq, EventKinds.NextInt64MinMax) { Int64Value = value, MinValueInt64 = minValue, MaxValueInt64 = maxValue });
        return value;
    }

    public override double NextDouble()
    {
        var value = _inner.NextDouble();
        Record(seq => new RandomEvent(seq, EventKinds.NextDouble) { DoubleValue = value });
        return value;
    }

    public override float NextSingle()
    {
        var value = _inner.NextSingle();
        Record(seq => new RandomEvent(seq, EventKinds.NextSingle) { SingleValue = value });
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
        // funnel through the same recording logic below.
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        _inner.NextBytes(buffer);
        var copy = buffer.ToArray();
        Record(seq => new RandomEvent(seq, EventKinds.NextBytes) { BytesValue = copy, Length = copy.Length });
    }

    private void Record(Func<int, RandomEvent> factory)
    {
        _stream.Record(_session.NextSequence, factory);
    }
}
