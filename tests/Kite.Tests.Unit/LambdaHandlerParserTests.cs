using Kite.Lambda.Execution;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class LambdaHandlerParserTests
{
    [Fact]
    public void ParseHandler_ValidFormat_ReturnsParts()
    {
        var (typeName, methodName) = LambdaExecutor.ParseHandler("OrderService::OrderService.Functions::HandleOrder");
        typeName.Should().Be("OrderService.Functions");
        methodName.Should().Be("HandleOrder");
    }

    [Fact]
    public void ParseHandler_InvalidFormat_ThrowsException()
    {
        var act = () => LambdaExecutor.ParseHandler("invalid-handler");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid handler format*");
    }

    [Fact]
    public void ParseHandler_TwoParts_ThrowsException()
    {
        var act = () => LambdaExecutor.ParseHandler("Assembly::TypeName");
        act.Should().Throw<InvalidOperationException>();
    }
}
