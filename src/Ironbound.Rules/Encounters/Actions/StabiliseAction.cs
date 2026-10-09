using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Kneeling beside somebody and keeping them in the world.
/// </summary>
/// <remarks>
/// A standard action and a Heal check, and it is the cheapest healing in the game: it costs no
/// spell, no slot and no charge, and it turns "dead in four rounds" into "alive but out of the
/// fight". A party with nobody trained in Heal discovers the difference the first time somebody
/// goes down with the cleric out of range.
/// </remarks>
public sealed class StabiliseAction : GameAction
{
    /// <summary>Fixed, unlike the dying creature's own check, which gets worse as they bleed.</summary>
    public const int Difficulty = 15;

    public StabiliseAction(Creature patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        Patient = patient;
    }

    public Creature Patient { get; }

    public override string Name => "stabilise";

    public override ActionCost Cost => ActionCost.Standard;

    public override bool CanPerform(ActionContext context) =>
        !ReferenceEquals(Patient, context.Actor)
        && Patient.HitPoints.State == HitPointState.Dying
        && !Bleeding.IsStable(Patient)
        && (context.Encounter.Battlefield is not { } field
            || field.IsWithinTouch(context.Actor, Patient));

    public override ActionResult Perform(ActionContext context)
    {
        var check = context.Actor.Skills.Check(Skill.Heal, context.Random, Difficulty);

        if (check.Succeeded == true && Bleeding.Find(Patient) is { } bleeding)
        {
            bleeding.Stabilise();
        }

        return new StabiliseResult(this, context.Actor, Patient, check);
    }
}

public sealed record StabiliseResult(
    GameAction Action,
    Creature Actor,
    Creature Patient,
    SkillCheck Check)
    : ActionResult(Action, Actor, Describe(Actor, Patient, Check))
{
    public bool Stabilised => Check.Succeeded == true;

    private static string Describe(Creature actor, Creature patient, SkillCheck check)
    {
        if (check.Untrained)
        {
            return $"{actor.Name} does not know how to help {patient.Name}";
        }

        return check.Succeeded == true
            ? $"{check} — {patient.Name} stops bleeding"
            : $"{check} — {patient.Name} is still bleeding";
    }
}
