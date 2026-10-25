namespace TagForceSpecial.Assets;

/// <summary>
/// Numeric attack extraction after the original EBOOT.BIN getter at 0x0881F334
/// has resolved a card's property record. Does not reconstruct card-ID lookup,
/// invalid-ID handling, register behavior, or PSP machine-code bytes.
/// </summary>
public static class CardAttackGetter
{
    public static int FromPropertyWord(uint word0)
    {
        uint encoded = (word0 >> 14) & 511;
        return encoded == 511 ? 0 : (int)encoded * 10;
    }
}
