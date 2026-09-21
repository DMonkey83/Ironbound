using System.Text.Json;
using System.Text.Json.Serialization;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Persistence;

/// <summary>
/// Turns an encounter into a save file and back.
/// </summary>
/// <remarks>
/// The random source's state travels with everything else, which is the point: a reloaded fight
/// does not merely look the same, it rolls the same. Options travel too, because a replay under
/// different rules is a different fight.
/// </remarks>
public static class GameSave
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(SavedGame save) => JsonSerializer.Serialize(save, Format);

    public static SavedGame FromJson(string json) =>
        JsonSerializer.Deserialize<SavedGame>(json, Format)
        ?? throw new InvalidDataException("The save file is empty.");

    // ---- capture ----

    public static SavedGame Capture(Encounter encounter, SavedCampaign? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        var state = encounter.Random.Capture();

        return new SavedGame(
            SavedGame.CurrentVersion,
            encounter.Rules,
            state.A,
            state.B,
            encounter.Tick,
            Capture(encounter.Battlefield),
            [.. encounter.Order.Select(c => Capture(c.Creature, encounter.Battlefield))],
            [.. encounter.Order.Select(Capture)],
            campaign);
    }

    private static SavedBattlefield? Capture(Battlefield? field)
    {
        if (field is null)
        {
            return null;
        }

        var blocked = new List<SavedSquare>();
        var difficult = new List<SavedSquare>();

        for (var x = 0; x < field.Width; x++)
        {
            for (var y = 0; y < field.Height; y++)
            {
                var square = new GridSquare(x, y);
                if (field.IsBlocked(square))
                {
                    blocked.Add(new SavedSquare(x, y));
                }

                if (field.IsDifficult(square))
                {
                    difficult.Add(new SavedSquare(x, y));
                }
            }
        }

        return new SavedBattlefield(field.Width, field.Height, [.. blocked], [.. difficult]);
    }

    private static SavedCombatant Capture(Combatant combatant) => new(
        combatant.Creature.Name,
        combatant.NaturalRoll,
        combatant.Initiative,
        combatant.NextTurnTick,
        combatant.OpportunitiesUsed,
        combatant.HasMoved,
        combatant.HasTakenFiveFootStep,
        combatant.HasActed,
        combatant.IsUnaware,
        combatant.Budget.HasStandard,
        combatant.Budget.HasMove,
        combatant.Budget.HasSwift);

    private static SavedModifier[] Capture(ModifierStack stack) =>
        [.. stack.Modifiers.Select(m => new SavedModifier(m.Value, m.Type, m.Source))];

    private static SavedDuration Capture(Duration duration) =>
        new(duration.Ticks, duration.IsPermanent);

    private static SavedCreature Capture(Creature creature, Battlefield? field) => new(
        creature.Name,
        creature.DefinitionId,
        creature.Allegiance,
        creature.Size,
        creature.Speed,
        creature.BaseAttackBonus,
        creature.BaseAttacksOfOpportunity,
        [.. creature.Feats.Select(feat => feat.Id)],
        [.. creature.Equipment.Worn.Select(entry => new SavedItem(entry.Item.Id, entry.Slot))],
        [.. creature.Levels.Select(level => new SavedClassLevel(level.Class.Id, level.Level))],
        [.. Skills.SkillInfo.All
            .Where(skill => creature.Skills.Ranks(skill) > 0
                || creature.Skills.Modifiers(skill).Total != 0)
            .Select(skill => new SavedSkill(
                skill, creature.Skills.Ranks(skill), Capture(creature.Skills.Modifiers(skill))))],
        [.. AbilityInfo.All.Select(a => CaptureAbility(creature.Abilities[a]))],
        new SavedHitPoints(
            creature.HitPoints.Base,
            creature.HitPoints.HitDice,
            creature.HitPoints.Damage,
            creature.HitPoints.Temporary,
            creature.HitPoints.Nonlethal),
        [.. SaveInfo.All.Select(s => new SavedSave(creature.Saves[s].Base, Capture(creature.Saves[s].Modifiers)))],
        Capture(creature.ArmorClass.Modifiers),
        Capture(creature.AttackModifiers),
        Capture(creature.DamageModifiers),
        Capture(creature.InitiativeModifiers),
        Capture(creature.SpeedModifiers),
        CaptureDefenses(creature.Defenses),
        CaptureSpells(creature.Spells),
        [.. creature.Attacks.Select(Capture)],
        [.. creature.Effects.Active.Select(Capture)],
        field?.SquareOf(creature) is { } square ? new SavedSquare(square.X, square.Y) : null);

    private static SavedAbility CaptureAbility(AbilityScore score) =>
        new(score.HasScore ? score.Base : 0, score.HasScore, Capture(score.Modifiers));

    private static SavedDefenses CaptureDefenses(DamageDefenses defenses) => new(
        [.. defenses.Reductions.Select(r => new SavedReduction(r.Amount, r.BypassedBy, r.Mode))],
        [.. defenses.Resistances.Select(r => new SavedResistance(r.Key, r.Value))],
        [.. defenses.Immunities],
        [.. defenses.Vulnerabilities]);

    private static SavedSpellcasting CaptureSpells(Spellcasting spells) => new(
        spells.CastingAbility,
        spells.CasterLevel,
        [.. spells.SlotLevels.Order().Select(l => new SavedSlot(l, spells.SlotsMaximum(l), spells.SlotsRemaining(l)))],
        [.. spells.Prepared.Select(s => s.Id)]);

    private static SavedWeapon Capture(WeaponAttack weapon) => new(
        weapon.Name,
        weapon.Attack.Critical.ThreatsOn,
        weapon.Attack.Critical.Multiplier,
        weapon.Attack.TargetsTouchArmorClass,
        weapon.Qualities,
        weapon.AttackAbility,
        weapon.DamageAbility,
        weapon.DamageScale,
        Capture(weapon.Attack.Modifiers),
        Capture(weapon.DamageModifiers),
        [.. weapon.Damage.Components.Select(c =>
            new SavedDamageComponent(c.Amount.ToString(), c.Type, c.MultipliedOnCritical))],
        weapon.RangeIncrement,
        weapon.MaximumIncrements);

    private static SavedEffect Capture(Effect effect)
    {
        var grants = (effect as ModifierEffect)?.GrantedModifiers;

        return new SavedEffect(
            effect.GetType().Name,
            effect.Name,
            Capture(effect.Duration),
            Capture(effect.Period),
            effect.TicksRemaining,
            effect.TicksUntilPeriod,
            grants is null ? null : [.. grants.Select(g => new SavedModifier(g.Value, g.Type, effect.Name))],
            grants is null ? null : [.. grants.Select(g => g.Target.Kind)],
            grants is null ? null : [.. grants.Select(g => g.Target.Which)],
            (effect as DamageOverTimeEffect)?.Amount.ToString(),
            (effect as DamageOverTimeEffect)?.DamageType,
            (effect as FastHealingEffect)?.Amount ?? (effect as RegenerationEffect)?.Amount,
            (effect as RegenerationEffect)?.SuspendedBy.ToArray(),
            // "Suspended" for a regeneration is suppression; for a bleed it is having stopped.
            // Same field, same meaning: this effect is currently not doing its thing.
            (effect as RegenerationEffect)?.IsSuspended
                ?? (effect as BleedingOutEffect)?.IsStable
                ?? false,
            effect.Condition);
    }

    // ---- restore ----

    /// <param name="library">Where spells and other content are found again. Passed rather than
    /// reached for, because a global catalogue is the thing every other part of this codebase has
    /// been careful not to be.</param>
    public static Encounter Restore(SavedGame save, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(library);

        if (save.Version != SavedGame.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Save version {save.Version} cannot be read by version {SavedGame.CurrentVersion}.");
        }

        var field = Restore(save.Ground);
        var creatures = save.Creatures.ToDictionary(
            saved => saved.Name,
            saved => Restore(saved, save.Rules, field, library),
            StringComparer.Ordinal);

        var order = new List<Combatant>();
        foreach (var saved in save.Order)
        {
            var creature = creatures[saved.Name];
            var combatant = new Combatant(creature, saved.NaturalRoll, saved.Initiative)
            {
                NextTurnTick = saved.NextTurnTick,
                OpportunitiesUsed = saved.OpportunitiesUsed,
                HasMoved = saved.HasMoved,
                HasTakenFiveFootStep = saved.HasTakenFiveFootStep,
                HasActed = saved.HasActed,
                IsUnaware = saved.IsUnaware,
            };

            combatant.Budget.Restore(saved.HasStandard, saved.HasMove, saved.HasSwift);
            order.Add(combatant);
        }

        var random = PcgRandom.FromState(new RandomState(save.RandomA, save.RandomB));
        return new Encounter(random, save.Rules, field, order, save.Tick);
    }

    private static Battlefield? Restore(SavedBattlefield? saved)
    {
        if (saved is null)
        {
            return null;
        }

        var field = new Battlefield(saved.Width, saved.Height);
        foreach (var square in saved.Blocked)
        {
            field.Block(new GridSquare(square.X, square.Y));
        }

        foreach (var square in saved.Difficult)
        {
            field.MakeDifficult(new GridSquare(square.X, square.Y));
        }

        return field;
    }

    private static Creature Restore(
        SavedCreature saved, RuleOptions rules, Battlefield? field, ContentLibrary library)
    {
        var abilities = new AbilityScores(AbilityInfo.All.Select((ability, index) =>
        {
            var score = saved.Abilities[index];
            return score.Present
                ? new AbilityScore(ability, score.Base)
                : AbilityScore.NonAbility(ability);
        }));

        var creature = new Creature(
            saved.Name, abilities, saved.HitPoints.Base, saved.HitPoints.HitDice, rules)
        {
            DefinitionId = saved.DefinitionId,
            Allegiance = saved.Allegiance,
            Size = saved.Size,
            Speed = saved.Speed,
            BaseAttackBonus = saved.BaseAttackBonus,
            BaseAttacksOfOpportunity = saved.BaseAttacksOfOpportunity,
        };

        for (var i = 0; i < AbilityInfo.All.Count; i++)
        {
            Fill(creature.Abilities[AbilityInfo.All[i]].Modifiers, saved.Abilities[i].Modifiers);
        }

        for (var i = 0; i < SaveInfo.All.Count; i++)
        {
            creature.Saves[SaveInfo.All[i]].Base = saved.Saves[i].Base;
            Fill(creature.Saves[SaveInfo.All[i]].Modifiers, saved.Saves[i].Modifiers);
        }

        Fill(creature.ArmorClass.Modifiers, saved.ArmorClass);
        Fill(creature.AttackModifiers, saved.Attack);
        Fill(creature.DamageModifiers, saved.Damage);
        Fill(creature.InitiativeModifiers, saved.Initiative);
        Fill(creature.SpeedModifiers, saved.SpeedModifiers);

        creature.HitPoints.Restore(
            saved.HitPoints.Damage, saved.HitPoints.Temporary, saved.HitPoints.Nonlethal);

        // Feats come back by identity only. Their static bonuses were captured with the
        // modifier stacks above, like any other modifier, so applying them again here would
        // quietly double every one of them. What the list is needed for is the handful of
        // feats the rules ask about by name.
        foreach (var id in saved.Feats)
        {
            creature.Feats.Add(library.GetFeat(id) ?? throw new InvalidDataException(
                $"The save has a feat '{id}', which no content file defines."));
        }

        RestoreDefenses(creature.Defenses, saved.Defenses);
        RestoreSpells(creature.Spells, saved.Spells, library);

        foreach (var weapon in saved.Weapons)
        {
            creature.Attacks.Add(Restore(weapon));
        }

        // Equipment, like feats, comes back by identity only: its bonuses were captured with
        // the modifier stacks and its weapon with the attack list. A weapon item is paired back
        // up with its attack by name, so that taking the thing off later removes the right one.
        foreach (var skill in saved.Skills)
        {
            creature.Skills.SetRanks(skill.Skill, skill.Ranks);
            Fill(creature.Skills.Modifiers(skill.Skill), skill.Modifiers);
        }

        // Levels come back by identity, like feats and equipment: everything they produced —
        // base attack, saves, hit dice — was captured as numbers. What would be missing without
        // them is the ability to gain another one and have it land in the right class.
        foreach (var taken in saved.Levels)
        {
            var definition = library.GetClass(taken.ClassId) ?? throw new InvalidDataException(
                $"The save has a level of '{taken.ClassId}', which no content file defines.");

            creature.Levels.Add(new Classes.ClassLevel(definition, taken.Level));
        }

        foreach (var carried in saved.Items)
        {
            var item = library.GetItem(carried.Id) ?? throw new InvalidDataException(
                $"The save has an item '{carried.Id}', which no content file defines.");

            creature.Equipment.Reattach(
                item,
                creature.Attacks.FirstOrDefault(
                    attack => string.Equals(attack.Name, item.Name, StringComparison.Ordinal)),
                carried.Slot);
        }

        foreach (var effect in saved.Effects)
        {
            creature.Effects.Reattach(Restore(effect), effect.TicksRemaining, effect.TicksUntilPeriod);
        }

        if (saved.Square is { } square && field is not null)
        {
            field.Place(creature, new GridSquare(square.X, square.Y));
        }

        return creature;
    }

    private static void Fill(ModifierStack stack, SavedModifier[] modifiers)
    {
        foreach (var modifier in modifiers)
        {
            stack.Add(modifier.Value, modifier.Type, modifier.Source);
        }
    }

    private static void RestoreDefenses(DamageDefenses defenses, SavedDefenses saved)
    {
        foreach (var reduction in saved.Reductions)
        {
            defenses.Reduce(reduction.Amount, reduction.BypassedBy, reduction.Mode);
        }

        foreach (var resistance in saved.Resistances)
        {
            defenses.Resist(resistance.Type, resistance.Amount);
        }

        foreach (var immunity in saved.Immunities)
        {
            defenses.MakeImmuneTo(immunity);
        }

        foreach (var vulnerability in saved.Vulnerabilities)
        {
            defenses.MakeVulnerableTo(vulnerability);
        }
    }

    private static void RestoreSpells(
        Spellcasting spells, SavedSpellcasting saved, ContentLibrary library)
    {
        spells.CastingAbility = saved.CastingAbility;
        spells.CasterLevel = saved.CasterLevel;

        foreach (var slot in saved.Slots)
        {
            spells.RestoreSlots(slot.Level, slot.Maximum, slot.Remaining);
        }

        // By identifier, not by display name: renaming "Fireball" must not quietly empty every
        // wizard's spellbook in every existing save.
        //
        // A spell the library has never heard of is fatal, and that is a pre-release choice. Right
        // now the only saves in existence are ones we made minutes ago, so a missing id means a
        // content file was renamed or broken and we want to hear about it at the point of failure
        // — not twenty minutes later while wondering why Merrin will not cast anything. Once there
        // are saves belonging to people who cannot regenerate them, this flips: losing one spell
        // is then far better than losing the campaign, and this becomes a skip plus a warning.
        foreach (var id in saved.Prepared)
        {
            spells.Prepare(library.GetSpell(id) ?? throw new InvalidDataException(
                $"The save has a spell '{id}', which no content file defines."));
        }
    }

    private static WeaponAttack Restore(SavedWeapon saved)
    {
        var attack = new Attack
        {
            Critical = new CriticalProfile(saved.ThreatsOn, saved.Multiplier),
            TargetsTouchArmorClass = saved.TargetsTouchArmorClass,
        };

        Fill(attack.Modifiers, saved.AttackModifiers);

        var packet = new DamagePacket();
        foreach (var component in saved.Damage)
        {
            packet.Add(new DamageComponent(
                DiceExpression.Parse(component.Amount), component.Type, component.MultipliedOnCritical));
        }

        var weapon = new WeaponAttack(saved.Name, attack, packet)
        {
            Qualities = saved.Qualities,
            AttackAbility = saved.AttackAbility,
            DamageAbility = saved.DamageAbility,
            DamageScale = saved.DamageScale,
            RangeIncrement = saved.RangeIncrement,
            MaximumIncrements = Math.Max(1, saved.MaximumIncrements),
        };

        Fill(weapon.DamageModifiers, saved.DamageModifiers);
        return weapon;
    }

    private static Effect Restore(SavedEffect saved)
    {
        var duration = saved.Duration.Permanent
            ? Duration.Permanent
            : Duration.FromTicks(saved.Duration.Ticks);

        switch (saved.Kind)
        {
            case nameof(ModifierEffect):
                // The condition tag has to come back too, or a stunned creature reloads as one
                // merely carrying a -2 and takes its turn as though nothing were wrong.
                var effect = new ModifierEffect(saved.Name, duration) { Condition = saved.Condition };
                for (var i = 0; i < (saved.Grants?.Length ?? 0); i++)
                {
                    effect.Grants(
                        saved.Grants![i].Value,
                        saved.Grants[i].Type,
                        new ModifierTarget(saved.GrantTargets![i], saved.GrantTargetWhich![i]));
                }

                return effect;

            case nameof(DamageOverTimeEffect):
                return new DamageOverTimeEffect(
                    saved.Name,
                    duration,
                    saved.Amount!,
                    saved.DamageType!.Value,
                    Duration.FromTicks(saved.Period.Ticks));

            case nameof(FastHealingEffect):
                return new FastHealingEffect(
                    saved.Name, duration, saved.Heal!.Value, Duration.FromTicks(saved.Period.Ticks));

            case nameof(BleedingOutEffect):
                var bleeding = new BleedingOutEffect();
                if (saved.Suspended)
                {
                    bleeding.Stabilise();
                }

                return bleeding;

            case nameof(RegenerationEffect):
                var regeneration = new RegenerationEffect(
                    saved.Name, duration, saved.Heal!.Value, saved.SuspendedBy ?? []);

                if (saved.Suspended)
                {
                    regeneration.Suspend();
                }

                return regeneration;

            default:
                throw new InvalidDataException($"Unknown effect kind '{saved.Kind}'.");
        }
    }
}
