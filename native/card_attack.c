/* Behavioral reconstruction from the independently recorded MIPS analysis.
 * The ID is forwarded unchanged in a0. Helper validity/bounds remain external.
 * This source is a compiler experiment, not a verified native match.
 */
typedef unsigned int u32;
extern const u32 *property_record(u32 card_id);

int card_attack(u32 card_id)
{
    u32 attack = (*property_record(card_id) & 0x007fc000u) >> 14;
    return attack == 511u ? 0 : (int)(attack * 10u);
}
