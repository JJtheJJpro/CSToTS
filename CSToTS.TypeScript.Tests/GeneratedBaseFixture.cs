namespace CSToTS.TypeScriptCompilerTests.Fixtures;

public interface IBaseContract
{
    string Name { get; set; }
}

public class GeneratedBase : IBaseContract
{
    public int Value = 0;

    public string Name { get; set; } = string.Empty;
}
