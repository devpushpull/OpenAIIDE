using AIIDEWPF.Services;

namespace AIIDEWPF.Tests;

[TestClass]
public class DiffServiceTests
{
    [TestMethod]
    public void ComputeDiff_IdenticalText_ShouldReturnNoChanges()
    {
        var text = "line1\nline2\nline3";
        var result = DiffService.ComputeDiff(text, text);
        Assert.AreEqual(0, result.AddedLines);
        Assert.AreEqual(0, result.RemovedLines);
        Assert.IsFalse(result.HasChanges);
    }

    [TestMethod]
    public void ComputeDiff_AddedLine_ShouldDetect()
    {
        var oldText = "line1\nline2";
        var newText = "line1\nline2\nline3";
        var result = DiffService.ComputeDiff(oldText, newText);
        Assert.AreEqual(1, result.AddedLines);
        Assert.AreEqual(0, result.RemovedLines);
        Assert.IsTrue(result.HasChanges);
    }

    [TestMethod]
    public void ComputeDiff_RemovedLine_ShouldDetect()
    {
        var oldText = "line1\nline2\nline3";
        var newText = "line1\nline3";
        var result = DiffService.ComputeDiff(oldText, newText);
        Assert.AreEqual(0, result.AddedLines);
        Assert.AreEqual(1, result.RemovedLines);
        Assert.IsTrue(result.HasChanges);
    }

    [TestMethod]
    public void ComputeDiff_ModifiedLine_ShouldDetect()
    {
        var oldText = "line1\nold line\nline3";
        var newText = "line1\nnew line\nline3";
        var result = DiffService.ComputeDiff(oldText, newText);
        Assert.IsTrue(result.HasChanges);
        Assert.AreEqual(1, result.AddedLines);
        Assert.AreEqual(1, result.RemovedLines);
    }

    [TestMethod]
    public void ComputeDiff_EmptyTexts_ShouldHandle()
    {
        var result = DiffService.ComputeDiff("", "");
        Assert.AreEqual(0, result.AddedLines);
        Assert.AreEqual(0, result.RemovedLines);
        Assert.IsFalse(result.HasChanges);
    }
}
