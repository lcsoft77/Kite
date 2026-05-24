using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Defines a shared XUnit test collection so that all integration test classes
/// share a single <see cref="EmulatorFixture"/> instance (one emulator per test run).
/// </summary>
[CollectionDefinition("EmulatorCollection")]
public class EmulatorCollection : ICollectionFixture<EmulatorFixture>
{
    // No members required – this class is only used for the [CollectionDefinition] attribute.
}
