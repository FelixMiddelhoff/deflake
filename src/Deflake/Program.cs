using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Reporting;
using Deflake.Core.Verdicts;

namespace Deflake;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            var command = args[0];

            return command switch
            {
                "investigate" => await HandleInvestigate(args.Skip(1).ToArray()),
                "repro" => await HandleRepro(args.Skip(1).ToArray()),
                "explain" => HandleExplain(args.Skip(1).ToArray()),
                "--help" or "-h" or "help" => PrintUsage() ?? 0,
                "--version" or "-v" => PrintVersion() ?? 0,
                _ => UsageError($"Unknown command: {command}"),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("deflake: cancelled by user.");
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"deflake: error: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.InnerException?.Message))
            {
                Console.Error.WriteLine($"  {ex.InnerException.Message}");
            }
            return 1;
        }
    }

    private static async Task<int> HandleInvestigate(string[] args)
    {
        var options = ParseInvestigateOptions(args, out var target);

        if (string.IsNullOrEmpty(target))
        {
            return UsageError("No project or solution specified for investigate command.");
        }

        if (!File.Exists(target))
        {
            return UsageError($"Target not found: {target}");
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var runner = new DotnetTestRunner();
            var verifier = new ReproVerifier(runner);
            var pipeline = new InvestigationPipeline(runner, verifier, options);

            var verdicts = await pipeline.InvestigateAsync(target, cts.Token);

            var reporter = options.Format switch
            {
                "json" => (IReporter)new JsonReporter(),
                "markdown" => new MarkdownReporter(),
                _ => new TextReporter(),
            };

            await OutputVerdicts(verdicts, reporter, options.ReportDir);

            var undiagnosedCount = verdicts.Count(v => v.Type == VerdictType.Inconclusive);
            return undiagnosedCount > 0 ? 1 : 0;
        }
        catch (TestRunTimeoutException)
        {
            Console.Error.WriteLine("deflake: investigation timeout (budget exhausted).");
            return 4;
        }
        catch (TestRunExecutionException ex)
        {
            if (ex.Message.Contains("build") || ex.Message.Contains("compile"))
            {
                Console.Error.WriteLine($"deflake: build failed: {ex.Message}");
                if (!string.IsNullOrEmpty(ex.InnerException?.Message))
                {
                    Console.Error.WriteLine(ex.InnerException.Message);
                }
                return 3;
            }
            throw;
        }
    }

    private static async Task<int> HandleRepro(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("No report file specified for repro command.");
        }

        var reportPath = args[0];
        if (!File.Exists(reportPath))
        {
            return UsageError($"Report file not found: {reportPath}");
        }

        Console.Error.WriteLine("deflake: repro command not yet implemented.");
        return 2;
    }

    private static int HandleExplain(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("No verdict ID specified for explain command.");
        }

        Console.Error.WriteLine("deflake: explain command not yet implemented.");
        return 2;
    }

    private static async Task OutputVerdicts(IEnumerable<Verdict> verdicts, IReporter reporter, string? reportDir)
    {
        if (reportDir != null && !Directory.Exists(reportDir))
        {
            Directory.CreateDirectory(reportDir);
        }

        foreach (var verdict in verdicts)
        {
            var output = await reporter.FormatAsync(verdict);

            if (reportDir != null)
            {
                var fileName = $"{verdict.Test.FullyQualifiedName.Replace('.', '_')}.txt";
                var filePath = Path.Combine(reportDir, fileName);
                File.WriteAllText(filePath, output);
            }
            else
            {
                Console.Out.Write(output);
            }
        }
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"deflake: error: {message}");
        Console.Error.WriteLine("Try 'deflake --help' for usage information.");
        return 2;
    }

    private static int? PrintUsage()
    {
        Console.Out.WriteLine("deflake — finds out why a test is flaky");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage:");
        Console.Out.WriteLine("  deflake investigate <project|solution> [options]");
        Console.Out.WriteLine("  deflake repro <report.json>");
        Console.Out.WriteLine("  deflake explain <VERDICT-ID>");
        Console.Out.WriteLine("  deflake --help | -h | help");
        Console.Out.WriteLine("  deflake --version | -v");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Investigate command options:");
        Console.Out.WriteLine("  --test <FQN>              Test to investigate (may be repeated)");
        Console.Out.WriteLine("  --from-trx <file>         Read failures from a TRX file");
        Console.Out.WriteLine("  --runs <n>                Run count before adaptive runs (default: 20)");
        Console.Out.WriteLine("  --max-runs <n>            Maximum runs per condition (default: same as --runs)");
        Console.Out.WriteLine("  --timeout <minutes>       Time budget for investigation (default: 60)");
        Console.Out.WriteLine("  --framework <tfm>         Target framework moniker (e.g. net8.0)");
        Console.Out.WriteLine("  --configuration <c>       Build configuration (default: Debug)");
        Console.Out.WriteLine("  --no-build                Do not rebuild before testing");
        Console.Out.WriteLine("  --report <dir>            Write reports to directory instead of console");
        Console.Out.WriteLine("  --format text|markdown|json  Output format (default: text)");
        Console.Out.WriteLine("  --record-replay           Record TimeProvider/Random during isolation run,");
        Console.Out.WriteLine("                            then replay deterministically (requires Deflake.Runtime)");
        return 0;
    }

    private static int? PrintVersion()
    {
        Console.Out.WriteLine("deflake 0.1.0");
        return 0;
    }

    private static InvestigationOptions ParseInvestigateOptions(string[] args, out string? target)
    {
        target = null;
        var options = new InvestigationOptions();
        var tests = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                if (arg == "--test" && i + 1 < args.Length)
                {
                    tests.Add(args[++i]);
                }
                else if (arg == "--from-trx" && i + 1 < args.Length)
                {
                    options.FromTrx = args[++i];
                }
                else if (arg == "--runs" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out var runs))
                    {
                        options.Runs = runs;
                    }
                }
                else if (arg == "--max-runs" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out var maxRuns))
                    {
                        options.MaxRuns = maxRuns;
                    }
                }
                else if (arg == "--timeout" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out var timeout))
                    {
                        options.TimeoutMinutes = timeout;
                    }
                }
                else if (arg == "--framework" && i + 1 < args.Length)
                {
                    options.Framework = args[++i];
                }
                else if (arg == "--configuration" && i + 1 < args.Length)
                {
                    options.Configuration = args[++i];
                }
                else if (arg == "--no-build")
                {
                    options.NoBuild = true;
                }
                else if (arg == "--report" && i + 1 < args.Length)
                {
                    options.ReportDir = args[++i];
                }
                else if (arg == "--format" && i + 1 < args.Length)
                {
                    options.Format = args[++i];
                }
                else if (arg == "--record-replay")
                {
                    options.RecordReplay = true;
                }
            }
            else if (target == null)
            {
                target = arg;
            }
        }

        if (tests.Count > 0)
        {
            options.Tests = tests;
        }

        return options;
    }
}
