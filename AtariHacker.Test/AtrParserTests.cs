using AtariHacker.Atari;
using System.IO;

namespace AtariHacker.Test;

public sealed class AtrParserTests
{
    private static byte[] CreateAtrHeader(ushort paragraphs, ushort sectorSize, ushort? paragraphsHigh = null)
    {
        var header = new byte[16];
        header[0] = 0x96;
        header[1] = 0x02;
        header[2] = (byte)(paragraphs & 0xFF);
        header[3] = (byte)((paragraphs >> 8) & 0xFF);
        header[4] = (byte)(sectorSize & 0xFF);
        header[5] = (byte)((sectorSize >> 8) & 0xFF);
        if (paragraphsHigh.HasValue)
        {
            header[6] = (byte)(paragraphsHigh.Value & 0xFF);
            header[7] = (byte)((paragraphsHigh.Value >> 8) & 0xFF);
        }
        return header;
    }

    private static byte[] CreateSdAtr(int sectorCount = 720)
    {
        var dataBytes = sectorCount * 128;
        var paragraphs = dataBytes / 16;
        var header = CreateAtrHeader((ushort)paragraphs, 128);
        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);
        return data;
    }

    private static byte[] CreateDdAtr(int sectorCount = 720)
    {
        var dataBytes = (3 * 128) + ((sectorCount - 3) * 256);
        var paragraphs = dataBytes / 16;
        var header = CreateAtrHeader((ushort)paragraphs, 256);
        var data = new byte[16 + dataBytes];
        Array.Copy(header, data, 16);
        return data;
    }

    [Fact]
    public void IsAtr_ReturnsTrueForValidAtrHeader()
    {
        var header = CreateAtrHeader((ushort)(720 * 128 / 16), 128);
        Assert.True(AtrParser.IsAtr(header));
    }

    [Fact]
    public void IsAtr_ReturnsFalseForDataShorterThan16Bytes()
    {
        var data = new byte[] { 0x96, 0x02 };
        Assert.False(AtrParser.IsAtr(data));
    }

    [Fact]
    public void IsAtr_ReturnsFalseForInvalidMagicBytes()
    {
        var data = new byte[16];
        Assert.False(AtrParser.IsAtr(data));
    }

    [Fact]
    public void ParseGeometry_ParsesSdCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(128, geometry.SectorSize);
        Assert.Equal(720, geometry.SectorCount);
        Assert.Equal("SD", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ParsesDdCorrectly()
    {
        var atr = CreateDdAtr(720);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(256, geometry.SectorSize);
        Assert.Equal(720, geometry.SectorCount);
        Assert.Equal("DD", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ParsesEdCorrectly()
    {
        var atr = CreateSdAtr(1040);
        var geometry = AtrParser.ParseGeometry(atr);
        Assert.Equal(128, geometry.SectorSize);
        Assert.Equal(1040, geometry.SectorCount);
        Assert.Equal("ED", geometry.Density);
    }

    [Fact]
    public void ParseGeometry_ThrowsForUnsupportedSectorSize()
    {
        var header = CreateAtrHeader(1000, 64);
        Assert.Throws<InvalidDataException>(() => AtrParser.ParseGeometry(header));
    }

    [Fact]
    public void ParseGeometry_ThrowsForNonAtrData()
    {
        var data = new byte[16];
        Assert.Throws<InvalidDataException>(() => AtrParser.ParseGeometry(data));
    }

    [Fact]
    public void ReadSector_ReturnsCorrectBytes()
    {
        var atr = CreateSdAtr(720);
        var sectorOffset = 16 + ((10 - 1) * 128);
        atr[sectorOffset] = 0xAB;

        var sector = AtrParser.ReadSector(atr, new AtrGeometry(128, 720, "SD"), 10);
        Assert.Equal(128, sector.Length);
        Assert.Equal(0xAB, sector[0]);
    }

    [Fact]
    public void ReadSector_ThrowsForSectorZero()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        Assert.Throws<ArgumentOutOfRangeException>(() => AtrParser.ReadSector(atr, geometry, 0));
    }

    [Fact]
    public void ReadSector_ThrowsForSectorBeyondCount()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        Assert.Throws<ArgumentOutOfRangeException>(() => AtrParser.ReadSector(atr, geometry, 721));
    }

    [Fact]
    public void ReadSector_HandlesSector1To3With128ByteLengthOnDdImages()
    {
        var atr = CreateDdAtr(720);
        var geometry = new AtrGeometry(256, 720, "DD");

        var sector1 = AtrParser.ReadSector(atr, geometry, 1);
        Assert.Equal(128, sector1.Length);

        var sector4 = AtrParser.ReadSector(atr, geometry, 4);
        Assert.Equal(256, sector4.Length);
    }

    [Fact]
    public void HasDosFilesystem_DetectsDos2xCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset] = 8;
        atr[vtocOffset + 1] = 0xD0;
        atr[vtocOffset + 2] = 0x02;

        Assert.True(AtrParser.HasDosFilesystem(atr));
    }

    [Fact]
    public void HasDosFilesystem_ReturnsFalseForNonDosImages()
    {
        var atr = CreateSdAtr(100);
        Assert.False(AtrParser.HasDosFilesystem(atr));
    }

    [Fact]
    public void GetSectorChain_FollowsSectorLinksCorrectly()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var s5Offset = 16 + ((5 - 1) * 128);
        atr[s5Offset + 125] = 0;
        atr[s5Offset + 126] = 10;

        var s10Offset = 16 + ((10 - 1) * 128);
        atr[s10Offset + 125] = 0;
        atr[s10Offset + 126] = 20;

        var s20Offset = 16 + ((20 - 1) * 128);
        atr[s20Offset + 125] = 0;
        atr[s20Offset + 126] = 0;

        var chain = AtrParser.GetSectorChain(atr, geometry, 5);
        Assert.Equal(3, chain.Count);
        Assert.Equal([5, 10, 20], chain);
    }

    [Fact]
    public void GetSectorChain_ThrowsOnLoop()
    {
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var s5Offset = 16 + ((5 - 1) * 128);
        atr[s5Offset + 125] = 0;
        atr[s5Offset + 126] = 10;

        var s10Offset = 16 + ((10 - 1) * 128);
        atr[s10Offset + 125] = 0;
        atr[s10Offset + 126] = 5;

        Assert.Throws<InvalidDataException>(() => AtrParser.GetSectorChain(atr, geometry, 5));
    }

    // ─── TryParseBootHeader tests ───────────────────────────────────────

    [Fact]
    public void TryParseBootHeader_ReturnsNullForEmptyData()
    {
        Assert.Null(AtrParser.TryParseBootHeader(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void TryParseBootHeader_ReturnsNullForShortData()
    {
        Assert.Null(AtrParser.TryParseBootHeader(new byte[] { 0xD0, 0x03, 0x00, 0x07 }));
    }

    [Fact]
    public void TryParseBootHeader_ReturnsNullForInvalidFlag()
    {
        Assert.Null(AtrParser.TryParseBootHeader(new byte[] { 0xFF, 0x03, 0x00, 0x07, 0x40, 0x15 }));
    }

    [Fact]
    public void TryParseBootHeader_ParsesStandardBootHeader()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0xD0, result!.Flag);
        Assert.Equal(3, result.SectorCount);
        Assert.Equal(0x0700, result.LoadAddress);
        Assert.Equal(0x1540, result.InitAddress);
    }

    [Fact]
    public void TryParseBootHeader_ParsesContinueFlag()
    {
        var data = new byte[] { 0x00, 0x03, 0x00, 0x07, 0x00, 0x07 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0x00, result!.Flag);
        Assert.Equal("Continue loading", result.Description);
    }

    [Fact]
    public void TryParseBootHeader_ParsesStopRunFlag()
    {
        var data = new byte[] { 0xD0, 0x03, 0x00, 0x07, 0x40, 0x15 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0xD0, result!.Flag);
        Assert.Equal("Stop/run", result.Description);
    }

    [Fact]
    public void TryParseBootHeader_ParsesLoadAndInitAddresses()
    {
        var data = new byte[] { 0xD0, 0x01, 0x00, 0x20, 0x00, 0x30 };
        var result = AtrParser.TryParseBootHeader(data);

        Assert.NotNull(result);
        Assert.Equal(0x2000, result!.LoadAddress);
        Assert.Equal(0x3000, result.InitAddress);
    }

    // ─── MyDOS Filesystem Tests ──────────────────────────────────────────

    private static byte[] CreateMyDosAtr(int sectorCount = 720, bool singleVtoc = true)
    {
        var atr = CreateSdAtr(sectorCount);
        var geometry = new AtrGeometry(128, sectorCount, "SD");

        // Write MyDOS VTOC at sector 360
        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset] = 0x02;     // DOS code
        atr[vtocOffset + 1] = (byte)(sectorCount & 0xFF);       // Total sectors low
        atr[vtocOffset + 2] = (byte)((sectorCount >> 8) & 0xFF); // Total sectors high
        atr[vtocOffset + 3] = (byte)((sectorCount - 12) & 0xFF); // Free sectors low
        atr[vtocOffset + 4] = (byte)(((sectorCount - 12) >> 8) & 0xFF); // Free sectors high
        atr[vtocOffset + 5] = 0x02;  // MyDOS extended VTOC flag

        if (singleVtoc)
        {
            // Single VTOC: next-VTOC = 0
            atr[vtocOffset + 6] = 0;
            atr[vtocOffset + 7] = 0;
        }
        else
        {
            // Multi-VTOC: chain to sector 1024
            atr[vtocOffset + 6] = 0x00; // 1024 & 0xFF
            atr[vtocOffset + 7] = 0x04; // (1024 >> 8) & 0xFF

            // Write secondary VTOC at sector 1024
            if (sectorCount >= 1024)
            {
                var secVtocOffset = 16 + ((1024 - 1) * 128);
                atr[secVtocOffset] = 0;     // Next VTOC low (0 = last)
                atr[secVtocOffset + 1] = 0; // Next VTOC high
                // Fill bitmap with all free
                for (var i = 2; i < 128; i++)
                    atr[secVtocOffset + i] = 0xFF;
            }
        }

        // Fill bitmap with all free initially
        for (var i = 10; i < 128; i++)
            atr[vtocOffset + i] = 0xFF;

        // Mark boot sectors (1-3) as used
        for (var s = 1; s <= 3; s++)
        {
            var byteIdx = 10 + ((s - 1) / 8);
            var bitIdx = (s - 1) % 8;
            atr[vtocOffset + byteIdx] &= (byte)~(1 << bitIdx);
        }

        // Mark VTOC (360) as used
        var vtocByteIdx = 10 + ((360 - 1) / 8);
        var vtocBitIdx = (360 - 1) % 8;
        atr[vtocOffset + vtocByteIdx] &= (byte)~(1 << vtocBitIdx);

        // Mark directory sectors (361-368) as used
        for (var s = 361; s <= 368 && s <= sectorCount; s++)
        {
            var dbIdx = 10 + ((s - 1) / 8);
            var dbBit = (s - 1) % 8;
            atr[vtocOffset + dbIdx] &= (byte)~(1 << dbBit);
        }

        return atr;
    }

    [Fact]
    public void HasMyDosFilesystem_DetectsMyDosCorrectly()
    {
        var atr = CreateMyDosAtr(720);
        Assert.True(AtrParser.HasMyDosFilesystem(atr));
    }

    [Fact]
    public void HasMyDosFilesystem_ReturnsFalseForDos2x()
    {
        var atr = CreateSdAtr(720);
        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset] = 8;
        atr[vtocOffset + 1] = 0xD0;
        atr[vtocOffset + 2] = 0x02;
        // Byte 5 is 0x00 for DOS 2.x (not 0x02)
        Assert.False(AtrParser.HasMyDosFilesystem(atr));
    }

    [Fact]
    public void HasMyDosFilesystem_ReturnsFalseForTooSmallDisk()
    {
        var atr = CreateSdAtr(100);
        Assert.False(AtrParser.HasMyDosFilesystem(atr));
    }

    [Fact]
    public void HasMyDosFilesystem_ReturnsFalseForInvalidVtocFlag()
    {
        var atr = CreateSdAtr(720);
        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset + 5] = 0xFF; // Invalid flag
        Assert.False(AtrParser.HasMyDosFilesystem(atr));
    }

    [Fact]
    public void HasMyDosFilesystem_ReturnsFalseForInvalidNextVtoc()
    {
        var atr = CreateSdAtr(720);
        var vtocOffset = 16 + ((360 - 1) * 128);
        atr[vtocOffset + 6] = 0xFF; // Invalid next-VTOC (beyond disk)
        atr[vtocOffset + 7] = 0xFF;
        Assert.False(AtrParser.HasMyDosFilesystem(atr));
    }

    [Fact]
    public void GetMyDosVtocChain_ReturnsSingleSector()
    {
        var atr = CreateMyDosAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        var chain = AtrParser.GetMyDosVtocChain(atr, geometry);
        Assert.Single(chain);
        Assert.Equal(360, chain[0]);
    }

    [Fact]
    public void GetMyDosVtocChain_ReturnsMultiSectorChain()
    {
        var atr = CreateMyDosAtr(1040, singleVtoc: false);
        var geometry = new AtrGeometry(128, 1040, "ED");
        var chain = AtrParser.GetMyDosVtocChain(atr, geometry);
        Assert.Equal(2, chain.Count);
        Assert.Equal(360, chain[0]);
        Assert.Equal(1024, chain[1]);
    }

    [Fact]
    public void GetMyDosFreeSectorCount_ReturnsStoredValue()
    {
        var atr = CreateMyDosAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");
        var freeCount = AtrParser.GetMyDosFreeSectorCount(atr, geometry);
        // 720 - 3 (boot) - 1 (VTOC) - 8 (directory) = 708
        Assert.Equal(708, freeCount);
    }

    [Fact]
    public void ReadMyDosDirectory_ReturnsEmptyForEmptyDisk()
    {
        var atr = CreateMyDosAtr(720);
        var entries = AtrParser.ReadMyDosDirectory(atr);
        Assert.Empty(entries);
    }

    [Fact]
    public void ReadMyDosDirectory_ReadsEntries()
    {
        var atr = CreateMyDosAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        // Write a directory entry in sector 361
        var dirOffset = 16 + ((361 - 1) * 128);
        atr[dirOffset] = 0x42;     // Flags: non-deleted, binary
        atr[dirOffset + 1] = 5;    // Sector count low
        atr[dirOffset + 2] = 0;    // Sector count high
        atr[dirOffset + 3] = 100;  // Start sector low
        atr[dirOffset + 4] = 0;    // Start sector high
        // Filename: "TEST    "
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("TEST    ");
        Array.Copy(nameBytes, 0, atr, dirOffset + 5, 8);
        // Extension: "TXT"
        var extBytes = System.Text.Encoding.ASCII.GetBytes("TXT");
        Array.Copy(extBytes, 0, atr, dirOffset + 13, 3);

        var entries = AtrParser.ReadMyDosDirectory(atr);
        Assert.Single(entries);
        Assert.Equal("TEST", entries[0].FileName);
        Assert.Equal("TXT", entries[0].Extension);
        Assert.Equal(100, entries[0].StartSector);
        Assert.Equal(5, entries[0].SectorCount);
        Assert.False(entries[0].IsSubdirectory);
    }

    [Fact]
    public void ReadMyDosDirectory_DetectsSubdirectoryFlag()
    {
        var atr = CreateMyDosAtr(720);
        var dirOffset = 16 + ((361 - 1) * 128);
        // Flags: 0x52 = 0x42 (binary) | 0x10 (subdirectory)
        atr[dirOffset] = 0x52;
        atr[dirOffset + 1] = 8;    // Sector count
        atr[dirOffset + 2] = 0;
        atr[dirOffset + 3] = 200;  // Start sector
        atr[dirOffset + 4] = 0;
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("SUBDIR  ");
        Array.Copy(nameBytes, 0, atr, dirOffset + 5, 8);
        var extBytes = System.Text.Encoding.ASCII.GetBytes("DIR");
        Array.Copy(extBytes, 0, atr, dirOffset + 13, 3);

        var entries = AtrParser.ReadMyDosDirectory(atr);
        Assert.Single(entries);
        Assert.Equal("SUBDIR", entries[0].FileName);
        Assert.True(entries[0].IsSubdirectory);
    }

    [Fact]
    public void GetSectorChain_UsesFull16BitLinks()
    {
        // Test that removing the & 0x03 mask allows sector numbers >= 1024
        var atr = CreateSdAtr(1040);
        var geometry = new AtrGeometry(128, 1040, "ED");

        // Set up a chain: sector 1000 -> sector 1024 -> sector 1030 -> 0
        // Sector 1000 links to 1024
        var s1000Offset = 16 + ((1000 - 1) * 128);
        atr[s1000Offset + 125] = (byte)(1024 >> 8);  // Upper byte (0x04)
        atr[s1000Offset + 126] = (byte)(1024 & 0xFF); // Lower byte (0x00)
        atr[s1000Offset + 127] = 120;                  // Byte count

        // Sector 1024 links to 1030
        var s1024Offset = 16 + ((1024 - 1) * 128);
        atr[s1024Offset + 125] = (byte)(1030 >> 8);  // Upper byte (0x04)
        atr[s1024Offset + 126] = (byte)(1030 & 0xFF); // Lower byte (0x06)
        atr[s1024Offset + 127] = 120;

        // Sector 1030 links to 0 (end)
        var s1030Offset = 16 + ((1030 - 1) * 128);
        atr[s1030Offset + 125] = 0;
        atr[s1030Offset + 126] = 0;
        atr[s1030Offset + 127] = 120;

        var chain = AtrParser.GetSectorChain(atr, geometry, 1000);
        Assert.Equal(3, chain.Count);
        Assert.Equal(1000, chain[0]);
        Assert.Equal(1024, chain[1]);
        Assert.Equal(1030, chain[2]);
    }

    [Fact]
    public void ExtractFile_UsesFull16BitLinks()
    {
        // Test that ExtractFile correctly follows 16-bit links for sectors >= 1024
        var atr = CreateSdAtr(1040);
        var geometry = new AtrGeometry(128, 1040, "ED");

        // Set up a chain: sector 1000 -> sector 1024 -> 0
        var s1000Offset = 16 + ((1000 - 1) * 128);
        atr[s1000Offset + 125] = (byte)(1024 >> 8);
        atr[s1000Offset + 126] = (byte)(1024 & 0xFF);
        atr[s1000Offset + 127] = 5;  // 5 bytes of data
        atr[s1000Offset] = 0x41;
        atr[s1000Offset + 1] = 0x42;
        atr[s1000Offset + 2] = 0x43;
        atr[s1000Offset + 3] = 0x44;
        atr[s1000Offset + 4] = 0x45;

        var s1024Offset = 16 + ((1024 - 1) * 128);
        atr[s1024Offset + 125] = 0;
        atr[s1024Offset + 126] = 0;
        atr[s1024Offset + 127] = 3;  // 3 bytes of data
        atr[s1024Offset] = 0x46;
        atr[s1024Offset + 1] = 0x47;
        atr[s1024Offset + 2] = 0x48;

        var entry = new AtrDirectoryEntry(0, "TEST", "BIN", 1000, 2, false, false, true);
        var extracted = AtrParser.ExtractFile(atr, geometry, entry);
        Assert.Equal(8, extracted.Length);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48 }, extracted);
    }

    [Fact]
    public void Dos2xSectorChain_StillWorksAfterMaskRemoval()
    {
        // Regression test: DOS 2.0 sector chain should still work
        // with the & 0x03 mask removed (upper bits are 0 for DOS 2.0)
        var atr = CreateSdAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        var s5Offset = 16 + ((5 - 1) * 128);
        atr[s5Offset + 125] = 0;   // Upper byte (File ID = 0, next sector bits = 0)
        atr[s5Offset + 126] = 10;  // Lower byte
        atr[s5Offset + 127] = 120;

        var s10Offset = 16 + ((10 - 1) * 128);
        atr[s10Offset + 125] = 0;
        atr[s10Offset + 126] = 20;
        atr[s10Offset + 127] = 120;

        var s20Offset = 16 + ((20 - 1) * 128);
        atr[s20Offset + 125] = 0;
        atr[s20Offset + 126] = 0;
        atr[s20Offset + 127] = 120;

        var chain = AtrParser.GetSectorChain(atr, geometry, 5);
        Assert.Equal(3, chain.Count);
        Assert.Equal([5, 10, 20], chain);
    }

    [Fact]
    public void ReadMyDosSubdirectory_ReadsNestedEntries()
    {
        var atr = CreateMyDosAtr(720);
        var geometry = new AtrGeometry(128, 720, "SD");

        // Write a subdirectory at sector 200 (8 sectors: 200-207)
        // Sector 200 links to 201, 201 links to 0
        var s200Offset = 16 + ((200 - 1) * 128);
        atr[s200Offset + 125] = 0;
        atr[s200Offset + 126] = 201; // Next sector
        atr[s200Offset + 127] = 128;

        var s201Offset = 16 + ((201 - 1) * 128);
        atr[s201Offset + 125] = 0;
        atr[s201Offset + 126] = 0;  // End of chain
        atr[s201Offset + 127] = 128;

        // Write a directory entry in the subdirectory (sector 200)
        atr[s200Offset] = 0x42;     // Flags: non-deleted, binary
        atr[s200Offset + 1] = 3;    // Sector count low
        atr[s200Offset + 2] = 0;    // Sector count high
        atr[s200Offset + 3] = (byte)(300 & 0xFF);  // Start sector low
        atr[s200Offset + 4] = (byte)((300 >> 8) & 0xFF); // Start sector high
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("NESTED  ");
        Array.Copy(nameBytes, 0, atr, s200Offset + 5, 8);
        var extBytes = System.Text.Encoding.ASCII.GetBytes("DAT");
        Array.Copy(extBytes, 0, atr, s200Offset + 13, 3);

        var entries = AtrParser.ReadMyDosSubdirectory(atr, geometry, 200);
        Assert.Single(entries);
        Assert.Equal("NESTED", entries[0].FileName);
        Assert.Equal("DAT", entries[0].Extension);
        Assert.Equal(300, entries[0].StartSector);
    }
}
