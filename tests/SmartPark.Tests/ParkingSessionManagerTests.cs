using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests;

public class ParkingSessionManagerTests
{
    // ────────────────────────────────────────────────────────────
    //  SHARED SETUP — create test doubles and the system-under-test.
    //  Moq's Mock<T> creates test doubles that can act as:
    //    - Stubs: .Setup().Returns() — provide canned answers
    //    - Mocks: .Verify()         — assert interactions happened
    //  You can use a constructor, or duplicate this in each test.
    // ────────────────────────────────────────────────────────────

    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly Mock<IMembershipService> _membershipStub = new();
    private readonly Mock<IParkingRepository> _repoStub = new();
    private readonly Mock<IDateTimeProvider> _dateTimeStub = new();
    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly ParkingSessionManager _manager;

    public ParkingSessionManagerTests()
    {
        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            _membershipStub.Object,
            _repoStub.Object,
            _dateTimeStub.Object);
    }

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows stub setup + mock verification pattern.
    //  .Setup().Returns() = STUB behavior (canned answer)
    //  .Verify()          = MOCK behavior (interaction assertion)
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckInAsync_NewVehicle_SavesTicketAndLooksUpMembership()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("PP-9999")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-9999")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        // Act
        var ticket = await _manager.CheckInAsync("PP-9999", VehicleType.Car);

        // Assert
        _membershipStub.Verify(m => m.GetMembershipTier("PP-9999"), Times.Once);
        _repoStub.Verify(r => r.SaveTicketAsync(ticket), Times.Once);
        Assert.Equal("PP-9999", ticket.Vehicle.LicensePlate);
        Assert.Equal(VehicleType.Car, ticket.Vehicle.Type);
        Assert.Equal(MembershipTier.Guest, ticket.Vehicle.Membership);
        Assert.Equal(new DateTime(2026, 3, 16, 10, 0, 0), ticket.CheckInTime);
    }

    [Fact]
    public async Task CheckInAsync_DuplicateLicensePlate_ThrowsInvalidOperationAndDoesNotSave()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-1234")).ReturnsAsync(new ParkingTicket());
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckInAsync("PP-1234", VehicleType.Car));
        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }

    [Fact]
    public async Task CheckOutAsync_PaymentSuccessful_UpdatesTicketAndSendsReceipt()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle
            {
                LicensePlate = "PP-5678",
                Type = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime = new DateTime(2026, 3, 16, 10, 0, 0)
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(ticket.CheckInTime.AddHours(2));
        _paymentStub.Setup(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>())).ReturnsAsync(true);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        // Act
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert
        _repoStub.Verify(r => r.UpdateTicketAsync(ticket), Times.Once);
        _notificationStub.Verify(n => n.SendReceiptAsync("012-345-678", It.IsAny<string>()), Times.Once);
        Assert.False(ticket.IsActive);
        Assert.Equal(ticket.CheckOutTime, _dateTimeStub.Object.Now);
        Assert.Equal(2_000m, result.TotalFee);
    }

    [Fact]
    public async Task CheckOutAsync_PaymentFailure_ThrowsAndDoesNotUpdateOrNotify()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle
            {
                LicensePlate = "PP-9998",
                Type = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime = new DateTime(2026, 3, 16, 10, 0, 0)
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(ticket.CheckInTime.AddMinutes(31));
        _paymentStub.Setup(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>())).ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _manager.CheckOutAsync(ticket.TicketId, "012-345-678"));
        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
        _notificationStub.Verify(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.True(ticket.IsActive);
    }

    [Fact]
    public async Task CheckOutAsync_NotificationFailure_SucceedsAfterPayment()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle
            {
                LicensePlate = "PP-9997",
                Type = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime = new DateTime(2026, 3, 16, 10, 0, 0)
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(ticket.CheckInTime.AddMinutes(61));
        _paymentStub.Setup(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>())).ReturnsAsync(true);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("SMS failure"));

        // Act
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert
        _repoStub.Verify(r => r.UpdateTicketAsync(ticket), Times.Once);
        Assert.Equal(1_000m, result.TotalFee);
        Assert.False(ticket.IsActive);
    }

    [Fact]
    public async Task CheckOutAsync_TicketNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _repoStub.Setup(r => r.GetTicketByIdAsync(It.IsAny<string>())).ReturnsAsync((ParkingTicket?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _manager.CheckOutAsync("UNKNOWN", "012-345-678"));
    }

    [Fact]
    public async Task CheckOutAsync_AlreadyCheckedOut_ThrowsInvalidOperationException()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle
            {
                LicensePlate = "PP-1000",
                Type = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime = new DateTime(2026, 3, 16, 10, 0, 0),
            CheckOutTime = new DateTime(2026, 3, 16, 12, 0, 0)
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckOutAsync(ticket.TicketId, "012-345-678"));
    }

    [Fact]
    public async Task CheckOutAsync_PaymentThenUpdateThenSendReceipt_OrderIsRespected()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            Vehicle = new Vehicle
            {
                LicensePlate = "PP-8888",
                Type = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime = new DateTime(2026, 3, 16, 10, 0, 0)
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync(ticket.TicketId)).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(ticket.CheckInTime.AddMinutes(61));

        var sequence = new MockSequence();
        _paymentStub.InSequence(sequence)
            .Setup(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>()))
            .ReturnsAsync(true);
        _repoStub.InSequence(sequence)
            .Setup(r => r.UpdateTicketAsync(ticket))
            .Returns(Task.CompletedTask);
        _notificationStub.InSequence(sequence)
            .Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert
        _paymentStub.Verify(p => p.ProcessPaymentAsync(ticket.TicketId, It.IsAny<decimal>()), Times.Once);
        _repoStub.Verify(r => r.UpdateTicketAsync(ticket), Times.Once);
        _notificationStub.Verify(n => n.SendReceiptAsync("012-345-678", It.IsAny<string>()), Times.Once);
    }
}
