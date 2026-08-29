using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DotnetNativeMcp.Core;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Mstat;

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

        switch (result.Data)
        {
            case SizeCommandData sizeData:
                await WriteSizeCommandDataAsync(sizeData, cancellationToken).ConfigureAwait(false);
                return;
            case SizeDiffCommandData sizeDiffData:
                await WriteSizeDiffCommandDataAsync(sizeDiffData, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask WriteSizeCommandDataAsync(SizeCommandData data, CancellationToken cancellationToken)
    {
        await WriteRowAsync(nameof(SizeCommandData.BinaryPath), data.BinaryPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeCommandData.GroupBy), data.GroupBy, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeCommandData.MstatPath), data.MstatPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeCommandData.FormatVersion), data.FormatVersion, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeCommandData.TotalAttributedBytes), FormatNumber(data.TotalAttributedBytes), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeCommandData.DeduplicatedMethodCount), FormatNumber(data.DeduplicatedMethodCount), cancellationToken).ConfigureAwait(false);

        await writer.WriteLineAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync("category totals".AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteTableAsync(
            ["category", "self-size", "total-size"],
            data.CategoryTotals.Select(category => new[]
            {
                category.Category,
                FormatNumber(category.TotalSize),
                FormatNumber(category.TotalSize),
            }),
            emptyMessage: "(none)",
            cancellationToken).ConfigureAwait(false);

        await writer.WriteLineAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync($"{data.GroupBy} breakdown".AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteTableAsync(
            [data.GroupBy, "bytes", "attributions"],
            data.Rows.Select(row => new[]
            {
                row.Key,
                FormatNumber(row.TotalSize),
                FormatNumber(row.AttributionCount),
            }),
            emptyMessage: "(none)",
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteSizeDiffCommandDataAsync(SizeDiffCommandData data, CancellationToken cancellationToken)
    {
        await WriteRowAsync(nameof(SizeDiffCommandData.BaselineBinaryPath), data.BaselineBinaryPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.CandidateBinaryPath), data.CandidateBinaryPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.GroupBy), data.GroupBy, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.BaselineMstatPath), data.BaselineMstatPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.CurrentMstatPath), data.CurrentMstatPath, cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.BaselineTotalSize), FormatNumber(data.BaselineTotalSize), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.CandidateTotalSize), FormatNumber(data.CandidateTotalSize), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.TotalSizeDelta), FormatSignedNumber(data.TotalSizeDelta), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.AddedBucketCount), FormatNumber(data.AddedBucketCount), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.RemovedBucketCount), FormatNumber(data.RemovedBucketCount), cancellationToken).ConfigureAwait(false);
        await WriteRowAsync(nameof(SizeDiffCommandData.ChangedBucketCount), FormatNumber(data.ChangedBucketCount), cancellationToken).ConfigureAwait(false);

        if (data.FailOnIncreaseBytes is long threshold)
        {
            await WriteRowAsync(nameof(SizeDiffCommandData.FailOnIncreaseBytes), FormatNumber(threshold), cancellationToken).ConfigureAwait(false);
            await WriteRowAsync(nameof(SizeDiffCommandData.ThresholdExceeded), data.ThresholdExceeded ? "true" : "false", cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteLineAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync("grew".AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteTableAsync(
            [data.GroupBy, "baseline", "candidate", "delta"],
            data.TopGrew.Select(ToDiffRow),
            emptyMessage: "(none)",
            cancellationToken).ConfigureAwait(false);

        await writer.WriteLineAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync("shrank".AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteTableAsync(
            [data.GroupBy, "baseline", "candidate", "delta"],
            data.TopShrank.Select(ToDiffRow),
            emptyMessage: "(none)",
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteRowAsync(string name, string value, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"{name,-20} {value}".AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteTableAsync(
        IReadOnlyList<string> headers,
        IEnumerable<string[]> rows,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        var materializedRows = rows.ToList();
        var widths = new int[headers.Count];
        for (var index = 0; index < headers.Count; index++)
        {
            widths[index] = headers[index].Length;
        }

        foreach (var row in materializedRows)
        {
            for (var index = 0; index < headers.Count; index++)
            {
                widths[index] = Math.Max(widths[index], row[index].Length);
            }
        }

        await writer.WriteLineAsync(FormatColumns(headers, widths).AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(FormatColumns(widths.Select(width => new string('-', width)).ToArray(), widths).AsMemory(), cancellationToken).ConfigureAwait(false);

        if (materializedRows.Count == 0)
        {
            await writer.WriteLineAsync(emptyMessage.AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var row in materializedRows)
        {
            await writer.WriteLineAsync(FormatColumns(row, widths).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string[] ToDiffRow(MstatSizeBucketDelta delta) =>
    [
        delta.Key,
        FormatNumber(delta.BaselineSize),
        FormatNumber(delta.CurrentSize),
        FormatSignedNumber(delta.SizeDelta),
    ];

    private static string FormatColumns(IReadOnlyList<string> values, int[] widths) =>
        string.Join("  ", values.Select((value, index) => value.PadRight(widths[index], ' ')));

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

    private static string FormatNumber(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNumber(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatSignedNumber(long value) =>
        value >= 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
}
