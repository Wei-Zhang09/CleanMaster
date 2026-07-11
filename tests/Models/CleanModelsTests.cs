using CleanMaster.Models;

namespace CleanMaster.Tests.Models;

public class CleanableItemTests
{
    [Fact]
    public void SizeText_WhenOneGBOrMore_FormatsWithTwoDecimals()
    {
        var item = new CleanableItem { SizeBytes = 1_073_741_824 };
        Assert.Equal("1.00 GB", item.SizeText);
    }

    [Theory]
    [InlineData(2_147_483_648, "2.00 GB")]  // 2 GB
    [InlineData(1_610_612_736, "1.50 GB")]  // 1.5 GB
    [InlineData(5_368_709_120, "5.00 GB")]  // 5 GB
    public void SizeText_GBValues_FormatsCorrectly(long bytes, string expected)
    {
        var item = new CleanableItem { SizeBytes = bytes };
        Assert.Equal(expected, item.SizeText);
    }

    [Fact]
    public void SizeText_WhenExactlyOneGB_FormatsAsOneGB()
    {
        var item = new CleanableItem { SizeBytes = 1_073_741_824 };
        Assert.Equal("1.00 GB", item.SizeText);
    }

    [Theory]
    [InlineData(1_048_576, "1.0 MB")]          // exactly 1 MB
    [InlineData(5_242_880, "5.0 MB")]          // exactly 5 MB
    [InlineData(1_073_741_823, "1024.0 MB")]   // just under 1 GB
    public void SizeText_MBValues_FormatsWithOneDecimal(long bytes, string expected)
    {
        var item = new CleanableItem { SizeBytes = bytes };
        Assert.Equal(expected, item.SizeText);
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]               // exactly 1 KB
    [InlineData(5120, "5.0 KB")]               // exactly 5 KB
    [InlineData(1_048_575, "1024.0 KB")]       // just under 1 MB
    public void SizeText_KBValues_FormatsWithOneDecimal(long bytes, string expected)
    {
        var item = new CleanableItem { SizeBytes = bytes };
        Assert.Equal(expected, item.SizeText);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(500, "500 B")]
    [InlineData(1023, "1023 B")]
    public void SizeText_LessThanOneKB_ShowsBytes(long bytes, string expected)
    {
        var item = new CleanableItem { SizeBytes = bytes };
        Assert.Equal(expected, item.SizeText);
    }

    [Theory]
    [InlineData(CleanSafety.Safe, "Safe")]
    [InlineData(CleanSafety.Caution, "Caution")]
    [InlineData(CleanSafety.Dangerous, "Dangerous")]
    public void SafetyText_KnownValues_ReturnsExpectedLabel(CleanSafety safety, string expected)
    {
        var item = new CleanableItem { Safety = safety };
        Assert.Equal(expected, item.SafetyText);
    }

    [Fact]
    public void SafetyText_DefaultValue_ReturnsUnknown()
    {
        // default(CleanSafety) is CleanSafety.Safe (value 0),
        // so test an out-of-range cast instead
        var item = new CleanableItem { Safety = (CleanSafety)99 };
        Assert.Equal("Unknown", item.SafetyText);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var item = new CleanableItem();
        Assert.Equal("", item.Name);
        Assert.Equal("", item.FullPath);
        Assert.Equal(0, item.SizeBytes);
        Assert.True(item.IsSelected);
        Assert.False(item.IsDirectory);
        Assert.Equal(CleanSafety.Safe, item.Safety);
    }
}

public class ScanCategoryResultTests
{
    [Fact]
    public void TotalSize_SumsAllItemSizeBytes()
    {
        var result = new ScanCategoryResult
        {
            Items = new List<CleanableItem>
            {
                new() { SizeBytes = 100 },
                new() { SizeBytes = 200 },
                new() { SizeBytes = 300 }
            }
        };
        Assert.Equal(600, result.TotalSize);
    }

    [Fact]
    public void TotalSize_WithEmptyItems_ReturnsZero()
    {
        var result = new ScanCategoryResult();
        Assert.Equal(0, result.TotalSize);
    }

    [Fact]
    public void ItemCount_ReturnsItemsCount()
    {
        var result = new ScanCategoryResult
        {
            Items = new List<CleanableItem>
            {
                new(), new(), new()
            }
        };
        Assert.Equal(3, result.ItemCount);
    }

    [Fact]
    public void ItemCount_WithEmptyItems_ReturnsZero()
    {
        var result = new ScanCategoryResult();
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public void TotalSizeText_GBRange_FormatsWithTwoDecimals()
    {
        var result = new ScanCategoryResult
        {
            Items = new List<CleanableItem>
            {
                new() { SizeBytes = 1_073_741_824 }
            }
        };
        Assert.Equal("1.00 GB", result.TotalSizeText);
    }

    [Fact]
    public void TotalSizeText_MBRange_FormatsWithOneDecimal()
    {
        var result = new ScanCategoryResult
        {
            Items = new List<CleanableItem>
            {
                new() { SizeBytes = 1_048_576 }
            }
        };
        Assert.Equal("1.0 MB", result.TotalSizeText);
    }

    [Fact]
    public void TotalSizeText_KBRange_FormatsWithOneDecimal()
    {
        var result = new ScanCategoryResult
        {
            Items = new List<CleanableItem>
            {
                new() { SizeBytes = 1024 }
            }
        };
        Assert.Equal("1.0 KB", result.TotalSizeText);
    }

    [Fact]
    public void TotalSizeText_Zero_FormatsAsZeroKB()
    {
        var result = new ScanCategoryResult();
        Assert.Equal("0.0 KB", result.TotalSizeText);
    }
}

public class DiskInfoTests
{
    [Fact]
    public void UsedPercent_CalculatesCorrectly()
    {
        var disk = new DiskInfo { TotalBytes = 1000, UsedBytes = 500 };
        Assert.Equal(50.0, disk.UsedPercent);
    }

    [Fact]
    public void UsedPercent_WhenTotalBytesIsZero_ReturnsZero()
    {
        var disk = new DiskInfo { TotalBytes = 0, UsedBytes = 0 };
        Assert.Equal(0, disk.UsedPercent);
    }

    [Fact]
    public void UsedPercent_FractionalValue_CalculatesCorrectly()
    {
        var disk = new DiskInfo { TotalBytes = 1000, UsedBytes = 333 };
        Assert.Equal(33.3, disk.UsedPercent, 1); // within 1 decimal place
    }

    [Fact]
    public void TotalText_FormatsAsGB()
    {
        var disk = new DiskInfo { TotalBytes = 500_000_000_000 };
        Assert.EndsWith("GB", disk.TotalText);
    }

    [Fact]
    public void UsedText_FormatsAsGB()
    {
        var disk = new DiskInfo { UsedBytes = 250_000_000_000 };
        Assert.EndsWith("GB", disk.UsedText);
    }

    [Fact]
    public void FreeText_FormatsAsGB()
    {
        var disk = new DiskInfo { FreeBytes = 250_000_000_000 };
        Assert.EndsWith("GB", disk.FreeText);
    }

    [Fact]
    public void DefaultDriveLetter_IsEmptyString()
    {
        var disk = new DiskInfo();
        Assert.Equal("", disk.DriveLetter);
    }
}

public class LargeFileItemTests
{
    [Theory]
    [InlineData("safe", "#10B981")]
    [InlineData("caution", "#F59E0B")]
    [InlineData("danger", "#EF4444")]
    public void SafetyColor_KnownValues_ReturnsCorrectColor(string hint, string expected)
    {
        var item = new LargeFileItem { SafetyHint = hint };
        Assert.Equal(expected, item.SafetyColor);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("other")]
    public void SafetyColor_UnknownValues_ReturnsDefault(string hint)
    {
        var item = new LargeFileItem { SafetyHint = hint };
        Assert.Equal("#64748B", item.SafetyColor);
    }

    [Theory]
    [InlineData("safe", "可安全删除")]
    [InlineData("caution", "请确认后删除")]
    [InlineData("danger", "谨慎删除")]
    public void SafetyText_KnownValues_ReturnsChineseLabel(string hint, string expected)
    {
        var item = new LargeFileItem { SafetyHint = hint };
        Assert.Equal(expected, item.SafetyText);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void SafetyText_UnknownValues_ReturnsEmpty(string hint)
    {
        var item = new LargeFileItem { SafetyHint = hint };
        Assert.Equal("", item.SafetyText);
    }

    [Fact]
    public void SizeText_GBRange_FormatsWithTwoDecimals()
    {
        var item = new LargeFileItem { SizeBytes = 2_147_483_648 };
        Assert.Equal("2.00 GB", item.SizeText);
    }

    [Fact]
    public void SizeText_MBRange_FormatsWithOneDecimal()
    {
        var item = new LargeFileItem { SizeBytes = 104_857_600 }; // 100 MB
        Assert.EndsWith("MB", item.SizeText);
    }

    [Fact]
    public void SizeText_KBRange_FormatsWithOneDecimal()
    {
        var item = new LargeFileItem { SizeBytes = 5120 }; // 5 KB
        Assert.EndsWith("KB", item.SizeText);
    }
}

public class CleanResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new CleanResult();
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.Equal(0, result.BytesFreed);
        Assert.NotNull(result.Errors);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.DeletedItems);
        Assert.Empty(result.DeletedItems);
    }

    [Fact]
    public void FreedText_GBRange_FormatsWithTwoDecimals()
    {
        var result = new CleanResult { BytesFreed = 1_073_741_824 };
        Assert.Equal("1.00 GB", result.FreedText);
    }

    [Fact]
    public void FreedText_MBRange_FormatsWithOneDecimal()
    {
        var result = new CleanResult { BytesFreed = 1_048_576 };
        Assert.Equal("1.0 MB", result.FreedText);
    }

    [Fact]
    public void FreedText_KBRange_FormatsWithOneDecimal()
    {
        var result = new CleanResult { BytesFreed = 2048 };
        Assert.Equal("2.0 KB", result.FreedText);
    }

    [Fact]
    public void FreedText_Zero_FormatsAsZeroKB()
    {
        var result = new CleanResult { BytesFreed = 0 };
        Assert.Equal("0.0 KB", result.FreedText);
    }
}

public class ScanProgressTests
{
    [Fact]
    public void ProgressPercent_CalculatesCorrectly()
    {
        var progress = new ScanProgress
        {
            CategoriesScanned = 3,
            TotalCategories = 10
        };
        Assert.Equal(30.0, progress.ProgressPercent);
    }

    [Fact]
    public void ProgressPercent_Complete_ReturnsOneHundred()
    {
        var progress = new ScanProgress
        {
            CategoriesScanned = 5,
            TotalCategories = 5
        };
        Assert.Equal(100.0, progress.ProgressPercent);
    }

    [Fact]
    public void ProgressPercent_WhenTotalCategoriesIsZero_ReturnsZero()
    {
        var progress = new ScanProgress
        {
            CategoriesScanned = 0,
            TotalCategories = 0
        };
        Assert.Equal(0, progress.ProgressPercent);
    }

    [Fact]
    public void ProgressPercent_NoProgress_ReturnsZero()
    {
        var progress = new ScanProgress
        {
            CategoriesScanned = 0,
            TotalCategories = 10
        };
        Assert.Equal(0, progress.ProgressPercent);
    }
}
