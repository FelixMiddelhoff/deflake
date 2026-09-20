using System;

namespace Culprit.Core.Trx;

/// <summary>The file is not a readable TRX test result file.</summary>
public sealed class TrxFormatException : Exception
{
    public TrxFormatException(string message)
        : base(message)
    {
    }

    public TrxFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
