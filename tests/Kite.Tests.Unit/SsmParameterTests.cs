using Kite.Core;
using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class SsmParameterTests
{
    private readonly InMemoryServiceRegistry _registry = new();

    [Fact]
    public void RegisterAndGetSsmParameter_ShouldWork()
    {
        var param = new SsmParameter { Name = "/app/config/key", Value = "value", Type = "String" };
        _registry.RegisterSsmParameter(param);
        _registry.GetSsmParameter("/app/config/key").Should().Be(param);
    }

    [Fact]
    public void GetSsmParameter_NonExistent_ReturnsNull()
    {
        _registry.GetSsmParameter("/non/existent").Should().BeNull();
    }

    [Fact]
    public void DeleteSsmParameter_ShouldRemoveParameter()
    {
        var param = new SsmParameter { Name = "/app/config/key", Value = "value", Type = "String" };
        _registry.RegisterSsmParameter(param);
        _registry.DeleteSsmParameter("/app/config/key");
        _registry.GetSsmParameter("/app/config/key").Should().BeNull();
    }

    [Fact]
    public void GetAllSsmParameters_ShouldReturnAll()
    {
        _registry.RegisterSsmParameter(new SsmParameter { Name = "/app/a", Value = "1", Type = "String" });
        _registry.RegisterSsmParameter(new SsmParameter { Name = "/app/b", Value = "2", Type = "String" });
        _registry.GetAllSsmParameters().Should().HaveCount(2);
    }

    [Fact]
    public void Builder_AddSsmParameter_RegistersParameter()
    {
        var emulator = KiteBuilder.Create()
            .AddSsmParameter("/app/config/db", "localhost", "String")
            .Build();

        var param = emulator.Registry.GetSsmParameter("/app/config/db");
        param.Should().NotBeNull();
        param!.Name.Should().Be("/app/config/db");
        param.Value.Should().Be("localhost");
        param.Type.Should().Be("String");
    }

    [Fact]
    public void Builder_AddSsmParameter_DefaultTypeIsString()
    {
        var emulator = KiteBuilder.Create()
            .AddSsmParameter("/app/config/key", "value")
            .Build();

        emulator.Registry.GetSsmParameter("/app/config/key")!.Type.Should().Be("String");
    }

    [Fact]
    public void Builder_LoadSsmParametersFromJson_LoadsParameters()
    {
        var json = """
            {
              "parameters": [
                { "Name": "/myapp/db", "Value": "localhost", "Type": "String" },
                { "Name": "/myapp/secret", "Value": "s3cr3t", "Type": "SecureString" }
              ]
            }
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var emulator = KiteBuilder.Create()
                .LoadSsmParametersFromJson(tempFile)
                .Build();

            emulator.Registry.GetSsmParameter("/myapp/db").Should().NotBeNull();
            emulator.Registry.GetSsmParameter("/myapp/db")!.Value.Should().Be("localhost");
            emulator.Registry.GetSsmParameter("/myapp/secret")!.Type.Should().Be("SecureString");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Builder_LoadSsmParametersFromJson_EmptyParameters_DoesNotFail()
    {
        var json = """{ "parameters": [] }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var emulator = KiteBuilder.Create()
                .LoadSsmParametersFromJson(tempFile)
                .Build();

            emulator.Registry.GetAllSsmParameters().Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Builder_LoadSsmParametersFromJson_NullName_ThrowsInvalidOperation()
    {
        var json = """
            {
              "parameters": [
                { "Name": null, "Value": "somevalue", "Type": "String" }
              ]
            }
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var act = () => KiteBuilder.Create()
                .LoadSsmParametersFromJson(tempFile)
                .Build();

            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*null or empty Name*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Builder_LoadSsmParametersFromJson_EmptyName_ThrowsInvalidOperation()
    {
        var json = """
            {
              "parameters": [
                { "Name": "", "Value": "somevalue", "Type": "String" }
              ]
            }
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            var act = () => KiteBuilder.Create()
                .LoadSsmParametersFromJson(tempFile)
                .Build();

            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*null or empty Name*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SsmParameter_DefaultValues_AreCorrect()
    {
        var param = new SsmParameter();
        param.Name.Should().BeEmpty();
        param.Value.Should().BeEmpty();
        param.Type.Should().Be("String");
        param.Version.Should().Be(1);
    }

    [Fact]
    public void SsmParameter_OverwriteExisting_ReplacesValue()
    {
        _registry.RegisterSsmParameter(new SsmParameter { Name = "/app/key", Value = "old", Type = "String" });
        _registry.RegisterSsmParameter(new SsmParameter { Name = "/app/key", Value = "new", Type = "String" });

        var param = _registry.GetSsmParameter("/app/key");
        param!.Value.Should().Be("new");
        _registry.GetAllSsmParameters().Should().HaveCount(1);
    }

    [Fact]
    public void Builder_AddSsmParameter_SecureStringType()
    {
        var emulator = KiteBuilder.Create()
            .AddSsmParameter("/app/secret", "s3cr3t", "SecureString")
            .Build();

        var param = emulator.Registry.GetSsmParameter("/app/secret");
        param!.Type.Should().Be("SecureString");
    }

    [Fact]
    public void Builder_AddSsmParameter_StringListType()
    {
        var emulator = KiteBuilder.Create()
            .AddSsmParameter("/app/hosts", "host1,host2,host3", "StringList")
            .Build();

        var param = emulator.Registry.GetSsmParameter("/app/hosts");
        param!.Type.Should().Be("StringList");
        param.Value.Should().Be("host1,host2,host3");
    }
}
