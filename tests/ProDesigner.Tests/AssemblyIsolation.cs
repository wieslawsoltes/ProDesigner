using Avalonia.Headless;
using Xunit;

// The application installs process-global font/style/render services. One UI dispatcher owns them for this assembly.
// Tests create and close their own views; compiler/process tests remain isolated through their own fixtures.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
