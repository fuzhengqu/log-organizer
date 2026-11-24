using LogOrganizer.Core;

namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class TicketNumberTests
{
    [TestMethod]
    [DataRow("0")]
    [DataRow("123456")]
    public void TryParse_AcceptsAsciiDigits(string input)
    {
        Assert.IsTrue(TicketNumber.TryParse(input, out var ticketNumber));
        Assert.AreEqual(input, ticketNumber);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("12a")]
    [DataRow("12-3")]
    [DataRow("１２３")]
    [DataRow("١٢٣")]
    public void TryParse_RejectsEmptyOrNonAsciiDigits(string? input)
    {
        Assert.IsFalse(TicketNumber.TryParse(input, out _));
    }
}
