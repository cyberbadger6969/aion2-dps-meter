using System.Globalization;

namespace AionMeter.Core.Combat;

/// <summary>A column of the breakdown's skill tables.</summary>
public enum SkillColumn
{
    Name,
    Hits,
    Damage,
    Dps,
    Average,
    Max,
    Crit,
    Share,
    Back,
    Front,
    Perfect,
    Heavy,
    Multi,
}

/// <summary>The order of a skill table: one column, either way. Names start A→Z, numbers biggest first.</summary>
public readonly record struct SkillOrder(SkillColumn Column, bool Descending)
{
    public static SkillOrder Default { get; } = new(SkillColumn.Damage, true);

    /// <summary>A click on a column header: the same column turns around, another one starts in its natural order.</summary>
    public SkillOrder Toggle(SkillColumn column) =>
        column == Column ? this with { Descending = !Descending } : new(column, column != SkillColumn.Name);

    /// <summary>
    /// The rows in this order. A rate with nothing to measure ("—", e.g. crit of a DoT-only skill) stays at the bottom
    /// whichever way the column runs; ties keep the bigger damage first.
    /// </summary>
    public IReadOnlyList<SkillRow> Apply(IEnumerable<SkillRow> rows, CultureInfo? culture = null)
    {
        var names = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        var order = this;
        var list = rows.ToList();
        list.Sort((a, b) =>
        {
            int result;
            if (order.Column == SkillColumn.Name)
            {
                result = names.Compare(a.Name, b.Name, CompareOptions.IgnoreCase);
                if (order.Descending) result = -result;
            }
            else if (order.Key(a) is not { } x || order.Key(b) is not { } y)
            {
                result = (order.Key(a) is null).CompareTo(order.Key(b) is null); // nothing measured: last
            }
            else
            {
                result = order.Descending ? y.CompareTo(x) : x.CompareTo(y);
            }
            if (result == 0) result = b.Damage.CompareTo(a.Damage);
            return result != 0 ? result : a.SkillCode.CompareTo(b.SkillCode);
        });
        return list;
    }

    private double? Key(SkillRow s) => Column switch
    {
        SkillColumn.Hits => s.Hits + s.DotTicks,
        SkillColumn.Dps => s.Dps,
        SkillColumn.Average => s.Average,
        SkillColumn.Max => s.Max,
        SkillColumn.Crit => s.CritRate,
        SkillColumn.Share => s.Share,
        SkillColumn.Back => s.BackRate,
        SkillColumn.Front => s.FrontRate,
        SkillColumn.Perfect => s.PerfectRate,
        SkillColumn.Heavy => s.HeavyRate,
        SkillColumn.Multi => s.MultiRate,
        _ => s.Damage,
    };
}
