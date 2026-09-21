using System;
using System.Collections.Generic;
using Deflake.Core.Execution;
using Deflake.Core.Experiments;
using Deflake.Core.Verdicts;
using Xunit;
using static Deflake.Core.Tests.VerdictTestSupport;

namespace Deflake.Core.Tests;

/// <summary>
/// The command a developer pastes into a shell. It has to describe the run that actually reproduced
/// the failure and nothing else, which is why it is rendered from that run's request rather than
/// assembled from the verdict.
/// </summary>
public sealed class ReproCommandTests
{
    private const string Project = @"C:\repo\tests\Shop.Tests\Shop.Tests.csproj";

    [Fact]
    public void The_command_runs_the_target_without_rebuilding_it()
    {
        var command = ReproCommandBuilder.Build(new TestRunRequest(Project));

        Assert.Equal(@"dotnet test C:\repo\tests\Shop.Tests\Shop.Tests.csproj --no-build", command);
    }

    [Fact]
    public void Filter_and_run_settings_are_passed_through_as_they_were_run()
    {
        var request = new TestRunRequest(Project)
        {
            Filter = "FullyQualifiedName=Shop.OrderTests.Total_is_summed",
            RunSettingsPath = @"C:\repo\parallel.runsettings",
        };

        var command = ReproCommandBuilder.Build(request);

        Assert.Contains("--filter FullyQualifiedName=Shop.OrderTests.Total_is_summed", command);
        Assert.Contains(@"--settings C:\repo\parallel.runsettings", command);
    }

    [Fact]
    public void A_path_with_a_space_is_quoted()
    {
        var request = new TestRunRequest(@"C:\my repo\Shop.Tests.csproj");

        var command = ReproCommandBuilder.Build(request);

        Assert.Contains(@"""C:\my repo\Shop.Tests.csproj""", command);
    }

    [Fact]
    public void The_environment_an_experiment_added_prefixes_the_command()
    {
        var request = new TestRunRequest(Project)
        {
            EnvironmentVariables = new Dictionary<string, string> { ["TZ"] = "Asia/Tokyo" },
        };

        var command = ReproCommandBuilder.Build(request);

        Assert.StartsWith("TZ=Asia/Tokyo dotnet test", command, StringComparison.Ordinal);
    }

    [Fact]
    public void The_environment_the_developer_already_had_is_not_repeated()
    {
        var baseline = new Dictionary<string, string> { ["DOTNET_CLI_UI_LANGUAGE"] = "en" };
        var request = new TestRunRequest(Project)
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["DOTNET_CLI_UI_LANGUAGE"] = "en",
                ["DOTNET_CULTURE"] = "de-DE",
            },
        };

        var command = ReproCommandBuilder.Build(request, baseline);

        Assert.StartsWith("DOTNET_CULTURE=de-DE dotnet test", command, StringComparison.Ordinal);
        Assert.DoesNotContain("DOTNET_CLI_UI_LANGUAGE", command);
    }

    [Fact]
    public void A_variable_the_experiment_changed_is_shown_even_when_the_baseline_had_it()
    {
        var baseline = new Dictionary<string, string> { ["DOTNET_CULTURE"] = "en-US" };
        var request = new TestRunRequest(Project)
        {
            EnvironmentVariables = new Dictionary<string, string> { ["DOTNET_CULTURE"] = "ja-JP" },
        };

        var command = ReproCommandBuilder.Build(request, baseline);

        Assert.StartsWith("DOTNET_CULTURE=ja-JP", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Variables_are_ordered_by_name_so_the_same_run_always_renders_the_same_command()
    {
        var request = new TestRunRequest(Project)
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["TZ"] = "UTC",
                ["DOTNET_CULTURE"] = "de-DE",
            },
        };

        var command = ReproCommandBuilder.Build(request);

        Assert.StartsWith("DOTNET_CULTURE=de-DE TZ=UTC dotnet test", command, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_with_a_space_is_quoted_so_the_prefix_stays_one_variable()
    {
        var request = new TestRunRequest(Project)
        {
            EnvironmentVariables = new Dictionary<string, string> { ["DEFLAKE_NOTE"] = "under load" },
        };

        var command = ReproCommandBuilder.Build(request);

        Assert.StartsWith(@"DEFLAKE_NOTE=""under load""", command, StringComparison.Ordinal);
    }

    [Fact]
    public void There_is_no_command_without_a_run_to_render()
    {
        Assert.Throws<ArgumentNullException>(() => ReproCommandBuilder.Build(null!));
    }

    [Fact]
    public void An_order_dependent_repro_filters_to_the_polluters_and_the_failing_test()
    {
        var verdict = new VerdictEngine().Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(ExperimentTestSupport.SiblingTest, ExperimentTestSupport.OtherClassTest)));

        Assert.NotNull(verdict.ReproRequest);
        Assert.Equal(
            TestFilter.ForTests(new[]
            {
                ExperimentTestSupport.SiblingTest,
                ExperimentTestSupport.OtherClassTest,
                ExperimentTestSupport.FailingTest,
            }),
            verdict.ReproRequest!.Filter);
    }

    [Fact]
    public void A_polluter_set_too_long_for_a_command_line_falls_back_to_the_scope_that_was_measured()
    {
        var manyPolluters = new string[400];
        for (var index = 0; index < manyPolluters.Length; index++)
        {
            manyPolluters[index] = $"Shop.HugeTests.A_very_long_test_name_that_eats_the_command_line_budget_{index}";
        }

        var verdict = new VerdictEngine().Decide(Input(
            isolation: Isolation(failures: 0),
            scope: ScopeLadder((ScopeLevel.Alone, 0), (ScopeLevel.Class, 18)),
            polluters: Polluters(manyPolluters)));

        Assert.Equal(VerdictType.OrderDependent, verdict.Type);
        Assert.NotNull(verdict.ReproCommand);

        // The scope request is what the ladder measured: it still reproduces, it is just wider.
        Assert.Null(verdict.ReproRequest!.Filter);
    }

    [Fact]
    public void A_repro_is_never_stored_as_a_command_without_the_run_behind_it()
    {
        var verdict = new Verdict(VerdictType.Inconclusive, ExperimentTestSupport.Identity());

        Assert.Throws<ArgumentNullException>(() => verdict.WithRepro("dotnet test", null!));
        Assert.Throws<ArgumentException>(() => verdict.WithRepro("  ", new TestRunRequest(Project)));
    }
}
