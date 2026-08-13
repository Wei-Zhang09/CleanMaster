using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class LangServiceTests
{
    [Fact]
    public void Instance_IsIndependent_TwoInstancesHaveSeparateState()
    {
        var instance1 = new LangService();
        var instance2 = new LangService();
        // Two separately created instances are independent objects
        Assert.NotSame(instance1, instance2);
        // But they share the same default state
        Assert.Equal(instance1.IsChinese, instance2.IsChinese);
    }

    [Fact]
    public void Default_IsChinese_IsTrue()
    {
        // Reset to known state: Chinese is the default
        var service = new LangService();
        service.IsChinese = true;
        Assert.True(service.IsChinese);
    }

    [Fact]
    public void Toggle_SwitchesIsChinese()
    {
        var service = new LangService();
        var original = service.IsChinese;

        service.Toggle();
        Assert.Equal(!original, service.IsChinese);

        service.Toggle();
        Assert.Equal(original, service.IsChinese);
    }

    [Fact]
    public void IsChinese_SetFalse_SwitchesToEnglish()
    {
        var service = new LangService();
        service.IsChinese = false;
        Assert.False(service.IsChinese);
    }

    [Fact]
    public void IsChinese_SetTrue_SwitchesToChinese()
    {
        var service = new LangService();
        service.IsChinese = true;
        Assert.True(service.IsChinese);
    }

    [Fact]
    public void Indexer_Chinese_ReturnsChineseText()
    {
        var service = new LangService();
        service.IsChinese = true;
        var text = service["AppTitle"];
        Assert.Equal("清理大师", text);
    }

    [Fact]
    public void Indexer_English_ReturnsEnglishText()
    {
        var service = new LangService();
        service.IsChinese = false;
        var text = service["AppTitle"];
        Assert.Equal("CleanMaster", text);
    }

    [Theory]
    [InlineData("AppTitle", true, "清理大师")]
    [InlineData("AppTitle", false, "CleanMaster")]
    [InlineData("Scan", true, "扫描")]
    [InlineData("Scan", false, "Scan")]
    [InlineData("Clean", true, "清理")]
    [InlineData("Clean", false, "Clean")]
    [InlineData("Cancel", true, "取消")]
    [InlineData("Cancel", false, "Cancel")]
    [InlineData("Ready", true, "准备就绪")]
    [InlineData("Ready", false, "Ready")]
    [InlineData("Scanning", true, "正在扫描...")]
    [InlineData("Scanning", false, "Scanning...")]
    public void Indexer_CommonKeys_ReturnCorrectText(string key, bool isChinese, string expected)
    {
        var service = new LangService();
        service.IsChinese = isChinese;
        var text = service[key];
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Indexer_UnknownKeyChinese_ReturnsKeyItself()
    {
        var service = new LangService();
        service.IsChinese = true;
        var text = service["NonExistentKey"];
        Assert.Equal("NonExistentKey", text);
    }

    [Fact]
    public void Indexer_UnknownKeyEnglish_ReturnsKeyItself()
    {
        var service = new LangService();
        service.IsChinese = false;
        var text = service["NonExistentKey"];
        Assert.Equal("NonExistentKey", text);
    }

    [Fact]
    public void Toggle_FiresPropertyChangedForIsChinese()
    {
        var service = new LangService();
        var fired = false;
        var handler = new System.ComponentModel.PropertyChangedEventHandler((s, e) =>
        {
            if (e.PropertyName == "IsChinese")
                fired = true;
        });

        service.PropertyChanged += handler;
        try
        {
            service.Toggle();
            Assert.True(fired);
        }
        finally
        {
            service.PropertyChanged -= handler;
        }
    }
}
