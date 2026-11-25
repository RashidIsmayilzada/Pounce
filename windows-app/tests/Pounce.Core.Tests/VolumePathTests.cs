namespace Pounce.Core.Tests;

/// <summary>Which files may sit on a slow volume, from the path's text alone.</summary>
public class VolumePathTests
{
    [Theory]
    [InlineData(@"\\server\share\Notes\plan.md")]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share/notes/plan.md")]
    [InlineData(@"\\?\UNC\server\share\plan.md")]
    [InlineData(@"\\?\unc\server\share\plan.md")]
    [InlineData(@"\/server\share\plan.md")]
    public void A_UNC_path_is_on_the_network(string path)
    {
        Assert.Equal(new PathVolume(VolumeKind.Network, null), VolumePath.Of(path));
    }

    [Theory]
    [InlineData(@"C:\Users\me\plan.md", @"C:\")]
    [InlineData(@"c:\users\me\plan.md", @"C:\")]
    [InlineData("Z:/Projects/plan.md", @"Z:\")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("E:", @"E:\")]
    [InlineData("E:plan.md", @"E:\")]
    [InlineData(@"\\?\C:\Users\me\plan.md", @"C:\")]
    [InlineData(@"\\.\d:\plan.md", @"D:\")]
    [InlineData(@"\\?\F:", @"F:\")]
    public void A_drive_letter_path_has_its_upper_case_root(string path, string root)
    {
        Assert.Equal(new PathVolume(VolumeKind.Drive, root), VolumePath.Of(path));
    }

    [Theory]
    [InlineData(@"\\?\Volume{0b2d1a8e-0000-0000-0000-100000000000}\plan.md")]
    [InlineData(@"\\.\pipe\pounce")]
    [InlineData(@"\\?\UNC")]
    [InlineData(@"\\?\C:plan.md")]
    [InlineData(@"Notes\plan.md")]
    [InlineData(@"\Notes\plan.md")]
    [InlineData("1:\\plan.md")]
    [InlineData("C")]
    [InlineData("")]
    public void Anything_else_is_unknown(string path)
    {
        Assert.Equal(new PathVolume(VolumeKind.Unknown, null), VolumePath.Of(path));
    }

    [Theory]
    [InlineData(DriveType.Network, true)]
    [InlineData(DriveType.Removable, true)]
    [InlineData(DriveType.CDRom, true)]
    [InlineData(DriveType.NoRootDirectory, true)]
    [InlineData(DriveType.Unknown, true)]
    [InlineData(DriveType.Fixed, false)]
    [InlineData(DriveType.Ram, false)]
    public void Only_fixed_and_RAM_disks_are_fast(DriveType type, bool slow)
    {
        Assert.Equal(slow, VolumePath.MayBeSlow(type));
    }

    [Theory]
    [InlineData(@"\\server\share\Tools\App.EXE", "type:.exe")]
    [InlineData(@"Z:\Links\Code.lnk", "type:.lnk")]
    [InlineData(@"\\server\share\Makefile", "type:")]
    [InlineData(@"\\server\share\Papers\paper.PDF", "type:.pdf")]
    public void A_file_on_a_slow_volume_is_keyed_by_its_type(string path, string key)
    {
        Assert.Equal(key, VolumePath.SlowFileIconKey(path));
    }

    [Theory]
    [InlineData(@"C:\Tools\App.exe", @"\\server\share\Tools\App.exe")]
    [InlineData(@"C:\Links\Code.lnk", @"Z:\Links\Code.lnk")]
    [InlineData(@"C:\Notes\Makefile", @"\\server\share\Makefile")]
    public void A_slow_file_never_shares_a_key_with_a_local_file_that_has_its_own_icon(string local, string slow)
    {
        Assert.StartsWith("file:", IconKey.ForFile(local), StringComparison.Ordinal);
        Assert.StartsWith("type:", VolumePath.SlowFileIconKey(slow), StringComparison.Ordinal);
        Assert.NotEqual(IconKey.ForFile(local), VolumePath.SlowFileIconKey(slow));
    }

    [Theory]
    [InlineData(@"C:\Notes\plan.md", @"\\server\share\Notes\plan.md")]
    [InlineData(@"C:\Papers\paper.pdf", @"Z:\Papers\Paper.PDF")]
    public void A_slow_document_shares_the_type_icon_of_local_documents(string local, string slow)
    {
        Assert.Equal(IconKey.ForFile(local), VolumePath.SlowFileIconKey(slow));
    }
}
