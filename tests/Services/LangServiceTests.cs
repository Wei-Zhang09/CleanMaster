using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class LangServiceTests
{
    [Fact]
    public void Instance_IsSingleton_ReturnsSameReference()
    {
        var instance1 = LangService.Instance;
        var instance2 = LangService.Instance;
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Default_IsChinese_IsTrue()
    {
        // Reset to known state: Chinese is the default
        var service = LangService.Instance;
        service.IsChinese = true;
        Assert.True(service.IsChinese);
    }

    [Fact]
    public void Toggle_SwitchesIsChinese()
    {
        var service = LangService.Instance;
        var original = service.IsChinese;

        service.Toggle();
        Assert.Equal(!original, service.IsChinese);

        service.Toggle();
        Assert.Equal(original, service.IsChinese);
    }

    [Fact]
    public void IsChinese_SetFalse_SwitchesToEnglish()
    {
        var service = LangService.Instance;
        service.IsChinese = false;
        Assert.False(service.IsChinese);
    }

    [Fact]
    public void IsChinese_SetTrue_SwitchesToChinese()
    {
        var service = LangService.Instance;
        service.IsChinese = true;
        Assert.True(service.IsChinese);
    }

    [Fact]
    public void Indexer_Chinese_ReturnsChineseText()
    {
        var service = LangService.Instance;
        service.IsChinese = true;
        var text = service["AppTitle"];
        Assert.Equal("清理大师", text);
    }

    [Fact]
    public void Indexer_English_ReturnsEnglishText()
    {
        var service = LangService.Instance;
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
        var service = LangService.Instance;
        service.IsChinese = isChinese;
        var text = service[key];
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Indexer_UnknownKeyChinese_ReturnsKeyItself()
    {
        var service = LangService.Instance;
        service.IsChinese = true;
        var text = service["NonExistentKey"];
        Assert.Equal("NonExistentKey", text);
    }

    [Fact]
    public void Indexer_UnknownKeyEnglish_ReturnsKeyItself()
    {
        var service = LangService.Instance;
        service.IsChinese = false;
        var text = service["NonExistentKey"];
        Assert.Equal("NonExistentKey", text);
    }

    [Fact]
    public void Toggle_FiresPropertyChangedForIsChinese()
    {
        var service = LangService.Instance;
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
