namespace Klexir.Engine.Abstractions;

/// <summary>Index into a page's slot directory, identifying one record within that page.</summary>
public readonly record struct SlotId(ushort Value)
{
    public override string ToString() => Value.ToString();
}
