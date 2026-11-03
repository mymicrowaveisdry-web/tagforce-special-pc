using TagForceSpecial.Cards;

namespace TagForceSpecial.Tools;

public static class CardCatalogImporter
{
    public static CardCatalog Read(Stream source, uint minimum, uint maximum) =>
        Assets.Import.PspCardCatalogImporter.Read(source, minimum, maximum);
}
