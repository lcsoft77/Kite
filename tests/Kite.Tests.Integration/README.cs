// Integration tests require a running emulator instance.
// These tests use real AWS SDKs pointed at the emulator.
// They are kept minimal here as stubs - full integration tests
// would be expanded based on specific test scenarios.

namespace Kite.Tests.Integration;

// Integration tests are defined in separate test class files.
// Run the emulator with: KiteBuilder.Create().WithPort(5555).Build().UseHost().RunAsync()
// Then configure AWS SDK clients with ServiceURL = "http://localhost:5555"
