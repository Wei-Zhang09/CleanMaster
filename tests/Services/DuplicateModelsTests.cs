using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class DuplicateModelsTests
{
    #region DuplicateGroup Tests

    [Fact]
    public void DuplicateGroup_WastedSpace_EqualsFileSizeTimesFilesMinusOne()
    {
        var group = new DuplicateGroup
        {
            Hash = "abc123",
            Files = new List<DuplicateFile>
            {
                new() { FileName = "a.txt", SizeBytes = 100 },
                new() { FileName = "b.txt", SizeBytes = 100 },
                new() { FileName = "c.txt", SizeBytes = 100 },
            }
        };

        Assert.Equal(200, group.WastedSpace); // 100 * (3 - 1) = 200
    }

    [Fact]
    public void DuplicateGroup_WastedSpace_ReturnsZero_WhenZeroFiles()
    {
        var group = new DuplicateGroup { Files = new List<DuplicateFile>() };

        Assert.Equal(0, group.WastedSpace);
    }

    [Fact]
    public void DuplicateGroup_WastedSpace_ReturnsZero_WhenOneFile()
    {
        var group = new DuplicateGroup
        {
            Files = new List<DuplicateFile>
            {
                new() { FileName = "single.txt", SizeBytes = 100 },
            }
        };

        Assert.Equal(0, group.WastedSpace);
    }

    [Fact]
    public void DuplicateGroup_FileSize_ReturnsZero_WhenFilesIsEmpty()
    {
        var group = new DuplicateGroup { Files = new List<DuplicateFile>() };

        Assert.Equal(0, group.FileSize);
    }

    [Fact]
    public void DuplicateGroup_FileSize_ReturnsFirstFileSize()
    {
        var group = new DuplicateGroup
        {
            Files = new List<DuplicateFile>
            {
                new() { FileName = "first.txt", SizeBytes = 1024 },
                new() { FileName = "second.txt", SizeBytes = 2048 },
            }
        };

        Assert.Equal(1024, group.FileSize);
    }

    #endregion

    #region DuplicateFile Tests

    [Theory]
    [InlineData(2_147_483_648, "2.00 GB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    [InlineData(500_000_000, "476.8 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(50_000, "48.8 KB")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(512, "512 B")]
    [InlineData(0, "0 B")]
    public void DuplicateFile_SizeText_FormatsCorrectly(long sizeBytes, string expected)
    {
        var file = new DuplicateFile { FileName = "test.txt", SizeBytes = sizeBytes };

        Assert.Equal(expected, file.SizeText);
    }

    [Fact]
    public void DuplicateFile_IsSelected_DefaultsToFalse()
    {
        var file = new DuplicateFile();
        Assert.False(file.IsSelected);
    }

    [Fact]
    public void DuplicateFile_IsKept_DefaultsToFalse()
    {
        var file = new DuplicateFile();
        Assert.False(file.IsKept);
    }

    [Fact]
    public void DuplicateFile_DefaultProperties_AreEmptyStrings()
    {
        var file = new DuplicateFile();

        Assert.Equal("", file.FileName);
        Assert.Equal("", file.FullPath);
    }

    #endregion
}
