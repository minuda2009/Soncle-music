// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using Soncle.Streams;

namespace Soncle.Streams.Tests;

public class ByteRangeTests
{
    [Fact]
    public void ParsesASuffixRange()
    {
        Assert.Equal(new ByteRange(500, 999), ByteRange.Parse("bytes=-500", 1000));
    }

    [Fact]
    public void ParsesAnOpenEndedRange()
    {
        Assert.Equal(new ByteRange(0, 999), ByteRange.Parse("bytes=0-", 1000));
    }

    [Fact]
    public void ParsesABoundedRangeAndClampsToTheSize()
    {
        Assert.Equal(new ByteRange(100, 199), ByteRange.Parse("bytes=100-199", 1000));
        Assert.Equal(new ByteRange(900, 999), ByteRange.Parse("bytes=900-5000", 1000));
    }

    [Fact]
    public void RangePastEndIsUnsatisfiable()
    {
        // the caller compares Start with the size (the proxy answers 416, as in streamproxy.test.mjs)
        var r = ByteRange.Parse("bytes=1000-", 1000);
        Assert.Equal(1000, r.Start);
        Assert.True(r.Start >= 1000);
    }

    [Fact]
    public void NoRangeMeansTheWholeFile()
    {
        Assert.Equal(new ByteRange(0, 999), ByteRange.Parse(null, 1000));
    }
}
