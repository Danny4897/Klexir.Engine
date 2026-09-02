using System.Buffers.Binary;
using Klexir.Engine.Abstractions;
using MonadicSharp;

namespace Klexir.Engine;

/// <summary>
/// Variable-length record layout for one fixed-size page. Layout: a 4-byte header (slot count, free-space offset),
/// a slot directory growing forward from byte 4 (4 bytes per slot: record offset, record length), and records
/// themselves growing backward from the end of the page. Deleting a slot zeroes its directory entry; it does not
/// reclaim or compact space — compaction is a later increment.
/// </summary>
public static class SlottedPage
{
    private const int HeaderSize = sizeof(ushort) * 2;
    private const int SlotEntrySize = sizeof(ushort) * 2;

    public static void Initialize(Span<byte> page)
    {
        page.Clear();
        WriteUInt16(page, 0, 0);
        WriteUInt16(page, 2, (ushort)page.Length);
    }

    public static Result<SlotId> Insert(Span<byte> page, ReadOnlySpan<byte> record)
    {
        var slotCount = ReadUInt16(page, 0);
        var freeSpaceOffset = ReadUInt16(page, 2);
        var directoryEnd = HeaderSize + (slotCount * SlotEntrySize);
        var recordStart = freeSpaceOffset - record.Length;

        if (recordStart < directoryEnd + SlotEntrySize)
        {
            return Result<SlotId>.Failure(Error.Create("Page has no room for this record."));
        }

        record.CopyTo(page[recordStart..]);

        var slotOffset = directoryEnd;
        WriteUInt16(page, slotOffset, (ushort)recordStart);
        WriteUInt16(page, slotOffset + 2, (ushort)record.Length);

        WriteUInt16(page, 0, (ushort)(slotCount + 1));
        WriteUInt16(page, 2, (ushort)recordStart);

        return Result<SlotId>.Success(new SlotId((ushort)slotCount));
    }

    public static Result<byte[]> Read(ReadOnlySpan<byte> page, SlotId slotId)
    {
        var slotCount = ReadUInt16(page, 0);
        if (slotId.Value >= slotCount)
        {
            return Result<byte[]>.Failure(Error.NotFound("Slot", slotId.ToString()));
        }

        var slotOffset = HeaderSize + (slotId.Value * SlotEntrySize);
        var recordOffset = ReadUInt16(page, slotOffset);
        var recordLength = ReadUInt16(page, slotOffset + 2);

        if (recordOffset == 0 && recordLength == 0)
        {
            return Result<byte[]>.Failure(Error.Create($"Slot {slotId} was deleted."));
        }

        return Result<byte[]>.Success(page.Slice(recordOffset, recordLength).ToArray());
    }

    public static Result<Unit> Delete(Span<byte> page, SlotId slotId)
    {
        var slotCount = ReadUInt16(page, 0);
        if (slotId.Value >= slotCount)
        {
            return Result<Unit>.Failure(Error.NotFound("Slot", slotId.ToString()));
        }

        var slotOffset = HeaderSize + (slotId.Value * SlotEntrySize);
        WriteUInt16(page, slotOffset, 0);
        WriteUInt16(page, slotOffset + 2, 0);
        return Result<Unit>.Success(Unit.Value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> page, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(offset, 2));

    private static void WriteUInt16(Span<byte> page, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(offset, 2), value);
}
