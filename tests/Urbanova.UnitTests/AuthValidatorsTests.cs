using FluentAssertions;
using Urbanova.Application.Auth;

namespace Urbanova.UnitTests;

/// <summary>Phase 3: first validation level (API input). Business + DB rules arrive in later phases.</summary>
public sealed class AuthValidatorsTests
{
    [Fact]
    public void Register_Valid_Passes()
    {
        var r = new RegisterRequestValidator().Validate(new RegisterRequest("user@example.com", "Str0ng!Pass1", "Ada"));
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-an-email", "Str0ng!Pass1")]
    [InlineData("user@example.com", "short")]
    [InlineData("", "Str0ng!Pass1")]
    [InlineData("user@example.com", "")]
    public void Register_Invalid_Fails(string email, string password)
    {
        var r = new RegisterRequestValidator().Validate(new RegisterRequest(email, password, null));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Login_Valid_Passes()
    {
        new LoginRequestValidator().Validate(new LoginRequest("user@example.com", "whatever1!")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Login_BlankPassword_Fails()
    {
        new LoginRequestValidator().Validate(new LoginRequest("user@example.com", "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Refresh_Empty_Fails()
    {
        new RefreshRequestValidator().Validate(new RefreshRequest("")).IsValid.Should().BeFalse();
    }
}
