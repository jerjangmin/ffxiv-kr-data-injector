// Run with: dotnet run --project mac/FFXIVInjector.Mac.Tests
string root = Path.Combine(Path.GetTempPath(), "mac-injector-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    await MacPatchServiceTests.Run(root);
    ReleaseUrlExtractorTests.Run();
}
finally
{
    Directory.Delete(root, true);
}
