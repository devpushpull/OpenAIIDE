using AIIDEWPF.Services;

namespace AIIDEWPF.Tests;

[TestClass]
public class TokenCounterServiceTests
{
    [TestMethod]
    public void CountTokens_EmptyString_ShouldReturnZero()
    {
        var result = TokenCounterService.CountTokens("");
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void CountTokens_SimpleEnglish_ShouldEstimateCorrectly()
    {
        // English words: ~1.3 tokens per word average
        var text = "Hello world this is a test";
        var result = TokenCounterService.CountTokens(text);
        Assert.IsTrue(result > 0, "Should count at least some tokens");
        Assert.IsTrue(result < 20, "Should be reasonable for short text");
    }

    [TestMethod]
    public void CountTokens_ChineseText_ShouldEstimateCorrectly()
    {
        var text = "你好世界这是一个测试";
        var result = TokenCounterService.CountTokens(text);
        Assert.IsTrue(result > 0, "Should count at least some tokens");
    }

    [TestMethod]
    public void CountTokens_CodeText_ShouldEstimateCorrectly()
    {
        var text = "public class Test { public void Method() { } }";
        var result = TokenCounterService.CountTokens(text);
        Assert.IsTrue(result > 0, "Should count at least some tokens");
        Assert.IsTrue(result <= 50, "Should be reasonable for single class");
    }
}
