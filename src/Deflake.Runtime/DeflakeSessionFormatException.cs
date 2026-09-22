using System;

namespace Deflake.Runtime;

/// <summary>
/// The file <see cref="SessionSerializer.Read"/> was asked to read is not a readable Deflake.Runtime
/// session file: missing, truncated, not JSON, or written by a schema version this build does not
/// understand. Mirrors how <c>Deflake.Core.Trx.TrxFormatException</c> refuses a bad TRX file - refused
/// with a named reason, never guessed at.
/// </summary>
public sealed class DeflakeSessionFormatException : Exception
{
    public DeflakeSessionFormatException(string message)
        : base(message)
    {
    }

    public DeflakeSessionFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
