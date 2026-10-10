using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Conditions;

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
    /// <summary>"casts" for a spell, "uses" for a power — the only word the log changes.</summary>
    public string Verb { get; init; } = "casts";

    public override string ToString()
    {
        var name = $"{Spell.MetamagicWords}{Spell.Name}";
        var header = $"{Caster.Name} {Verb} {name} at {Aim}";

        // A difficulty class is only worth printing for something that can be saved against;
        // on a magic missile or a touch of battle rage it read as a number nobody rolls against.
        return Targets.Count == 0
            ? $"{header} — nothing in range"
            : Spell.Save is null ? header : $"{header} (DC {DifficultyClass})";
    }
}

/// <summary>
/// How a spell-shaped effect is being let loose: as a spell from a slot, or as a power with
/// numbers of its own.
/// </summary>
/// <remarks>
/// A spell takes its caster level and difficulty class from the caster's spellcasting. Channel
/// energy does not — ten plus half the cleric's level plus Charisma — and neither does any other
/// power, so whatever lets one loose says what they are.
/// </remarks>
public sealed record Invocation
{
    /// <summary>An ordinary spell from an ordinary slot.</summary>
    public static Invocation Spell { get; } = new();

    /// <summary>Null to use the caster's own.</summary>
    public int? CasterLevel { get; init; }

    /// <summary>Null to use the spell's own difficulty class.</summary>
    public int? DifficultyClass { get; init; }

    public string Verb { get; init; } = "casts";

    /// <summary>
    /// Whether it is a real spell, which is what school powers such as intense spells care
    /// about. A power that already counts the bonus in its own dice says false.
    /// </summary>
    public bool IsSpell { get; init; } = true;

    /// <summary>Whoever a selective channel leaves out.</summary>
    public IReadOnlyCollection<Creature> Excluded { get; init; } = [];

    /// <summary>
    /// Whether the magic is the caster's own, so that her own powers shape it: a Healing
    /// cleric's blessing on a cure, an evoker's intense spells. False for a potion, which was
    /// made by somebody else and does what it was made to do whoever drinks it.
    /// </summary>
    public bool OwnMagic { get; init; } = true;

    /// <summary>How a power is let loose: its own level and DC, and "uses".</summary>
    public static Invocation Of(Power power)
    {
        ArgumentNullException.ThrowIfNull(power);

        return power.Use == PowerUse.Spell
            ? new Invocation { Verb = "casts", IsSpell = true }
            : new Invocation
            {
                CasterLevel = power.CasterLevel,
                DifficultyClass = power.DifficultyClass,
                Verb = "uses",
                IsSpell = false,
            };
    }

    /// <summary>
    /// How a potion or an oil is let loose: at the level it was brewed at, with the save a potion
    /// has, and none of the drinker's own powers. Still a spell, as far as anything that asks is
    /// concerned.
    /// </summary>
    /// <param name="verb">"drinks", "applies": the word the log uses.</param>
    public static Invocation FromItem(ConsumableDefinition use, string verb)
    {
        ArgumentNullException.ThrowIfNull(use);
        ArgumentNullException.ThrowIfNull(verb);

        return new Invocation
        {
            CasterLevel = use.CasterLevel,
            DifficultyClass = use.DifficultyClass,
            Verb = verb,
            IsSpell = true,
            OwnMagic = false,
        };
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
    /// <summary>Half as much again, rounded down: Empower Spell, and Healer's Blessing on a cure.</summary>
    public static int Empowered(int rolled) => rolled + (rolled / 2);

    /// <summary>
    /// What a spell's dice come to with its metamagic: maximized, every die at its top; empowered,
    /// half as much again; both, the maximum plus half of what was actually rolled, which is how
    /// the book adds the two together. The dice are rolled either way, so the stream does not
    /// care which feats were used.
    /// </summary>
    private static int Rolled(Spell spell, Dice.DiceExpression dice, IRandomSource random, bool empowered)
    {
        var rolled = dice.Roll(random).Total;

        if (!spell.Metamagic.HasFlag(Metamagic.Maximize))
        {
            return empowered ? Empowered(rolled) : rolled;
        }

        return empowered ? dice.Maximum + (rolled / 2) : dice.Maximum;
    }

    public static SpellCast Resolve(
        Creature caster,
        Spell spell,
        SpellAim aim,
        IRandomSource random,
        RuleOptions? rules = null,
        Battlefield? field = null,
        Invocation? how = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(random);
        rules ??= caster.Rules;
        how ??= Invocation.Spell;

        var difficultyClass = how.DifficultyClass ?? caster.Spells.SaveDC(spell);
        var level = how.CasterLevel ?? caster.Spells.CasterLevel;
        var results = new List<SpellTargetResult>();

        // An area spell rolls its dice once and everyone in it saves against the same number;
        // rolling per victim would quietly make a fireball far swingier than the rules intend.
        // A ray rolls lazily instead, so a miss costs no damage dice at all.
        var shared = IsArea(spell) ? PreRoll(caster, spell, level, how, random) : null;

        // Anybody none of its effects is for — the living, to a channel that only burns the
        // undead — is not touched by it at all, and is not in the log either.
        foreach (var target in Gather(caster, spell, aim, field).Except(how.Excluded)
            .Where(target => spell.Does.Count == 0 || spell.Does.Any(effect => effect.AppliesTo(target))))
        {
            results.Add(Affect(caster, spell, target, difficultyClass, level, how, shared, random, rules));
        }

        return new SpellCast(caster, spell, aim, difficultyClass, results) { Verb = how.Verb };
    }

    private static bool IsArea(Spell spell) =>
        spell.Target is BurstTarget or SelfTarget { RadiusFeet: > 0 };

    /// <remarks>
    /// Effects meant for different creatures that roll the same dice share one roll: a channel
    /// heals the living and burns the undead by the same number, as the book rolls it once.
    /// </remarks>
    private static int[] PreRoll(Creature caster, Spell spell, int level, Invocation how, IRandomSource random)
    {
        var rolls = new int[spell.Does.Count];

        for (var i = 0; i < spell.Does.Count; i++)
        {
            var twin = spell.Does[i].Only is null ? -1 : Enumerable.Range(0, i).FirstOrDefault(
                earlier => spell.Does[earlier].Only is not null && Dice(spell.Does[earlier]) is { } dice && dice == Dice(spell.Does[i]),
                -1);

            rolls[i] = twin >= 0 ? rolls[twin] : Roll(caster, spell, spell.Does[i], i, level, how, random);
        }

        return rolls;
    }

    private static SpellDice? Dice(SpellEffect effect) => effect switch
    {
        DealDamage hurt => hurt.Amount,
        Restore mend => mend.Amount,
        Bolster shore => shore.Amount,
        _ => null,
    };

    /// <summary>
    /// One effect's number, with everything that changes it: empowered, a blessed cure, and the
    /// evocation school's intense spells — which is added after the empowering, and only to the
    /// first thing a spell rolls for damage, because the rule says once a spell.
    /// </summary>
    private static int Roll(
        Creature caster, Spell spell, SpellEffect effect, int index, int level, Invocation how, IRandomSource random)
    {
        switch (effect)
        {
            case DealDamage hurt:
            {
                var rolled = Rolled(spell, hurt.Amount.At(level), random, spell.Empowered);

                var first = spell.Does.Take(index).All(earlier => earlier is not DealDamage);
                if (how.IsSpell && how.OwnMagic && first && spell.School == SpellSchool.Evocation)
                {
                    rolled += ClassPowers.IntenseBonus(caster);
                }

                return rolled;
            }

            case Restore mend:
            {
                var blessed = how.OwnMagic && spell.Has("cure")
                    && ClassPowers.HasDomainPower(caster, GrantedPowerEffect.HealersBlessing);
                return Rolled(spell, mend.Amount.At(level), random, spell.Empowered || blessed);
            }

            case Bolster shore:
                return Rolled(spell, shore.Amount.At(level), random, spell.Empowered);

            default:
                return 0;
        }
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
            SelfTarget { RadiusFeet: > 0 } around => Around(caster, around.RadiusFeet, field),
            SelfTarget => [caster],
            PointTarget => [],
            BurstTarget burst => InBurst(caster, aim, burst with { RadiusFeet = RadiusOf(spell, burst) }, field),
            _ => aim.Creature is { } one ? [one] : [],
        };

        return [.. candidates.Where(target => Touches(caster, spell.Affects, target))];
    }

    /// <summary>
    /// A burst centred on the caster. Without any ground there is nobody else to catch, so it
    /// is the caster alone — which keeps a channel worth something in a fight with no map.
    /// </summary>
    private static IReadOnlyList<Creature> Around(Creature caster, int radius, Battlefield? field) =>
        field?.SquareOf(caster) is { } middle ? field.CreaturesWithin(middle, radius) : [caster];

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

    /// <summary>A burst's radius as cast: twice as far widened.</summary>
    public static int RadiusOf(Spell spell, BurstTarget burst)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(burst);
        return spell.Metamagic.HasFlag(Metamagic.Widen) ? burst.RadiusFeet * 2 : burst.RadiusFeet;
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
        int level,
        Invocation how,
        IReadOnlyList<int>? shared,
        IRandomSource random,
        RuleOptions rules)
    {
        AttackResult? attack = null;

        // An elf does not fall asleep however the spell is aimed or whether she saves.
        if (target.Race is { } race && race.IsImmuneTo(spell))
        {
            return new SpellTargetResult(target, null, null, 0, 0, [$"immune ({race.Name.ToLowerInvariant()})"]);
        }

        if (spell.Target is RayTarget)
        {
            attack = ShootRay(caster, spell, target, random, rules);
            if (!attack.IsHit)
            {
                return new SpellTargetResult(target, attack, null, 0, 0, []);
            }
        }

        // Spell resistance, against a spell and not a power, and only from a foe: a friend lets
        // a cure through, as the book lets anybody lower it for a spell they want.
        if (how.IsSpell && spell.AllowsResistance && target.SpellResistance > 0 && caster.IsEnemyOf(target)
            && Resistance.Check(caster, target, level, random) is { Succeeded: false } resisted)
        {
            return new SpellTargetResult(target, attack, null, 0, 0, [resisted.ToString()]);
        }

        SavingThrowResult? save = null;

        // A save only against something worth saving against: a channel that heals this target
        // and harms only others is accepted, not resisted.
        if (spell.Save is { } which && spell.Does.Any(effect => effect.IsHarmful && effect.AppliesTo(target)))
        {
            save = SaveRerolls.Attempt(
                target, which, difficultyClass, random, rules, ClassSaves.Against(target, spell, which),
                serious: Serious(caster, spell, target, level, shared));

            if (save.Succeeded && spell.OnSave == SaveOutcome.Negates)
            {
                return new SpellTargetResult(target, attack, save, 0, 0, []);
            }
        }

        var damage = 0;
        var healed = 0;
        var applied = new List<string>();

        for (var i = 0; i < spell.Does.Count; i++)
        {
            if (!spell.Does[i].AppliesTo(target))
            {
                continue;
            }

            switch (spell.Does[i])
            {
                case DealDamage hurt:
                    var rolled = shared?[i] ?? Roll(caster, spell, hurt, i, level, how, random);
                    damage += Hurt(target, hurt, Mitigate(target, spell, save, rolled), random, rules);
                    break;

                case Restore mend:
                    var restored = target.HitPoints.Heal(shared?[i] ?? Roll(caster, spell, mend, i, level, how, random));
                    healed += restored;

                    if (restored > 0)
                    {
                        Bleeding.Healed(target);
                    }

                    // Magical healing closes a bleeding wound as well as the hit points — any
                    // of them: a rogue's, a critical's, a deadly stroke's.
                    if (restored > 0 && Bleeds.Stop(target))
                    {
                        applied.Add("stops bleeding");
                    }

                    break;

                case Bolster shore:
                    var extra = shared?[i] ?? Roll(caster, spell, shore, i, level, how, random);
                    target.HitPoints.GrantTemporary(extra);
                    applied.Add($"gains {extra} temporary hit points");
                    break;

                case Bestow bestow:
                    // Extend Spell: twice as long, for anything the spell hangs on somebody.
                    target.Effects.Apply(bestow.Effect.Build(level, spell.Metamagic.HasFlag(Metamagic.Extend) ? 2 : 1));
                    applied.Add($"gains {bestow.Name}");
                    break;
            }
        }

        return new SpellTargetResult(target, attack, save, damage, healed, applied);
    }

    /// <summary>
    /// Whether failing this save would put the target down or out of the fight — the failure
    /// worth a once-a-day reroll. Damage that would take it to nought or below, or a condition
    /// that takes its turns or its feet away.
    /// </summary>
    /// <remarks>
    /// An area spell's damage is already rolled and is known; anything else is judged on its
    /// average, since rolling it early would move the dice for everybody without the feat.
    /// </remarks>
    private static bool Serious(Creature caster, Spell spell, Creature target, int level, IReadOnlyList<int>? shared)
    {
        var damage = 0;

        for (var i = 0; i < spell.Does.Count; i++)
        {
            var effect = spell.Does[i];
            if (!effect.AppliesTo(target))
            {
                continue;
            }

            switch (effect)
            {
                case DealDamage hurt:
                    damage += shared?[i] ?? (int)Math.Ceiling(hurt.Amount.At(level).Average);
                    break;

                case Bestow { Effect.Condition: { } condition }
                    when ConditionInfo.Of(condition).DeniesActions
                        || ConditionInfo.Of(condition).SingleAction
                        || condition is Condition.Prone or Condition.Frightened or Condition.Blinded:
                    return true;
            }
        }

        return damage > 0 && damage >= target.HitPoints.Current;
    }

    /// <summary>
    /// What a save leaves of a damage roll. Half on a success for a half-save spell, as written —
    /// unless it was a Reflex save and the target has evasion, which turns half into nothing.
    /// </summary>
    private static int Mitigate(Creature target, Spell spell, SavingThrowResult? save, int rolled)
    {
        if (save is null || spell.OnSave != SaveOutcome.Half)
        {
            return rolled;
        }

        return RogueDefences.Evade(target, save, rolled);
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
        int amount,
        IRandomSource random,
        RuleOptions rules) =>
        Hurt(target, amount, hurt.Type, random, rules);

    /// <summary>
    /// Deals an amount already rolled, of one type, through the target's defences, and returns
    /// what it took: a spell's damage after its save, a flask's splash. Resistance and immunity
    /// count; damage reduction is only ever against physical damage, so it does not.
    /// </summary>
    /// <remarks>
    /// Halving happens first and resistance second, which is the order the rules give and the
    /// reason this is rebuilt as a flat packet rather than mitigated straight off the roll.
    /// </remarks>
    public static int Hurt(
        Creature target,
        int amount,
        DamageType type,
        IRandomSource random,
        RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        if (amount <= 0)
        {
            return 0;
        }

        var packet = DamagePacket.Weapon(amount.ToString(System.Globalization.CultureInfo.InvariantCulture), type);
        var taken = target.Defenses.Apply(packet.Roll(random), DamageBypass.None, rules ?? target.Rules);
        target.HitPoints.Take(taken.Total);

        return taken.Total;
    }
}
