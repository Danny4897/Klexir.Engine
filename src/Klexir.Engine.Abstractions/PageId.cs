namespace Klexir.Engine.Abstractions;

/// <summary>Zero-based index of a fixed-size page within a store's backing file.</summary>
public readonly record struct PageId(uint Value)
{
    public override string ToString() => Value.ToString();
}
