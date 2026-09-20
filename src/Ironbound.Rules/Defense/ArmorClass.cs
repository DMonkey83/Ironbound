using Ironbound.Rules.Abilities;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Defense;

/// <summary>
/// How hard a creature is to hit: 10, plus Dexterity, plus a <see cref="ModifierStack"/> of
/// everything else. Dexterity is held separately rather than pushed into the stack because
/// only Dexterity is subject to an armour cap and only Dexterity is denied while flat-footed;
/// a plain modifier could not express either rule.
/// </summary>
/// <remarks>
/// Magic armour folds into its armour bonus — a +1 breastplate is <c>armor 7</c>, not
/// <c>armor 6 + enhancement 1</c>. Otherwise a touch attack would wrongly keep the +1.
/// </remarks>
public sealed class ArmorClass(AbilityScore dexterity)
{
    /// <summary>Everyone starts here.</summary>
    public const int BaseValue = 10;

    private readonly Dictionary<string, int> _dexterityCaps = new(StringComparer.Ordinal);

    public AbilityScore Dexterity { get; } = dexterity ?? throw new ArgumentNullException(nameof(dexterity));

    /// <summary>Armour, shield, natural armour, deflection, dodge, and anything else.</summary>
    public ModifierStack Modifiers { get; } = new();

    /// <summary>
    /// The creature's size modifier, kept apart from <see cref="Modifiers"/> on purpose. Size is
    /// not a bonus granted by a source that could expire; it is what the creature <em>is</em>, so
    /// it is derived rather than stored — which also means nothing has to reach into the stack to
    /// refresh it when Enlarge Person lands.
    /// </summary>
    public int SizeModifier { get; set; }

    /// <summary>
    /// Lowest Dexterity bonus any worn item allows, or null when nothing caps it. Caps are
    /// held per source so that taking off a tower shield restores the breastplate's limit
    /// instead of removing the cap entirely.
    /// </summary>
    public int? MaxDexterityBonus => _dexterityCaps.Count == 0 ? null : _dexterityCaps.Values.Min();

    public void CapDexterity(string source, int maximum)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        _dexterityCaps[source] = maximum;
    }

    public bool RemoveDexterityCap(string source) => _dexterityCaps.Remove(source);

    /// <summary>AC against an ordinary attack.</summary>
    public int Total => Value(DefenseOptions.None);

    /// <summary>AC against a ray or other touch attack.</summary>
    public int Touch => Value(DefenseOptions.TouchAttack);

    /// <summary>AC while unable to react.</summary>
    public int FlatFooted => Value(DefenseOptions.DexterityDenied);

    public int Value(DefenseOptions options = DefenseOptions.None) =>
        BaseValue + SizeModifier + DexterityContribution(options)
        + Modifiers.TotalWhere(m => Applies(m, options));

    /// <summary>
    /// The full sum with the base and Dexterity written in as entries, so the log line
    /// accounts for the whole number rather than just the modifiers.
    /// </summary>
    public ModifierBreakdown Explain(DefenseOptions options = DefenseOptions.None)
    {
        var stack = new ModifierStack();
        stack.Add(Modifier.Untyped(BaseValue, "Base"));

        if (SizeModifier != 0)
        {
            stack.Add(new Modifier(SizeModifier, BonusType.Size, "Size"));
        }

        var dexterity = DexterityContribution(options);
        if (dexterity != 0)
        {
            var cap = MaxDexterityBonus;
            var capped = cap is not null && Dexterity.Modifier > cap.Value && dexterity > 0;
            stack.Add(Modifier.Untyped(dexterity, capped ? $"Dexterity (capped at +{cap})" : "Dexterity"));
        }

        foreach (var modifier in Modifiers.Modifiers)
        {
            if (Applies(modifier, options))
            {
                stack.Add(modifier);
            }
        }

        return stack.Explain();
    }

    private int DexterityContribution(DefenseOptions options)
    {
        var dexterity = Dexterity.Modifier;

        // A penalty is not a benefit you can be denied, and no armour caps how clumsy you are.
        if (dexterity <= 0)
        {
            return dexterity;
        }

        if ((options & DefenseOptions.DexterityDenied) != 0)
        {
            return 0;
        }

        return MaxDexterityBonus is { } cap ? Math.Min(dexterity, cap) : dexterity;
    }

    private static bool Applies(Modifier modifier, DefenseOptions options)
    {
        if ((options & DefenseOptions.TouchAttack) != 0
            && modifier.Type is BonusType.Armor or BonusType.Shield or BonusType.NaturalArmor)
        {
            return false;
        }

        // Dodge depends on moving, so it goes when Dexterity does. Penalties stay.
        if ((options & DefenseOptions.DexterityDenied) != 0
            && modifier.Type == BonusType.Dodge
            && !modifier.IsPenalty)
        {
            return false;
        }

        return true;
    }

    /// <summary>The stat-block line: "AC 19, touch 13, flat-footed 16".</summary>
    public override string ToString() => $"AC {Total}, touch {Touch}, flat-footed {FlatFooted}";
}
