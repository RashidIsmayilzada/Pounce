namespace Pounce.Core.Tests;

/// <summary>"Launch at login" is on only when the Run entry starts this very Pounce.exe.</summary>
public class LoginCommandTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\Programs\Pounce\Pounce.exe";

    [Fact]
    public void The_command_is_the_quoted_path()
    {
        Assert.Equal("\"" + Exe + "\"", LoginCommand.For(Exe));
        Assert.True(LoginCommand.PointsAt(LoginCommand.For(Exe), Exe));
    }

    [Theory]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Programs\\Pounce\\Pounce.exe\"")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Programs\\Pounce\\Pounce.exe")]
    [InlineData("  \"c:\\users\\ME\\appdata\\local\\programs\\pounce\\POUNCE.EXE\"  ")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Programs\\Pounce\\Pounce.exe\" --minimized")]
    public void Points_at_this_executable_ignoring_case_quotes_and_arguments(string command)
    {
        Assert.True(LoginCommand.PointsAt(command, Exe));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("\"C:\\Users\\me\\Downloads\\Pounce\\Pounce.exe\"")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Programs\\Pounce\\Pounce.exe.old\"")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Programs\\Pounce\\Pounce.exe --minimized")]
    public void A_missing_moved_or_other_copy_does_not_count(string? command)
    {
        Assert.False(LoginCommand.PointsAt(command, Exe));
    }

    [Fact]
    public void The_executable_may_itself_come_quoted()
    {
        Assert.True(LoginCommand.PointsAt(Exe, "\"" + Exe + "\""));
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x01 }, true)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, true)]
    [InlineData(new byte[] { }, false)]
    [InlineData(null, false)]
    public void Task_manager_switches_it_off_with_an_odd_first_byte(byte[]? data, bool off)
    {
        Assert.Equal(off, LoginCommand.IsSwitchedOff(data));
    }
}
