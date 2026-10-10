using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Drinking a potion from the belt, or rubbing on an oil: a standard action, and one that drops
/// the drinker's guard, so anybody standing next to it gets a swing first.
/// </summary>
/// <remarks>
/// The drinker is the caster and the target both, at the level the potion was brewed at, and
/// nothing of the drinker's own — a Healing cleric's blessing, an evoker's intense spells —
/// goes into it. Getting it off the belt costs nothing; the book's move action to draw a stored
/// item is not modelled. Cut down by the swing it drew, the drinker never gets the stopper out,
/// and the potion is still on the belt.
/// </remarks>
public sealed class DrinkPotionAction : GameAction
{
    public DrinkPotionAction(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
    }

    public ItemDefinition Item { get; }

    public override string Name => Item.Name;

    public override ActionCost Cost => ActionCost.Standard;

    /// <summary>A potion or an oil, and one of it on the actor's own belt.</summary>
    public override bool CanPerform(ActionContext context) =>
        ItemUse.IsPotion(Item) && Consumables.Count(context.Actor, Item) > 0;

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var opportunities = ItemUse.Provoke(context);

        if (!actor.IsConscious)
        {
            return new PotionResult(this, actor, Item, actor, null, opportunities);
        }

        actor.Equipment.UseUp(Item);
        return new PotionResult(this, actor, Item, actor, ItemUse.Pour(Item, actor, context), opportunities);
    }
}

/// <summary>
/// Pouring a potion down the throat of a friend who is down: the whole round, kneeling beside
/// them with one's guard down, and the potion off one's own belt.
/// </summary>
/// <remarks>
/// The book makes giving a potion to somebody unconscious a full-round action. It is the patient
/// who drinks it, so it is the patient the potion counts as caster and target; a conscious friend
/// can drink their own.
/// </remarks>
public sealed class AdministerPotionAction : GameAction
{
    public AdministerPotionAction(ItemDefinition item, Creature patient)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(patient);
        Item = item;
        Patient = patient;
    }

    public ItemDefinition Item { get; }

    public Creature Patient { get; }

    public override string Name => Item.Name;

    public override ActionCost Cost => ActionCost.FullRound;

    /// <summary>
    /// A potion or an oil on the actor's belt, and an ally who is alive, out cold and within
    /// arm's length — next to them, for anybody Medium or smaller.
    /// </summary>
    public override bool CanPerform(ActionContext context) =>
        ItemUse.IsPotion(Item)
        && Consumables.Count(context.Actor, Item) > 0
        && !ReferenceEquals(Patient, context.Actor)
        && Patient.IsAllyOf(context.Actor)
        && Patient.IsAlive
        && !Patient.IsConscious
        && (context.Encounter.Battlefield is not { } field || field.IsWithinTouch(context.Actor, Patient));

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var opportunities = ItemUse.Provoke(context);

        if (!actor.IsConscious)
        {
            return new PotionResult(this, actor, Item, Patient, null, opportunities);
        }

        actor.Equipment.UseUp(Item);
        return new PotionResult(this, actor, Item, Patient, ItemUse.Pour(Item, Patient, context), opportunities);
    }
}

/// <summary>
/// Throwing something off the belt: a flask of acid or alchemist's fire, a tanglefoot bag, a
/// thunderstone. A standard action and a ranged attack, so it provokes from anybody in reach.
/// </summary>
/// <remarks>
/// <para>
/// None of these is a weapon. The roll is made with a weapon built for it and thrown away
/// after, so nothing ever joins the thrower's attacks, comes back after the fight, or has to be
/// paired by name in a save. The item is used up the moment it leaves the hand, hit or miss —
/// after any swing the throw drew, so a thrower cut down by one has thrown nothing.
/// </para>
/// <para>
/// At a creature it is a ranged touch attack — an attack roll on a bag of glue that nobody
/// needs to be trained in — with Dexterity, size, distance, cover, prone and firing into a melee
/// all counting, and no Strength on the damage, no sneak attack and no stance. A thunderstone,
/// or anything thrown at a square, is against armour class 5, with the distance added by hand
/// and a natural 1 always a miss. The book aims at a corner where four squares meet; here it is
/// a square, and the splash covers that square and the eight round it.
/// </para>
/// <para>
/// A flask or a thunderstone that misses still lands somewhere: a d8, with 1 short, back along
/// the line to the thrower, and 2 to 8 round the aim point clockwise from there, so many squares
/// as the throw was range increments, stopping short of a wall or the edge of the map. A
/// tanglefoot bag that misses is simply gone.
/// </para>
/// </remarks>
public sealed class ThrowItemAction : GameAction
{
    /// <summary>The armour class of a spot on the ground.</summary>
    public const int GroundArmorClass = 5;

    /// <summary>What a hit with a tanglefoot bag files the entanglement under.</summary>
    public const string TangledName = "Tanglefoot bag";

    /// <summary>What a failed save against one files being glued to the floor under.</summary>
    public const string StuckName = "Stuck fast";

    /// <summary>What a thunderstone files the deafness under.</summary>
    public const string DeafenedName = "Thunderstone";

    /// <summary>How far a splash reaches from where the flask breaks.</summary>
    public const int SplashFeet = Distance.FeetPerSquare;

    /// <summary>The ways a miss can go, clockwise on the map from straight up (y running down).</summary>
    private static readonly (int X, int Y)[] Compass =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    private ThrowItemAction(ItemDefinition item, Creature? target, GridSquare? square)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        Target = target;
        Square = square;
    }

    /// <summary>
    /// At a creature: a flask straight at it, or a tanglefoot bag. A thunderstone is thrown at
    /// the ground, so for one this means the creature's square.
    /// </summary>
    public static ThrowItemAction At(ItemDefinition item, Creature target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ThrowItemAction(item, target, null);
    }

    /// <summary>At a square: a thunderstone, or a flask thrown to splash whoever is round it.</summary>
    public static ThrowItemAction At(ItemDefinition item, GridSquare square) => new(item, null, square);

    public ItemDefinition Item { get; }

    /// <summary>The creature it was thrown at, when it was thrown at one.</summary>
    public Creature? Target { get; }

    /// <summary>The square it was thrown at, when it was thrown at one.</summary>
    public GridSquare? Square { get; }

    /// <summary>
    /// Whether the throw is at the ground — armour class 5 — rather than at somebody: a square
    /// was chosen, or the item is one only ever thrown at the ground.
    /// </summary>
    public bool AtGround => Target is null || Item.Consumable?.Aim == ThrowAim.Square;

    public override string Name => Item.Name;

    public override ActionCost Cost => ActionCost.Standard;

    /// <summary>
    /// Something thrown, on the actor's belt; aimed at the kind of thing it can be aimed at; on
    /// a map, with a clear line to the aim point and no more than five range increments away.
    /// Not at oneself, not at the dead, not into a wall, and not from flat on one's back, where
    /// the book allows no ranged attack but a crossbow's.
    /// </summary>
    public override bool CanPerform(ActionContext context)
    {
        var actor = context.Actor;

        if (Item.Consumable is not { IsThrown: true } use
            || Consumables.Count(actor, Item) == 0
            || actor.IsProne
            || !Fits(use))
        {
            return false;
        }

        if (Target is { } target && (!target.IsAlive || ReferenceEquals(target, actor)))
        {
            return false;
        }

        if (context.Encounter.Battlefield is not { } field
            || field.SquareOf(actor) is not { } from
            || AimedAt(field) is not { } aimed
            || aimed == from
            || !field.IsPassable(aimed))
        {
            return false;
        }

        return field.HasLineOfSight(from, aimed) && Distance.Between(from, aimed) <= use.MaximumRange;
    }

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var use = Item.Consumable!;
        var field = context.Encounter.Battlefield!;
        var from = field.SquareOf(actor)!.Value;
        var aimed = AimedAt(field)!.Value;
        var direct = AtGround ? null : Target;

        // A ranged attack in somebody's reach is careless, and the swing comes first.
        var opportunities = Opportunities.Provoke(context.Encounter, actor, from);
        if (!actor.IsConscious)
        {
            return new ThrowResult(this, actor, Item, from, aimed, null, direct, null, null, [], opportunities);
        }

        // Gone the moment it leaves the hand, hit or miss: nothing brings a broken flask back.
        actor.Equipment.UseUp(Item);

        var weapon = WeaponFor(Item);
        var feet = Distance.Between(from, aimed);
        var attack = direct is null
            ? AtTheGround(actor, weapon, feet, context.Random)
            : AtSomebody(context, actor, weapon, direct);

        GridSquare? landed = attack.IsHit ? aimed : null;
        int? die = null;

        if (!attack.IsHit && Deviates(use))
        {
            die = context.Random.NextDie(8);
            landed = Deviate(field, from, aimed, die.Value, weapon.IncrementsAt(feet));
        }

        var effects = landed is { } spot
            ? Land(context, use, field, spot, attack.IsHit ? direct : null, attack)
            : [];

        return new ThrowResult(this, actor, Item, from, aimed, landed, direct, attack, die, effects, opportunities);
    }

    /// <summary>
    /// The bonus a creature would throw this item at a target with, as the throw itself works it
    /// out — for a screen to show the odds or an autopilot to weigh them.
    /// </summary>
    public static ModifierBreakdown AttackBonus(Creature thrower, ItemDefinition item, Creature target, Battlefield? field)
    {
        ArgumentNullException.ThrowIfNull(thrower);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(target);

        return Strike.ThrownBonus(thrower, WeaponFor(item), target, field);
    }

    /// <summary>
    /// Where a throw that missed comes down: <paramref name="die"/> is the d8, 1 short along the
    /// line back to <paramref name="from"/> and 2 to 8 clockwise round the aim point from there;
    /// <paramref name="squares"/> is how many range increments the throw was. It stops at the
    /// last open square before a wall or the edge of the map.
    /// </summary>
    public static GridSquare Deviate(Battlefield field, GridSquare from, GridSquare aimed, int die, int squares)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfLessThan(die, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(die, 8);

        var (dx, dy) = Compass[(Towards(aimed, from) + die - 1) % Compass.Length];
        var landed = aimed;

        for (var step = 0; step < squares; step++)
        {
            var next = new GridSquare(landed.X + dx, landed.Y + dy);
            if (!field.IsPassable(next))
            {
                break;
            }

            landed = next;
        }

        return landed;
    }

    /// <summary>
    /// Which of the eight ways points from one square towards another: whichever is nearest the
    /// true line. Worked out in whole numbers — a line is diagonal when its lesser side is more
    /// than √2 − 1 of its greater, which squared is (lesser + greater)² &gt; 2·greater² — so a
    /// replay can never land a flask somewhere else.
    /// </summary>
    private static int Towards(GridSquare from, GridSquare to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var greater = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var lesser = Math.Min(Math.Abs(dx), Math.Abs(dy));

        if (greater == 0)
        {
            return 0;
        }

        (int X, int Y) way = (lesser + greater) * (lesser + greater) > 2 * greater * greater
            ? (Math.Sign(dx), Math.Sign(dy))
            : Math.Abs(dx) >= Math.Abs(dy) ? (Math.Sign(dx), 0) : (0, Math.Sign(dy));

        return Array.IndexOf(Compass, way);
    }

    private bool Fits(ConsumableDefinition use) => use.Aim switch
    {
        ThrowAim.Either => true,
        ThrowAim.Creature => Target is not null,

        // A creature chosen for a thunderstone is where it goes off.
        ThrowAim.Square => true,
        _ => false,
    };

    private GridSquare? AimedAt(Battlefield field) => Target is { } target ? field.SquareOf(target) : Square;

    /// <summary>A flask or a thunderstone that misses lands somewhere; a tanglefoot bag does not matter where.</summary>
    private static bool Deviates(ConsumableDefinition use) =>
        use.Kind is ConsumableKind.Splash or ConsumableKind.Thunderstone;

    /// <summary>
    /// The weapon the roll is made with, belonging to nobody: thrown, five increments at most,
    /// Dexterity to aim it, no Strength behind it, and no kind, so no proficiency is asked about.
    /// </summary>
    private static WeaponAttack WeaponFor(ItemDefinition item)
    {
        var use = item.Consumable ?? throw new ArgumentException($"The {item.Name} is not something to throw.", nameof(item));
        var weapon = WeaponAttack.Ranged(
            item.Name,
            use.Damage ?? "0",
            use.DamageType,
            Math.Max(1, use.RangeIncrement),
            maximumIncrements: ConsumableDefinition.MaximumIncrements);

        weapon.Attack.TargetsTouchArmorClass = use.TouchAttack;
        return weapon;
    }

    /// <summary>
    /// Straight at somebody, against touch armour class for a flask or a bag: caught flat-footed
    /// or not, behind cover or not, lying down or not. A critical is a flask's ×2 on its own dice.
    /// </summary>
    private static AttackResult AtSomebody(ActionContext context, Creature actor, WeaponAttack weapon, Creature target)
    {
        var field = context.Encounter.Battlefield;
        var state = DefenseOptions.None;

        if (target.DeniesDexterity
            || (context.Encounter.IsFlatFootedTo(target, actor) && !UncannyDodge.Has(target)))
        {
            state |= DefenseOptions.DexterityDenied;
        }

        return weapon.Attack.Resolve(
            target.ArmorClass,
            context.Random,
            Strike.ThrownBonus(actor, weapon, target, field),
            state,
            context.Rules,
            Strike.CoverFor(actor, target, field, weapon),
            Strike.ProneFor(weapon, target),
            weapon.Attack.Critical,
            Martial.ConfirmationBonus(actor));
    }

    /// <summary>
    /// At a spot on the ground: armour class 5, the distance added by hand, a natural 1 a miss
    /// and a natural 20 a hit. Nothing to critically hit, so nothing to confirm.
    /// </summary>
    private static AttackResult AtTheGround(Creature actor, WeaponAttack weapon, int feet, IRandomSource random)
    {
        var bonus = Strike.ThrownBonus(actor, weapon, null, null, feet);
        var natural = random.NextDie(Attack.DieSides);
        var total = natural + bonus.Total;
        var hit = natural != 1 && (natural == Attack.DieSides || total >= GroundArmorClass);

        return new AttackResult
        {
            NaturalRoll = natural,
            Bonus = bonus,
            Total = total,
            TargetArmorClass = GroundArmorClass,
            Options = DefenseOptions.None,
            Outcome = hit ? AttackOutcome.Hit : AttackOutcome.Miss,
            Threatened = false,
            CriticalMultiplier = 1,
        };
    }

    /// <summary>What happens where it comes down, to everybody it reaches.</summary>
    /// <param name="hit">Whoever it hit square on, if anybody: a flask's direct hit, a bag's.</param>
    private List<ThrowEffect> Land(
        ActionContext context, ConsumableDefinition use, Battlefield field, GridSquare spot, Creature? hit, AttackResult attack)
    {
        var effects = new List<ThrowEffect>();

        switch (use.Kind)
        {
            case ConsumableKind.Splash:
                if (hit is not null)
                {
                    effects.Add(DirectHit(context, use, hit, attack));
                }

                // Everybody else within five feet of where it broke, friend or foe — the thrower
                // too, if it was thrown at somebody next to it.
                foreach (var splashed in Within(field, spot, SplashFeet).Where(other => !ReferenceEquals(other, hit)))
                {
                    var taken = Casting.Hurt(splashed, use.Splash, use.DamageType, context.Random, context.Rules);
                    effects.Add(new ThrowEffect(splashed, taken, false, null, []) { DamageType = use.DamageType });
                }

                break;

            case ConsumableKind.Tanglefoot when hit is not null:
                effects.Add(Stick(context, use, hit));
                break;

            case ConsumableKind.Thunderstone:
                effects.AddRange(Within(field, spot, use.RadiusFeet).Select(heard => Bang(context, use, heard)));
                break;
        }

        return effects;
    }

    /// <summary>The living within so many feet of a square, in the order they came onto the map.</summary>
    private static IEnumerable<Creature> Within(Battlefield field, GridSquare spot, int feet) =>
        field.CreaturesWithin(spot, feet).Where(creature => creature.IsAlive);

    /// <summary>
    /// A flask square on: its dice, rolled again for a critical, through the target's
    /// resistances — and alchemist's fire left burning for one more go a round later.
    /// </summary>
    private ThrowEffect DirectHit(ActionContext context, ConsumableDefinition use, Creature target, AttackResult attack)
    {
        var dice = DiceExpression.Parse(use.Damage!);
        var rolled = 0;
        for (var time = 0; time < attack.CriticalMultiplier; time++)
        {
            rolled += dice.Roll(context.Random).Total;
        }

        var taken = Casting.Hurt(target, rolled, use.DamageType, context.Random, context.Rules);
        var applied = new List<string>();

        // Nothing to burn on the dead, nor on anything fire cannot touch.
        if (use.Burn is { } burn && target.IsAlive && !target.Defenses.IsImmuneTo(use.DamageType))
        {
            target.Effects.Apply(new DamageOverTimeEffect(BurnName(Item), Duration.Rounds(1), burn, use.DamageType));
            applied.Add($"burning: {burn} more next round unless put out");
        }

        return new ThrowEffect(target, taken, true, null, applied) { DamageType = use.DamageType };
    }

    /// <summary>
    /// A tanglefoot bag landing: entangled for 2d4 rounds, and glued to the floor for as long on
    /// a failed Reflex save. Anything Huge or bigger tears through the goo without noticing.
    /// </summary>
    private static ThrowEffect Stick(ActionContext context, ConsumableDefinition use, Creature target)
    {
        if (target.Size >= CreatureSize.Huge)
        {
            return new ThrowEffect(target, 0, true, null, ["too big to be held"]);
        }

        var rounds = DiceExpression.Parse(use.Rounds ?? "1").Roll(context.Random).Total;
        target.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(rounds), TangledName));
        var applied = new List<string> { $"entangled for {Rounds(rounds)}" };

        var save = target.Saves.Attempt(Save.Reflex, use.DifficultyClass, context.Random, context.Rules);
        if (save.Failed)
        {
            target.Effects.Apply(ConditionInfo.Effect(Condition.Anchored, Duration.Rounds(rounds), StuckName));
            applied.Add("stuck fast");
        }

        return new ThrowEffect(target, 0, true, save, applied);
    }

    /// <summary>A thunderstone going off within earshot: Fortitude, or deafened for an hour.</summary>
    private static ThrowEffect Bang(ActionContext context, ConsumableDefinition use, Creature heard)
    {
        var save = heard.Saves.Attempt(Save.Fortitude, use.DifficultyClass, context.Random, context.Rules);
        if (save.Succeeded)
        {
            return new ThrowEffect(heard, 0, false, save, []);
        }

        var hours = Math.Max(1, use.DeafenedHours);
        heard.Effects.Apply(ConditionInfo.Effect(Condition.Deafened, Duration.Hours(hours), DeafenedName));
        return new ThrowEffect(heard, 0, false, save, [hours == 1 ? "deafened for 1 hour" : $"deafened for {hours} hours"]);
    }

    private static string Rounds(int rounds) => rounds == 1 ? "1 round" : $"{rounds} rounds";

    /// <summary>"Alchemist's fire": the burn is filed under the item that started it.</summary>
    public static string BurnName(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Name.Length == 0 ? item.Id : char.ToUpperInvariant(item.Name[0]) + item.Name[1..];
    }
}

/// <summary>
/// Pulling free of a tanglefoot bag's goo: the whole round, a Strength check or Escape Artist,
/// whichever is better, against the bag's 17.
/// </summary>
/// <remarks>
/// It frees the feet, not the rest: the creature is no longer stuck fast, but it is still
/// entangled until the goo goes brittle, as the book has it.
/// </remarks>
public sealed class BreakFreeAction : GameAction
{
    /// <summary>A tanglefoot bag's: the only thing in the game that sticks anybody fast.</summary>
    public const int TanglefootDifficulty = 17;

    public BreakFreeAction(int difficulty = TanglefootDifficulty) => Difficulty = difficulty;

    public int Difficulty { get; }

    public override string Name => "break free";

    public override ActionCost Cost => ActionCost.FullRound;

    public override bool CanPerform(ActionContext context) => context.Actor.Has(Condition.Anchored);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var strength = actor.AbilityCheck(Ability.Strength).Total;
        var escape = actor.Skills.CanAttempt(Skill.EscapeArtist) ? actor.Skills.Total(Skill.EscapeArtist) : int.MinValue;

        string roll;
        bool freed;

        if (escape > strength)
        {
            var check = actor.Skills.Check(Skill.EscapeArtist, context.Random, Difficulty);
            roll = check.ToString();
            freed = check.Succeeded == true;
        }
        else
        {
            var natural = context.Random.NextDie(20);
            var total = natural + strength;
            freed = total >= Difficulty;
            roll = $"{actor.Name} Strength: d20 [{natural}] {strength:+0;-0;+0} = {total} "
                + $"vs DC {Difficulty} — {(freed ? "success" : "failure")}";
        }

        if (freed)
        {
            while (actor.Effects.Remove(Condition.Anchored) is not null)
            {
            }
        }

        return new BreakFreeResult(this, actor, roll, freed);
    }
}

/// <summary>
/// Rolling on the ground to put out alchemist's fire before it burns again: the whole round and
/// a DC 15 Reflex save, with two for the rolling.
/// </summary>
public sealed class PutOutFlamesAction : GameAction
{
    public const int Difficulty = 15;

    /// <summary>What rolling on the ground is worth on the save.</summary>
    public const int RollingBonus = 2;

    public override string Name => "put out the flames";

    public override ActionCost Cost => ActionCost.FullRound;

    /// <summary>Whether anything is burning on a creature: fire on a clock.</summary>
    public static bool IsBurning(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return Flames(creature).Any();
    }

    public override bool CanPerform(ActionContext context) => IsBurning(context.Actor);

    public override ActionResult Perform(ActionContext context)
    {
        var actor = context.Actor;
        var save = actor.Saves.Attempt(
            Save.Reflex,
            Difficulty,
            context.Random,
            context.Rules,
            [new Modifier(RollingBonus, BonusType.Circumstance, "Rolling on the ground")]);

        if (save.Succeeded)
        {
            foreach (var flame in Flames(actor).ToList())
            {
                actor.Effects.Remove(flame.Name);
            }
        }

        return new PutOutFlamesResult(this, actor, save, save.Succeeded);
    }

    private static IEnumerable<DamageOverTimeEffect> Flames(Creature creature) =>
        creature.Effects.Active.OfType<DamageOverTimeEffect>().Where(effect => effect.DamageType == DamageType.Fire);
}

/// <summary>What the potion actions share: what counts as one, the swing it draws, and drinking it.</summary>
internal static class ItemUse
{
    public static bool IsPotion(ItemDefinition item) => item.Consumable is { IsPotion: true, Spell: not null };

    /// <summary>"drinks" a potion, "applies" an oil.</summary>
    public static string Verb(ConsumableDefinition use) => use.Kind == ConsumableKind.Oil ? "applies" : "drinks";

    public static IReadOnlyList<StrikeResult> Provoke(ActionContext context) =>
        context.Encounter.Battlefield?.SquareOf(context.Actor) is { } standing
            ? Opportunities.Provoke(context.Encounter, context.Actor, standing)
            : [];

    /// <summary>The spell in the bottle, let loose on whoever drinks it, at the bottle's level.</summary>
    public static SpellCast Pour(ItemDefinition item, Creature drinker, ActionContext context)
    {
        var use = item.Consumable!;
        return Casting.Resolve(
            drinker,
            use.Spell!,
            SpellAim.At(drinker),
            context.Random,
            context.Rules,
            context.Encounter.Battlefield,
            Invocation.FromItem(use, Verb(use)));
    }
}

/// <summary>
/// A potion drunk or given, with the swings it drew. <see cref="Cast"/> is null when one of them
/// cut the actor down first, and then nothing was used.
/// </summary>
/// <param name="Drinker">Who it went into: the actor, or the friend it was given to.</param>
public sealed record PotionResult(
    GameAction Action,
    Creature Actor,
    ItemDefinition Item,
    Creature Drinker,
    SpellCast? Cast,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Describe(Actor, Item, Drinker, Cast, Opportunities))
{
    /// <summary>Whether the potion went into anybody, and so off the belt.</summary>
    public bool Used => Cast is not null;

    /// <summary>What it did to the drinker, when it did anything.</summary>
    public SpellTargetResult? Outcome => Cast?.Targets.FirstOrDefault();

    /// <remarks>
    /// Written here rather than taken from the cast, which would say the drinker cast the
    /// spell at themselves.
    /// </remarks>
    private static string Describe(
        Creature actor, ItemDefinition item, Creature drinker, SpellCast? cast, IReadOnlyList<StrikeResult> opportunities)
    {
        var oil = item.Consumable?.Kind == ConsumableKind.Oil;
        var given = !ReferenceEquals(actor, drinker);
        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;

        if (cast is null)
        {
            var doing = given ? $"giving {drinker.Name} the {item.Name}" : $"{(oil ? "applying" : "drinking")} the {item.Name}";
            return $"{actor.Name} is cut down {doing}{provoked}";
        }

        var done = (given, oil) switch
        {
            (false, false) => $"{actor.Name} drinks the {item.Name}",
            (false, true) => $"{actor.Name} applies the {item.Name}",
            (true, false) => $"{actor.Name} gives {drinker.Name} the {item.Name}",
            (true, true) => $"{actor.Name} applies the {item.Name} to {drinker.Name}",
        };

        return done + provoked;
    }
}

/// <summary>What a throw did to one creature it reached.</summary>
/// <param name="Damage">What got past its resistances: a flask's dice, or a splash.</param>
/// <param name="Direct">Hit square on — a flask's direct hit, a tanglefoot bag — rather than
/// splashed or within earshot.</param>
/// <param name="Save">The Reflex against the goo, the Fortitude against the bang.</param>
/// <param name="Applied">What it was left with: "burning…", "entangled for 5 rounds", "stuck fast".</param>
public sealed record ThrowEffect(
    Creature Creature,
    int Damage,
    bool Direct,
    SavingThrowResult? Save,
    IReadOnlyList<string> Applied)
{
    /// <summary>The type of a flask's damage, for the log; null for anything that deals none.</summary>
    public DamageType? DamageType { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();

        if (DamageType is { } type)
        {
            var kind = type.ToString().ToLowerInvariant();
            parts.Add(Direct ? $"{Damage} {kind} damage" : $"{Damage} {kind} splash");
        }

        if (Save is not null)
        {
            parts.Add(Save.ToString());
        }

        parts.AddRange(Applied);

        if (parts.Count == 0)
        {
            parts.Add("unharmed");
        }

        return $"{Creature.Name}: {string.Join("; ", parts)} ({Creature.HitPoints})";
    }
}

/// <summary>
/// A throw: where from, where at, where it came down, the roll, and everybody it reached.
/// </summary>
/// <param name="Aimed">The square aimed at: the target's, or the one chosen.</param>
/// <param name="Landed">Where it broke: the aim point on a hit, wherever a miss took it, and null
/// for a tanglefoot bag that missed or a throw never made.</param>
/// <param name="Target">The creature it was thrown at, when it was thrown at one rather than at
/// the ground.</param>
/// <param name="Attack">Null when a swing the throw drew cut the thrower down first, and nothing
/// was thrown or used.</param>
/// <param name="DeviationDie">The d8 a miss rolled for where it went, when it rolled one.</param>
public sealed record ThrowResult(
    GameAction Action,
    Creature Actor,
    ItemDefinition Item,
    GridSquare From,
    GridSquare Aimed,
    GridSquare? Landed,
    Creature? Target,
    AttackResult? Attack,
    int? DeviationDie,
    IReadOnlyList<ThrowEffect> Effects,
    IReadOnlyList<StrikeResult> Opportunities)
    : ActionResult(Action, Actor, Describe(Actor, Item, Aimed, Landed, Target, Attack, DeviationDie, Opportunities))
{
    /// <summary>Whether it left the hand at all.</summary>
    public bool Thrown => Attack is not null;

    public bool IsHit => Attack?.IsHit == true;

    private static string Describe(
        Creature actor,
        ItemDefinition item,
        GridSquare aimed,
        GridSquare? landed,
        Creature? target,
        AttackResult? attack,
        int? die,
        IReadOnlyList<StrikeResult> opportunities)
    {
        var provoked = opportunities.Count > 0 ? $", provoking {opportunities.Count}" : string.Empty;

        if (attack is null)
        {
            return $"{actor.Name} is cut down before throwing the {item.Name}{provoked}";
        }

        var text = $"{actor.Name} throws the {item.Name} at {target?.Name ?? aimed.ToString()}{provoked}: {attack}";

        if (die is { } rolled && landed is { } spot)
        {
            text += rolled == 1
                ? $"; it falls short (d8 [1]) and lands at {spot}"
                : $"; it goes wide (d8 [{rolled}]) and lands at {spot}";
        }

        return text;
    }
}

/// <summary>A try at pulling free of the goo, with the roll that decided it.</summary>
/// <param name="Roll">The Strength check or the Escape Artist check, as the log writes it.</param>
public sealed record BreakFreeResult(GameAction Action, Creature Actor, string Roll, bool Freed)
    : ActionResult(Action, Actor, Freed
        ? $"{Actor.Name} tears free of the goo"
        : $"{Actor.Name} strains against the goo and stays stuck fast");

/// <summary>A try at putting out the flames, with the save that decided it.</summary>
public sealed record PutOutFlamesResult(GameAction Action, Creature Actor, SavingThrowResult Save, bool PutOut)
    : ActionResult(Action, Actor, PutOut
        ? $"{Actor.Name} rolls on the ground and puts the flames out"
        : $"{Actor.Name} rolls on the ground but is still burning");
