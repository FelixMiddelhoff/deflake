using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Deflake.Runtime.Tests;

public class SessionSerializerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"deflake-session-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        if (File.Exists(_path + ".tmp"))
        {
            File.Delete(_path + ".tmp");
        }
    }

    private static RuntimeSession BuildSampleSession()
    {
        var session = RuntimeSession.StartRecording("MyApp.Tests.OrderTests.ExpiresAfterTimeout");
        var timeProvider = new RecordingTimeProvider(session, new ManualTimeProvider());
        timeProvider.GetUtcNow();
        timeProvider.GetTimestamp();
        var timer = (ManualTimer)timeProvider.CreateTimer(_ => { }, null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        timer.Fire();

        var random = new RecordingRandom(session, "OrderTests.ctor", new Random(1));
        random.NextDouble();
        random.Next(1, 10);

        return session;
    }

    [Fact]
    public void Write_then_Read_round_trips_every_recorded_value()
    {
        var original = BuildSampleSession();

        SessionSerializer.Write(original, _path);
        var restored = SessionSerializer.Read(_path);

        Assert.Equal(original.TestName, restored.TestName);
        Assert.Equal(original.Machine, restored.Machine);
        Assert.Equal(original.DotnetVersion, restored.DotnetVersion);
        Assert.Equal(original.TimestampFrequency, restored.TimestampFrequency);

        Assert.Equal(
            original.TimeProviderEvents.Select(e => (e.Seq, e.Kind, e.Utc, e.Timestamp, e.DueTime, e.Period, e.TimerSeq)),
            restored.TimeProviderEvents.Select(e => (e.Seq, e.Kind, e.Utc, e.Timestamp, e.DueTime, e.Period, e.TimerSeq)));

        var originalRandom = original.RandomStreams["OrderTests.ctor"];
        var restoredRandom = restored.RandomStreams["OrderTests.ctor"];
        Assert.Equal(originalRandom.Count, restoredRandom.Count);
        for (var i = 0; i < originalRandom.Count; i++)
        {
            Assert.Equal(originalRandom[i].Kind, restoredRandom[i].Kind);
            Assert.Equal(originalRandom[i].DoubleValue, restoredRandom[i].DoubleValue);
            Assert.Equal(originalRandom[i].IntValue, restoredRandom[i].IntValue);
            Assert.Equal(originalRandom[i].MinValue, restoredRandom[i].MinValue);
            Assert.Equal(originalRandom[i].MaxValue, restoredRandom[i].MaxValue);
        }
    }

    [Fact]
    public void The_written_JSON_has_the_documented_shape()
    {
        var session = BuildSampleSession();

        SessionSerializer.Write(session, _path);
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("MyApp.Tests.OrderTests.ExpiresAfterTimeout", root.GetProperty("test").GetString());
        Assert.True(root.TryGetProperty("recordedAtUtc", out _));
        Assert.True(root.TryGetProperty("machine", out _));
        Assert.True(root.TryGetProperty("dotnetVersion", out _));
        Assert.True(root.TryGetProperty("timestampFrequency", out _));

        var timeProviderEvents = root.GetProperty("timeProvider").EnumerateArray().ToList();
        Assert.Equal(4, timeProviderEvents.Count); // GetUtcNow, GetTimestamp, CreateTimer, TimerFired
        Assert.Equal("GetUtcNow", timeProviderEvents[0].GetProperty("kind").GetString());
        Assert.Equal(0, timeProviderEvents[0].GetProperty("seq").GetInt32());
        Assert.Equal("CreateTimer", timeProviderEvents[2].GetProperty("kind").GetString());
        Assert.True(timeProviderEvents[2].TryGetProperty("dueTime", out _));
        Assert.Equal("TimerFired", timeProviderEvents[3].GetProperty("kind").GetString());
        Assert.True(timeProviderEvents[3].TryGetProperty("timerSeq", out _));

        var randomStreams = root.GetProperty("randomStreams");
        Assert.True(randomStreams.TryGetProperty("OrderTests.ctor", out var streamEvents));
        Assert.Equal(2, streamEvents.GetArrayLength());
        Assert.Equal("NextDouble", streamEvents[0].GetProperty("kind").GetString());
        Assert.Equal("NextMinMax", streamEvents[1].GetProperty("kind").GetString());
    }

    [Fact]
    public void Write_creates_the_parent_directory_when_it_does_not_exist()
    {
        var nestedPath = Path.Combine(Path.GetTempPath(), $"deflake-nested-{Guid.NewGuid():N}", "runtime-sessions", "session.json");
        var session = BuildSampleSession();

        try
        {
            SessionSerializer.Write(session, nestedPath);

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            var dir = Path.GetDirectoryName(Path.GetDirectoryName(nestedPath))!;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Write_leaves_no_temp_file_behind()
    {
        var session = BuildSampleSession();

        SessionSerializer.Write(session, _path);

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Read_throws_a_session_format_exception_for_a_missing_file()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.json");

        Assert.Throws<DeflakeSessionFormatException>(() => SessionSerializer.Read(missingPath));
    }

    [Fact]
    public void Read_throws_a_session_format_exception_for_corrupt_JSON()
    {
        File.WriteAllText(_path, "{ this is not valid json ");

        Assert.Throws<DeflakeSessionFormatException>(() => SessionSerializer.Read(_path));
    }

    [Fact]
    public void Read_throws_a_session_format_exception_for_a_truncated_file()
    {
        var session = BuildSampleSession();
        SessionSerializer.Write(session, _path);
        var fullJson = File.ReadAllText(_path);
        File.WriteAllText(_path, fullJson.Substring(0, fullJson.Length / 2));

        Assert.Throws<DeflakeSessionFormatException>(() => SessionSerializer.Read(_path));
    }

    [Fact]
    public void Read_throws_a_session_format_exception_for_an_unrecognized_schema_version()
    {
        File.WriteAllText(
            _path,
            "{ \"schemaVersion\": 999, \"test\": \"x\", \"recordedAtUtc\": \"2026-01-01T00:00:00Z\", "
            + "\"machine\": \"m\", \"dotnetVersion\": \"8.0.0\", \"timestampFrequency\": 1 }");

        var ex = Assert.Throws<DeflakeSessionFormatException>(() => SessionSerializer.Read(_path));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Read_throws_a_session_format_exception_for_an_empty_file()
    {
        File.WriteAllText(_path, string.Empty);

        Assert.Throws<DeflakeSessionFormatException>(() => SessionSerializer.Read(_path));
    }

    [Fact]
    public void Write_rejects_a_null_session_or_an_empty_path()
    {
        Assert.Throws<ArgumentNullException>(() => SessionSerializer.Write(null!, _path));
        Assert.Throws<ArgumentException>(() => SessionSerializer.Write(RuntimeSession.StartRecording(), ""));
    }

    [Fact]
    public void Read_rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => SessionSerializer.Read(""));
    }
}
