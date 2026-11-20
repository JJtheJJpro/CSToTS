namespace CSToTS;

public enum TypeScriptOutputLayout
{
    NamespaceDirectories,
    Flat
}

public sealed record TypeScriptOutputOptions
{
    public TypeScriptOutputLayout FileLayout { get; init; } = TypeScriptOutputLayout.NamespaceDirectories;

    public bool EmitNamespaces { get; init; }
}
