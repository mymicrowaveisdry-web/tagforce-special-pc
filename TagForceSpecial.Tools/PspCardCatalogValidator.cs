using TagForceSpecial.Cards;

namespace TagForceSpecial.Tools;

public static class PspCardCatalogValidator
{
    public static void Validate(CardDefinition card) => Assets.Import.PspCardCatalogValidator.Validate(card);
}
