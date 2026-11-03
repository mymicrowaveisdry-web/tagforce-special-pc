namespace TagForceSpecial.Cards.CatalogStorage;

/// <summary>Application-owned catalog reference. Failed loads preserve the last valid immutable snapshot.</summary>
public sealed class CardCatalogProvider
{
    private CardCatalog current;
    public CardCatalog Current => Volatile.Read(ref current);

    public CardCatalogProvider(CardCatalog initial) => current = initial ?? throw new ArgumentNullException(nameof(initial));

    public LoadedCardCatalog Reload(string packageDirectory)
    {
        var loaded = CardCatalogPackage.Load(packageDirectory);
        Interlocked.Exchange(ref current, loaded.Catalog);
        return loaded;
    }
}
