namespace TagForceSpecial.Assets;

/// <summary>Reads a card's attack from a resolved property record.</summary>
public static class CardAttackGetter
{
    public static int FromPropertyWord(uint word0)
    {
        uint encoded = (word0 >> 14) & 511;
        return encoded == 511 ? 0 : (int)encoded * 10;
    }
}
