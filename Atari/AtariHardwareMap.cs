using AtariHackerMCP.State;

namespace AtariHackerMCP.Atari;

public static class AtariHardwareMap
{
    public static IReadOnlyDictionary<ushort, SymbolEntry> HardwareSymbols { get; } =
        new Dictionary<ushort, SymbolEntry>
        {
            // ── OS ROM Vectors (OsRom group) ──────────────────────────────
            [0xC000] = OsRom("SYSVBL"),
            [0xC002] = OsRom("SYSVBV"),
            [0xC00C] = OsRom("SETVBV"),
            [0xC012] = OsRom("XITVBV"),
            [0xC300] = OsRom("CIOV"),
            [0xC400] = OsRom("SIOV"),
            [0xE400] = OsRom("SIO_INIT"),
            [0xE410] = OsRom("CIOV"),
            [0xE456] = OsRom("SIOV"),
            [0xE459] = OsRom("SIO"),
            [0xFFFA] = OsRom("NMIVEC"),
            [0xFFFC] = OsRom("RESVEC"),
            [0xFFFE] = OsRom("IRQVEC"),

            // ── GTIA ($D000–$D0FF) — Hardware group ─────────────────────
            // Player/missile graphics
            [0xD000] = Hw("HPOSP0"),
            [0xD001] = Hw("HPOSP1"),
            [0xD002] = Hw("HPOSP2"),
            [0xD003] = Hw("HPOSP3"),
            [0xD004] = Hw("HPOSM0"),
            [0xD005] = Hw("HPOSM1"),
            [0xD006] = Hw("HPOSM2"),
            [0xD007] = Hw("HPOSM3"),
            [0xD008] = Hw("SIZEP0"),
            [0xD009] = Hw("SIZEP1"),
            [0xD00A] = Hw("SIZEP2"),
            [0xD00B] = Hw("SIZEP3"),
            [0xD00C] = Hw("SIZEM"),
            [0xD00D] = Hw("GRAFP0"),
            [0xD00E] = Hw("GRAFP1"),
            [0xD00F] = Hw("GRAFP2"),
            [0xD010] = Hw("GRAFP3"),
            [0xD011] = Hw("GRAFM"),

            // Color registers
            [0xD012] = Hw("COLPM0"),
            [0xD013] = Hw("COLPM1"),
            [0xD014] = Hw("COLPM2"),
            [0xD015] = Hw("COLPM3"),
            [0xD016] = Hw("COLPF0"),
            [0xD017] = Hw("COLPF1"),
            [0xD018] = Hw("COLPF2"),
            [0xD019] = Hw("COLPF3"),
            [0xD01A] = Hw("COLBK"),

            // Control registers
            [0xD01B] = Hw("PRIOR"),
            [0xD01C] = Hw("VDELAY"),
            [0xD01D] = Hw("GRACTL"),
            [0xD01E] = Hw("HITCLR"),
            [0xD01F] = Hw("CONSOL"),

            // ── POKEY ($D200–$D2FF) — Hardware group ─────────────────────
            // Sound registers
            [0xD200] = Hw("AUDF1"),
            [0xD201] = Hw("AUDC1"),
            [0xD202] = Hw("AUDF2"),
            [0xD203] = Hw("AUDC2"),
            [0xD204] = Hw("AUDF3"),
            [0xD205] = Hw("AUDC3"),
            [0xD206] = Hw("AUDF4"),
            [0xD207] = Hw("AUDC4"),
            [0xD208] = Hw("AUDCTL"),
            [0xD209] = Hw("STIMER"),

            // I/O registers (read/write aliasing)
            [0xD20A] = Hw("KBCODE"),  // Read: keyboard code
            // $D20A read also: RANDOM (same address, different function)
            [0xD20A] = Hw("RANDOM"),  // Note: same address as KBCODE, read-only

            // Potentiometer registers
            [0xD20B] = Hw("POT0"),
            [0xD20C] = Hw("POT1"),
            [0xD20D] = Hw("POT2"),
            [0xD20E] = Hw("POT3"),
            [0xD20F] = Hw("POT4"),

            // Additional POKEY aliases (documented separately)
            // $D20D write: SEROUT (serial out)
            // $D20E write: IRQEN (interrupt enable)
            // $D20E read:  IRQST (interrupt status)
            // $D20F:       SKCTL (serial port control) / SKSTAT (read: serial port status)

            // POKEY reserved range ($D210–$D21F)
            [0xD210] = Hw("POKEY_RESV0"),
            [0xD211] = Hw("POKEY_RESV1"),
            [0xD212] = Hw("POKEY_RESV2"),
            [0xD213] = Hw("POKEY_RESV3"),
            [0xD214] = Hw("POKEY_RESV4"),
            [0xD215] = Hw("POKEY_RESV5"),
            [0xD216] = Hw("POKEY_RESV6"),
            [0xD217] = Hw("POKEY_RESV7"),
            [0xD218] = Hw("POKEY_RESV8"),
            [0xD219] = Hw("POKEY_RESV9"),
            [0xD21A] = Hw("POKEY_RESVA"),
            [0xD21B] = Hw("POKEY_RESVB"),
            [0xD21C] = Hw("POKEY_RESVC"),
            [0xD21D] = Hw("POKEY_RESVD"),
            [0xD21E] = Hw("POKEY_RESVE"),
            [0xD21F] = Hw("POKEY_RESVF"),

            // ── PIA ($D300–$D3FF) — Hardware group ───────────────────────
            [0xD300] = Hw("PORTA"),
            [0xD301] = Hw("PORTB"),
            [0xD302] = Hw("PACTL"),
            [0xD303] = Hw("PBCTL"),

            // ── ANTIC ($D400–$D4FF) — Hardware group ─────────────────────
            [0xD400] = Hw("DMACTL"),
            [0xD401] = Hw("CHACTL"),
            [0xD402] = Hw("DLISTL"),
            [0xD403] = Hw("DLISTH"),
            [0xD404] = Hw("HSCROL"),
            [0xD405] = Hw("VSCROL"),
            [0xD406] = Hw("ANTIC_RESV0"),  // Reserved
            [0xD407] = Hw("PMBASE"),
            [0xD408] = Hw("ANTIC_RESV1"),  // Reserved
            [0xD409] = Hw("CHBASE"),
            [0xD40A] = Hw("WSYNC"),
            [0xD40B] = Hw("VCOUNT"),
            [0xD40C] = Hw("ANTIC_RESV2"),  // Reserved
            [0xD40D] = Hw("ANTIC_RESV3"),  // Reserved
            [0xD40E] = Hw("NMIEN"),
            [0xD40F] = Hw("NMIST"),

            // ANTIC reserved range ($D410–$D41F)
            [0xD410] = Hw("ANTIC_RESV4"),
            [0xD411] = Hw("ANTIC_RESV5"),
            [0xD412] = Hw("ANTIC_RESV6"),
            [0xD413] = Hw("ANTIC_RESV7"),
            [0xD414] = Hw("ANTIC_RESV8"),
            [0xD415] = Hw("ANTIC_RESV9"),
            [0xD416] = Hw("ANTIC_RESVA"),
            [0xD417] = Hw("ANTIC_RESVB"),
            [0xD418] = Hw("ANTIC_RESVC"),
            [0xD419] = Hw("ANTIC_RESVD"),
            [0xD41A] = Hw("ANTIC_RESVE"),
            [0xD41B] = Hw("ANTIC_RESVF"),
            [0xD41C] = Hw("ANTIC_RESV10"),
            [0xD41D] = Hw("ANTIC_RESV11"),
            [0xD41E] = Hw("ANTIC_RESV12"),
            [0xD41F] = Hw("ANTIC_RESV13"),
        };

    public static IReadOnlyDictionary<byte, SymbolEntry> ZeroPageSymbols { get; } =
        new Dictionary<byte, SymbolEntry>
        {
            // ── OS Zero Page Variables (OsVariables group) ────────────────

            // $00–$07: Reserved / LINZBS
            [0x00] = OsVar("LINZBS0"),
            [0x01] = OsVar("LINZBS1"),
            [0x02] = OsVar("RTCLOK2"),  // Real-time clock (low byte)
            [0x03] = OsVar("RTCLOK1"),  // Real-time clock (mid byte)
            [0x04] = OsVar("RTCLOK0"),  // Real-time clock (high byte)
            [0x05] = OsVar("LINZBS5"),
            [0x06] = OsVar("LINZBS6"),
            [0x07] = OsVar("LINZBS7"),

            // $08–$0F: I/O Control Block
            [0x08] = OsVar("ICCOM"),
            [0x09] = OsVar("ICBAL"),
            [0x0A] = OsVar("ICBAH"),  // Also DOSVEC
            [0x0B] = OsVar("ICPTL"),
            [0x0C] = OsVar("ICPTH"),  // Also DOSINI
            [0x0D] = OsVar("ICBLL"),
            [0x0E] = OsVar("ICBLH"),
            [0x0F] = OsVar("ICAX1Z"),

            // $10–$1F: Floating Point Registers (FR0–FR6)
            [0x10] = OsVar("FR0_0"),
            [0x11] = OsVar("FR0_1"),
            [0x12] = OsVar("FR0_2"),
            [0x13] = OsVar("FR0_3"),
            [0x14] = OsVar("FR0_4"),  // Also POKMSK
            [0x15] = OsVar("FR0_5"),
            [0x16] = OsVar("EEXP"),
            [0x17] = OsVar("FR1_0"),
            [0x18] = OsVar("FR1_1"),  // Also RTCLOCK
            [0x19] = OsVar("FR1_2"),
            [0x1A] = OsVar("FR1_3"),
            [0x1B] = OsVar("FR1_4"),
            [0x1C] = OsVar("FR1_5"),
            [0x1D] = OsVar("FR2_0"),
            [0x1E] = OsVar("FR2_1"),
            [0x1F] = OsVar("FR2_2"),

            // $20–$3F: User Zero Page (documented as available for user programs)
            [0x20] = OsVar("USER0"),
            [0x21] = OsVar("USER1"),
            [0x22] = OsVar("USER2"),
            [0x23] = OsVar("USER3"),
            [0x24] = OsVar("USER4"),
            [0x25] = OsVar("USER5"),
            [0x26] = OsVar("USER6"),
            [0x27] = OsVar("USER7"),
            [0x28] = OsVar("USER8"),
            [0x29] = OsVar("USER9"),
            [0x2A] = OsVar("USERA"),
            [0x2B] = OsVar("USERB"),
            [0x2C] = OsVar("ICAX2Z"),  // Also user ZP
            [0x2D] = OsVar("USERD"),
            [0x2E] = OsVar("USERE"),
            [0x2F] = OsVar("USERF"),
            [0x30] = OsVar("USER10"),
            [0x31] = OsVar("USER11"),
            [0x32] = OsVar("USER12"),
            [0x33] = OsVar("USER13"),
            [0x34] = OsVar("USER14"),
            [0x35] = OsVar("USER15"),
            [0x36] = OsVar("USER16"),
            [0x37] = OsVar("USER17"),
            [0x38] = OsVar("USER18"),
            [0x39] = OsVar("USER19"),
            [0x3A] = OsVar("USER1A"),
            [0x3B] = OsVar("USER1B"),
            [0x3C] = OsVar("USER1C"),
            [0x3D] = OsVar("USER1D"),
            [0x3E] = OsVar("USER1E"),
            [0x3F] = OsVar("USER1F"),

            // $40–$4F: OS Variables
            [0x40] = OsVar("NMIEN_V"),   // NMI enable shadow
            [0x41] = OsVar("NMIRES_V"),  // NMI resume
            [0x42] = OsVar("RUNAD"),     // Run address (XEX loader)
            [0x43] = OsVar("RUNADH"),    // Run address high
            [0x44] = OsVar("INITAD"),    // Init address (XEX loader)
            [0x45] = OsVar("INITADH"),   // Init address high
            [0x46] = OsVar("RAMSIZ"),    // RAM size
            [0x47] = OsVar("MEMTOP"),    // Top of memory (low)
            [0x48] = OsVar("MEMTOPH"),   // Top of memory (high)
            [0x49] = OsVar("MEMLO"),     // Bottom of free memory (low)
            [0x4A] = OsVar("MEMLOH"),    // Bottom of free memory (high)
            [0x4B] = OsVar("DVSTAT"),    // Device status buffer
            [0x4C] = OsVar("CBAUD"),     // Cassette baud rate
            [0x4D] = OsVar("CRETRY"),    // Cassette retry count
            [0x4E] = OsVar("COLDST"),    // Cold start flag
            [0x4F] = OsVar("RECVDN"),    // Receive done flag

            // $50–$5F: OS Variables
            [0x50] = OsVar("BUFRFL"),
            [0x51] = OsVar("ROWCRS"),    // Cursor row (temporary)
            [0x52] = OsVar("COLCRS"),    // Cursor column (temporary)
            [0x53] = OsVar("DINDEX"),    // Display mode index
            [0x54] = OsVar("SAVMSC"),    // Saved screen memory (low)
            [0x55] = OsVar("SAVMSCH"),   // Saved screen memory (high)
            [0x56] = OsVar("OLDROW"),    // Old row
            [0x57] = OsVar("OLDCOL"),    // Old column
            [0x58] = OsVar("SAVMSC"),    // (alias)
            [0x59] = OsVar("SAVMSCH"),   // (alias)
            [0x5A] = OsVar("OLDROW"),    // (alias)
            [0x5B] = OsVar("OLDCOL"),    // (alias)
            [0x5C] = OsVar("HOLD1"),
            [0x5D] = OsVar("HOLD2"),
            [0x5E] = OsVar("HOLD3"),
            [0x5F] = OsVar("HOLD4"),

            // $60–$6F: OS Variables
            [0x60] = OsVar("CRSINV"),
            [0x61] = OsVar("KEYDEF"),
            [0x62] = OsVar("SWPFLG"),
            [0x63] = OsVar("SCRFLG"),
            [0x64] = OsVar("CRETRY"),    // (alias)
            [0x65] = OsVar("BUFADR"),
            [0x66] = OsVar("BUFADRH"),
            [0x67] = OsVar("TIMER1"),
            [0x68] = OsVar("ADRESS"),
            [0x69] = OsVar("ADRESSH"),
            [0x6A] = OsVar("TIMER2"),
            [0x6B] = OsVar("TEMPA"),
            [0x6C] = OsVar("TEMPB"),
            [0x6D] = OsVar("TEMPC"),
            [0x6E] = OsVar("TEMPD"),
            [0x6F] = OsVar("TEMPE"),

            // $70–$7F: OS Variables
            [0x70] = OsVar("SDLSTL"),    // Display list pointer (low)
            [0x71] = OsVar("SDLSTH"),    // Display list pointer (high)
            [0x72] = OsVar("SSKCTL"),    // Serial port control shadow
            [0x73] = OsVar("LCOUNT"),    // Line count
            [0x74] = OsVar("LMARGN"),    // Left margin
            [0x75] = OsVar("RMARGN"),    // Right margin
            [0x76] = OsVar("ROWCRS"),    // Cursor row
            [0x77] = OsVar("COLCRS"),    // Cursor column
            [0x78] = OsVar("DINDEX"),    // Display index
            [0x79] = OsVar("CH"),        // Last character read
            [0x7A] = OsVar("CHACT"),     // Character control shadow
            [0x7B] = OsVar("CHBAS"),     // Character base shadow
            [0x7C] = OsVar("CH"),        // (alias)
            [0x7D] = OsVar("FILDAT"),
            [0x7E] = OsVar("DSPFLG"),
            [0x7F] = OsVar("SSFLAG"),

            // $80–$FF: Cassette Buffer / User
            [0x80] = OsVar("CASSBF0"),
            [0x81] = OsVar("CASSBF1"),
            [0x82] = OsVar("DSTAT"),     // Screen status
            [0x83] = OsVar("ATRACT"),    // Attract mode flag
            [0x84] = OsVar("DRKMSK"),    // Dark attract mask
            [0x85] = OsVar("COLRSH"),    // Color shift
            [0x86] = OsVar("LMARGN"),    // (alias, also $74)
            [0x87] = OsVar("RMARGN"),    // (alias, also $75)
            [0x88] = OsVar("ROWINC"),    // Cursor row increment
            [0x89] = OsVar("COLINC"),    // Cursor column increment
            [0x8A] = OsVar("ROWDEL"),
            [0x8B] = OsVar("COLDEL"),
            [0x8C] = OsVar("TABMAP0"),
            [0x8D] = OsVar("TABMAP1"),
            [0x8E] = OsVar("TABMAP2"),
            [0x8F] = OsVar("TABMAP3"),
            [0x90] = OsVar("TABMAP4"),
            [0x91] = OsVar("TABMAP5"),
            [0x92] = OsVar("TABMAP6"),
            [0x93] = OsVar("TABMAP7"),
            [0x94] = OsVar("TABMAP8"),
            [0x95] = OsVar("TABMAP9"),
            [0x96] = OsVar("TABMAPA"),
            [0x97] = OsVar("TABMAPB"),
            [0x98] = OsVar("TABMAPC"),
            [0x99] = OsVar("TABMAPD"),
            [0x9A] = OsVar("TABMAPE"),
            [0x9B] = OsVar("TABMAPF"),
            [0x9C] = OsVar("LOGCOL"),
            [0x9D] = OsVar("ADDCOR"),
            [0x9E] = OsVar("MIKCOL"),
            [0x9F] = OsVar("SHFAMT"),
            [0xA0] = OsVar("GPRIOR"),    // GTIA priority shadow
            [0xA1] = OsVar("PADDL0"),    // Paddle 0
            [0xA2] = OsVar("PADDL1"),    // Paddle 1
            [0xA3] = OsVar("PADDL2"),    // Paddle 2
            [0xA4] = OsVar("PADDL3"),    // Paddle 3
            [0xA5] = OsVar("PADDL4"),    // Paddle 4 (STICK0)
            [0xA6] = OsVar("PADDL5"),    // Paddle 5 (STICK1)
            [0xA7] = OsVar("PADDL6"),    // Paddle 6 (STICK2)
            [0xA8] = OsVar("PADDL7"),    // Paddle 7 (STICK3)
            [0xA9] = OsVar("STICK0"),    // Joystick 0
            [0xAA] = OsVar("STICK1"),    // Joystick 1
            [0xAB] = OsVar("STICK2"),    // Joystick 2
            [0xAC] = OsVar("STICK3"),    // Joystick 3
            [0xAD] = OsVar("STRIG0"),    // Trigger 0
            [0xAE] = OsVar("STRIG1"),    // Trigger 1
            [0xAF] = OsVar("STRIG2"),    // Trigger 2
            [0xB0] = OsVar("STRIG3"),    // Trigger 3
            [0xB1] = OsVar("CSTAT"),
            [0xB2] = OsVar("FMODE"),
            [0xB3] = OsVar("FMSZ"),
            [0xB4] = OsVar("BITMSK"),
            [0xB5] = OsVar("CHSALT"),
            [0xB6] = OsVar("PCOLR0"),
            [0xB7] = OsVar("PCOLR1"),
            [0xB8] = OsVar("PCOLR2"),
            [0xB9] = OsVar("PCOLR3"),
            [0xBA] = OsVar("COLOR0"),    // Playfield color 0 shadow
            [0xBB] = OsVar("COLOR1"),    // Playfield color 1 shadow
            [0xBC] = OsVar("COLOR2"),    // Playfield color 2 shadow
            [0xBD] = OsVar("COLOR3"),    // Playfield color 3 shadow
            [0xBE] = OsVar("COLOR4"),    // Playfield color 4 shadow
            [0xBF] = OsVar("GLBFLG"),
            [0xC0] = OsVar("AUTFLG"),
            [0xC1] = OsVar("SHPDVS"),
            [0xC2] = OsVar("BRKKEY"),
            [0xC3] = OsVar("BRKFLG"),
            [0xC4] = OsVar("DSPFLG"),
            [0xC5] = OsVar("RAMTOP"),
            [0xC6] = OsVar("RAMTOP_V"),
            [0xC7] = OsVar("BUFADR"),
            [0xC8] = OsVar("BUFADRH"),
            [0xC9] = OsVar("BUFADRL"),
            [0xCA] = OsVar("TIMER1"),
            [0xCB] = OsVar("ADRESS"),
            [0xCC] = OsVar("ADRESSH"),
            [0xCD] = OsVar("TIMER2"),
            [0xCE] = OsVar("TEMPA"),
            [0xCF] = OsVar("TEMPB"),
            [0xD0] = OsVar("COLOR0"),
            [0xD1] = OsVar("COLOR1"),
            [0xD2] = OsVar("COLOR2"),
            [0xD3] = OsVar("COLOR3"),
            [0xD4] = OsVar("COLOR4"),
            [0xD5] = OsVar("GLBFLG"),
            [0xD6] = OsVar("AUTFLG"),
            [0xD7] = OsVar("SHPDVS"),
            [0xD8] = OsVar("STICK0"),
            [0xD9] = OsVar("STICK1"),
            [0xDA] = OsVar("STICK2"),
            [0xDB] = OsVar("STICK3"),
            [0xDC] = OsVar("STRIG0"),
            [0xDD] = OsVar("STRIG1"),
            [0xDE] = OsVar("STRIG2"),
            [0xDF] = OsVar("STRIG3"),
            [0xE0] = OsVar("CSTAT"),
            [0xE1] = OsVar("FMODE"),
            [0xE2] = OsVar("FMSZ"),
            [0xE3] = OsVar("BITMSK"),
            [0xE4] = OsVar("CHSALT"),
            [0xE5] = OsVar("PCOLR0"),
            [0xE6] = OsVar("PCOLR1"),
            [0xE7] = OsVar("PCOLR2"),
            [0xE8] = OsVar("PCOLR3"),
            [0xE9] = OsVar("ROWAC0"),
            [0xEA] = OsVar("ROWAC1"),
            [0xEB] = OsVar("ROWAC2"),
            [0xEC] = OsVar("ROWAC3"),
            [0xED] = OsVar("ROWAC4"),
            [0xEE] = OsVar("ROWAC5"),
            [0xEF] = OsVar("ROWAC6"),
            [0xF0] = OsVar("ROWAC7"),
            [0xF1] = OsVar("ROWAC8"),
            [0xF2] = OsVar("ROWAC9"),
            [0xF3] = OsVar("ROWACA"),
            [0xF4] = OsVar("ROWACB"),
            [0xF5] = OsVar("ROWACC"),
            [0xF6] = OsVar("ROWACD"),
            [0xF7] = OsVar("ROWACE"),
            [0xF8] = OsVar("ROWACF"),
            [0xF9] = OsVar("RAMTOP"),
            [0xFA] = OsVar("RAMSIZ"),
            [0xFB] = OsVar("MEMTOP"),
            [0xFC] = OsVar("MEMTOPH"),
            [0xFD] = OsVar("MEMLO"),
            [0xFE] = OsVar("MEMLOH"),
            [0xFF] = OsVar("DVSTAT"),
        };

    public static void Populate(SymbolTable table)
    {
        table.Clear();
        foreach (var pair in HardwareSymbols)
        {
            table[pair.Key] = pair.Value;
        }
    }

    public static void PopulateZeroPage(ZeroPageMap map)
    {
        map.Clear();
        foreach (var pair in ZeroPageSymbols)
        {
            map[pair.Key] = pair.Value;
        }
    }

    public static bool TryGetHardwareSymbol(ushort address, out SymbolEntry entry) =>
        HardwareSymbols.TryGetValue(address, out entry!);

    public static bool TryGetZeroPageHardwareSymbol(byte address, out SymbolEntry entry) =>
        ZeroPageSymbols.TryGetValue(address, out entry!);

    private static SymbolEntry Hw(string label) => new(label, null, true, false, SymbolGroup.Hardware);
    private static SymbolEntry OsRom(string label) => new(label, null, true, false, SymbolGroup.OsRom);
    private static SymbolEntry OsVar(string label) => new(label, null, true, false, SymbolGroup.OsVariables);
}
