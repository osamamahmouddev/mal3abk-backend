using FluentAssertions;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.UnitTests;

public sealed class ResultTests
{
    [Fact]
    public void Success_ReturnsSuccessResult()
    {
        // Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void SuccessWithValue_ReturnsSuccessResultContainingValue()
    {
        // Act
        var result = Result.Success(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_ReturnsFailureResultContainingError()
    {
        // Arrange
        var error = Error.Validation("Test.Code", "Validation error occurred.");

        // Act
        var result = Result.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void FailureWithValue_ThrowsInvalidOperationException_WhenValueAccessed()
    {
        // Arrange
        var error = Error.NotFound("User.NotFound", "User does not exist.");
        var result = Result.Failure<string>(error);

        // Act
        var act = () => _ = result.Value;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be accessed*");
    }

    [Fact]
    public void ImplicitOperator_FromNonNull_ReturnsSuccess()
    {
        // Act
        Result<string> result = "test-value";

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test-value");
    }

    [Fact]
    public void ImplicitOperator_FromNull_ReturnsFailureWithNullValue()
    {
        // Act
        string? nullString = null;
        Result<string> result = nullString;

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.NullValue);
    }

    [Fact]
    public void Match_WhenSuccess_ExecutesOnSuccess()
    {
        // Arrange
        var result = Result.Success("hello");

        // Act
        var outcome = result.Match(
            onSuccess: val => val.ToUpperInvariant(),
            onFailure: _ => "error");

        // Assert
        outcome.Should().Be("HELLO");
    }

    [Fact]
    public void Match_WhenFailure_ExecutesOnFailure()
    {
        // Arrange
        var result = Result.Failure<string>(Error.Forbidden("Access.Denied", "Denied."));

        // Act
        var outcome = result.Match(
            onSuccess: val => val.ToUpperInvariant(),
            onFailure: err => err.Code);

        // Assert
        outcome.Should().Be("Access.Denied");
    }

    [Fact]
    public void Map_WhenSuccess_TransformsValue()
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(20);
    }

    [Fact]
    public void Map_WhenFailure_PropagatesError()
    {
        // Arrange
        var error = Error.Conflict("Resource.Conflict", "Conflict occurred.");
        var result = Result.Failure<int>(error);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    [Fact]
    public void Bind_WhenSuccess_ReturnsNextResult()
    {
        // Arrange
        var result = Result.Success("42");

        // Act
        var bound = result.Bind(str => int.TryParse(str, out var num)
            ? Result.Success(num)
            : Result.Failure<int>(Error.Validation("Parse.Error", "Cannot parse integer.")));

        // Assert
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be(42);
    }

    [Fact]
    public void Bind_WhenFirstFails_PropagatesOriginalError()
    {
        // Arrange
        var originalError = Error.Unauthorized("Auth.Failed", "Unauthorized access.");
        var result = Result.Failure<string>(originalError);

        // Act
        var bound = result.Bind(str => Result.Success(str.Length));

        // Assert
        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(originalError);
    }
}
