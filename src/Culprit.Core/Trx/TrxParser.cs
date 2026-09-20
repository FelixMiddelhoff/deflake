using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Culprit.Core.Trx;

/// <summary>
/// Reads the TRX result files that <c>dotnet test --logger trx</c> writes. Test frameworks fill
/// these files differently (xUnit puts the full name in <c>testName</c>, NUnit only the method),
/// so the full name is built from the test definition rather than taken from the result.
/// </summary>
public static class TrxParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TestRunResult ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static TestRunResult ParseText(string trx)
    {
        using var reader = new StringReader(trx);
        return Parse(reader);
    }

    public static TestRunResult Parse(Stream stream)
    {
        using var reader = XmlReader.Create(stream, ReaderSettings);
        return Read(reader);
    }

    private static TestRunResult Parse(TextReader text)
    {
        using var reader = XmlReader.Create(text, ReaderSettings);
        return Read(reader);
    }

    /// <summary>
    /// Untrusted input: no DTDs or external entities, and no character checks, because test
    /// output may legitimately contain characters XML forbids once a framework has escaped them.
    /// </summary>
    private static XmlReaderSettings ReaderSettings { get; } = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CheckCharacters = false,
    };

    private static TestRunResult Read(XmlReader reader)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new TrxFormatException("The file is not valid XML: " + exception.Message, exception);
        }

        var root = document.Root;
        if (root is null || root.Name != Ns + "TestRun")
        {
            throw new TrxFormatException("The file is not a TRX file: the root element is not <TestRun>.");
        }

        var definitions = ReadDefinitions(root);
        var results = root.Element(Ns + "Results")?.Elements(Ns + "UnitTestResult") ?? Enumerable.Empty<XElement>();
        var tests = results
            .Where(result => !IsDataDrivenParent(result))
            .Select(result => ReadResult(result, definitions))
            .ToList();
        return new TestRunResult(OrderByStart(tests));
    }

    private static Dictionary<string, string> ReadDefinitions(XElement root)
    {
        var fullNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitions = root.Element(Ns + "TestDefinitions")?.Elements(Ns + "UnitTest") ?? Enumerable.Empty<XElement>();
        foreach (var definition in definitions)
        {
            var id = (string?)definition.Attribute("id");
            var method = definition.Element(Ns + "TestMethod");
            var className = (string?)method?.Attribute("className");
            var methodName = (string?)method?.Attribute("name");
            if (id is not null && !string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(methodName))
            {
                fullNames[id] = WithoutArguments(className + "." + methodName);
            }
        }

        return fullNames;
    }

    /// <summary>MSTest writes one summary result per data-driven test and one result per data row; keep the rows.</summary>
    private static bool IsDataDrivenParent(XElement result)
    {
        return (string?)result.Attribute("resultType") == "DataDrivenTest";
    }

    private static TestResult ReadResult(XElement result, Dictionary<string, string> fullNames)
    {
        var displayName = (string?)result.Attribute("testName") ?? string.Empty;
        var testId = (string?)result.Attribute("testId");
        var fullName = testId is not null && fullNames.TryGetValue(testId, out var known) ? known : WithoutArguments(displayName);

        var rawOutcome = (string?)result.Attribute("outcome") ?? string.Empty;
        var output = result.Element(Ns + "Output");
        var error = output?.Element(Ns + "ErrorInfo");

        return new TestResult(
            fullName,
            displayName,
            MapOutcome(rawOutcome),
            rawOutcome,
            ReadTime((string?)result.Attribute("startTime")),
            ReadTime((string?)result.Attribute("endTime")),
            ReadDuration((string?)result.Attribute("duration")),
            (string?)error?.Element(Ns + "Message"),
            (string?)error?.Element(Ns + "StackTrace"),
            (string?)output?.Element(Ns + "StdOut"));
    }

    /// <summary>
    /// NUnit puts a theory case's arguments into the method name (<c>Cases(1)</c>), xUnit does not.
    /// A test is identified by its method, so cut at the first parenthesis: a method name can
    /// never contain one, which makes this safe even when the arguments contain parentheses.
    /// </summary>
    private static string WithoutArguments(string name)
    {
        var parenthesis = name.IndexOf('(');
        return parenthesis < 0 ? name : name.Substring(0, parenthesis);
    }

    private static TestOutcome MapOutcome(string raw)
    {
        switch (raw)
        {
            case "Passed":
            case "PassedButRunAborted":
            case "Completed":
                return TestOutcome.Passed;
            case "Failed":
            case "Error":
            case "Timeout":
                return TestOutcome.Failed;
            case "Aborted":
            case "Disconnected":
                return TestOutcome.Aborted;
            default:
                // NotExecuted, NotRunnable, Inconclusive, Pending, Warning, InProgress and anything new.
                return TestOutcome.NotRun;
        }
    }

    private static DateTimeOffset ReadTime(string? text)
    {
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : DateTimeOffset.MinValue;
    }

    private static TimeSpan ReadDuration(string? text)
    {
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var duration) ? duration : TimeSpan.Zero;
    }

    /// <summary>Stable sort: results without a start time keep the position they had in the file.</summary>
    private static List<TestResult> OrderByStart(List<TestResult> tests)
    {
        return tests
            .Select((test, index) => (test, index))
            .OrderBy(pair => pair.test.StartTime)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.test)
            .ToList();
    }
}
