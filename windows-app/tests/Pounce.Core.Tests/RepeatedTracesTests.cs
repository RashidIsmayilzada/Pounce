namespace Pounce.Core.Tests;

/// <summary>Logging the same stack trace over and over only once.</summary>
public class RepeatedTracesTests
{
    [Fact]
    public void The_first_trace_is_written_in_full_and_its_repeats_are_counted()
    {
        var traces = new RepeatedTraces();

        Assert.Equal("trace A", traces.Describe("trace A", "A"));
        Assert.Equal("A (same stack trace as before, repeat 1)", traces.Describe("trace A", "A"));
        Assert.Equal("A (same stack trace as before, repeat 2)", traces.Describe("trace A", "A"));
    }

    [Fact]
    public void A_different_trace_is_written_in_full_and_starts_the_count_over()
    {
        var traces = new RepeatedTraces();
        traces.Describe("trace A", "A");
        traces.Describe("trace A", "A");

        Assert.Equal("trace B", traces.Describe("trace B", "B"));
        Assert.Equal("trace A", traces.Describe("trace A", "A"));
        Assert.Equal("A (same stack trace as before, repeat 1)", traces.Describe("trace A", "A"));
    }
}
