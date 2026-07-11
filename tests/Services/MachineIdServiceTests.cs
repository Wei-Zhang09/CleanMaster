using System.Text.RegularExpressions;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class MachineIdServiceTests
{
    private readonly MachineIdService _service = new();

    [Fact]
    public void GetMachineId_ReturnsNonNullOrEmptyString()
    {
        var id = _service.GetMachineId();

        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    [Fact]
    public void GetMachineId_ReturnsSameValue_OnSecondCall()
    {
        var first = _service.GetMachineId();
        var second = _service.GetMachineId();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetMachineId_ReturnsExactly32Characters()
    {
        var id = _service.GetMachineId();

        Assert.Equal(32, id.Length);
    }

    [Fact]
    public void GetMachineId_ContainsOnlyUppercaseHexCharacters()
    {
        var id = _service.GetMachineId();

        Assert.Matches("^[0-9A-F]{32}$", id);
    }
}
