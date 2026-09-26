using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.Runtime;

public sealed class RuntimePreviewLease : IDisposable
{
    private AssemblyLoadContext? _context;
    public Control Root { get; }
    internal RuntimePreviewLease(Control root, AssemblyLoadContext? context) { Root = root; _context = context; }
    public void Dispose() { _context?.Unload(); _context = null; }
}

/// <summary>Full Avalonia runtime XAML loading for explicitly trusted documents and compiled projects.</summary>
/// <remarks>Collectible assembly contexts aid lifecycle management; they are not a security or process sandbox.</remarks>
public sealed class RuntimePreviewEngine
{
    public RuntimePreviewLease Load(TrustedPreviewRequest request, string? assemblyPath = null)
    {
        if (!request.Trusted) throw new UnauthorizedAccessException("Runtime XAML may execute arbitrary code. Explicit trust is required.");
        Dispatcher.UIThread.VerifyAccess();
        var tree = XamlSyntaxTree.Parse(request.Source);
        PreviewAssemblyContext? context = null;
        try
        {
            Assembly? assembly = null; object? rootInstance = null;
            if (assemblyPath is not null)
            {
                assemblyPath = Path.GetFullPath(assemblyPath);
                if (!File.Exists(assemblyPath)) throw new FileNotFoundException("The compiled project assembly was not found.", assemblyPath);
                context = new PreviewAssemblyContext(assemblyPath);
                assembly = context.LoadFromAssemblyPath(assemblyPath);
                if (tree.Root.Get("x:Class") is { } className)
                {
                    var type = assembly.GetType(className, throwOnError: true)!;
                    if (!typeof(Control).IsAssignableFrom(type)) throw new InvalidOperationException("The XAML code-behind class must derive from Avalonia Control.");
                    rootInstance = Activator.CreateInstance(type) ?? throw new InvalidOperationException("The preview root needs a public parameterless constructor or a dedicated design-time view.");
                }
            }
            var source = request.Source;
            if (tree.Root.Get("x:Class") is not null)
                source = EditApplication.Apply(source, [XamlEdits.SetAttribute(tree, tree.Root, "x:Class", null)]);
            Uri? uri = null;
            if (assembly is not null && request.ProjectPath is not null && request.DocumentPath is not null)
                uri = new Uri("avares://" + assembly.GetName().Name + "/" + Path.GetRelativePath(Path.GetDirectoryName(request.ProjectPath)!, request.DocumentPath).Replace('\\', '/'));
            var result = AvaloniaRuntimeXamlLoader.Load(source, assembly, rootInstance, uri, designMode: true);
            Control root;
            if (result is Window window)
            {
                root = window.Content as Control ?? throw new InvalidOperationException("The preview window has no visual content.");
                window.Content = null;
            }
            else root = result as Control ?? throw new InvalidOperationException("The XAML root must be an Avalonia control.");
            return new(root, context);
        }
        catch { context?.Unload(); throw; }
    }
    private sealed class PreviewAssemblyContext(string assemblyPath) : AssemblyLoadContext("ProDesigner.Preview." + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true || name.Name is "SkiaSharp" or "HarfBuzzSharp")
            {
                var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
                if (shared is not null) return shared;
                return Default.LoadFromAssemblyName(name);
            }
            var path = _resolver.ResolveAssemblyToPath(name);
            path ??= Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
        protected override nint LoadUnmanagedDll(string name)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
