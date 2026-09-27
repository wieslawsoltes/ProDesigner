using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.DesignSystems;

/// <summary>An editable attribute on a named template element, or $root. Null resets the local value.</summary>
public sealed record ComponentOverride(string Target, string Property, string? Value);
public sealed record ComponentVariant(string Name, ComponentOverride[] Overrides);
public sealed record ComponentDefinition(string Id, string Name, long Revision, string Xaml, ComponentVariant[] Variants);
/// <summary>The last generated fragment is a conflict baseline, not a replacement for the document source.</summary>
public sealed record ComponentInstance(string Id, string ComponentId, string DocumentId, string RootName, string? Variant,
    ComponentOverride[] Overrides, long AppliedRevision, string Baseline);
public sealed record DesignSystemState(ComponentDefinition[] Components, ComponentInstance[] Instances)
{
    public static DesignSystemState Empty => new([], []);
}
public sealed record ComponentUpdate(string InstanceId, string DocumentId, TextEdit Edit, ComponentInstance UpdatedInstance);
public sealed class ComponentConflictException(string message) : InvalidOperationException(message);

/// <summary>Source-preserving component materialization with explicit variants, instance overrides and conflict detection.</summary>
public static class ComponentEngine
{
    public static ComponentDefinition Capture(string name, XamlSyntaxTree tree, XamlElement element)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A component name is required.", nameof(name));
        if (element.IsProperty) throw new InvalidOperationException("Capture a visual element, not a property element.");
        var definition = new ComponentDefinition(Guid.NewGuid().ToString("N"), name, 1, XamlNames.ExportFragment(tree, element), []);
        Validate(definition); return definition;
    }
    public static ComponentDefinition Revise(ComponentDefinition definition, string xaml, ComponentVariant[]? variants = null)
    {
        var revised = definition with { Revision = checked(definition.Revision + 1), Xaml = xaml, Variants = variants ?? definition.Variants };
        Validate(revised); return revised;
    }
    public static string Render(ComponentDefinition definition, string rootName, string? variant = null, IReadOnlyList<ComponentOverride>? overrides = null)
    {
        Validate(definition); XmlConvert.VerifyNCName(rootName);
        var tree = XamlSyntaxTree.Parse(definition.Xaml);
        var values = new Dictionary<(string Target, string Property), ComponentOverride>();
        if (variant is not null)
        {
            var selected = definition.Variants.SingleOrDefault(v => v.Name == variant) ?? throw new InvalidOperationException($"Unknown variant '{variant}'.");
            foreach (var value in selected.Overrides) values[(value.Target, value.Property)] = value;
        }
        foreach (var value in overrides ?? []) { ValidateOverride(value); values[(value.Target, value.Property)] = value; }
        var edits = new List<TextEdit>();
        foreach (var value in values.Values)
        {
            var target = ResolveTarget(tree, value.Target);
            edits.Add(XamlEdits.SetAttribute(tree, target, value.Property, value.Value));
        }
        tree = XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source, edits));
        // Rename through temporary identifiers to prevent cascading A -> B -> C substitution.
        var mappings = new Dictionary<(string Scope, string Name), (string Temporary, string Final)>();
        var index = 0;
        foreach (var node in tree.Elements)
        {
            if (XamlNames.Name(node) is not { } name) continue;
            var scope = XamlNames.Scope(node).Id;
            var final = node == tree.Root ? rootName : rootName + "__" + index + "_" + name;
            if (!mappings.TryAdd((scope, name), ("_PD_TEMP_" + index++, final))) throw new InvalidOperationException("The component contains duplicate names in one namescope.");
        }
        edits.Clear();
        foreach (var node in tree.Elements)
        {
            foreach (var attribute in node.Attributes)
            {
                var value = attribute.Value;
                if (XamlNames.IsName(node, attribute.Name)) value = mappings[(XamlNames.Scope(node).Id, value)].Final;
                else
                {
                    var names = mappings.Where(p => p.Key.Scope == XamlNames.Scope(node).Id).ToArray();
                    foreach (var pair in names) value = XamlNames.RewriteReference(attribute.Name, value, pair.Key.Name, pair.Value.Temporary);
                    foreach (var pair in names) value = XamlNames.RewriteReference(attribute.Name, value, pair.Value.Temporary, pair.Value.Final);
                }
                if (value != attribute.Value) edits.Add(XamlEdits.SetAttribute(tree, node, attribute.Name, value));
            }
        }
        if (XamlNames.Name(tree.Root) is null)
        {
            // CLR Name avoids inventing a language prefix or changing inherited namespace bindings.
            edits.Add(XamlEdits.SetAttribute(tree, tree.Root, "Name", rootName));
        }
        var source = EditApplication.Apply(tree.Source, edits); XamlSyntaxTree.Parse(source); return source;
    }
    public static ComponentInstance CreateInstance(ComponentDefinition definition, string documentId, string rootName,
        string? variant = null, ComponentOverride[]? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("A document identity is required.", nameof(documentId));
        var values = overrides ?? [];
        return new(Guid.NewGuid().ToString("N"), definition.Id, documentId, rootName, variant, values, definition.Revision,
            Render(definition, rootName, variant, values));
    }
    public static ComponentUpdate PrepareUpdate(ComponentDefinition definition, ComponentInstance instance, XamlSyntaxTree document)
    {
        if (definition.Id != instance.ComponentId) throw new InvalidOperationException("The instance belongs to a different component.");
        var matches = document.Elements.Where(n => XamlNames.Name(n) == instance.RootName && XamlNames.Scope(n) == document.Root).ToArray();
        if (matches.Length != 1) throw new ComponentConflictException($"Instance '{instance.RootName}' is missing or ambiguous. Relink it explicitly.");
        var node = matches[0]; var current = document.Source.Substring(node.Span.Start, node.Span.Length);
        if (current != instance.Baseline) throw new ComponentConflictException($"Instance '{instance.RootName}' was edited directly. Preserve it by detaching, or record its changes as explicit overrides before updating.");
        var rendered = Render(definition, instance.RootName, instance.Variant, instance.Overrides);
        return new(instance.Id, instance.DocumentId, new(node.Span, rendered, current), instance with { Baseline = rendered, AppliedRevision = definition.Revision });
    }
    public static void Validate(ComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 256 || definition.Revision < 1 || definition.Variants is null || definition.Variants.Length > 256)
            throw new InvalidDataException("Invalid component metadata.");
        var tree = XamlSyntaxTree.Parse(definition.Xaml);
        if (tree.Root.IsProperty || tree.Elements.Count > 10000) throw new InvalidDataException("Invalid or oversized component template.");
        var names = new HashSet<(string, string)>();
        foreach (var node in tree.Elements)
            if (XamlNames.Name(node) is { } name && !names.Add((XamlNames.Scope(node).Id, name))) throw new InvalidDataException("Duplicate component name in one namescope.");
        var variants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in definition.Variants)
        {
            if (variant is null || string.IsNullOrWhiteSpace(variant.Name) || !variants.Add(variant.Name) || variant.Overrides is null || variant.Overrides.Length > 2000) throw new InvalidDataException("Invalid or duplicate variant.");
            var used = new HashSet<(string, string)>();
            foreach (var value in variant.Overrides)
            {
                ValidateOverride(value); ResolveTarget(tree, value.Target);
                if (!used.Add((value.Target, value.Property))) throw new InvalidDataException("Duplicate variant property.");
            }
        }
    }
    public static void Validate(DesignSystemState state)
    {
        if (state.Components is null || state.Instances is null || state.Components.Length > 1000 || state.Instances.Length > 10000) throw new InvalidDataException("Invalid design-system size.");
        var components = new HashSet<string>();
        foreach (var component in state.Components) { Validate(component); if (!components.Add(component.Id)) throw new InvalidDataException("Duplicate component identity."); }
        var identities = new HashSet<string>(); var targets = new HashSet<(string, string)>();
        foreach (var instance in state.Instances)
        {
            if (instance is null || !identities.Add(instance.Id) || !components.Contains(instance.ComponentId) || !targets.Add((instance.DocumentId, instance.RootName)) || instance.Overrides is null || instance.Overrides.Length > 2000 || string.IsNullOrWhiteSpace(instance.DocumentId) || instance.AppliedRevision < 1)
                throw new InvalidDataException("Invalid or duplicate component instance.");
            XmlConvert.VerifyNCName(instance.RootName); XamlSyntaxTree.Parse(instance.Baseline);
            foreach (var value in instance.Overrides) ValidateOverride(value);
        }
    }
    private static XamlElement ResolveTarget(XamlSyntaxTree tree, string name)
    {
        if (name == "$root") return tree.Root;
        var matches = tree.Elements.Where(n => XamlNames.Name(n) == name && XamlNames.Scope(n) == tree.Root).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException($"Override target '{name}' must uniquely name an element in the component's root namescope.");
    }
    private static void ValidateOverride(ComponentOverride value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Target) || value.Target.Length > 256 || value.Property is null || value.Value?.Length > 65536) throw new InvalidDataException("Invalid component override.");
        XmlConvert.VerifyName(value.Property);
        if (value.Property is "Name" or "xmlns" || value.Property.Contains(':')) throw new InvalidOperationException("Overrides cannot change identity or XML namespace directives.");
    }
}
