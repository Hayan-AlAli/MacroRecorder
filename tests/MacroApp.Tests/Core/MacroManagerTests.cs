using System.IO;
using FluentAssertions;
using MacroApp.Core;
using Xunit;

namespace MacroApp.Tests.Core;

public class MacroManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "macroapp-lib-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Export_writes_a_copy_without_moving_the_macro()
    {
        var manager = new MacroManager(Path.Combine(_root, "lib"));
        var macro = await manager.CreateAsync("Export me");
        string stored = macro.FilePath!;
        string exported = Path.Combine(_root, "out", "copy.mcr");

        await manager.ExportAsync(macro, exported);

        File.Exists(exported).Should().BeTrue();
        macro.FilePath.Should().Be(stored);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_gives_two_ids()
    {
        var source = new MacroManager(Path.Combine(_root, "source"));
        var original = await source.CreateAsync("Shared");
        string file = Path.Combine(_root, "shared.mcr");
        await source.ExportAsync(original, file);

        var library = new MacroManager(Path.Combine(_root, "lib"));
        var first = await library.ImportAsync(file);
        var second = await library.ImportAsync(file);

        first.Id.Should().Be(original.Id);
        second.Id.Should().NotBe(first.Id);
        second.Name.Should().Be("Shared");
        library.Macros.Should().HaveCount(2);
    }
}
