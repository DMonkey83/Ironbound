using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// Turns a creature's class features into the <see cref="Power"/>s it can use, and says how
/// many times a day each pool allows.
/// </summary>
/// <remarks>
/// Every power is rebuilt from the creature each time it is asked for. The dice of a channel
/// grow with the cleric and the bonus of a force missile with the wizard, and the cheapest way
/// to never show a stale number is to never keep one.
/// </remarks>
public static class ClassPowers
{
    public const string ChannelPool = "channel-energy";

    public const string ArcaneBondPool = "arcane-bond";

    public const string ResiliencyPool = "resiliency";

    public const string DefensiveRollPool = "defensive-roll";

    /// <summary>How far a channel reaches around the cleric.</summary>
    public const int ChannelRadius = 30;

    /// <summary>The name the guarded stance's effect is filed under.</summary>
    public const string GuardedStanceLabel = "Guarded Stance";

    /// <summary>The name a borrowed Weapon Master feat's effect is filed under.</summary>
    public const string WeaponMasterLabel = "Weapon Master";

    /// <summary>Everything this creature can use, in a stable order: class by class, then table order.</summary>
    public static IReadOnlyList<Power> For(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var powers = new List<Power>();

        if (Channel(creature) is { } channel)
        {
            powers.Add(channel);
        }

        powers.AddRange(ChannelFeats(creature));

        foreach (var domain in creature.Choices.Domains)
        {
            foreach (var granted in domain.Powers)
            {
                if (ClassFeatures.LevelOf(creature, FeatureIds.Domains) >= granted.Level
                    && Granted(creature, granted.Effect) is { } power)
                {
                    powers.Add(power);
                }
            }
        }

        if (creature.Choices.School is { } school && ClassFeatures.Has(creature, FeatureIds.ArcaneSchool))
        {
            foreach (var granted in school.Powers)
            {
                if (ClassFeatures.LevelOf(creature, FeatureIds.ArcaneSchool) >= granted.Level
                    && Granted(creature, granted.Effect) is { } power)
                {
                    powers.Add(power);
                }
            }
        }

        foreach (var talent in creature.Choices.Talents)
        {
            if (FromTalent(creature, talent) is { } power)
            {
                powers.Add(power);
            }
        }

        powers.AddRange(Bonded(creature));

        return powers;
    }

    /// <summary>
    /// How many uses a pool holds each day, before anything is spent. Rage counts rounds and
    /// dimensional steps counts feet; everything else counts uses.
    /// </summary>
    public static int PerDay(Creature creature, string pool)
    {
        ArgumentNullException.ThrowIfNull(creature);

        int Plus3(Ability ability) => Math.Max(0, 3 + creature.Abilities[ability].Modifier);

        return pool switch
        {
            ChannelPool => ClassFeatures.Has(creature, FeatureIds.ChannelEnergy)
                ? Plus3(Ability.Charisma) + (ExtraChannels * creature.Feats.Count(feat => feat.Effect == FeatEffect.ExtraChannel))
                : 0,
            "rebuke-death" or "battle-rage" => Plus3(Ability.Wisdom),
            "weapon-master" => ClassFeatures.LevelOf(creature, FeatureIds.Domains),
            "force-missile" or "acid-dart" => Plus3(Ability.Intelligence),
            "dimensional-steps" => 30 * ClassFeatures.LevelOf(creature, FeatureIds.ArcaneSchool),
            ArcaneBondPool => creature.Choices.BondedObject && ClassFeatures.Has(creature, FeatureIds.ArcaneBond) ? 1 : 0,
            "renewed-vigor" or ResiliencyPool or DefensiveRollPool => 1,
            Rage.Pool => Rage.RoundsPerDay(creature),
            _ when SaveRerolls.IsPool(pool) => SaveRerolls.PerDay(creature, pool),
            _ => 0,
        };
    }

    /// <summary>What is left in a pool today.</summary>
    public static int Left(Creature creature, string pool)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return pool == Rage.Pool
            ? Rage.RoundsLeft(creature)
            : Math.Max(0, PerDay(creature, pool) - creature.DailyUses.SpentFrom(pool));
    }

    /// <summary>The kind of energy this cleric channels, or null for somebody who does not.</summary>
    public static ChannelKind? ChannelKindOf(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!ClassFeatures.Has(creature, FeatureIds.ChannelEnergy))
        {
            return null;
        }

        // The god decides for a good or an evil one; a neutral god's cleric, or a godless one,
        // decides for herself, and is assumed to heal when nothing says otherwise.
        if (creature.Choices.Deity is { } deity)
        {
            if (Alignments.IsGood(deity.Alignment))
            {
                return ChannelKind.Positive;
            }

            if (Alignments.IsEvil(deity.Alignment))
            {
                return ChannelKind.Negative;
            }
        }

        return creature.Choices.Channel ?? ChannelKind.Positive;
    }

    /// <summary>One d6 at first level and another every odd level: a die per row of the table.</summary>
    public static int ChannelDice(Creature creature) => ClassFeatures.Rank(creature, FeatureIds.ChannelEnergy);

    /// <summary>What each Extra Channel adds to a day's channels.</summary>
    public const int ExtraChannels = 2;

    /// <summary>What Improved Channel adds to the difficulty class.</summary>
    public const int ImprovedChannelBonus = 2;

    /// <summary>Ten, half the cleric's level, and Charisma — and two more with Improved Channel.</summary>
    public static int ChannelDifficulty(Creature creature) =>
        10 + (ClassFeatures.LevelOf(creature, FeatureIds.ChannelEnergy) / 2)
        + creature.Abilities[Ability.Charisma].Modifier
        + (creature.HasFeat(FeatEffect.ImprovedChannel) ? ImprovedChannelBonus : 0);

    private static Power? Channel(Creature creature)
    {
        if (ChannelKindOf(creature) is not { } kind)
        {
            return null;
        }

        var dice = $"{ChannelDice(creature)}d6";
        var positive = kind == ChannelKind.Positive;
        var name = positive ? "Channel Positive Energy" : "Channel Negative Energy";
        var level = ClassFeatures.LevelOf(creature, FeatureIds.ChannelEnergy);
        var energy = positive ? DamageType.Positive : DamageType.Negative;

        // Everyone in the burst, foe and friend alike: that is the rule, and Selective
        // Channeling is the feat that buys a way round it. Positive energy heals the living and
        // burns the undead, negative the other way about, and only the burned get a save.
        var effect = new Spell(ChannelPool, name, 0, SpellSchool.Conjuration)
        {
            Range = SpellRange.Personal,
            Target = new SelfTarget { RadiusFeet = ChannelRadius },
            Affects = SpellAffects.Everyone,
            Save = Save.Will,
            OnSave = SaveOutcome.Half,
            Does = positive
                ?
                [
                    new Restore(SpellDice.Fixed(dice)) { Only = SpellFilter.Living },
                    new DealDamage(SpellDice.Fixed(dice), energy) { Only = SpellFilter.Undead },
                ]
                :
                [
                    new DealDamage(SpellDice.Fixed(dice), energy) { Only = SpellFilter.Living },
                    new Restore(SpellDice.Fixed(dice)) { Only = SpellFilter.Undead },
                ],
        };

        return new Power(ChannelPool, name, effect, ActionCost.Standard)
        {
            DifficultyClass = ChannelDifficulty(creature),
            CasterLevel = level,
            Description = positive
                ? $"Heals {dice} to every living creature within {ChannelRadius} ft, foes included; burns the undead (Will half)."
                : $"Deals {dice} negative energy to every living creature within {ChannelRadius} ft (Will half); heals the undead.",
        };
    }

    /// <summary>
    /// The channels the feats add, each drawing on the same daily pool: turning or commanding the
    /// undead, and healing or harming outsiders of one alignment or one element and nobody else.
    /// </summary>
    private static IEnumerable<Power> ChannelFeats(Creature creature)
    {
        if (ChannelKindOf(creature) is not { } kind)
        {
            yield break;
        }

        var dice = $"{ChannelDice(creature)}d6";
        var level = ClassFeatures.LevelOf(creature, FeatureIds.ChannelEnergy);
        var difficulty = ChannelDifficulty(creature);
        var energy = kind == ChannelKind.Positive ? DamageType.Positive : DamageType.Negative;

        if (creature.HasFeat(FeatEffect.TurnUndead) && kind == ChannelKind.Positive)
        {
            // Fleeing as if panicked, for a minute. Panicked is waiting for the monster rules;
            // frightened is the nearest condition the engine has, and it does run.
            yield return new Power(TurnUndeadId, "Turn Undead", new Spell(TurnUndeadId, "Turn Undead", 0, SpellSchool.Conjuration)
            {
                Range = SpellRange.Personal,
                Target = new SelfTarget { RadiusFeet = ChannelRadius },
                Affects = SpellAffects.Enemies,
                Save = Save.Will,
                OnSave = SaveOutcome.Negates,
                Does =
                [
                    new Bestow(new EffectDefinition
                    {
                        Name = "Turned",
                        Condition = Conditions.Condition.Frightened,
                        DurationTicks = TurnedRounds * Duration.TicksPerRound,
                    }) { Only = SpellFilter.Undead },
                ],
            }, ActionCost.Standard)
            {
                Pool = ChannelPool,
                DifficultyClass = difficulty,
                CasterLevel = level,
                Description = $"A channel spent making every undead foe within {ChannelRadius} ft flee for a minute (Will negates).",
            };
        }

        if (creature.HasFeat(FeatEffect.CommandUndead) && kind == ChannelKind.Negative)
        {
            yield return new Power(CommandUndeadId, "Command Undead", new Spell(CommandUndeadId, "Command Undead", 0, SpellSchool.Necromancy)
            {
                Range = SpellRange.Personal,
                Target = new SelfTarget { RadiusFeet = ChannelRadius },
                Affects = SpellAffects.Enemies,
                Save = Save.Will,
                OnSave = SaveOutcome.Negates,
            }, ActionCost.Standard)
            {
                Pool = ChannelPool,
                Use = PowerUse.Command,
                DifficultyClass = difficulty,
                CasterLevel = level,
                Description = $"A channel spent taking undead foes within {ChannelRadius} ft into service, up to {level} hit dice of them (Will negates).",
            };
        }

        foreach (var feat in creature.Feats.Where(feat =>
            feat.Effect is FeatEffect.AlignmentChannel or FeatEffect.ElementalChannel && feat.Choice is not null))
        {
            var subtype = feat.Choice!;
            var only = SpellFilter.Outsiders(subtype);

            foreach (var heals in new[] { true, false })
            {
                var id = $"{(heals ? "heal" : "harm")}-{subtype}-outsiders";
                var name = $"Channel: {(heals ? "heal" : "harm")} {subtype} outsiders";

                yield return new Power(id, name, new Spell(id, name, 0, SpellSchool.Conjuration)
                {
                    Range = SpellRange.Personal,
                    Target = new SelfTarget { RadiusFeet = ChannelRadius },
                    Affects = SpellAffects.Everyone,
                    Save = heals ? null : Save.Will,
                    OnSave = SaveOutcome.Half,
                    Does = heals
                        ? [new Restore(SpellDice.Fixed(dice)) { Only = only }]
                        : [new DealDamage(SpellDice.Fixed(dice), energy) { Only = only }],
                }, ActionCost.Standard)
                {
                    Pool = ChannelPool,
                    DifficultyClass = difficulty,
                    CasterLevel = level,
                    Description = heals
                        ? $"A channel that heals {dice} to {subtype} outsiders within {ChannelRadius} ft, and nobody else."
                        : $"A channel that deals {dice} to {subtype} outsiders within {ChannelRadius} ft (Will half), and nobody else.",
                };
            }
        }
    }

    public const string TurnUndeadId = "turn-undead";

    public const string CommandUndeadId = "command-undead";

    /// <summary>How long a turned undead flees: a minute.</summary>
    public const int TurnedRounds = 10;

    private static Power? Granted(Creature creature, GrantedPowerEffect effect)
    {
        var domainLevel = ClassFeatures.LevelOf(creature, FeatureIds.Domains);
        var wizard = ClassFeatures.LevelOf(creature, FeatureIds.ArcaneSchool);
        var wisdom = creature.Abilities[Ability.Wisdom].Modifier;

        switch (effect)
        {
            case GrantedPowerEffect.RebukeDeath:
            {
                var amount = $"1d4{Signed(domainLevel / 2)}";
                return new Power("rebuke-death", "Rebuke Death", new Spell("rebuke-death", "Rebuke Death", 0, SpellSchool.Conjuration)
                {
                    Range = SpellRange.Touch,
                    Affects = SpellAffects.Everyone,
                    Does = [new Restore(SpellDice.Fixed(amount))],
                }, ActionCost.Standard)
                {
                    Provokes = true,
                    CasterLevel = domainLevel,
                    DifficultyClass = 10 + (domainLevel / 2) + wisdom,
                    Requires = PowerRequirement.TargetDying,
                    Description = $"Touch somebody below nought hit points: heals {amount}.",
                };
            }

            case GrantedPowerEffect.BattleRage:
            {
                var bonus = ClassFeatures.HalfMinOne(domainLevel);
                return new Power("battle-rage", "Battle Rage", new Spell("battle-rage", "Battle Rage", 0, SpellSchool.Enchantment)
                {
                    Range = SpellRange.Touch,
                    Affects = SpellAffects.Allies,
                    Does =
                    [
                        new Bestow(new EffectDefinition
                        {
                            Name = "Battle Rage",
                            DurationTicks = Duration.TicksPerRound,
                            Grants = [new ModifierGrant(ModifierTarget.Damage, bonus, BonusType.Untyped)],
                        }),
                    ],
                }, ActionCost.Standard)
                {
                    Provokes = true,
                    CasterLevel = domainLevel,
                    DifficultyClass = 10 + (domainLevel / 2) + wisdom,
                    Description = $"Touch an ally: +{bonus} on damage for a round.",
                };
            }

            case GrantedPowerEffect.WeaponMaster when creature.Choices.WeaponMasterFeat is { } feat:
                return new Power("weapon-master", "Weapon Master", new Spell("weapon-master", "Weapon Master", 0, SpellSchool.Transmutation)
                {
                    Range = SpellRange.Personal,
                    Target = new SelfTarget(),
                    Affects = SpellAffects.Caster,
                }, ActionCost.Swift)
                {
                    CasterLevel = domainLevel,
                    Use = PowerUse.BorrowFeat,
                    Description = $"The use of {feat.Title} for a round.",
                };

            case GrantedPowerEffect.ForceMissile:
            {
                var amount = $"1d4{Signed(IntenseBonus(creature))}";
                return new Power("force-missile", "Force Missile", new Spell("force-missile", "Force Missile", 0, SpellSchool.Evocation)
                {
                    Range = SpellRange.Medium,
                    Affects = SpellAffects.Enemies,
                    Does = [new DealDamage(SpellDice.Fixed(amount), DamageType.Force)],
                }, ActionCost.Standard)
                {
                    Provokes = true,
                    CasterLevel = wizard,
                    DifficultyClass = 10 + (wizard / 2) + creature.Abilities[Ability.Intelligence].Modifier,
                    Description = $"An unerring missile of force: {amount}.",
                };
            }

            case GrantedPowerEffect.AcidDart:
            {
                var amount = $"1d6{Signed(wizard / 2)}";
                return new Power("acid-dart", "Acid Dart", new Spell("acid-dart", "Acid Dart", 0, SpellSchool.Conjuration)
                {
                    Range = SpellRange.Of(30),
                    Target = new RayTarget(),
                    Affects = SpellAffects.Enemies,
                    Does = [new DealDamage(SpellDice.Fixed(amount), DamageType.Acid)],
                }, ActionCost.Standard)
                {
                    Provokes = true,
                    CasterLevel = wizard,
                    DifficultyClass = 10 + (wizard / 2) + creature.Abilities[Ability.Intelligence].Modifier,
                    Description = $"A ranged touch attack within 30 ft: {amount} acid.",
                };
            }

            case GrantedPowerEffect.DimensionalSteps:
                return new Power("dimensional-steps", "Dimensional Steps", new Spell("dimensional-steps", "Dimensional Steps", 0, SpellSchool.Conjuration)
                {
                    Range = SpellRange.Of(Left(creature, "dimensional-steps")),
                    Target = new PointTarget(),
                    Affects = SpellAffects.Caster,
                }, ActionCost.Move)
                {
                    CasterLevel = wizard,
                    Use = PowerUse.Teleport,
                    UseCost = 5,
                    Description = $"Teleport to a free square, five feet at a time, {30 * wizard} ft a day.",
                };

            default:
                // Passive (Healer's Blessing, Intense Spells), recorded only (Summoner's Charm),
                // or not built yet (Elemental Wall, the universalist's two).
                return null;
        }
    }

    private static Power? FromTalent(Creature creature, TalentDefinition talent)
    {
        var barbarian = ClassFeatures.LevelOf(creature, FeatureIds.Rage);
        var constitution = creature.Abilities[Ability.Constitution].Modifier;

        switch (talent.Effect)
        {
            case TalentEffect.RenewedVigor:
            {
                var dice = 1 + (Math.Max(0, barbarian - 4) / 4);
                var amount = $"{dice}d8{Signed(constitution)}";
                return new Power("renewed-vigor", talent.Name, new Spell("renewed-vigor", talent.Name, 0, SpellSchool.Conjuration)
                {
                    Range = SpellRange.Personal,
                    Target = new SelfTarget(),
                    Affects = SpellAffects.Caster,
                    Does = [new Restore(SpellDice.Fixed(amount))],
                }, ActionCost.Standard)
                {
                    CasterLevel = barbarian,
                    Requires = PowerRequirement.Raging,
                    Description = $"Once a day, mid-rage: heal {amount}.",
                };
            }

            case TalentEffect.GuardedStance:
            {
                var rounds = Math.Max(1, constitution);
                return new Power("guarded-stance", talent.Name, new Spell("guarded-stance", talent.Name, 0, SpellSchool.Abjuration)
                {
                    Range = SpellRange.Personal,
                    Target = new SelfTarget(),
                    Affects = SpellAffects.Caster,
                    Does =
                    [
                        // No grants of its own: the dodge bonus is against melee only, so the
                        // armour class asks for it attack by attack while this is running.
                        new Bestow(new EffectDefinition
                        {
                            Name = GuardedStanceLabel,
                            DurationTicks = rounds * Duration.TicksPerRound,
                        }),
                    ],
                }, ActionCost.Move)
                {
                    // Drawn from the rage itself at no cost: it can be used as often as there is
                    // a rage to use it in, and the count shown is the rounds of rage left.
                    Pool = Rage.Pool,
                    UseCost = 0,
                    CasterLevel = barbarian,
                    Requires = PowerRequirement.Raging,
                    Description = $"+{GuardedStanceBonus(creature)} dodge against melee for {rounds} round(s).",
                };
            }

            default:
                return null;
        }
    }

    /// <summary>One power per spell in the book she has the slots to cast at all, sharing one use a day.</summary>
    private static IEnumerable<Power> Bonded(Creature creature)
    {
        if (PerDay(creature, ArcaneBondPool) <= 0)
        {
            yield break;
        }

        foreach (var spell in creature.Spells.Spellbook)
        {
            if (creature.Spells.SlotsMaximum(spell.Level) <= 0 && creature.Spells.SpecialtyMaximum(spell.Level) <= 0)
            {
                continue;
            }

            yield return new Power($"{ArcaneBondPool}:{spell.Id}", $"{spell.Name} (bonded)", spell, spell.CastingTime)
            {
                Pool = ArcaneBondPool,
                Provokes = true,
                CasterLevel = creature.Spells.CasterLevel,
                DifficultyClass = creature.Spells.SaveDC(spell),
                Use = PowerUse.Spell,
                Description = $"Once a day, {spell.Name} from the bonded object, without a slot.",
            };
        }
    }

    /// <summary>
    /// What the evocation school's intense spells adds: half the wizard's level, at least one.
    /// Nought for anybody else.
    /// </summary>
    public static int IntenseBonus(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return HasSchoolPower(creature, GrantedPowerEffect.IntenseSpells)
            ? ClassFeatures.HalfMinOne(ClassFeatures.LevelOf(creature, FeatureIds.ArcaneSchool))
            : 0;
    }

    /// <summary>Whether a cleric's domains have given her this power by now.</summary>
    public static bool HasDomainPower(Creature creature, GrantedPowerEffect effect)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var level = ClassFeatures.LevelOf(creature, FeatureIds.Domains);
        return creature.Choices.Domains.Any(domain =>
            domain.Powers.Any(power => power.Effect == effect && power.Level <= level));
    }

    /// <summary>Whether a wizard's school has given her this power by now.</summary>
    public static bool HasSchoolPower(Creature creature, GrantedPowerEffect effect)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var level = ClassFeatures.LevelOf(creature, FeatureIds.ArcaneSchool);
        return creature.Choices.School is { } school
            && school.Powers.Any(power => power.Effect == effect && power.Level <= level);
    }

    /// <summary>One, and one more for every six barbarian levels.</summary>
    public static int GuardedStanceBonus(Creature creature) =>
        1 + (ClassFeatures.LevelOf(creature, FeatureIds.Rage) / 6);

    private static string Signed(int value) => value switch
    {
        > 0 => $"+{value}",
        < 0 => value.ToString(),
        _ => string.Empty,
    };
}
