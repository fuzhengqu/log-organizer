namespace LogOrganizer.Core;

public static class TicketNumber
{
    public static bool TryParse(string? value, out string ticketNumber)
    {
        ticketNumber = value ?? string.Empty;
        return value is { Length: > 0 } && value.All(static character => character is >= '0' and <= '9');
    }
}
