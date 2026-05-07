# SmartPark TDD Assignment Report

## Student Information
- Name: [Your Full Name]
- Class: [Your Class, e.g., E3]
- Date: May 7, 2026

## Test Scenario Matrix
| ID  | Scenario | Input (Vehicle, Time, etc.) | Expected Output (KHR) | Test Method Name |
|-----|----------|-----------------------------|-----------------------|------------------|
| TC01 | Grace Period | Car, 30 min duration | 0 KHR | CalculateFee_GracePeriod_ThirtyMinutesOrLess_ReturnsFree |
| TC02 | Basic Motorcycle | Motorcycle, 1 hour | 500 KHR | CalculateFee_BasicHourlyRates_ReturnsExpected |
| TC03 | Duration Rounding | Car, 1h 1min | 2,000 KHR (2 hrs billed) | CalculateFee_DurationRounding_AlwaysRoundsUp |
| TC04 | Car Daily Cap | Car, 12 hours | 8,000 KHR | CalculateFee_DailyCap_AppliesMaximumCap |
| TC05 | Weekend Surcharge | Car, Sat, 2 hours | 2,400 KHR (2000 + 20%) | CalculateFee_WeekendSurcharge_AppliesTwentyPercent_ForCar |
| TC06 | Holiday Priority | Car, Holiday + Sat, 2h | 3,000 KHR (50% only) | CalculateFee_HolidayOnWeekend_UsesHolidayOnly |
| TC07 | Gold Membership | Car, 2h, Gold Member | 1,500 KHR (2000 - 25%) | CalculateFee_MembershipDiscounts_AppliesCorrectly |
| TC08 | Lost Ticket | Car, 1h, Lost Ticket | 21,000 KHR (1000 + 20k) | CalculateFee_LostTicket_AddsPenaltyRegardlessOfDiscount |
| TC09 | Overnight Fee | Car, 8 PM - 11 PM | Base + 2,000 KHR | CalculateFee_OvernightFee_AppliesFlatFee |
| TC10 | Invalid Dates | Check-out < Check-in | ArgumentException | CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException |

## Property-Based Test Properties
1. **Non-Negativity**: The total fee must never be less than 0.
2. **Grace Period Integrity**: Any stay ≤30 minutes must result in a fee of 0 (unless ticket is lost).
3. **Daily Cap Boundary**: The base fee for any vehicle must never exceed its specific DailyCap.
4. **Monotonicity**: If a stay duration increases, the fee must be greater than or equal to the shorter stay.
5. **Membership Advantage**: A Platinum member must always pay less than or equal to a Guest for the same session.

## Test Execution Results
[Paste screenshots of your test runs here, showing green checkmarks for all tests.]

## Traceability Matrix
| Business Rule | Test Method Name | Status |
|---------------|------------------|--------|
| Validation: Throw ArgumentException if checkOut < checkIn | CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException | Pass |
| Grace Period: ≤30 mins = 0 KHR | CalculateFee_GracePeriod_ThirtyMinutesOrLess_ReturnsFree | Pass |
| Rounding: Billable hours = Math.Ceiling((totalMinutes - 30) / 60.0) | CalculateFee_DurationRounding_AlwaysRoundsUp | Pass |
| Daily Cap: Motorcycle 4k, Car 8k, SUV 12k | CalculateFee_DailyCap_AppliesMaximumCap | Pass |
| Surcharges: Weekend 20% or Holiday 50% (Holiday priority) | CalculateFee_HolidayOnWeekend_UsesHolidayOnly | Pass |
| Discounts: Silver 10%, Gold 25%, Platinum 40% on (Base + Surcharge) | CalculateFee_MembershipDiscounts_AppliesCorrectly | Pass |
| Lost Ticket: +20,000 KHR (not discounted) | CalculateFee_LostTicket_AddsPenaltyRegardlessOfDiscount | Pass |

## Git Commit History
[List your commits with RED/GREEN/REFACTOR tags, e.g., "[RED] Add test for motorcycle rate"]

## Reflections
[Write your reflections on the TDD process, challenges faced, and what you learned.]