using System;
using Culprit.Core.Execution;
using Xunit;

namespace Culprit.Core.Tests;

public class TestFilterTests
{
    [Fact]
    public void One_test_is_selected_by_exact_full_name()
    {
        Assert.Equal("FullyQualifiedName=Shop.OrderTests.Total_is_summed", TestFilter.ForTest("Shop.OrderTests.Total_is_summed"));
    }

    [Fact]
    public void Several_tests_are_joined_with_or()
    {
        var filter = TestFilter.ForTests(new[] { "A.B.One", "A.B.Two" });

        Assert.Equal("FullyQualifiedName=A.B.One|FullyQualifiedName=A.B.Two", filter);
    }

    [Fact]
    public void Duplicates_are_removed_and_the_order_is_kept()
    {
        var filter = TestFilter.ForTests(new[] { "A.Second", "A.First", "A.Second" });

        Assert.Equal("FullyQualifiedName=A.Second|FullyQualifiedName=A.First", filter);
    }

    [Fact]
    public void Nested_classes_keep_their_plus_sign()
    {
        Assert.Equal("FullyQualifiedName=Fix.Outer+Inner.Nested", TestFilter.ForTest("Fix.Outer+Inner.Nested"));
    }

    [Theory]
    [InlineData("A.B.Method(x)", "A.B.Method\\(x\\)")]
    [InlineData("A.B.C&D", "A.B.C\\&D")]
    [InlineData("A.B.C|D", "A.B.C\\|D")]
    [InlineData("A.B.C=D", "A.B.C\\=D")]
    [InlineData("A.B.C!D", "A.B.C\\!D")]
    [InlineData("A.B.C~D", "A.B.C\\~D")]
    [InlineData("A.B.C\\D", "A.B.C\\\\D")]
    [InlineData("A.B.(&|=!~)", "A.B.\\(\\&\\|\\=\\!\\~\\)")]
    public void Characters_special_to_the_filter_are_escaped(string name, string expected)
    {
        Assert.Equal(expected, TestFilter.Escape(name));
    }

    [Theory]
    [InlineData("Ünïcödé.Tëst.Méthod")]
    [InlineData("Generic.Class`1.Method")]
    [InlineData("A.B.Name with spaces")]
    [InlineData("A.B.Method<int>")]
    public void Other_characters_are_left_alone(string name)
    {
        Assert.Equal(name, TestFilter.Escape(name));
    }

    [Fact]
    public void An_escaped_name_cannot_smuggle_in_another_condition()
    {
        var filter = TestFilter.ForTest("A.B.C|FullyQualifiedName=Evil");

        Assert.Equal("FullyQualifiedName=A.B.C\\|FullyQualifiedName\\=Evil", filter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => TestFilter.Escape(name));
    }

    [Theory]
    [InlineData("A.B\nC")]
    [InlineData("A.B\0C")]
    [InlineData("A.B\tC")]
    public void Control_characters_are_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => TestFilter.Escape(name));
    }

    [Fact]
    public void An_empty_list_is_refused_because_an_empty_filter_runs_everything()
    {
        Assert.Throws<ArgumentException>(() => TestFilter.ForTests(Array.Empty<string>()));
    }

    [Fact]
    public void A_missing_list_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => TestFilter.ForTests(null!));
    }

    [Fact]
    public void A_filter_that_is_too_long_for_a_command_line_is_refused_with_the_numbers()
    {
        var names = new[] { new string('a', 60), new string('b', 60) };

        var exception = Assert.Throws<ArgumentException>(() => TestFilter.ForTests(names, maxLength: 100));

        Assert.Contains("2 tests", exception.Message);
        Assert.Contains("100", exception.Message);
    }

    [Fact]
    public void A_filter_exactly_at_the_limit_is_accepted()
    {
        var filter = TestFilter.ForTest(new string('a', 30));

        Assert.Equal(filter, TestFilter.ForTests(new[] { new string('a', 30) }, maxLength: filter.Length));
    }
}
