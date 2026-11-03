using System.Collections.Frozen;

namespace TagForceSpecial.Cards;

public enum CardKind { Unknown, Monster, Spell, Trap }
public enum CardEffectState { Unknown, None, Referenced }

/// <summary>Immutable identifiers only. Binding existence does not imply executable support.</summary>
public sealed class CardEffectBinding
{
    public CardEffectState State { get; }
    public IReadOnlyList<string> References { get; }
    public static CardEffectBinding Unknown { get; } = new(CardEffectState.Unknown, []);
    public static CardEffectBinding None { get; } = new(CardEffectState.None, []);

    public CardEffectBinding(CardEffectState state, IEnumerable<string> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var snapshot = references.Take(65).ToArray();
        if (!Enum.IsDefined(state) || snapshot.Length > 64 ||
            snapshot.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256) ||
            snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            (state == CardEffectState.Referenced ? snapshot.Length == 0 : snapshot.Length != 0))
            throw new ArgumentException("Effect state and distinct nonempty reference IDs must agree.");
        State = state;
        References = Array.AsReadOnly(snapshot);
    }
}

public sealed record CardStaticMetadata(CardKind Kind, CardEffectBinding Effects);

/// <summary>Reviewed IDs and reference keys for the initial source build; no text, stats or effect execution.</summary>
public static class TfspCardMetadata
{
    public const string CardInfoSha256 = "6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060";
    public static IReadOnlyDictionary<uint, CardStaticMetadata> Reviewed { get; } =
        new uint[] { 4012, 4030, 4032, 4033, 4071, 4084, 5644, 6960 }
            .Select(id => new KeyValuePair<uint, CardStaticMetadata>(id, new(CardKind.Monster, CardEffectBinding.None)))
            .Concat(new uint[] { 4345, 4348, 4349, 4350, 4352, 4840 }.Select(id =>
                new KeyValuePair<uint, CardStaticMetadata>(id, new(CardKind.Spell,
                    new(CardEffectState.Referenced, [$"preview-{id}-v1"])))))
            .ToFrozenDictionary();

    public static CardCatalog Apply(CardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Source.ContainerSha256 != CardInfoSha256)
            throw new InvalidDataException("Initial static bindings require the reviewed cardinfo source hash.");
        return new(catalog.Source, catalog.Cards.Select(c => Reviewed.TryGetValue(c.GameCardId, out var metadata)
            ? c with { Kind = metadata.Kind, Effects = metadata.Effects } : c));
    }
}
