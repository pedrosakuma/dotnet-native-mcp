using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Dgml;

public sealed record DgmlNode(string Id, string Label, string? Category);

public sealed record DgmlEdge(string Source, string Target, string? Label);

public sealed record DgmlGraph(
    string FilePath,
    IReadOnlyList<DgmlNode> Nodes,
    IReadOnlyList<DgmlEdge> Edges);

public static class DgmlReader
{
    private static readonly XNamespace DgmlNamespace = "http://schemas.microsoft.com/vs/2009/dgml";

    public static NativeResult<DgmlGraph> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<DgmlGraph>(ErrorKinds.InvalidArgument, "dgmlPath must not be empty.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return NativeResult.Fail<DgmlGraph>(ErrorKinds.DgmlNotFound, $"DGML sidecar not found: '{fullPath}'.");

        try
        {
            using var stream = File.OpenRead(fullPath);
            var document = XDocument.Load(stream, LoadOptions.None);
            if (document.Root is null || document.Root.Name != DgmlNamespace + "DirectedGraph")
            {
                return NativeResult.Fail<DgmlGraph>(
                    ErrorKinds.InternalError,
                    $"'{Path.GetFileName(fullPath)}' is not a DGML DirectedGraph document.");
            }

            var nodes = (document.Root.Element(DgmlNamespace + "Nodes")?.Elements(DgmlNamespace + "Node") ?? [])
                .Select(ParseNode)
                .ToList();
            var edges = (document.Root.Element(DgmlNamespace + "Links")?.Elements(DgmlNamespace + "Link") ?? [])
                .Select(ParseEdge)
                .ToList();

            return NativeResult.Ok(
                $"Read {nodes.Count} DGML node(s) and {edges.Count} edge(s) from '{Path.GetFileName(fullPath)}'.",
                new DgmlGraph(
                    fullPath,
                    new ReadOnlyCollection<DgmlNode>(nodes),
                    new ReadOnlyCollection<DgmlEdge>(edges)));
        }
        catch (XmlException ex)
        {
            return NativeResult.Fail<DgmlGraph>(
                ErrorKinds.InternalError,
                $"Malformed DGML in '{Path.GetFileName(fullPath)}': {ex.Message}",
                ex.ToString());
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<DgmlGraph>(
                ErrorKinds.InternalError,
                $"Failed to read '{fullPath}': {ex.Message}",
                ex.ToString());
        }
    }

    public static string GetDefaultDgmlPath(string binaryPath) => Path.ChangeExtension(binaryPath, ".dgml");

    public static bool HasSiblingDgml(string binaryPath) => File.Exists(GetDefaultDgmlPath(binaryPath));

    private static DgmlNode ParseNode(XElement element)
    {
        var id = (string?)element.Attribute("Id");
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("DGML node is missing required 'Id'.");

        var label = (string?)element.Attribute("Label") ?? string.Empty;
        var category = NormalizeOptional((string?)element.Attribute("Category"));
        return new DgmlNode(id, label, category);
    }

    private static DgmlEdge ParseEdge(XElement element)
    {
        var source = (string?)element.Attribute("Source");
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidDataException("DGML link is missing required 'Source'.");

        var target = (string?)element.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidDataException("DGML link is missing required 'Target'.");

        return new DgmlEdge(source, target, NormalizeOptional((string?)element.Attribute("Label")));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
