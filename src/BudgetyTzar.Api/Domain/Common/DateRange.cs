namespace BudgetyTzar.Api;

public readonly record struct DateRange
{
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    public DateRange(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
        }

        StartDate = startDate;
        EndDate = endDate;
    }

    public bool Contains(DateOnly date) => StartDate <= date && date <= EndDate;

    public bool Overlaps(DateRange other) => StartDate <= other.EndDate && other.StartDate <= EndDate;
}
