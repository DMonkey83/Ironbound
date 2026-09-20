using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Magic;

/// <summary>What a spell did to one creature.</summary>
public sealed record SpellTargetResult(
    Creature Target,
    AttackResult? Attack,
    SavingThrowResult? Save,
    int Damage,
    int Healed,
    IReadOnlyList<string> Applied)
{
    public bool Missed => Attack is { IsHit: false };

    public override string ToString()
    {
        if (Missed)
        {
            return $"{Target.Name}: {Attack}";
        }

        var parts = new List<string>();

        if (Attack is not null)
        {
            parts.Add(Attack.ToString());
        }

        if (Save is not null)
        {
            parts.Add(Save.ToString());
        }

        if (Damage > 0)
        {
            parts.Add($"{Damage} damage");
        }

        if (Healed > 0)
        {
            parts.Add($"healed {Healed}");
        }

        parts.AddRange(Applied);

        return $"{Target.Name}: {string.Join("; ", parts)} ({Target.HitPoints})";
    }
}

/// <summary>One spell, cast.</summary>
public sealed record SpellCast(
    Creature Caster,
    Spell Spell,
    SpellAim Aim,
    int DifficultyClass,
    IReadOnlyList<SpellTargetResult> Targets)
{
    public override string ToString()
    {
        var header = $"{Caster.Name} casts {Spell.Name} at {Aim}";
        return Targets.Count == 0
            ? $"{header} — nothing in range"
            : $"{header} (DC {DifficultyClass})";
    }
}

/// <summary>
/// Turns a written-down spell into things happening to creatures.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about turns or encounters: it takes a caster, a spell, somewhere to
/// point it, and the dice. Casting provoking an attack of opportunity belongs to the action that
/// wraps this, not here.
/// </remarks>
public static class Casting
{
    public static SpellCast Resolve(
        Creature caster,
        Spell spell,
        SpellAim aim,
        IRandomSource random,
        RuleOptions? rules = null,
        Battlefield? field = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(random);
        rules ??= caster.Rules;

        var difficultyClass = caster.Spells.SaveDC(spell);
        var results = new List<SpellTargetResult>();

        // An area spell rolls its dice once and everyone in it saves against the same number;
        // rolling per victim would quietly make a fireball far swingier than the rules intend.
        // A ray rolls lazily instead, so a miss costs no damage dice at all.
        var shared = spell.Target is BurstTarget ? PreRoll(caster, spell, random) : null;

        foreach (var target in Gather(caster, spell, aim, field))
        {
            results.Add(Affect(caster, spell, target, difficultyClass, shared, random, rules));
        }

        return new SpellCast(caster, spell, aim, difficultyClass, results);
    }

    private static int[] PreRoll(Creature caster, Spell spell, IRandomSource random)
    {
        var level = caster.Spells.CasterLevel;

        return [.. spell.Does.Select(effect => effect switch
        {
            DealDamage hurt => hurt.Amount.At(level).Roll(random).Total,
            Restore mend => mend.Amount.At(level).Roll(random).Total,
            _ => 0,
        })];
    }

    /// <summary>Everyone the spell lands on, in a stable order.</summary>
    public static IReadOnlyList<Creature> Gather(
        Creature caster,
        Spell spell,
        SpellAim aim,
        Battlefield? field)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);

        IReadOnlyList<Creature> candidates = spell.Target switch
        {
            SelfTarget => [caster],
            BurstTarget burst => InBurst(caster, aim, burst, field),
            _ => aim.Creature is { } one ? [one] : [],
        };

        return [.. candidates.Where(target => Touches(caster, spell.Affects, target))];
    }

    private static IReadOnlyList<Creature> InBurst(
        Creature caster,
        SpellAim aim,
        BurstTarget burst,
        Battlefield? field)
    {
        if (field is null)
        {
            return [];
        }

        var centre = aim.Point ?? (aim.Creature is { } at ? field.SquareOf(at) : field.SquareOf(caster));
        return centre is { } middle ? field.CreaturesWithin(middle, burst.RadiusFeet) : [];
    }

    private static bool Touches(Creature caster, SpellAffects affects, Creature target) => affects switch
    {
        SpellAffects.Everyone => true,
        SpellAffects.Enemies => caster.IsEnemyOf(target),
        SpellAffects.Allies => ReferenceEquals(caster, target) || caster.IsAllyOf(target),
        _ => ReferenceEquals(caster, target),
    };

    private static SpellTargetResult Affect(
        Creature caster,
        Spell spell,
        Creature target,
        int difficultyClass,
        IReadOnlyList<int>? shared,
        IRandomSource random,
        RuleOptions rules)
    {
        AttackResult? attack = null;

        if (spell.Target is RayTarget)
        {
            attack = ShootRay(caster, spell, target, random, rules);
            if (!attack.IsHit)
            {
                return new SpellTargetResult(target, attack, null, 0, 0, []);
            }
        }

        SavingThrowResult? save = null;
        if (spell.Save is { } which)
        {
            save = target.Saves.Attempt(which, difficultyClass, random, rules);
            if (save.Succeeded && spell.OnSave == SaveOutcome.Negates)
            {
                return new SpellTargetResult(target, attack, save, 0, 0, []);
            }
        }

        var halved = save is { Succeeded: true } && spell.OnSave == SaveOutcome.Half;
        var damage = 0;
        var healed = 0;
        var applied = new List<string>();

        var level = caster.Spells.CasterLevel;

        for (var i = 0; i < spell.Does.Count; i++)
        {
            switch (spell.Does[i])
            {
                case DealDamage hurt:
                    var rolled = shared?[i] ?? hurt.Amount.At(level).Roll(random).Total;
                    damage += Hurt(target, hurt, rolled, halved, random, rules);
                    break;

                case Restore mend:
                    healed += target.HitPoints.Heal(
                        shared?[i] ?? mend.Amount.At(level).Roll(random).Total);
                    break;

                case Bestow bestow:
                    target.Effects.Apply(bestow.Effect.Build(level));
                    applied.Add($"gains {bestow.Name}");
                    break;
            }
        }

        return new SpellTargetResult(target, attack, save, damage, healed, applied);
    }

    /// <summary>
    /// A ranged touch attack. The battlefield is deliberately withheld from the bonus: flanking
    /// is a melee affair, and a ray fired from across the room should not benefit from it.
    /// </summary>
    private static AttackResult ShootRay(
        Creature caster,
        Spell spell,
        Creature target,
        IRandomSource random,
        RuleOptions rules)
    {
        var ray = new WeaponAttack(spell.Name, new Attack { TargetsTouchArmorClass = true }, new DamagePacket())
        {
            AttackAbility = Ability.Dexterity,
        };

        return ray.Attack.Resolve(
            target.ArmorClass,
            random,
            Strike.AttackBonus(caster, ray, target, field: null),
            DefenseOptions.None,
            rules);
    }

    private static int Hurt(
        Creature target,
        DealDamage hurt,
        int rolled,
        bool halved,
        IRandomSource random,
        RuleOptions rules)
    {
        var amount = halved ? rolled / 2 : rolled;

        if (amount <= 0)
        {
            return 0;
        }

        // Halving happens first and resistance second, which is the order the rules give and the
        // reason this is rebuilt as a flat packet rather than mitigated straight off the roll.
        var packet = DamagePacket.Weapon(amount.ToString(), hurt.Type);
        var taken = target.Defenses.Apply(packet.Roll(random), DamageBypass.None, rules);
        target.HitPoints.Take(taken.Total);

        return taken.Total;
    }
}
