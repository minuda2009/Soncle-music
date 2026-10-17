// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Services;
using Soncle.TestKit;

namespace Soncle.Services.Tests;

/// <summary>Golden test: the C# classifier must match <c>renderer/devices.js</c>.</summary>
public class DeviceClassifierTests
{
    [Fact]
    public void ClassifyMatchesTheJs()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("devices/classify.json"))).RootElement;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var label = c.GetProperty("label").GetString()!;
            var bt = c.GetProperty("bt").EnumerateArray().Select(x => x.GetString()!).ToList();
            var expected = c.GetProperty("result");
            var got = DeviceClassifier.Classify(label, bt);
            Assert.Equal(expected.GetProperty("label").GetString(), got.Label);
            Assert.Equal(expected.GetProperty("model").GetString(), got.Model);
            Assert.Equal(expected.GetProperty("endpoint").GetString(), got.Endpoint);
            Assert.Equal(expected.GetProperty("type").GetString(), got.Type);
            Assert.Equal(expected.GetProperty("bluetooth").GetBoolean(), got.Bluetooth);
            Assert.Equal(expected.GetProperty("handsFree").GetBoolean(), got.HandsFree);
        }
    }

    [Fact]
    public void DeviceInfoMatchesTheJs()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("devices/classify.json"))).RootElement;
        var info = root.GetProperty("deviceInfo");
        foreach (var p in info.EnumerateObject())
        {
            var got = DeviceClassifier.DeviceInfo[p.Name];
            Assert.Equal(p.Value.GetProperty("name").GetString(), got.Name);
            Assert.Equal(p.Value.GetProperty("icon").GetString(), got.Icon);
            Assert.Equal(p.Value.GetProperty("preset").GetString(), got.Preset);
        }
        Assert.Equal(DeviceClassifier.DeviceInfo.Count, info.EnumerateObject().Count());
    }

    [Fact]
    public void AndroidOutputLabelMatchesTheJs()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("devices/classify.json"))).RootElement;
        var cases = root.GetProperty("androidLabels");
        Assert.True(cases.GetArrayLength() >= 15);
        foreach (var c in cases.EnumerateArray())
        {
            var input = c.GetProperty("input");
            string? Str(string k) => input.TryGetProperty(k, out var v) ? v.GetString() : null;
            var bt = input.TryGetProperty("bluetooth", out var b) && b.GetBoolean();
            Assert.Equal(c.GetProperty("label").GetString(), DeviceClassifier.AndroidOutputLabel(Str("type"), Str("name"), bt));
        }
    }
}
