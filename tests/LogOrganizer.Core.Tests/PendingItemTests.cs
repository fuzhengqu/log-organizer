namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class PendingItemTests
{
    [TestMethod]
    public void RetainsPathNameAndOptionalError()
    {
        var item = new PendingItem(@"D:\Downloads\_收件\capture.png", "capture.png", "目标被占用");

        Assert.AreEqual(@"D:\Downloads\_收件\capture.png", item.SourcePath);
        Assert.AreEqual("capture.png", item.DisplayName);
        Assert.AreEqual("目标被占用", item.Error);
    }

    [TestMethod]
    public void ErrorIsOptional()
    {
        var item = new PendingItem(@"D:\Downloads\_收件\capture.png", "capture.png");

        Assert.IsNull(item.Error);
    }
}