using CSToTS;
using Xunit;

namespace CSToTS.Tests;

public class OutputOptionsTests
{
    [Fact]
    public void GetFullTypeScriptCode_RejectsNullType()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => TypeScript.GetFullTypeScriptCode(null!, new TypeScriptOutputOptions()));

        Assert.Equal("type", exception.ParamName);
    }

    [Fact]
    public void GetFullTypeScriptCode_RejectsNullOptions()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => TypeScript.GetFullTypeScriptCode(typeof(string), null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void GetFullTypeScriptCode_RejectsUnknownFileLayout()
    {
        TypeScriptOutputOptions options = new()
        {
            FileLayout = (TypeScriptOutputLayout)int.MaxValue
        };

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => TypeScript.GetFullTypeScriptCode(typeof(string), options));

        Assert.Equal("FileLayout", exception.ParamName);
    }
}
