using System.Globalization;
using System.Windows;
using CleanMaster.Converters;

namespace CleanMaster.Tests.Converters;

public class ViewVisibilityConverterTests
{
    [Fact]
    public void Convert_WhenValueEqualsParameter_ReturnsVisible()
    {
        var converter = new ViewVisibilityConverter();
        var result = converter.Convert("home", typeof(Visibility), "home", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_WhenValueDoesNotEqualParameter_ReturnsCollapsed()
    {
        var converter = new ViewVisibilityConverter();
        var result = converter.Convert("settings", typeof(Visibility), "home", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_WhenValueIsNull_ReturnsCollapsed()
    {
        var converter = new ViewVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), "home", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_WhenParameterIsNullAndValueIsNull_ReturnsVisible()
    {
        var converter = new ViewVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_WhenParameterIsNullAndValueIsNotNull_ReturnsCollapsed()
    {
        var converter = new ViewVisibilityConverter();
        var result = converter.Convert("home", typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        var converter = new ViewVisibilityConverter();
        Assert.Throws<NotImplementedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(string), "home", CultureInfo.InvariantCulture));
    }
}

public class BoolVisibilityConverterTests
{
    [Fact]
    public void Convert_True_ReturnsVisible()
    {
        var converter = new BoolVisibilityConverter();
        var result = converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Convert_FalseOrNull_ReturnsCollapsed(object value)
    {
        var converter = new BoolVisibilityConverter();
        var result = converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_NonBoolTruthy_ReturnsCollapsed()
    {
        var converter = new BoolVisibilityConverter();
        // "true" string is not bool true, so it won't match `value is true`
        var result = converter.Convert("true", typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        var converter = new BoolVisibilityConverter();
        Assert.Throws<NotImplementedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}

public class InverseBoolConverterTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Convert_Bool_Inverts(bool input, bool expected)
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(input, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ConvertBack_Bool_Inverts(bool input, bool expected)
    {
        var converter = new InverseBoolConverter();
        var result = converter.ConvertBack(input, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonBool_PassesThrough()
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert("hello", typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Convert_Null_PassesThrough()
    {
        var converter = new InverseBoolConverter();
        var result = converter.Convert(null!, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_NonBool_PassesThrough()
    {
        var converter = new InverseBoolConverter();
        var result = converter.ConvertBack(42, typeof(int), null, CultureInfo.InvariantCulture);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertBack_Null_PassesThrough()
    {
        var converter = new InverseBoolConverter();
        var result = converter.ConvertBack(null!, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }
}

public class NullVisibilityConverterTests
{
    [Fact]
    public void Convert_NonNull_ReturnsVisible()
    {
        var converter = new NullVisibilityConverter();
        var result = converter.Convert("hello", typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        var converter = new NullVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_ZeroInt_ReturnsVisible()
    {
        // 0 is a non-null value
        var converter = new NullVisibilityConverter();
        var result = converter.Convert(0, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsVisible()
    {
        var converter = new NullVisibilityConverter();
        var result = converter.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        var converter = new NullVisibilityConverter();
        Assert.Throws<NotImplementedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
