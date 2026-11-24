using System.Diagnostics;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Random;
using GamesByMalcs.Melvor2.Saves;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A separate game built from the editor load path, used by one thread only. A copy that has fought can't take a
/// snapshot again, so each target that fights gets a fresh one. Every method enters the simulating scope, so the
/// guards act for its calls.
/// </summary>
internal sealed partial class Copy : IDisposable
{
    private Copy(MelvorGame game, double buildMs, CopyConditions conditions)
    {
        Game = game;
        BuildMs = buildMs;
        Conditions = conditions;
    }

    /// <summary>The conditions this copy's game made, for resets.</summary>
    public CopyConditions Conditions { get; }

    /// <summary>Collects conditions the game makes on this thread for the copy, until disposed.</summary>
    public CopyConditions.Collecting CollectConditions() => CopyConditions.Collect(Conditions);

    public MelvorGame Game { get; }

    public CombatEntity Combat { get; private set; } = null!;

    public BankEntity Bank => Game.Bank;

    public double BuildMs { get; }

    /// <summary>The copy's number among those built while its host was starting, for the start's log; else 0.</summary>
    public int StartNumber { get; private init; }

    public double ApplyMs { get; private set; }

    /// <summary>
    /// The skill the player's attack stance trains most after the setup is applied (full id, Hitpoints aside), or
    /// null if it can't be read.
    /// </summary>
    public string? MainSkill { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Each change as applied by <see cref="Prepare"/>; null when it had none.</summary>
    public List<ChangeOutcome>? ChangeOutcomes { get; private set; }

    /// <summary>Builds a new game with its data loaded and no save (≈0.75 s, ≈90 MB).</summary>
    public static Copy Build()
    {
        using var scope = Guards.Enter();
        var watch = Stopwatch.StartNew();
        var conditions = new CopyConditions();
        using var collect = CopyConditions.Collect(conditions);
        var load = StartStages.CopyBuilding();
        var game = new MelvorGame();
        EventHandler<GamesByMalcs.Melvor2.Core.LoadingProgressedEventArgs>? progressed =
            load == null ? null : (_, _) => load.Progressed();
        game.LoadingProgressed += progressed;
        try
        {
            game.LoadForEditor();
        }
        catch
        {
            game.Dispose();
            throw;
        }
        game.LoadingProgressed -= progressed;
        load?.Done();
        return new Copy(game, watch.Elapsed.TotalMilliseconds, conditions) { StartNumber = load?.Number ?? 0 };
    }

    /// <summary>Reads a snapshot into the copy, as the game reads its own saves (ends in OnSaveLoaded).</summary>
    public void Apply(Snapshot snapshot)
    {
        using var scope = Guards.Enter();
        var watch = Stopwatch.StartNew();
        Game._characterName = snapshot.Character; // what the save header sets before a real load
        using (var reader = new SaveReader(new MemoryStream(snapshot.Bytes, writable: false), Game.Data, Game.Entities))
            Game.ReadFrom(reader);
        Combat = (CombatEntity)Game.Entities.GetObjectById(LiveGame.CombatId);
        ApplyMs = watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Makes the copy the run's setup: the snapshot, then the loadout's swap through the game's own path when the
    /// setup names a slot, then the setup's changes, then time frozen. The RNG is reseeded per target when it fights.
    /// With <paramref name="throwOnRefusal"/> (run copies), a change the game refuses throws; otherwise (check copies)
    /// it is only reported in <see cref="ChangeOutcomes"/>.
    /// </summary>
    public void Prepare(
        Snapshot snapshot,
        string? loadoutSlot,
        ChangeList? changes = null,
        bool ignoreRequirements = false,
        bool throwOnRefusal = true
    )
    {
        using var collect = CollectConditions();
        Apply(snapshot);
        CapLevels();
        if (loadoutSlot != null)
            Swap(loadoutSlot);
        if (changes is { IsEmpty: false })
            ChangeOutcomes = Changes.Apply(this, changes, ignoreRequirements, throwOnRefusal);
        FreezeTime();
        Ready();
        StartStages.CopyPrepared(StartNumber);
    }

    /// <summary>
    /// Puts on a saved loadout in a prepared copy through the game's own swap, for a master's monster fought with a
    /// loadout of its own: what the loadout holds (gear, toolbelt, stance, prayers, spell, food) replaces the setup's,
    /// the rest of the setup stays. The copy's reset puts the setup back.
    /// </summary>
    public void WearLoadout(string loadoutSlot)
    {
        using var collect = CollectConditions();
        Swap(loadoutSlot);
        Ready();
    }

    /// <summary>The loadout slot the copy wears now.</summary>
    public string CurrentLoadout => Combat.Loadouts.CurrentLoadout.Data.Id;

    private void Swap(string loadoutSlot)
    {
        using var scope = Guards.Enter();
        var loadouts = Combat.Loadouts;
        var slot =
            loadouts.ActiveLoadouts.Keys.FirstOrDefault(s => s.Id == loadoutSlot)
            ?? throw new InvalidOperationException($"no loadout '{loadoutSlot}' in the copy");
        loadouts.SwapToLoadout(slot);
    }

    /// <summary>
    /// Gives a prepared copy <paramref name="changes"/> on top of what it was prepared with, and makes it ready to
    /// fight as <see cref="Prepare"/> leaves a copy. Refused changes are only reported in
    /// <see cref="ChangeOutcomes"/>. Undo it with a <see cref="CopyUndo"/> taken before. The changes keyed in
    /// <paramref name="ignoring"/> have their requirements ignored.
    /// </summary>
    public void Change(ChangeList changes, bool ignoreRequirements, IReadOnlySet<ChangeKey>? ignoring = null)
    {
        using var collect = CollectConditions();
        ChangeOutcomes = Changes.Apply(this, changes, ignoreRequirements, throwOnRefusal: false, ignoring);
        Ready();
    }

    // What every setup needs after its gear is on: consumables that last, the main skill read, and a settled game.
    private void Ready()
    {
        RaiseStacks();
        Changes.StockRunes(this);
        MainSkill = ReadMainSkill();
        using (Guards.Enter())
            Settle();
    }

    /// <summary>
    /// Brings every skill's level down to the build's level cap, as the game's own level update does. A save read
    /// back keeps the level it was written with, even above the cap, and the game only caps it at the next XP
    /// change. Without this a copy would fight its first trial above the cap and the rest at it, and a copy reset
    /// after a fight would start where its last fight left the stats, so results would depend on what the copy
    /// fought before.
    /// </summary>
    private void CapLevels()
    {
        using var scope = Guards.Enter();
        foreach (var leveled in Game.LeveledEntities)
            leveled.Experience.SetLevelCap(leveled.Experience.LevelCap);
    }

    /// <summary>The fewest of an equipped consumable a prepared copy holds.</summary>
    private const int StackFloor = 10_000;

    /// <summary>
    /// Raises every equipped stack the game uses up in combat (ammunition, lanterns and other stackable equipment, and
    /// toolbelt charges) to at least <see cref="StackFloor"/>. Fights top consumables back up after each tick, but the
    /// game unequips a stack the moment it reaches zero, before the top-up sees it: a small stack (one lantern, a few
    /// arrows) would be gone for the rest of the target. Nothing in combat reads the stack's size.
    /// </summary>
    private void RaiseStacks()
    {
        if (!Dependencies.IsAvailable(Dependencies.Adjust))
            return;
        using var scope = Guards.Enter();
        var equipment = Combat.Player.Equipment;
        foreach (var slot in equipment.Config.EquipmentSlots)
        {
            if (
                equipment.TryGetActiveSlot(slot, out var active)
                && active is ActiveEquipmentSlot { IsEmpty: false, IsOccupied: false } stack
                && stack.Equipped!.Equipment.CanEquipQuantity
                && stack.Quantity is > 0 and < StackFloor
            )
                stack.AddQuantity(StackFloor - stack.Quantity);
        }
        foreach (var stack in Combat.Toolbelt._activeSlots.Values)
        {
            if (stack.Equipped != null && stack.Quantity is > 0 and < StackFloor)
                stack.AddQuantity(StackFloor - stack.Quantity);
        }
    }

    private const string HitpointsId = "MelvorBase:Hitpoints";

    /// <summary>What the setup wears and how it fights, for the setup bar; empty parts if they can't be read.</summary>
    public SetupGear ReadGear()
    {
        if (!Dependencies.IsAvailable(Dependencies.Gear))
            return SetupGear.None;
        using var scope = Guards.Enter();
        return SetupGear.Read(Combat, null);
    }

    // The stance's experience gains: the one with the most XP per damage, Hitpoints aside.
    private string? ReadMainSkill()
    {
        if (!Dependencies.IsAvailable(Dependencies.MainSkill))
            return null;
        using var scope = Guards.Enter();
        try
        {
            return Combat
                .Player.Stats.AttackStance.Value.ExperienceGain.Where(g => g.Component.Entity.Id != HitpointsId)
                .OrderByDescending(g => g.ExperiencePerDamage)
                .Select(g => g.Component.Entity.Id)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException)
        {
            Log.WarnOnce("the copy's main skill couldn't be read", e);
            return null;
        }
    }

    /// <summary>
    /// Sets the copy's RNG from the run seed and the target's key, in place: every component caches
    /// the RNG object, so its state is restored rather than the object replaced.
    /// </summary>
    public void Reseed(ulong runSeed, string targetKey) =>
        Game.RNG.RestoreState(new Xoshiro256StarStarRandom(TargetSeeds.Of(runSeed, targetKey)).GetState());

    /// <summary>
    /// Fixes the time of day at noon and stops its timer: OnSaveLoaded set it from a setting or the clock.
    /// </summary>
    private void FreezeTime()
    {
        using var scope = Guards.Enter();
        var timeOfDay = Game.TimeAndWeather.TimeOfDay;
        timeOfDay.SyncWithSystemClock = false;
        timeOfDay.IncrementTimer.Pause();
        timeOfDay.SetCurrentTime(TimeSpan.FromHours(12));
    }

    /// <summary>
    /// Settles the copy through the game's own APIs: no action running, combat stopped, no combat effects, no area
    /// selected and a self-sorting bank sorted. A prepared copy starts every fight this way, and a reset leaves it so.
    /// </summary>
    private void Settle()
    {
        Game.ActionManager.TryStopActive(force: true); // the player's own action (thieving, say) adds modifiers
        Combat.Action.TryStop();
        Combat.CombatEffects.ClearEffects();
        Combat.Areas.UnselectArea();
        // A bank that sorts itself does so at the first loot: start sorted, so a fight leaves the order as it was.
        if (Bank.Inventory.AutoSort)
            Bank.Inventory.SortItemsInAllTabs();
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        using var scope = Guards.Enter();
        Game.Dispose();
    }
}
