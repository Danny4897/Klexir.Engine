namespace Klexir.Engine.Abstractions;

public readonly record struct TransactionId(Guid Value)
{
    public static TransactionId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
