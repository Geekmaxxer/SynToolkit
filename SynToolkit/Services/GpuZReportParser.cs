#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SynToolkit.Services
{
    internal sealed record GpuZDetail(string Label, string Value);

    internal sealed record GpuZCardDetails(string CardName, IReadOnlyList<GpuZDetail> Details);

    internal static class GpuZReportParser
    {
        internal static IReadOnlyList<GpuZCardDetails> Parse(string report) => Parse(XDocument.Parse(report));

        internal static IReadOnlyList<GpuZCardDetails> Parse(Stream report) => Parse(XDocument.Load(report));

        private static IReadOnlyList<GpuZCardDetails> Parse(XDocument document) => document.Root?
            .Elements("card")
            .Select(CreateCard)
            .Where(card => !string.IsNullOrWhiteSpace(card.CardName))
            .ToList()
            ?? [];

        private static GpuZCardDetails CreateCard(XElement card)
        {
            List<GpuZDetail> details = new();
            Add(details, "GPU", JoinParts(
                Value(card, "gpuname"),
                Value(card, "vendor"),
                Value(card, "subvendor")));
            Add(details, "Silicon", JoinParts(
                WithUnit(Value(card, "processsize"), "nm"),
                WithUnit(Value(card, "diesize"), "mm\u00B2"),
                AddSuffix(FormatMillions(Value(card, "transistors")), " transistors"),
                Value(card, "releasedate")));
            Add(details, "Board", JoinParts(
                Combine(Value(card, "vendorid"), Value(card, "deviceid"), ":"),
                WithPrefix("Revision", Value(card, "gpurevision")),
                WithPrefix("UEFI", ToYesNo(Value(card, "biosuefi")))));
            Add(details, "BIOS", Value(card, "biosversion"));
            Add(details, "Bus interface", Value(card, "businterface"));
            Add(details, "Platform", JoinParts(
                WithPrefix("DirectX", Value(card, "directxsupport")),
                WithPrefix("Resizable BAR", Value(card, "resizablebar"))));
            Add(details, "Memory", JoinParts(
                FormatMemory(card),
                WithUnit(NonZero(Value(card, "membandwidth")), "GB/s")));
            Add(details, "Compute", JoinParts(
                WithSuffix(NonZero(Value(card, "numshadersunified")), " Shader Units"),
                WithSuffix(NonZero(Value(card, "numrops")), " ROPs"),
                WithSuffix(NonZero(Value(card, "numtmus")), " TMUs")));
            Add(details, "Throughput", JoinParts(
                WithUnit(NonZero(Value(card, "fillratepixel")), "GPixel/s"),
                WithUnit(NonZero(Value(card, "fillratetexel")), "GTexel/s")));
            Add(details, "Current clocks", JoinParts(
                WithPrefix("GPU", WithUnit(NonZero(Value(card, "clockgpu")), "MHz")),
                WithPrefix("Memory", WithUnit(NonZero(Value(card, "clockmem")), "MHz"))));
            Add(details, "Boost clock", WithUnit(NonZero(Value(card, "clockgpuboost")), "MHz"));
            Add(details, "Driver", JoinParts(
                Value(card, "driverversion"),
                Value(card, "driverdate"),
                Value(card, "whql")));
            Add(details, "Features", FormatFeatures(card));

            return new GpuZCardDetails(Value(card, "cardname") ?? string.Empty, details);
        }

        private static string? Value(XElement card, string name)
        {
            string? value = card.Element(name)?.Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void Add(ICollection<GpuZDetail> details, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details.Add(new GpuZDetail(label, value));
            }
        }

        private static string? JoinParts(params string?[] values)
        {
            string[] populatedValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
            return populatedValues.Length == 0 ? null : string.Join(" \u00B7 ", populatedValues);
        }

        private static string? FormatMemory(XElement card)
        {
            List<string> parts = new();
            AddPart(parts, Value(card, "memtype"));
            AddPart(parts, Value(card, "memvendor"));
            string? busWidth = NonZero(Value(card, "membuswidth"));
            if (busWidth is not null)
            {
                parts.Add(busWidth + "-bit");
            }

            return parts.Count == 0 ? null : string.Join(" \u00B7 ", parts);
        }

        private static string? FormatFeatures(XElement card)
        {
            (string ElementName, string DisplayName)[] features =
            [
                ("cuda", "CUDA"),
                ("opencl", "OpenCL"),
                ("dxcompute", "DirectCompute"),
                ("physx", "PhysX"),
                ("dxr", "Ray Tracing"),
                ("directml", "DirectML"),
                ("opengl", "OpenGL")
            ];
            string[] enabled = features
                .Where(feature => Value(card, feature.ElementName) == "1")
                .Select(feature => feature.DisplayName)
                .ToArray();
            return enabled.Length == 0 ? null : string.Join(", ", enabled);
        }

        private static string? FormatMillions(string? value)
        {
            if (!long.TryParse(value, out long millions) || millions == 0)
            {
                return null;
            }

            return millions >= 1000
                ? (millions / 1000d).ToString("0.##", CultureInfo.InvariantCulture) + " billion"
                : millions.ToString(CultureInfo.InvariantCulture) + " million";
        }

        private static string? WithUnit(string? value, string unit) => NonZero(value) is string nonZero ? nonZero + " " + unit : null;

        private static string? WithPrefix(string prefix, string? value) => value is null ? null : prefix + " " + value;

        private static string? WithSuffix(string? value, string suffix) => value is null ? null : value + suffix;

        private static string? AddSuffix(string? value, string suffix) => value is null ? null : value + suffix;

        private static string? ToYesNo(string? value) => value switch
        {
            "1" => "Yes",
            "0" => "No",
            _ => null
        };

        private static string? NonZero(string? value) => value is "0" or "0.0" ? null : value;

        private static string? Combine(string? first, string? second, string separator) => first is not null && second is not null
            ? first + separator + second
            : first ?? second;

        private static void AddPart(ICollection<string> values, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }
    }
}