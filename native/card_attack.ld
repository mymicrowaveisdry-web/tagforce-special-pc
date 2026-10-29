/* Link in the ORIGINAL ELF domain, before PSP load-base relocation. */
ENTRY(card_attack)
property_record = 0x0001ad64;
SECTIONS
{
  .text 0x0001b334 : { KEEP(*(.text.card_attack)) }
  /DISCARD/ : { *(.comment) *(.note*) *(.pdr) *(.mdebug*) *(.eh_frame*) }
}
