using System.Text.Json;

namespace GeoAnim.Tests;

public class ConstructionTests
{
    [Fact]
    public void KeysOnAStraightLineAreLeftOut()
    {
        static Primitive.Key K(double t, object v, Easing e = Easing.Linear) => new(t, v, e);
        List<Primitive.Key> keys =
        [
            K(0, 0.0, Easing.Step), K(40, 1.0), K(80, 2.0), K(120, 3.0005), K(160, 4.0),
            K(200, 6.0), K(240, 8.0), K(240, 0.0, Easing.Step), K(280, 0.5), K(320, 1.0, Easing.EaseInOut),
        ];
        // 40 → 120 lie on the line from 0 to 160; at 200 the slope changes; steps and eased keys stay
        Assert.Equal([0.0, 160, 240, 240, 280, 320], AnimationData.Simplify(keys).Select(k => k.Time));
        List<Primitive.Key> points = [K(0, new[] { 0.0, 0 }), K(40, new[] { 1.0, 2 }), K(80, new[] { 2.0, 4 }), K(120, new[] { 3.0, 7 })];
        Assert.Equal([0.0, 80, 120], AnimationData.Simplify(points).Select(k => k.Time));
    }

    private static JsonElement Json(Construction c) => JsonDocument.Parse(c.Build().ToJson()).RootElement;

    private static JsonElement Track(JsonElement json, int primitive, string property) =>
        json.GetProperty("primitives")[primitive - 1].GetProperty("tracks").GetProperty(property);

    private static string Keys(JsonElement track) => track.GetRawText();

    [Fact]
    public void StartingPictureIsAtTimeZero()
    {
        var c = new Construction { Title = "T" };
        c.Point("A", 2, 3);
        JsonElement json = Json(c);

        Assert.Equal(0, json.GetProperty("duration").GetDouble());
        Assert.Equal(0, json.GetProperty("steps").GetArrayLength());
        // dot and label
        Assert.Equal(2, json.GetProperty("primitives").GetArrayLength());
        Assert.Equal("dot", json.GetProperty("primitives")[0].GetProperty("type").GetString());
        Assert.Equal("[[0,5,\"s\"]]", Keys(Track(json, 1, "r")));
        Assert.Equal("[[0,\"$A$\",\"s\"]]", Keys(Track(json, 2, "text")));
    }

    [Fact]
    public void SegmentGrowsFromAToB()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0, new Options { NoLabel = true });
        GeoPoint b = c.Point("B", 4, 2, new Options { NoLabel = true });
        c.Step("draw");
        c.Pause(100);
        c.Segment("s", a, b, new Options { Duration = 800 });
        JsonElement json = Json(c);

        // primitives: A, B, s
        Assert.Equal("line", json.GetProperty("primitives")[2].GetProperty("type").GetString());
        Assert.Equal("[[100,0,\"s\"]]", Keys(Track(json, 3, "x1")));
        Assert.Equal("[[100,0,\"s\"],[900,4,\"io\"]]", Keys(Track(json, 3, "x2")));
        Assert.Equal("[[100,0,\"s\"],[900,2,\"io\"]]", Keys(Track(json, 3, "y2")));
        Assert.Equal(900, json.GetProperty("duration").GetDouble());
        Assert.Equal("[{\"caption\":\"draw\",\"start\":0,\"end\":900}]", json.GetProperty("steps").GetRawText());
    }

    [Fact]
    public void CircleIsDrawnFromThePointOnIt()
    {
        var c = new Construction();
        GeoPoint m = c.Point("M", 0, 0);
        GeoPoint p = c.Point("P", 0, 3);
        c.Step("circle");
        c.Circle("k", m, p, new Options { Easing = Easing.Linear });
        JsonElement json = Json(c);

        Assert.Equal("[[0,90,\"s\"]]", Keys(Track(json, 5, "start")));
        Assert.Equal("[[0,0,\"s\"],[1200,360]]", Keys(Track(json, 5, "sweep")));
        Assert.Equal("[[0,3,\"s\"]]", Keys(Track(json, 5, "r")));
    }

    [Fact]
    public void TogetherTakesAsLongAsTheLongestAction()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0);
        GeoPoint b = c.Point("B", 4, 0);
        c.Step("both");
        c.Together(() =>
        {
            c.Segment("s", a, b, new Options { Duration = 300 });
            c.Circle("k", a, b, new Options { Duration = 1000 });
        });
        c.Step("after");
        c.Pause(200);
        JsonElement json = Json(c);

        Assert.Equal("[[0,0,\"s\"],[1000,360,\"io\"]]", Keys(Track(json, 6, "sweep")));
        Assert.Equal("[[0,0,\"s\"],[300,4,\"io\"]]", Keys(Track(json, 5, "x2")));
        Assert.Equal(1200, json.GetProperty("duration").GetDouble());
        Assert.Equal(1000, json.GetProperty("steps")[1].GetProperty("start").GetDouble());
    }

    [Fact]
    public void EmptyStepGetsAPause()
    {
        var c = new Construction();
        c.Step("nothing happens");
        c.Step("neither");
        JsonElement json = Json(c);
        Assert.Equal(2 * Construction.EmptyStepDuration, json.GetProperty("duration").GetDouble());
    }

    [Fact]
    public void MovingPointIsSampledAndDependentObjectsFollowExactly()
    {
        var c = new Construction();
        GeoPoint m = c.Point("M", 0, 0);
        GeoPoint p = c.Point("P", 3, 0);
        c.Circle("k", m, p);
        c.Step("move");
        c.Move(p, new Vec(0, 4), duration: 400, easing: Easing.Linear);
        JsonElement json = Json(c);

        // 400 ms in steps of 40 ms
        JsonElement radius = Track(json, 5, "r");
        Assert.Equal(11, radius.GetArrayLength());
        for (int i = 1; i <= 10; i++)
        {
            double u = i / 10.0;
            Vec position = new(3 * (1 - u), 4 * u);
            Assert.Equal(40 * i, radius[i][0].GetDouble());
            Assert.Equal(Math.Round(position.Length, 4), radius[i][1].GetDouble(), 4);
            // in a sampled block every step is linear
            Assert.Equal(2, radius[i].GetArrayLength());
        }
        Assert.Equal(400, json.GetProperty("duration").GetDouble());
    }

    [Fact]
    public void PointOnCircleRunsAlongTheCircle()
    {
        var c = new Construction();
        GeoCircle k = c.Circle("k", c.Point("M", 0, 0), 2);
        GeoPoint p = c.PointOn("P", k, 0);
        c.Step("glide");
        c.Move(p, 180, duration: 400, easing: Easing.Linear);
        JsonElement json = Json(c);
        JsonElement x = Track(json, 4, "x");
        JsonElement y = Track(json, 4, "y");
        // halfway at 90°: (0, 2), not on the chord – as the player interpolates the keys (linearly between samples)
        static double At(JsonElement track, double t)
        {
            JsonElement[] keys = track.EnumerateArray().ToArray();
            int i = Array.FindLastIndex(keys, k => k[0].GetDouble() <= t);
            if (i == keys.Length - 1)
            {
                return keys[i][1].GetDouble();
            }
            double t0 = keys[i][0].GetDouble(), t1 = keys[i + 1][0].GetDouble();
            return keys[i][1].GetDouble() + (keys[i + 1][1].GetDouble() - keys[i][1].GetDouble()) * (t - t0) / (t1 - t0);
        }
        Assert.Equal(0, At(x, 200), 2);
        Assert.Equal(2, At(y, 200), 2);
        // fewer keys than samples: those on a straight line are left out
        Assert.True(x.GetArrayLength() < 11, "keys " + x.GetArrayLength());
    }

    [Fact]
    public void FullTurnIsDistinctFromNullAngle()
    {
        var c = new Construction();
        GeoPoint s = c.Point("S", 0, 0);
        GeoPoint a = c.Point("A", 2, 0);
        GeoCircle k = c.Circle("k", s, 2);
        GeoPoint p = c.PointOn("P", k, 0);
        GeoAngle w = c.Angle("w", a, s, p);

        Assert.Equal(0, w.Degrees);

        c.Step("full turn");
        c.Move(p, 360, duration: 400, easing: Easing.Linear);
        JsonElement json = Json(c);

        Assert.Equal(360, w.Degrees);
        JsonElement sweep = json.GetProperty("primitives").EnumerateArray()
            .Single(p => p.GetProperty("type").GetString() == "angle")
            .GetProperty("tracks").GetProperty("sweep");
        Assert.Equal(0, sweep[0][1].GetDouble());
        Assert.Equal(360, sweep[sweep.GetArrayLength() - 1][1].GetDouble());
    }

    // the result depends on the movement, not on how the position on the circle is written
    [Theory]
    [InlineData(270, 630, 360)]
    [InlineData(-90, 270, 360)]
    [InlineData(-90, -450, 0)]
    [InlineData(-90, 630, 360)]
    [InlineData(270, 270, 0)]
    public void FullTurnFollowsTheMovement(double from, double to, double expected)
    {
        var c = new Construction();
        GeoPoint s = c.Point("S", 0, 0);
        GeoPoint a = c.Point("A", 0, -2);
        GeoCircle k = c.Circle("k", s, 2);
        GeoPoint p = c.PointOn("P", k, from);
        GeoAngle w = c.Angle("w", a, s, p);
        Assert.Equal(0, w.Degrees);
        c.Step("turn");
        c.Move(p, to, duration: 800, easing: Easing.Linear);
        c.Build();
        Assert.Equal(expected, w.Degrees);
    }

    // without a movement: the point is placed one turn on ("on k at 360")
    [Theory]
    [InlineData(0, 360, 360)]
    [InlineData(0, 0, 0)]
    [InlineData(270, 630, 360)]
    [InlineData(270, 270, 0)]
    [InlineData(270, -90, 0)]
    [InlineData(0, -360, 0)]
    public void FullAnglePlacedAtOnce(double firstArm, double second, double expected)
    {
        var c = new Construction();
        GeoPoint s = c.Point("S", 6.5, -2.2);
        double radians = firstArm * Math.PI / 180;
        GeoPoint a = c.Point("A", 6.5 + 2 * Math.Cos(radians), -2.2 + 2 * Math.Sin(radians));
        GeoCircle k = c.Circle("k", s, 2, new Options { Hidden = true });
        GeoPoint b = c.PointOn("B", k, second);
        GeoAngle w = c.Angle("w", a, s, b);
        Assert.Equal(expected, w.Degrees);
        JsonElement sweep = Json(c).GetProperty("primitives").EnumerateArray()
            .Single(p => p.GetProperty("type").GetString() == "angle")
            .GetProperty("tracks").GetProperty("sweep");
        Assert.Equal(expected, sweep[sweep.GetArrayLength() - 1][1].GetDouble());
    }

    [Fact]
    public void FullTurnOfTheFirstArm()
    {
        var c = new Construction();
        GeoPoint s = c.Point("S", 0, 0);
        GeoPoint a = c.Point("A", 2, 0);
        GeoPoint p = c.Point("P", 2, 0);
        GeoAngle w = c.Angle("w", a, s, p);
        c.Step("first arm clockwise once round");
        c.Rotate([a], s, -360, duration: 800, easing: Easing.Linear);
        Assert.Equal(360, w.Degrees);
        c.Step("and back");
        c.Rotate([a], s, 360, duration: 800, easing: Easing.Linear);
        c.Build();
        Assert.Equal(0, w.Degrees);
    }

    [Fact]
    public void HideFadesOut()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0, new Options { NoLabel = true });
        c.Step("hide");
        c.Hide([a], duration: 300);
        JsonElement json = Json(c);
        Assert.Equal("[[0,1,\"s\"],[300,0,\"o\"]]", Keys(Track(json, 1, "opacity")));
    }

    [Fact]
    public void HiddenObjectIsDrawnWhenShown()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0, new Options { Hidden = true, NoLabel = true });
        c.Step("show");
        c.Show([a], duration: 200);
        JsonElement json = Json(c);
        Assert.Equal("[[0,0,\"s\"],[200,5,\"o\"]]", Keys(Track(json, 1, "r")));
    }

    [Fact]
    public void Mistakes()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0);
        GeoPoint b = c.Point("B", 1, 0);
        Assert.Equal("The name A is already used.", Assert.Throws<GeoException>(() => c.Point("A", 1, 1)).Message);
        Assert.Throws<GeoException>(() => c.View = new ViewWindow(0, 1, 0, 1));
        Assert.Equal("\"pause\" is only possible inside a step.", Assert.Throws<GeoException>(() => c.Pause(100)).Message);
        GeoPoint m = c.Midpoint("M", a, b);
        c.Step("x");
        Assert.Contains("constructed from other objects", Assert.Throws<GeoException>(() => c.Move(m, new Vec(1, 1))).Message);
        Assert.Throws<GeoException>(() => c.Segment("s", a, a));
    }

    [Fact]
    public void JsonEscapesForScriptElements()
    {
        var c = new Construction { Title = "</script><b>&" };
        string json = c.Build().ToJson();
        Assert.DoesNotContain("</script", json);
        Assert.DoesNotContain("<b>", json);
        Assert.Equal("</script><b>&", JsonDocument.Parse(json).RootElement.GetProperty("title").GetString());
    }
}
