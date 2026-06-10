using System.Collections.ObjectModel;
using System.Xml;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Dgml;

public sealed record DgmlNode(string Id, string Label, string? Category);

/// <summary>
/// A directed dependency edge. <paramref name="Label"/> carries the ILC retention reason
/// (the DGML <c>Reason</c> attribute, e.g. <c>call</c>, <c>reloc</c>, <c>Reflectable type</c>)
/// when present, falling back to the generic DGML <c>Label</c> attribute.
/// </summary>
public sealed record DgmlEdge(string Source, string Target, string? Label);

public sealed record DgmlGraph(
    string FilePath,
    IReadOnlyList<DgmlNode> Nodes,
    IReadOnlyList<DgmlEdge> Edges);

public static class DgmlReader
{
    private const string DgmlNamespaceUri = "http://schemas.microsoft.com/vs/2009/dgml";

    public static NativeResult<DgmlGraph> Read(string path) =>
        Read(path, ResourceLimits.MaxDgmlBytes, ResourceLimits.MaxDgmlNodes, ResourceLimits.MaxDgmlEdges);

    internal static NativeResult<DgmlGraph> Read(string path, long maxBytes, int maxNodes, int maxEdges)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<DgmlGraph>(ErrorKinds.InvalidArgument, "dgmlPath must not be empty.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return NativeResult.Fail<DgmlGraph>(ErrorKinds.DgmlNotFound, $"DGML sidecar not found: '{Path.GetFileName(fullPath)}'.");

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length > maxBytes)
            {
                return NativeResult.Fail<DgmlGraph>(
                    ErrorKinds.FileTooLarge,
                    $"DGML sidecar '{Path.GetFileName(fullPath)}' is {info.Length} bytes, which exceeds the limit of {maxBytes} bytes.");
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxBytes * 4L,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };

            using var stream = File.OpenRead(fullPath);
            if (stream.CanSeek && stream.Length > maxBytes)
            {
                return NativeResult.Fail<DgmlGraph>(
                    ErrorKinds.FileTooLarge,
                    $"DGML sidecar '{Path.GetFileName(fullPath)}' is {stream.Length} bytes, which exceeds the limit of {maxBytes} bytes.");
            }
            using var reader = XmlReader.Create(stream, settings);

            var nodes = new List<DgmlNode>();
            var edges = new List<DgmlEdge>();
            string? rootName = null;
            string? rootNs = null;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;

                if (rootName is null)
                {
                    rootName = reader.LocalName;
                    rootNs = reader.NamespaceURI;
                    if (rootName != "DirectedGraph" || rootNs != DgmlNamespaceUri)
                    {
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.InternalError,
                            $"'{Path.GetFileName(fullPath)}' is not a DGML DirectedGraph document.");
                    }
                    continue;
                }

                if (reader.NamespaceURI != DgmlNamespaceUri) continue;

                if (reader.LocalName == "Node")
                {
                    if (nodes.Count >= maxNodes)
                    {
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.FileTooLarge,
                            $"DGML sidecar '{Path.GetFileName(fullPath)}' exceeds the maximum of {maxNodes} nodes.");
                    }

                    var id = reader.GetAttribute("Id");
                    if (string.IsNullOrWhiteSpace(id))
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.InternalError,
                            $"Malformed DGML in '{Path.GetFileName(fullPath)}': DGML node is missing required 'Id'.");

                    var label = reader.GetAttribute("Label") ?? string.Empty;
                    var category = NormalizeOptional(reader.GetAttribute("Category"));
                    nodes.Add(new DgmlNode(id, label, category));
                }
                else if (reader.LocalName == "Link")
                {
                    if (edges.Count >= maxEdges)
                    {
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.FileTooLarge,
                            $"DGML sidecar '{Path.GetFileName(fullPath)}' exceeds the maximum of {maxEdges} edges.");
                    }

                    var source = reader.GetAttribute("Source");
                    if (string.IsNullOrWhiteSpace(source))
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.InternalError,
                            $"Malformed DGML in '{Path.GetFileName(fullPath)}': DGML link is missing required 'Source'.");

                    var target = reader.GetAttribute("Target");
                    if (string.IsNullOrWhiteSpace(target))
                        return NativeResult.Fail<DgmlGraph>(
                            ErrorKinds.InternalError,
                            $"Malformed DGML in '{Path.GetFileName(fullPath)}': DGML link is missing required 'Target'.");

                    var edgeLabel = NormalizeOptional(reader.GetAttribute("Reason"))
                        ?? NormalizeOptional(reader.GetAttribute("Label"));
                    edges.Add(new DgmlEdge(source, target, edgeLabel));
                }
            }

            if (rootName is null)
            {
                return NativeResult.Fail<DgmlGraph>(
                    ErrorKinds.InternalError,
                    $"'{Path.GetFileName(fullPath)}' is not a DGML DirectedGraph document.");
            }

            return NativeResult.Ok(
                $"Read {nodes.Count} DGML node(s) and {edges.Count} edge(s) from '{Path.GetFileName(fullPath)}'.",
                new DgmlGraph(
                    fullPath,
                    new ReadOnlyCollection<DgmlNode>(nodes),
                    new ReadOnlyCollection<DgmlEdge>(edges)));
        }
        catch (InvalidDataException ex)
        {
            return NativeResult.Fail<DgmlGraph>(
                ErrorKinds.InternalError,
                $"Malformed DGML in '{Path.GetFileName(fullPath)}'.",
                SanitisedError.From(ex, fullPath));
        }
        catch (XmlException ex)
        {
            return NativeResult.Fail<DgmlGraph>(
                ErrorKinds.InternalError,
                $"Malformed DGML in '{Path.GetFileName(fullPath)}'.",
                SanitisedError.From(ex, fullPath));
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<DgmlGraph>(
                ErrorKinds.InternalError,
                $"Failed to read '{Path.GetFileName(fullPath)}'.",
                SanitisedError.From(ex, fullPath));
        }
    }

    public static string GetDefaultDgmlPath(string binaryPath) => Path.ChangeExtension(binaryPath, ".dgml");

    public static bool HasSiblingDgml(string binaryPath) => File.Exists(GetDefaultDgmlPath(binaryPath));

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
