using System.Collections;
using System.Linq;
using System.Reflection;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;

namespace DotnetNativeMcp.Cli.Output;

public sealed class TableOutputWriter(TextWriter writer) : IOutputWriter
{
    public async ValueTask WriteAsync<T>(NativeResult<T> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await writer.WriteLineAsync(result.Summary.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (result.Error is not null)
        {
            await WriteRowAsync("kind", result.Error.Kind, cancellationToken).ConfigureAwait(false);
            await WriteRowAsync("message", result.Error.Message, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.Error.Detail))
            {
                await WriteRowAsync("detail", result.Error.Detail, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (result.Data is null)
        {
            return;
        }

        if (result.Data is ITableRenderable renderable)
        {
            await renderable.WriteTableAsync(writer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.Data is IEnumerable<InstructionView> instructions)
        {
            await WriteInstructionTableAsync(instructions, cancellationToken).ConfigureAwait(false);
            return;
        }

        var properties = result.Data.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties.Where(static property => !IsSequenceProperty(property)))
        {
            await WriteRowAsync(property.Name, FormatValue(property.GetValue(result.Data)), cancellationToken).ConfigureAwait(false);
        }

        foreach (var property in properties.Where(static property => IsSequenceProperty(property)))
        {
            if (property.GetValue(result.Data) is not IEnumerable sequence)
            {
                continue;
            }

            var items = sequence.Cast<object?>().ToList();
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync(property.Name.AsMemory(), cancellationToken).ConfigureAwait(false);

            if (items.Count == 0)
            {
                await writer.WriteLineAsync("(empty)".AsMemory(), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var firstItem = items.FirstOrDefault(static item => item is not null);
            if (firstItem is null || IsSimpleValue(firstItem.GetType()))
            {
                foreach (var item in items)
                {
                    await writer.WriteLineAsync($"- {FormatValue(item)}".AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            await WriteObjectTableAsync(items, firstItem.GetType(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsSequenceProperty(PropertyInfo property) =>
        property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType);

    private static bool IsSimpleValue(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid);

    private async ValueTask WriteObjectTableAsync(List<object?> items, Type rowType, CancellationToken cancellationToken)
    {
        var columns = rowType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var widths = columns.Select(property => property.Name.Length).ToArray();
        var rows = new List<string[]>(items.Count);

        foreach (var item in items)
        {
            var values = columns.Select(property => FormatValue(property.GetValue(item))).ToArray();
            rows.Add(values);

            for (var index = 0; index < values.Length; index++)
            {
                widths[index] = Math.Max(widths[index], values[index].Length);
            }
        }

        await WriteColumnsAsync(columns.Select(property => property.Name).ToArray(), widths, cancellationToken).ConfigureAwait(false);
        await WriteColumnsAsync(widths.Select(width => new string('-', width)).ToArray(), widths, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            await WriteColumnsAsync(row, widths, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteColumnsAsync(string[] columns, int[] widths, CancellationToken cancellationToken)
    {
        var parts = new string[columns.Length];
        for (var index = 0; index < columns.Length; index++)
        {
            parts[index] = columns[index].PadRight(widths[index]);
        }

        await writer.WriteLineAsync(string.Join("  ", parts).AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteInstructionTableAsync(
        IEnumerable<InstructionView> instructions,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{"Address",-18} {"Bytes",-20} {"Mnemonic",-12} Operands".AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var instruction in instructions)
        {
            await writer.WriteLineAsync(
                    $"{instruction.AddressHex,-18} {instruction.Bytes,-20} {instruction.Mnemonic,-12} {instruction.Operands}".AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask WriteRowAsync(string name, string value, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{name,-20} {value}".AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable sequence)
        {
            var items = new List<string>();
            foreach (var item in sequence)
            {
                items.Add(item?.ToString() ?? string.Empty);
            }

            return string.Join(", ", items);
        }

        return value.ToString() ?? string.Empty;
    }
}
