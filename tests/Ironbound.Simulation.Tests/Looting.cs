namespace Ironbound.Simulation.Tests;

/// <summary>Shorthands for the loot the tests keep asking about.</summary>
internal static class Looting
{
    /// <summary>Everything lying in any container the party can see, open or not.</summary>
    public static IEnumerable<BagEntry> Lying(this Campaign run) =>
        run.Containers.SelectMany(container => container.Contents);

    /// <summary>Empties every open container the party can see into the bag. Returns how many things came.</summary>
    public static int TakeEverything(this Campaign run)
    {
        var before = run.Bag.Count;
        foreach (var container in run.Containers.Where(container => container.IsOpen && !container.IsEmpty))
        {
            run.TakeAll(container.Id);
        }

        return run.Bag.Count - before;
    }

    /// <summary>The bag's line for an item, which the test expects to be there.</summary>
    public static BagEntry InBag(this Campaign run, string id) =>
        run.Bag.Find(id) ?? throw new Xunit.Sdk.XunitException($"There is no {id} in the bag: {run.Bag}.");
}
