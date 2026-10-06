using System.Globalization;
using AionMeter.Core.Combat;

namespace AionMeter.Tests;

public class SkillOrderTests
{
    private static SkillRow Row(int code, string name, long damage, int hits, int dots = 0, long max = 0, double? crit = null) =>
        new(code, name, hits, dots, damage, damage / 100.0, damage / 1_000_000.0, damage / Math.Max(1, hits + dots), 0, max,
            crit, null, null, null, null, null);

    private static readonly SkillRow[] Skills =
    [
        Row(1, "Incandescent Blow", 572_700, 121, max: 14_390, crit: 0.157),
        Row(2, "Bursting Blow", 566_700, 58, max: 19_760, crit: 0.103),
        Row(3, "Piercing Strike", 208_100, 11, max: 20_310, crit: 1.0),
        Row(4, "Curse", 61_000, 0, dots: 38), // DoT only: no crit to measure
        Row(5, "Arc Lash", 41_000, 9, max: 20_310, crit: 0.5),
    ];

    private static int[] Codes(SkillOrder order) =>
        order.Apply(Skills, CultureInfo.InvariantCulture).Select(s => s.SkillCode).ToArray();

    [Fact]
    public void Default_is_biggest_damage_first_and_a_second_click_reverses()
    {
        Assert.Equal([1, 2, 3, 4, 5], Codes(SkillOrder.Default));
        Assert.Equal([5, 4, 3, 2, 1], Codes(SkillOrder.Default.Toggle(SkillColumn.Damage)));
    }

    [Fact]
    public void Rate_with_nothing_measured_stays_last_either_way()
    {
        var crit = SkillOrder.Default.Toggle(SkillColumn.Crit);
        Assert.True(crit.Descending);
        Assert.Equal([3, 5, 1, 2, 4], Codes(crit));
        Assert.Equal([2, 1, 5, 3, 4], Codes(crit.Toggle(SkillColumn.Crit)));
    }

    [Fact]
    public void Names_start_a_to_z()
    {
        var name = SkillOrder.Default.Toggle(SkillColumn.Name);
        Assert.False(name.Descending);
        Assert.Equal([5, 2, 4, 1, 3], Codes(name));
        Assert.Equal([3, 1, 4, 2, 5], Codes(name.Toggle(SkillColumn.Name)));
    }

    [Fact]
    public void Hits_count_dot_ticks_and_ties_keep_the_bigger_damage_first()
    {
        Assert.Equal([1, 2, 4, 3, 5], Codes(new SkillOrder(SkillColumn.Hits, true)));
        // Piercing Strike and Arc Lash share the biggest hit: the one with more damage leads.
        Assert.Equal([3, 5, 2, 1, 4], Codes(new SkillOrder(SkillColumn.Max, true)));
    }
}
