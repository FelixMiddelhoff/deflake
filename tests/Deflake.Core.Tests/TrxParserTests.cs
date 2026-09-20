using System;
using System.IO;
using System.Linq;
using System.Text;
using Deflake.Core.Trx;
using Xunit;

namespace Deflake.Core.Tests;

public class TrxParserTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // ---- Real files written by dotnet test -------------------------------------------------

    [Fact]
    public void A_real_xunit_file_yields_every_result_with_full_names()
    {
        var run = TrxParser.ParseFile(FixturePath("xunit.trx"));

        Assert.Equal(7, run.Tests.Count);
        Assert.Contains(run.Tests, test => test.FullName == "Fix.SampleTests.Passes" && test.Outcome == TestOutcome.Passed);
        Assert.Contains(run.Tests, test => test.FullName == "Fix.SampleTests.Skipped" && test.Outcome == TestOutcome.NotRun);
        Assert.Equal(3, run.Failures.Count());
    }

    [Fact]
    public void A_real_xunit_file_keeps_theory_cases_apart_but_under_one_full_name()
    {
        var run = TrxParser.ParseFile(FixturePath("xunit.trx"));

        var cases = run.Named("Fix.SampleTests.Theory").OrderBy(test => test.DisplayName).ToList();

        Assert.Equal(new[] { "Fix.SampleTests.Theory(n: 1)", "Fix.SampleTests.Theory(n: 2)" }, cases.Select(test => test.DisplayName));
        Assert.Equal(new[] { TestOutcome.Passed, TestOutcome.Failed }, cases.Select(test => test.Outcome));
    }

    [Fact]
    public void A_real_xunit_file_provides_message_and_stack_of_failures()
    {
        var run = TrxParser.ParseFile(FixturePath("xunit.trx"));

        var failure = Assert.Single(run.Named("Fix.SampleTests.Fails"));

        Assert.Contains("Assert.Equal() Failure", failure.ErrorMessage);
        Assert.Contains("Tests.cs:line 9", failure.StackTrace);
        Assert.Equal("Failed", failure.RawOutcome);
    }

    [Fact]
    public void Special_characters_in_a_message_survive()
    {
        var run = TrxParser.ParseFile(FixturePath("xunit.trx"));

        var failure = Assert.Single(run.Named("Fix.SampleTests.Throws"));

        Assert.EndsWith("boom & <xml> \"quotes\"", failure.ErrorMessage);
    }

    [Fact]
    public void Arguments_in_a_method_name_are_not_part_of_the_full_name()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="Cases(&quot;x (y)&quot;)" outcome="Passed" />
                <UnitTestResult testId="b" testName="Ns.Cls.Fallback(1)" outcome="Passed" />
              </Results>
              <TestDefinitions>
                <UnitTest name="Cases" id="a"><TestMethod className="Ns.Cls" name="Cases(&quot;x (y)&quot;)" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;

        var run = TrxParser.ParseText(trx);

        Assert.Equal(new[] { "Ns.Cls.Cases", "Ns.Cls.Fallback" }, run.Tests.Select(test => test.FullName));
        Assert.Equal("Cases(\"x (y)\")", run.Tests[0].DisplayName);
    }

    [Fact]
    public void A_real_nunit_file_gets_full_names_from_the_definitions_although_the_result_holds_only_the_method_name()
    {
        var run = TrxParser.ParseFile(FixturePath("nunit.trx"));

        Assert.Equal(6, run.Tests.Count);
        var cases = run.Named("Fix.SampleTests.Cases").OrderBy(test => test.DisplayName).ToList();
        Assert.Equal(new[] { "Cases(1)", "Cases(2)" }, cases.Select(test => test.DisplayName));
        Assert.Equal(TestOutcome.Failed, Assert.Single(run.Named("Fix.SampleTests.Throws")).Outcome);
    }

    [Fact]
    public void Results_are_ordered_by_start_time()
    {
        var run = TrxParser.ParseFile(FixturePath("xunit.trx"));

        var starts = run.Tests.Select(test => test.StartTime).ToList();

        Assert.Equal(starts.OrderBy(time => time), starts);
    }

    [Fact]
    public void Parsing_a_stream_gives_the_same_result_as_parsing_the_file()
    {
        using var stream = File.OpenRead(FixturePath("xunit.trx"));

        var run = TrxParser.Parse(stream);

        Assert.Equal(7, run.Tests.Count);
    }

    // ---- Outcomes ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Passed", TestOutcome.Passed)]
    [InlineData("PassedButRunAborted", TestOutcome.Passed)]
    [InlineData("Completed", TestOutcome.Passed)]
    [InlineData("Failed", TestOutcome.Failed)]
    [InlineData("Error", TestOutcome.Failed)]
    [InlineData("Timeout", TestOutcome.Failed)]
    [InlineData("Aborted", TestOutcome.Aborted)]
    [InlineData("Disconnected", TestOutcome.Aborted)]
    [InlineData("NotExecuted", TestOutcome.NotRun)]
    [InlineData("Inconclusive", TestOutcome.NotRun)]
    [InlineData("NotRunnable", TestOutcome.NotRun)]
    [InlineData("Pending", TestOutcome.NotRun)]
    [InlineData("SomethingNewInTheFuture", TestOutcome.NotRun)]
    [InlineData("", TestOutcome.NotRun)]
    public void Outcomes_are_mapped(string raw, TestOutcome expected)
    {
        var run = TrxParser.ParseText(Trx(Result("t1", "A.B.C", raw)));

        var test = Assert.Single(run.Tests);
        Assert.Equal(expected, test.Outcome);
        Assert.Equal(raw, test.RawOutcome);
    }

    [Fact]
    public void Only_failed_tests_are_failures()
    {
        var run = TrxParser.ParseText(Trx(
            Result("t1", "A.Passed", "Passed"),
            Result("t2", "A.Failed", "Failed"),
            Result("t3", "A.Skipped", "NotExecuted"),
            Result("t4", "A.Crashed", "Aborted")));

        Assert.Equal(new[] { "A.Failed" }, run.Failures.Select(test => test.FullName));
    }

    // ---- Questions about one test -----------------------------------------------------------

    [Fact]
    public void Passed_needs_a_passing_case_and_no_failing_case()
    {
        var run = TrxParser.ParseText(Trx(
            Result("a1", "A.Green", "Passed"),
            Result("b1", "A.Mixed", "Passed"),
            Result("b2", "A.Mixed", "Failed"),
            Result("c1", "A.Skipped", "NotExecuted"),
            Result("d1", "A.Crashed", "Aborted")));

        Assert.True(run.Passed("A.Green"));
        Assert.False(run.Passed("A.Mixed"));
        Assert.False(run.Passed("A.Skipped"));
        Assert.False(run.Passed("A.Crashed"));
        Assert.False(run.Passed("A.Missing"));
    }

    [Fact]
    public void Failed_is_true_for_failures_and_aborts_only()
    {
        var run = TrxParser.ParseText(Trx(
            Result("a1", "A.Red", "Failed"),
            Result("b1", "A.Crashed", "Aborted"),
            Result("c1", "A.Green", "Passed"),
            Result("d1", "A.Skipped", "NotExecuted")));

        Assert.True(run.Failed("A.Red"));
        Assert.True(run.Failed("A.Crashed"));
        Assert.False(run.Failed("A.Green"));
        Assert.False(run.Failed("A.Skipped"));
        Assert.False(run.Failed("A.Missing"));
    }

    // ---- Missing and unusual parts ----------------------------------------------------------

    [Fact]
    public void A_result_without_a_definition_uses_its_test_name_as_full_name()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="unknown" testName="Only.Name" outcome="Passed" />
              </Results>
            </TestRun>
            """;

        var test = Assert.Single(TrxParser.ParseText(trx).Tests);

        Assert.Equal("Only.Name", test.FullName);
        Assert.Equal("Only.Name", test.DisplayName);
    }

    [Fact]
    public void Missing_output_times_and_duration_give_neutral_values()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="x" testName="N" outcome="Failed" />
              </Results>
            </TestRun>
            """;

        var test = Assert.Single(TrxParser.ParseText(trx).Tests);

        Assert.Null(test.ErrorMessage);
        Assert.Null(test.StackTrace);
        Assert.Null(test.StandardOutput);
        Assert.Equal(DateTimeOffset.MinValue, test.StartTime);
        Assert.Equal(DateTimeOffset.MinValue, test.EndTime);
        Assert.Equal(TimeSpan.Zero, test.Duration);
    }

    [Fact]
    public void Results_without_start_times_keep_their_file_order()
    {
        var run = TrxParser.ParseText(Trx(
            Result("t1", "A.Third", "Passed"),
            Result("t2", "A.First", "Passed"),
            Result("t3", "A.Second", "Passed")));

        Assert.Equal(new[] { "A.Third", "A.First", "A.Second" }, run.Tests.Select(test => test.FullName));
    }

    [Fact]
    public void Times_and_duration_are_read_with_their_offset()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="x" testName="N" outcome="Passed" duration="00:00:01.5000000"
                    startTime="2026-09-20T22:36:21.4532778+02:00" endTime="2026-09-20T22:36:22.9532778+02:00" />
              </Results>
            </TestRun>
            """;

        var test = Assert.Single(TrxParser.ParseText(trx).Tests);

        Assert.Equal(TimeSpan.FromSeconds(1.5), test.Duration);
        Assert.Equal(TimeSpan.FromHours(2), test.StartTime.Offset);
        Assert.Equal(TimeSpan.FromSeconds(1.5), test.EndTime - test.StartTime);
    }

    [Fact]
    public void A_file_without_results_is_an_empty_run()
    {
        var run = TrxParser.ParseText("""<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />""");

        Assert.Empty(run.Tests);
    }

    [Fact]
    public void Data_driven_summary_results_are_dropped_and_their_rows_kept()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="p" testName="Rows" outcome="Failed" resultType="DataDrivenTest" />
                <UnitTestResult testId="p" testName="Rows (Data Row 0)" outcome="Passed" resultType="DataDrivenDataRow" />
                <UnitTestResult testId="p" testName="Rows (Data Row 1)" outcome="Failed" resultType="DataDrivenDataRow" />
              </Results>
            </TestRun>
            """;

        var run = TrxParser.ParseText(trx);

        Assert.Equal(new[] { "Rows (Data Row 0)", "Rows (Data Row 1)" }, run.Tests.Select(test => test.DisplayName));
    }

    [Fact]
    public void Standard_output_is_read()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="x" testName="N" outcome="Passed">
                  <Output><StdOut>hello</StdOut></Output>
                </UnitTestResult>
              </Results>
            </TestRun>
            """;

        Assert.Equal("hello", Assert.Single(TrxParser.ParseText(trx).Tests).StandardOutput);
    }

    [Fact]
    public void A_byte_order_mark_is_accepted()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Trx(Result("t1", "A.B", "Passed"))))
            .ToArray();

        var run = TrxParser.Parse(new MemoryStream(bytes));

        Assert.Single(run.Tests);
    }

    [Fact]
    public void Characters_that_xml_forbids_do_not_make_the_file_unreadable()
    {
        const string trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="x" testName="N" outcome="Failed">
                  <Output><ErrorInfo><Message>bell&#x7; and escape&#x1B;</Message></ErrorInfo></Output>
                </UnitTestResult>
              </Results>
            </TestRun>
            """;

        var test = Assert.Single(TrxParser.ParseText(trx).Tests);

        Assert.Equal("bell\a and escape", test.ErrorMessage);
    }

    // ---- Bad input --------------------------------------------------------------------------

    [Fact]
    public void Text_that_is_not_xml_is_reported_clearly()
    {
        var exception = Assert.Throws<TrxFormatException>(() => TrxParser.ParseText("this is not xml"));

        Assert.Contains("not valid XML", exception.Message);
    }

    [Fact]
    public void Xml_with_another_root_element_is_not_a_trx_file()
    {
        var exception = Assert.Throws<TrxFormatException>(() => TrxParser.ParseText("<Other />"));

        Assert.Contains("<TestRun>", exception.Message);
    }

    [Fact]
    public void The_right_root_element_in_the_wrong_namespace_is_not_a_trx_file()
    {
        Assert.Throws<TrxFormatException>(() => TrxParser.ParseText("<TestRun xmlns=\"urn:other\" />"));
    }

    [Fact]
    public void Document_type_declarations_are_refused_so_entities_cannot_be_abused()
    {
        const string trx = """
            <!DOCTYPE TestRun [<!ENTITY boom "boom">]>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />
            """;

        Assert.Throws<TrxFormatException>(() => TrxParser.ParseText(trx));
    }

    [Fact]
    public void A_missing_file_is_a_file_error_and_not_a_format_error()
    {
        Assert.Throws<FileNotFoundException>(() => TrxParser.ParseFile(FixturePath("does-not-exist.trx")));
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static string Trx(params string[] results)
    {
        var definitions = string.Concat(results.Select(result =>
        {
            var id = Attribute(result, "testId");
            var name = Attribute(result, "testName");
            var lastDot = name.LastIndexOf('.');
            return $"""<UnitTest name="{name}" id="{id}"><TestMethod className="{name[..lastDot]}" name="{name[(lastDot + 1)..]}" /></UnitTest>""";
        }));

        return $"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>{string.Concat(results)}</Results>
              <TestDefinitions>{definitions}</TestDefinitions>
            </TestRun>
            """;
    }

    private static string Result(string testId, string fullName, string outcome)
    {
        return $"""<UnitTestResult testId="{testId}" testName="{fullName}" outcome="{outcome}" />""";
    }

    private static string Attribute(string element, string name)
    {
        var start = element.IndexOf(name + "=\"", StringComparison.Ordinal) + name.Length + 2;
        return element[start..element.IndexOf('"', start)];
    }
}
