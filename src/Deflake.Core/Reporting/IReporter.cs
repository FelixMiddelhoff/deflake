using System.Threading.Tasks;
using Deflake.Core.Verdicts;

namespace Deflake.Core.Reporting;

/// <summary>
/// Formats a verdict into its output representation. The formatter is stateless and produces
/// deterministic output given the same verdict, for testing and reproducibility.
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Formats a verdict into a string representation. The same verdict always produces identical output.
    /// </summary>
    /// <param name="verdict">The verdict to format.</param>
    /// <returns>The formatted verdict as a string.</returns>
    Task<string> FormatAsync(Verdict verdict);
}
