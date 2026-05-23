using AIIDEWPF.Services;

namespace AIIDEWPF.Tests;

[TestClass]
public class ConfigServiceTests
{
    [TestMethod]
    public void EncryptDecrypt_RoundTrip_ShouldReturnOriginal()
    {
        // Arrange
        var original = "sk-test-key-12345";

        // Act
        var encrypted = SecureConfigHelper.Encrypt(original);
        var decrypted = SecureConfigHelper.Decrypt(encrypted);

        // Assert
        Assert.AreEqual(original, decrypted);
        Assert.IsTrue(encrypted.StartsWith("DPAPI:"));
    }

    [TestMethod]
    public void Decrypt_Plaintext_ShouldReturnAsIs()
    {
        var plaintext = "not-encrypted-key";
        var result = SecureConfigHelper.Decrypt(plaintext);
        Assert.AreEqual(plaintext, result);
    }

    [TestMethod]
    public void Encrypt_EmptyString_ShouldReturnEmpty()
    {
        var result = SecureConfigHelper.Encrypt("");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void Encrypt_AlreadyEncrypted_ShouldNotDoubleEncrypt()
    {
        var original = "sk-test-key-67890";
        var encrypted = SecureConfigHelper.Encrypt(original);
        var encryptedAgain = SecureConfigHelper.Encrypt(encrypted); // should be no-op
        Assert.AreEqual(encrypted, encryptedAgain);
    }
}
