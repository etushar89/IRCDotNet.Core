using IRCDotNet.Core.Protocol;
using Xunit;

namespace IRCDotNet.Tests.Protocol;

public class IrcCaseMappingTests
{
    [Fact]
    public void ToLowerAscii_MixedCase_ConvertsToLowercase()
    {
        // Arrange
        var input = "TestUser123";

        // Act
        var result = IrcCaseMapping.ToLowerAscii(input);

        // Assert
        Assert.Equal("testuser123", result);
    }

    [Fact]
    public void ToLowerRfc1459_WithSpecialChars_ConvertsCorrectly()
    {
        // Arrange
        var input = "Test[User]\\^";

        // Act
        var result = IrcCaseMapping.ToLowerRfc1459(input);

        // Assert
        Assert.Equal("test{user}|~", result);
    }

    [Fact]
    public void ToLowerRfc1459Strict_WithSpecialChars_ConvertsCorrectly()
    {
        // Arrange
        var input = "Test[User]\\";

        // Act
        var result = IrcCaseMapping.ToLowerRfc1459Strict(input);

        // Assert
        Assert.Equal("test{user}|", result);
    }

    [Fact]
    public void ToLower_WithDifferentMappings_ReturnsCorrectResults()
    {
        // Arrange
        var input = "Test[User]^";

        // Act
        var ascii = IrcCaseMapping.ToLower(input, CaseMappingType.Ascii);
        var rfc1459 = IrcCaseMapping.ToLower(input, CaseMappingType.Rfc1459);
        var rfc1459Strict = IrcCaseMapping.ToLower(input, CaseMappingType.Rfc1459Strict);

        // Assert
        Assert.Equal("test[user]^", ascii);
        Assert.Equal("test{user}~", rfc1459);
        Assert.Equal("test{user}^", rfc1459Strict);
    }

    [Fact]
    public void Equals_SameStringsRfc1459_ReturnsTrue()
    {
        // Arrange
        var a = "Test[User]";
        var b = "test{user}";

        // Act
        var result = IrcCaseMapping.Equals(a, b, CaseMappingType.Rfc1459);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_DifferentStrings_ReturnsFalse()
    {
        // Arrange
        var a = "TestUser";
        var b = "DifferentUser";

        // Act
        var result = IrcCaseMapping.Equals(a, b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_SameStringsRfc1459_ReturnsSameHash()
    {
        // Arrange
        var a = "Test[User]";
        var b = "test{user}";

        // Act
        var hashA = IrcCaseMapping.GetHashCode(a, CaseMappingType.Rfc1459);
        var hashB = IrcCaseMapping.GetHashCode(b, CaseMappingType.Rfc1459);

        // Assert
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void CreateComparer_WithRfc1459_ComparesCorrectly()
    {
        // Arrange
        var comparer = IrcCaseMapping.CreateComparer(CaseMappingType.Rfc1459);
        var a = "Test[User]";
        var b = "test{user}";

        // Act
        var areEqual = comparer.Equals(a, b);
        var hashA = comparer.GetHashCode(a);
        var hashB = comparer.GetHashCode(b);

        // Assert
        Assert.True(areEqual);
        Assert.Equal(hashA, hashB);
    }
}

public class IrcCaseDictionaryTests
{
    [Fact]
    public void IrcCaseDictionary_WithRfc1459_HandlesSpecialChars()
    {
        // Arrange
        var dict = new IrcCaseDictionary<string>();
        dict["Test[User]"] = "value1";

        // Act
        var result = dict.TryGetValue("test{user}", out var value);

        // Assert
        Assert.True(result);
        Assert.Equal("value1", value);
    }

    [Fact]
    public void IrcCaseDictionary_ContainsKey_WorksWithCaseMapping()
    {
        // Arrange
        var dict = new IrcCaseDictionary<int>();
        dict["TestChannel"] = 42;

        // Act
        var contains = dict.ContainsKey("testchannel");

        // Assert
        Assert.True(contains);
    }
}

public class IrcCaseHashSetTests
{
    [Fact]
    public void IrcCaseHashSet_Contains_WorksWithCaseMapping()
    {
        // Arrange
        var set = new IrcCaseHashSet();
        set.Add("Test[User]");

        // Act
        var contains = set.Contains("test{user}");

        // Assert
        Assert.True(contains);
    }

    [Fact]
    public void IrcCaseHashSet_Add_PreventsDuplicates()
    {
        // Arrange
        var set = new IrcCaseHashSet();
        set.Add("TestUser");

        // Act
        var added = set.Add("testuser");

        // Assert
        Assert.False(added);
        Assert.Single(set);
    }
}
