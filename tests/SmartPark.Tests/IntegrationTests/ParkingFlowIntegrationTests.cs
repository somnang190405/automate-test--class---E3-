using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests.IntegrationTests;

public class ParkingFlowIntegrationTests
{
    // ────────────────────────────────────────────────────────────
    //  INTEGRATION TEST SETUP
    //  Uses REAL components for business logic, and TEST DOUBLES
    //  only for external boundaries:
    //
    //  Real objects:
    //    ParkingFeeCalculator       — real (pure logic, no side effects)
    //    InMemoryParkingRepository  — fake (working in-memory implementation)
    //
    //  Test doubles (via Moq, used as stubs here):
    //    IPaymentGateway            — stub (always returns success)
    //    INotificationService       — stub (does nothing)
    //    IDateTimeProvider          — stub (returns controlled time)
    //    IMembershipService         — stub (returns Guest for all)
    // ────────────────────────────────────────────────────────────

    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly InMemoryParkingRepository _repository = new();  // fake
    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly ParkingSessionManager _manager;

    // Fake clock — set this in each test to control time
    private DateTime _currentTime = new(2026, 3, 16, 10, 0, 0); // Monday 10 AM

    public ParkingFlowIntegrationTests()
    {
        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,          // real fake, not a Moq object
            dateTimeStub.Object);
    }

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows how to advance time between operations.
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_CheckInAndCheckOut_CalculatesCorrectFee()
    {
        // Arrange — check in at 10:00 AM
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var ticket = await _manager.CheckInAsync("TEST-001", VehicleType.Car);

        // Act — check out at 12:30 PM (2.5 hours later → 2 billable hours after grace)
        _currentTime = new DateTime(2026, 3, 16, 12, 30, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert — Car: 2 hours × 1,000 = 2,000 KHR
        Assert.Equal(2_000m, result.TotalFee);
    }

    #region Full Parking Flow

    [Fact]
    public async Task FullFlow_MultipleVehicles_CheckOutOneLeavesOthersActive()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket1 = await _manager.CheckInAsync("CAR-001", VehicleType.Car);
        var ticket2 = await _manager.CheckInAsync("CAR-002", VehicleType.Car);
        var ticket3 = await _manager.CheckInAsync("MOTO-001", VehicleType.Motorcycle);

        _currentTime = _currentTime.AddHours(2);
        await _manager.CheckOutAsync(ticket1.TicketId, "012-345-678");

        var activeTickets = await _repository.GetAllActiveTicketsAsync();
        Assert.Equal(2, activeTickets.Count());
        Assert.Contains(activeTickets, t => t.Vehicle.LicensePlate == "CAR-002");
        Assert.Contains(activeTickets, t => t.Vehicle.LicensePlate == "MOTO-001");
    }

    [Fact]
    public async Task FullFlow_PaymentFailure_LeavesTicketActive()
    {
        var paymentFailure = new Mock<IPaymentGateway>();
        paymentFailure.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(false);

        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        var manager = new ParkingSessionManager(
            _feeCalculator,
            paymentFailure.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,
            dateTimeStub.Object);

        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await manager.CheckInAsync("FAIL-001", VehicleType.Car);

        _currentTime = _currentTime.AddMinutes(61);
        await Assert.ThrowsAsync<Exception>(() => manager.CheckOutAsync(ticket.TicketId, "012-345-678"));

        var storedTicket = await _repository.GetTicketByIdAsync(ticket.TicketId);
        Assert.NotNull(storedTicket);
        Assert.True(storedTicket!.IsActive);
    }

    [Fact]
    public async Task FullFlow_GracePeriodLostTicket_ReturnsPenaltyOnly()
    {
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("GRACE-001", VehicleType.Car);

        _currentTime = _currentTime.AddMinutes(15);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678", isLostTicket: true);

        Assert.Equal(20_000m, result.TotalFee);
    }

    [Fact]
    public async Task FullFlow_GoldMemberWeekendOvernight_CalculatesDiscountAndOvernight()
    {
        // Arrange a custom membership response for this plate
        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier("GOLD-001")).Returns(MembershipTier.Gold);
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var customManager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,
            dateTimeStub.Object);

        _currentTime = new DateTime(2026, 3, 14, 21, 0, 0); // Sunday 9 PM
        var ticket = await customManager.CheckInAsync("GOLD-001", VehicleType.Car);

        _currentTime = _currentTime.AddHours(3); // 12 AM Monday
        var result = await customManager.CheckOutAsync(ticket.TicketId, "012-345-678");

        Assert.True(result.TotalFee > 0);
        Assert.Contains("Overnight", result.Breakdown);
    }

    #endregion

    #region Multiple Vehicles
    // Test concurrent parking sessions and their lifecycle
    #endregion

    #region Error Recovery
    // Test system state consistency after error conditions
    #endregion

    #region Edge-to-Edge Scenarios
    // Test complex combinations of fee modifiers working together
    #endregion
}
