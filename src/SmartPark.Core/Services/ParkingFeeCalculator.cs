using SmartPark.Core.Models;

namespace SmartPark.Core.Services;

/// <summary>
/// Core pricing engine. Pure calculation service with no external dependencies.
/// Students: implement this class using TDD (Red-Green-Refactor).
/// </summary>
public class ParkingFeeCalculator
{
    // ── Pricing constants (from spec §4) ────────────────────────

    // Hourly rates (KHR)
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour = 1_000m;
    private const decimal SuvRatePerHour = 1_500m;

    // Daily caps (KHR)
    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap = 8_000m;
    private const decimal SuvDailyCap = 12_000m;

    // Time-based rules
    private const int GracePeriodMinutes = 30;
    private const decimal OvernightFlatFee = 2_000m;
    private const int OvernightHourThreshold = 22; // 10 PM

    // Surcharges
    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    // Membership discounts
    private const decimal SilverDiscountRate = 0.10m;
    private const decimal GoldDiscountRate = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    // Penalties
    private const decimal LostTicketPenalty = 20_000m;

    /// <summary>
    /// Calculates the parking fee following the 9-step flow in the spec.
    /// </summary>
    /// <remarks>
    /// Steps:
    ///   1. Validate: checkOut before checkIn → ArgumentException
    ///   2. Grace period: total ≤ 30 min → free (lost-ticket penalty still applies)
    ///   3. Duration: billableHours = ⌈(totalMinutes − 30) / 60⌉, min 1
    ///   4. Base fee: billableHours × hourlyRate, capped at dailyCap
    ///   5. Overnight: +2,000 KHR if session spans past 22:00
    ///   6. Surcharge: weekend +20% OR holiday +50% on baseFee (not both)
    ///   7. Discount: (baseFee + surcharge) × membershipRate
    ///   8. Lost ticket: +20,000 KHR (not subject to discounts)
    ///   9. Total: baseFee + surcharge − discount + overnight + penalty (min 0)
    /// </remarks>
    public ParkingFeeResult CalculateFee(
        VehicleType vehicleType,
        MembershipTier membership,
        DateTime checkIn,
        DateTime checkOut,
        bool isLostTicket = false,
        bool isHoliday = false)
    {
        if (checkOut < checkIn)
            throw new ArgumentException("checkOut must be after checkIn.", nameof(checkOut));

        var totalMinutes = (checkOut - checkIn).TotalMinutes;
        if (totalMinutes <= GracePeriodMinutes)
        {
            var penalty = isLostTicket ? LostTicketPenalty : 0m;
            return new ParkingFeeResult
            {
                BaseFee = 0m,
                SurchargeAmount = 0m,
                DiscountAmount = 0m,
                LostTicketPenalty = penalty,
                TotalFee = penalty,
                Breakdown = BuildBreakdown(0m, 0m, 0m, penalty, 0m)
            };
        }

        var billableHours = CalculateBillableHours(totalMinutes);
        var hourlyRate = GetHourlyRate(vehicleType);
        var dailyCap = GetDailyCap(vehicleType);
        var baseFee = Math.Min(billableHours * hourlyRate, dailyCap);
        var surchargeAmount = CalculateSurcharge(baseFee, isHoliday, checkIn.DayOfWeek);
        var discountAmount = CalculateDiscount(membership, baseFee + surchargeAmount);
        var overnightFee = CalculateOvernightFee(checkIn, checkOut);
        var lostTicketPenalty = isLostTicket ? LostTicketPenalty : 0m;
        var totalFee = Math.Max(baseFee + surchargeAmount - discountAmount + overnightFee + lostTicketPenalty, 0m);

        return new ParkingFeeResult
        {
            BaseFee = baseFee,
            SurchargeAmount = surchargeAmount,
            DiscountAmount = discountAmount,
            LostTicketPenalty = lostTicketPenalty,
            TotalFee = totalFee,
            Breakdown = BuildBreakdown(baseFee, surchargeAmount, discountAmount, lostTicketPenalty, overnightFee)
        };
    }

    private static int CalculateBillableHours(double totalMinutes)
    {
        var billableMinutes = totalMinutes - GracePeriodMinutes;
        return Math.Max(1, (int)Math.Ceiling(billableMinutes / 60.0));
    }

    private static decimal GetHourlyRate(VehicleType vehicleType) => vehicleType switch
    {
        VehicleType.Motorcycle => MotorcycleRatePerHour,
        VehicleType.Car => CarRatePerHour,
        VehicleType.SUV => SuvRatePerHour,
        _ => throw new ArgumentOutOfRangeException(nameof(vehicleType), vehicleType, "Unsupported vehicle type.")
    };

    private static decimal GetDailyCap(VehicleType vehicleType) => vehicleType switch
    {
        VehicleType.Motorcycle => MotorcycleDailyCap,
        VehicleType.Car => CarDailyCap,
        VehicleType.SUV => SuvDailyCap,
        _ => throw new ArgumentOutOfRangeException(nameof(vehicleType), vehicleType, "Unsupported vehicle type.")
    };

    private static decimal CalculateSurcharge(decimal baseFee, bool isHoliday, DayOfWeek dayOfWeek)
    {
        if (isHoliday)
            return baseFee * HolidaySurchargeRate;

        return dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? baseFee * WeekendSurchargeRate
            : 0m;
    }

    private static decimal CalculateDiscount(MembershipTier membership, decimal subtotal)
    {
        var rate = membership switch
        {
            MembershipTier.Silver => SilverDiscountRate,
            MembershipTier.Gold => GoldDiscountRate,
            MembershipTier.Platinum => PlatinumDiscountRate,
            _ => 0m
        };

        return subtotal * rate;
    }

    private static decimal CalculateOvernightFee(DateTime checkIn, DateTime checkOut)
    {
        var cutoff = new DateTime(checkIn.Year, checkIn.Month, checkIn.Day, OvernightHourThreshold, 0, 0);
        return checkOut > cutoff ? OvernightFlatFee : 0m;
    }

    private static string BuildBreakdown(
        decimal baseFee,
        decimal surchargeAmount,
        decimal discountAmount,
        decimal lostTicketPenalty,
        decimal overnightFee)
    {
        return $"Base={baseFee}; Surcharge={surchargeAmount}; Discount={discountAmount}; Overnight={overnightFee}; LostPenalty={lostTicketPenalty}";
    }
}
