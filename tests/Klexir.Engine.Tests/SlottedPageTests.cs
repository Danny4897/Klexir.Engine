using FluentAssertions;
using Klexir.Engine.Abstractions;
using Xunit;

namespace Klexir.Engine.Tests;

public sealed class SlottedPageTests
{
    [Fact]
    public void Insert_then_Read_roundtrips_a_record()
    {
        var page = new byte[128];
        SlottedPage.Initialize(page);

        var slot = SlottedPage.Insert(page, "hello"u8);
        var read = SlottedPage.Read(page, slot.Value);

        slot.IsSuccess.Should().BeTrue();
        read.Value.Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public void Insert_returns_sequential_slot_ids()
    {
        var page = new byte[128];
        SlottedPage.Initialize(page);

        var first = SlottedPage.Insert(page, "a"u8);
        var second = SlottedPage.Insert(page, "bb"u8);

        first.Value.Should().Be(new SlotId(0));
        second.Value.Should().Be(new SlotId(1));
    }

    [Fact]
    public void Insert_fails_when_the_page_has_no_room_left()
    {
        var page = new byte[16];
        SlottedPage.Initialize(page);

        var result = SlottedPage.Insert(page, new byte[64]);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Read_fails_for_an_out_of_range_slot()
    {
        var page = new byte[128];
        SlottedPage.Initialize(page);

        var result = SlottedPage.Read(page, new SlotId(0));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Delete_then_Read_fails_for_that_slot()
    {
        var page = new byte[128];
        SlottedPage.Initialize(page);
        var slot = SlottedPage.Insert(page, "hello"u8).Value;

        var deleted = SlottedPage.Delete(page, slot);
        var read = SlottedPage.Read(page, slot);

        deleted.IsSuccess.Should().BeTrue();
        read.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Insert_after_a_delete_of_an_earlier_slot_still_succeeds_from_remaining_free_space()
    {
        // Delete does not reclaim or compact the deleted record's bytes yet (that's a later increment) —
        // this only proves a subsequent insert isn't broken by an earlier delete.
        var page = new byte[128];
        SlottedPage.Initialize(page);
        var first = SlottedPage.Insert(page, "first"u8).Value;
        SlottedPage.Delete(page, first);

        var second = SlottedPage.Insert(page, "second"u8);

        second.IsSuccess.Should().BeTrue();
        SlottedPage.Read(page, second.Value).Value.Should().Equal("second"u8.ToArray());
    }
}
