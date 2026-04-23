namespace Artect.Core.Schema;

public sealed record Sequence(
    string Schema,
    string Name,
    string SqlType,
    long StartValue,
    long Increment,
    long? MinValue,
    long? MaxValue,
    bool IsCycling);
