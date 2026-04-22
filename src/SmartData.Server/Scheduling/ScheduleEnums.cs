namespace SmartData.Server.Scheduling;

/// <summary>Day-of-week filter bitmask. <c>Sun = 1</c>.</summary>
[Flags]
public enum Days
{
    None = 0,
    Sun = 1, Mon = 2, Tue = 4, Wed = 8, Thu = 16, Fri = 32, Sat = 64,
    Weekdays = Mon | Tue | Wed | Thu | Fri,
    Weekends = Sat | Sun,
    All = 127,
}

/// <summary>Month filter bitmask.</summary>
[Flags]
public enum Months
{
    None = 0,
    Jan = 1 << 0,  Feb = 1 << 1,  Mar = 1 << 2,  Apr = 1 << 3,
    May = 1 << 4,  Jun = 1 << 5,  Jul = 1 << 6,  Aug = 1 << 7,
    Sep = 1 << 8,  Oct = 1 << 9,  Nov = 1 << 10, Dec = 1 << 11,
    Quarters = Jan | Apr | Jul | Oct,
    All = 4095,
}

/// <summary>Week-of-month selection for <c>[MonthlyDow]</c>.</summary>
[Flags]
public enum Weeks
{
    None = 0,
    First = 1, Second = 2, Third = 4, Fourth = 8, Last = 16,
    All = 31,
}

/// <summary>Day-of-month selection for <c>[Monthly]</c>. <c>Last</c> is a sentinel for end-of-month.</summary>
[Flags]
public enum Day
{
    None = 0,
    D1  = 1 << 0,  D2  = 1 << 1,  D3  = 1 << 2,  D4  = 1 << 3,
    D5  = 1 << 4,  D6  = 1 << 5,  D7  = 1 << 6,  D8  = 1 << 7,
    D9  = 1 << 8,  D10 = 1 << 9,  D11 = 1 << 10, D12 = 1 << 11,
    D13 = 1 << 12, D14 = 1 << 13, D15 = 1 << 14, D16 = 1 << 15,
    D17 = 1 << 16, D18 = 1 << 17, D19 = 1 << 18, D20 = 1 << 19,
    D21 = 1 << 20, D22 = 1 << 21, D23 = 1 << 22, D24 = 1 << 23,
    D25 = 1 << 24, D26 = 1 << 25, D27 = 1 << 26, D28 = 1 << 27,
    D29 = 1 << 28, D30 = 1 << 29, D31 = 1 << 30,
    Last = 1 << 31,
}

/// <summary>Interval unit for <c>[Every]</c>.</summary>
public enum Unit { Seconds, Minutes, Hours, Days }
