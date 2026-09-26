using System.Diagnostics;
using System.Text;
using ProDesigner.Core;

namespace ProDesigner.Workspaces;

/// <summary>Explicitly trusted, cancellable SDK restore/build with bounded output and process-tree termination.</summary>
public sealed class DotNetProjectBuilder : IProjectBuildService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
    public string? DotNetPath { get; init; }
    public async Task<ProjectBuildResult> BuildAsync(ProjectBuildRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Trusted) throw new UnauthorizedAccessException("Project restore and build execute project code. Explicit workspace trust is required.");
        var path = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(path) || Path.GetExtension(path) != ".csproj") throw new FileNotFoundException("Choose an existing C# project.", path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(Timeout);
        await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var arguments = new List<string> { "build", path, "--configuration", request.Configuration, "--nologo", "--verbosity", "minimal" };
            if (!string.IsNullOrEmpty(request.TargetFramework)) { arguments.Add("--framework"); arguments.Add(request.TargetFramework); }
            var build = await RunAsync(path, arguments, timeout.Token).ConfigureAwait(false);
            if (build.Code != 0) return new(false, null, [new("BUILD001", "Project restore/build failed. See the build log.", DiagnosticSeverity.Error, File: path)], build.Output);
            var query = new List<string> { "msbuild", path, "-nologo", "-getProperty:TargetPath", "-p:Configuration=" + request.Configuration };
            if (!string.IsNullOrEmpty(request.TargetFramework)) query.Add("-p:TargetFramework=" + request.TargetFramework);
            var output = await RunAsync(path, query, timeout.Token).ConfigureAwait(false);
            var assembly = output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(File.Exists);
            if (output.Code != 0 || assembly is null)
                return new(false, null, [new("BUILD002", "The output assembly could not be resolved. For multi-targeted projects choose a target framework.", DiagnosticSeverity.Error, File: path)], build.Output + "\n" + output.Output);
            return new(true, Path.GetFullPath(assembly), [], build.Output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, null, [new("BUILD003", "The project build exceeded its time limit and was terminated.", DiagnosticSeverity.Error, File: path)], "Build timed out.");
        }
        finally { _gate.Release(); }
    }
    private async Task<(int Code, string Output)> RunAsync(string projectPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var host = DotNetPath ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host) { WorkingDirectory = Path.GetDirectoryName(projectPath)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        // Container platforms sometimes export an unrelated Platform variable that MSBuild interprets as a build platform.
        start.Environment.Remove("Platform"); start.Environment.Remove("PLATFORM");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The .NET SDK process could not be started.");
        var buffer = new StringBuilder(); var sync = new object();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                lock (sync) { if (buffer.Length < 2 * 1024 * 1024) buffer.AppendLine(line); }
        }
        var stdout = ReadAsync(process.StandardOutput); var stderr = ReadAsync(process.StandardError);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { }
            throw;
        }
        return (process.ExitCode, buffer.ToString());
    }
}
