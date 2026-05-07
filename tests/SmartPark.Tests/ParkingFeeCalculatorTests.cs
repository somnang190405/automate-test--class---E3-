using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);  // Monday
        var checkOut = checkIn;

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
        Assert.Equal(0m, result.BaseFee);
        Assert.Equal(0m, result.SurchargeAmount);
        Assert.Equal(0m, result.DiscountAmount);
        Assert.Equal(0m, result.LostTicketPenalty);
    }

    #region Basic Fee Calculation

    [Theory]
    [InlineData(VehicleType.Motorcycle, 2, 0, 1_000)]
    [InlineData(VehicleType.Car, 3, 0, 3_000)]
    [InlineData(VehicleType.SUV, 1, 0, 1_500)]
    public void CalculateFee_BasicHourlyRates_ReturnsExpected(VehicleType vehicleType, int hours, int extraMinutes, decimal expectedTotal)
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddHours(hours).AddMinutes(extraMinutes);

        // Act
        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedTotal, result.TotalFee);
    }

    #endregion

    #region Grace Period

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(30)]
    public void CalculateFee_GracePeriod_ThirtyMinutesOrLess_ReturnsFree(int durationMinutes)
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddMinutes(durationMinutes);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_GracePeriod_ThirtyOneMinutes_ReturnsOneHour()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddMinutes(31);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(1_000m, result.TotalFee);
    }

    #endregion

    #region Duration Rounding

    [Theory]
    [InlineData(61, 1)]
    [InlineData(150, 2)]
    [InlineData(90, 1)]
    public void CalculateFee_DurationRounding_AlwaysRoundsUp(int durationMinutes, int expectedHours)
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = checkIn.AddMinutes(durationMinutes);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedHours * 1_000m, result.TotalFee);
    }

    #endregion

    #region Daily Cap

    [Theory]
    [InlineData(VehicleType.Motorcycle, 10, 4_000)]
    [InlineData(VehicleType.Car, 12, 8_000)]
    [InlineData(VehicleType.SUV, 10, 12_000)]
    public void CalculateFee_DailyCap_AppliesMaximumCap(VehicleType vehicleType, int hours, decimal expectedTotal)
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 6, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedTotal, result.TotalFee);
        Assert.Equal(expectedTotal, result.BaseFee);
    }

    #endregion

    #region Overnight Fee

    [Theory]
    [InlineData("2026-03-16T20:00:00", "2026-03-16T23:00:00", 5_000)]
    [InlineData("2026-03-16T23:00:00", "2026-03-17T06:00:00", 9_000)]
    public void CalculateFee_OvernightFee_AppliesFlatFee(string checkInText, string checkOutText, decimal expectedTotal)
    {
        // Arrange
        var checkIn = DateTime.Parse(checkInText);
        var checkOut = DateTime.Parse(checkOutText);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedTotal, result.TotalFee);
        Assert.Equal(2_000m, result.TotalFee - result.BaseFee - result.SurchargeAmount + result.DiscountAmount - result.LostTicketPenalty);
    }

    [Fact]
    public void CalculateFee_OvernightFee_NotAppliedBefore10PM()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 8, 0, 0);
        var checkOut = new DateTime(2026, 3, 16, 17, 0, 0);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(8_000m, result.TotalFee);
        Assert.Equal(0m, result.LostTicketPenalty);
    }

    #endregion

    #region Weekend Surcharge

    [Fact]
    public void CalculateFee_WeekendSurcharge_AppliesTwentyPercent_ForCar()
    {
        var checkIn = new DateTime(2026, 3, 14, 10, 0, 0); // Sunday
        var checkOut = checkIn.AddMinutes(61);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(1_200m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_WeekendSurcharge_AppliesTwentyPercent_ForMotorcycle()
    {
        var checkIn = new DateTime(2026, 3, 15, 10, 0, 0); // Sunday
        var checkOut = checkIn.AddMinutes(61);

        var result = _calculator.CalculateFee(VehicleType.Motorcycle, MembershipTier.Guest, checkIn, checkOut);

        Assert.Equal(600m, result.TotalFee);
    }

    #endregion

    #region Holiday Surcharge

    [Fact]
    public void CalculateFee_HolidaySurcharge_AppliesFiftyPercent()
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var checkOut = checkIn.AddMinutes(61);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        Assert.Equal(1_500m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_HolidayOnWeekend_UsesHolidayOnly()
    {
        var checkIn = new DateTime(2026, 3, 14, 10, 0, 0); // Sunday
        var checkOut = checkIn.AddMinutes(61);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isHoliday: true);

        Assert.Equal(1_500m, result.TotalFee);
    }

    #endregion

    #region Membership Discounts

    [Theory]
    [InlineData(MembershipTier.Silver, 1_800)]
    [InlineData(MembershipTier.Gold, 1_500)]
    [InlineData(MembershipTier.Platinum, 1_200)]
    public void CalculateFee_MembershipDiscounts_AppliesCorrectly(MembershipTier membership, decimal expectedTotal)
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, membership, checkIn, checkOut);

        Assert.Equal(expectedTotal, result.TotalFee);
    }

    #endregion

    #region Lost Ticket

    [Fact]
    public void CalculateFee_LostTicket_AddsPenaltyRegardlessOfDiscount()
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddHours(2);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Platinum, checkIn, checkOut, isLostTicket: true);

        Assert.Equal(1_200m + 20_000m, result.TotalFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    [Fact]
    public void CalculateFee_LostTicketDuringGracePeriod_ReturnsPenaltyOnly()
    {
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(15);

        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut, isLostTicket: true);

        Assert.Equal(20_000m, result.TotalFee);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException()
    {
        var checkIn = new DateTime(2026, 3, 16, 11, 0, 0);
        var checkOut = checkIn.AddHours(-1);

        Assert.Throws<ArgumentException>(() =>
            _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut));
    }

    #endregion

    #region Property-Based Tests

    [Property(Arbitrary = new[] { typeof(ParkingFeeCalculatorTests) })]
    public void CalculateFee_TotalFee_IsNeverNegative(ParkingSessionData session)
    {
        var result = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            session.CheckOut,
            session.IsLostTicket,
            session.IsHoliday);

        Assert.True(result.TotalFee >= 0m);
    }

    [Property(Arbitrary = new[] { typeof(ParkingFeeCalculatorTests) })]
    public void CalculateFee_GracePeriod_IsAlwaysFree(ParkingSessionData session)
    {
        var checkOut = session.CheckIn.AddMinutes(30);

        var result = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            checkOut,
            session.IsLostTicket,
            session.IsHoliday);

        var expected = session.IsLostTicket ? 20_000m : 0m;
        Assert.Equal(expected, result.TotalFee);
    }

    [Property(Arbitrary = new[] { typeof(ParkingFeeCalculatorTests) })]
    public void CalculateFee_LongRuns_CostMoreOrEqual(ParkingSessionData session)
    {
        var shortCheckOut = session.CheckIn.AddMinutes(31);
        var longCheckOut = session.CheckIn.AddHours(2);

        var shortFee = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            shortCheckOut,
            session.IsLostTicket,
            session.IsHoliday).TotalFee;

        var longFee = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            longCheckOut,
            session.IsLostTicket,
            session.IsHoliday).TotalFee;

        Assert.True(longFee >= shortFee);
    }

    [Property(Arbitrary = new[] { typeof(ParkingFeeCalculatorTests) })]
    public void CalculateFee_MemberPaysLessOrEqualThanGuest(ParkingSessionData session)
    {
        var guestFee = _calculator.CalculateFee(
            session.VehicleType,
            MembershipTier.Guest,
            session.CheckIn,
            session.CheckOut,
            session.IsLostTicket,
            session.IsHoliday).TotalFee;

        var memberFee = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            session.CheckOut,
            session.IsLostTicket,
            session.IsHoliday).TotalFee;

        Assert.True(memberFee <= guestFee);
    }

    [Property(Arbitrary = new[] { typeof(ParkingFeeCalculatorTests) })]
    public void CalculateFee_LostTicketAddsExactPenalty(ParkingSessionData session)
    {
        var noLost = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            session.CheckOut,
            isLostTicket: false,
            session.IsHoliday).TotalFee;

        var lost = _calculator.CalculateFee(
            session.VehicleType,
            session.MembershipTier,
            session.CheckIn,
            session.CheckOut,
            isLostTicket: true,
            session.IsHoliday).TotalFee;

        Assert.Equal(20_000m, lost - noLost);
    }

    public static Arbitrary<ParkingSessionData> ValidParkingSessionData()
    {
        var sessionGenerator =
            from dayOffset in Gen.Choose(0, 365 * 2)
            from minuteOfDay in Gen.Choose(0, 24 * 60 - 1)
            from duration in Gen.Choose(31, 48 * 60)
            from vehicleType in Gen.Elements(VehicleType.Motorcycle, VehicleType.Car, VehicleType.SUV)
            from membershipTier in Gen.Elements(MembershipTier.Guest, MembershipTier.Silver, MembershipTier.Gold, MembershipTier.Platinum)
            from isHoliday in Arb.Generate<bool>()
            from isLostTicket in Arb.Generate<bool>()
            let checkIn = new DateTime(2024, 1, 1).AddDays(dayOffset).AddMinutes(minuteOfDay)
            let checkOut = checkIn.AddMinutes(duration)
            select new ParkingSessionData(checkIn, checkOut, vehicleType, membershipTier, isHoliday, isLostTicket);

        return Arb.From(sessionGenerator);
    }

    public sealed record ParkingSessionData(
        DateTime CheckIn,
        DateTime CheckOut,
        VehicleType VehicleType,
        MembershipTier MembershipTier,
        bool IsHoliday,
        bool IsLostTicket);

    #endregion
}
