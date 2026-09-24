using BookingService.Domain;
using BookingService.StateMachine;
using FluentAssertions;
using Xunit;

namespace UnitTests;

public class BookingStateMachineTests
{
    [Theory]
    [InlineData(BookingStatus.Initiated, BookingStatus.Queued, true)]
    [InlineData(BookingStatus.Queued, BookingStatus.Processing, true)]
    [InlineData(BookingStatus.Processing, BookingStatus.SeatLocked, true)]
    [InlineData(BookingStatus.SeatLocked, BookingStatus.PaymentPending, true)]
    [InlineData(BookingStatus.PaymentPending, BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.SeatLocked, BookingStatus.Expired, true)]
    [InlineData(BookingStatus.PaymentPending, BookingStatus.PaymentFailed, true)]
    public void Allows_documented_happy_and_failure_paths(BookingStatus from, BookingStatus to, bool expected)
    {
        BookingStateMachine.CanTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed, BookingStatus.SeatLocked)]
    [InlineData(BookingStatus.Expired, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Initiated, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Queued)]
    public void Rejects_illegal_transitions(BookingStatus from, BookingStatus to)
    {
        BookingStateMachine.CanTransition(from, to).Should().BeFalse();
        var act = () => BookingStateMachine.Transition(from, to);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions_except_documented_ones()
    {
        // Confirmed, Expired, Cancelled and Failed are terminal until an
        // explicit refund/cancellation Saga is implemented.
        BookingStateMachine.CanTransition(BookingStatus.Expired, BookingStatus.Confirmed).Should().BeFalse();
        BookingStateMachine.CanTransition(BookingStatus.Failed, BookingStatus.Queued).Should().BeFalse();
    }
}
