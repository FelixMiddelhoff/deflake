using System;

namespace Deflake.Core.Execution;

/// <summary>
/// The <c>dotnet test</c> process tree did not exit within its <see cref="TestRunRequest.Timeout"/>
/// and was killed. Whatever it was doing (a hang, a deadlock, an infinite loop) is itself evidence,
/// but there is no TRX file to read: the caller decides how to score this run.
/// </summary>
public sealed class TestRunTimeoutException : Exception
{
    public TestRunTimeoutException(TimeSpan timeout, string message)
        : base(message)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
