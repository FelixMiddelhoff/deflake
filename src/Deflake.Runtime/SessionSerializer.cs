using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deflake.Runtime;

/// <summary>
/// Reads and writes a <see cref="RuntimeSession"/> as the JSON session format (schema version 1)
/// described in the runtime design doc: <c>System.Text.Json</c> only, no new dependency. A version
/// bump is expected on any breaking change to the shape below; a reader refuses an unknown schema
/// version with <see cref="DeflakeSessionFormatException"/> rather than guessing at its shape.
/// </summary>
public static class SessionSerializer
{
    /// <summary>The schema version this build of Deflake.Runtime writes, and the only one it reads.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes <paramref name="session"/> to <paramref name="path"/>, creating parent directories as needed.</summary>
    public static void Write(RuntimeSession session, string path)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        var dto = ToDto(session);
        var json = JsonSerializer.Serialize(dto, Options);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file and move it into place so a reader never observes a partially-written
        // file: a process killed mid-write (timeout, crash) leaves either the previous file or nothing,
        // never a truncated one.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Deserializes the session at <paramref name="path"/>, refusing a missing, corrupt, or unrecognized-schema file.</summary>
    public static RuntimeSession Read(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new DeflakeSessionFormatException($"No Deflake.Runtime session file was found at '{path}'.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new DeflakeSessionFormatException($"'{path}' could not be read: {ex.Message}", ex);
        }

        SessionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SessionDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new DeflakeSessionFormatException($"'{path}' is not a valid Deflake.Runtime session file: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new DeflakeSessionFormatException($"'{path}' is empty and is not a Deflake.Runtime session file.");
        }

        if (dto.SchemaVersion != CurrentSchemaVersion)
        {
            throw new DeflakeSessionFormatException(
                $"'{path}' has schema version {dto.SchemaVersion}, but this build of Deflake.Runtime only "
                + $"understands version {CurrentSchemaVersion}.");
        }

        return FromDto(dto);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new TimeProviderEventJsonConverter());
        options.Converters.Add(new RandomEventJsonConverter());
        return options;
    }

    private static SessionDto ToDto(RuntimeSession session)
    {
        return new SessionDto
        {
            SchemaVersion = CurrentSchemaVersion,
            Test = session.TestName,
            RecordedAtUtc = session.RecordedAtUtc,
            Machine = session.Machine,
            DotnetVersion = session.DotnetVersion,
            TimestampFrequency = session.TimestampFrequency,
            TimeProvider = session.TimeProviderEvents.ToList(),
            RandomStreams = session.RandomStreams.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.Ordinal),
        };
    }

    private static RuntimeSession FromDto(SessionDto dto)
    {
        var randomStreams = (dto.RandomStreams ?? new Dictionary<string, List<RandomEvent>>())
            .Select(pair => new KeyValuePair<string, IReadOnlyList<RandomEvent>>(pair.Key, pair.Value));

        return RuntimeSession.FromSerialized(
            dto.Test ?? string.Empty,
            dto.RecordedAtUtc,
            dto.Machine ?? string.Empty,
            dto.DotnetVersion ?? string.Empty,
            dto.TimestampFrequency,
            dto.TimeProvider ?? new List<TimeProviderEvent>(),
            randomStreams);
    }

    /// <summary>The top-level session document shape. Property names are camelCased by <see cref="Options"/>.</summary>
    private sealed class SessionDto
    {
        public int SchemaVersion { get; set; }

        public string? Test { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public string? Machine { get; set; }

        public string? DotnetVersion { get; set; }

        public long TimestampFrequency { get; set; }

        public List<TimeProviderEvent>? TimeProvider { get; set; }

        public Dictionary<string, List<RandomEvent>>? RandomStreams { get; set; }
    }
}

/// <summary>
/// Writes/reads a <see cref="TimeProviderEvent"/> as <c>{ seq, kind, ... }</c> with only the fields that
/// apply to that event's <c>kind</c> present, matching the shape in the runtime design doc.
/// </summary>
internal sealed class TimeProviderEventJsonConverter : JsonConverter<TimeProviderEvent>
{
    public override TimeProviderEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var seq = root.GetProperty("seq").GetInt32();
        var kind = root.GetProperty("kind").GetString() ?? string.Empty;

        return kind switch
        {
            EventKinds.GetUtcNow => new TimeProviderEvent(seq, kind) { Utc = root.GetProperty("value").GetDateTimeOffset() },
            EventKinds.GetTimestamp => new TimeProviderEvent(seq, kind) { Timestamp = root.GetProperty("value").GetInt64() },
            EventKinds.CreateTimer => new TimeProviderEvent(seq, kind)
            {
                DueTime = TimeSpan.Parse(root.GetProperty("dueTime").GetString()!),
                Period = TimeSpan.Parse(root.GetProperty("period").GetString()!),
            },
            EventKinds.TimerFired => new TimeProviderEvent(seq, kind)
            {
                TimerSeq = root.GetProperty("timerSeq").GetInt32(),
                Utc = root.GetProperty("atUtc").GetDateTimeOffset(),
            },
            _ => new TimeProviderEvent(seq, kind),
        };
    }

    public override void Write(Utf8JsonWriter writer, TimeProviderEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("seq", value.Seq);
        writer.WriteString("kind", value.Kind);

        switch (value.Kind)
        {
            case EventKinds.GetUtcNow:
                writer.WriteString("value", value.Utc!.Value);
                break;
            case EventKinds.GetTimestamp:
                writer.WriteNumber("value", value.Timestamp!.Value);
                break;
            case EventKinds.CreateTimer:
                writer.WriteString("dueTime", value.DueTime!.Value.ToString("c"));
                writer.WriteString("period", value.Period!.Value.ToString("c"));
                break;
            case EventKinds.TimerFired:
                writer.WriteNumber("timerSeq", value.TimerSeq!.Value);
                writer.WriteString("atUtc", value.Utc!.Value);
                break;
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Writes/reads a <see cref="RandomEvent"/> as <c>{ seq, kind, ... }</c> with only the fields that apply
/// to that event's <c>kind</c> present, matching the shape in the runtime design doc.
/// </summary>
internal sealed class RandomEventJsonConverter : JsonConverter<RandomEvent>
{
    public override RandomEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var seq = root.GetProperty("seq").GetInt32();
        var kind = root.GetProperty("kind").GetString() ?? string.Empty;

        return kind switch
        {
            EventKinds.Next => new RandomEvent(seq, kind) { IntValue = root.GetProperty("value").GetInt32() },
            EventKinds.NextMax => new RandomEvent(seq, kind)
            {
                IntValue = root.GetProperty("value").GetInt32(),
                MaxValue = root.GetProperty("max").GetInt32(),
            },
            EventKinds.NextMinMax => new RandomEvent(seq, kind)
            {
                IntValue = root.GetProperty("value").GetInt32(),
                MinValue = root.GetProperty("min").GetInt32(),
                MaxValue = root.GetProperty("max").GetInt32(),
            },
            EventKinds.NextInt64 => new RandomEvent(seq, kind) { Int64Value = root.GetProperty("value").GetInt64() },
            EventKinds.NextInt64Max => new RandomEvent(seq, kind)
            {
                Int64Value = root.GetProperty("value").GetInt64(),
                MaxValueInt64 = root.GetProperty("max").GetInt64(),
            },
            EventKinds.NextInt64MinMax => new RandomEvent(seq, kind)
            {
                Int64Value = root.GetProperty("value").GetInt64(),
                MinValueInt64 = root.GetProperty("min").GetInt64(),
                MaxValueInt64 = root.GetProperty("max").GetInt64(),
            },
            EventKinds.NextDouble => new RandomEvent(seq, kind) { DoubleValue = root.GetProperty("value").GetDouble() },
            EventKinds.NextSingle => new RandomEvent(seq, kind) { SingleValue = root.GetProperty("value").GetSingle() },
            EventKinds.NextBytes => new RandomEvent(seq, kind)
            {
                BytesValue = root.GetProperty("value").GetBytesFromBase64(),
                Length = root.GetProperty("length").GetInt32(),
            },
            _ => new RandomEvent(seq, kind),
        };
    }

    public override void Write(Utf8JsonWriter writer, RandomEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("seq", value.Seq);
        writer.WriteString("kind", value.Kind);

        switch (value.Kind)
        {
            case EventKinds.Next:
                writer.WriteNumber("value", value.IntValue!.Value);
                break;
            case EventKinds.NextMax:
                writer.WriteNumber("value", value.IntValue!.Value);
                writer.WriteNumber("max", value.MaxValue!.Value);
                break;
            case EventKinds.NextMinMax:
                writer.WriteNumber("value", value.IntValue!.Value);
                writer.WriteNumber("min", value.MinValue!.Value);
                writer.WriteNumber("max", value.MaxValue!.Value);
                break;
            case EventKinds.NextInt64:
                writer.WriteNumber("value", value.Int64Value!.Value);
                break;
            case EventKinds.NextInt64Max:
                writer.WriteNumber("value", value.Int64Value!.Value);
                writer.WriteNumber("max", value.MaxValueInt64!.Value);
                break;
            case EventKinds.NextInt64MinMax:
                writer.WriteNumber("value", value.Int64Value!.Value);
                writer.WriteNumber("min", value.MinValueInt64!.Value);
                writer.WriteNumber("max", value.MaxValueInt64!.Value);
                break;
            case EventKinds.NextDouble:
                writer.WriteNumber("value", value.DoubleValue!.Value);
                break;
            case EventKinds.NextSingle:
                writer.WriteNumber("value", value.SingleValue!.Value);
                break;
            case EventKinds.NextBytes:
                writer.WriteBase64String("value", value.BytesValue!);
                writer.WriteNumber("length", value.Length!.Value);
                break;
        }

        writer.WriteEndObject();
    }
}
