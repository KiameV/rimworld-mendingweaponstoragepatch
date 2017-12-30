using Harmony;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;
using WeaponStorage;

namespace MendingWeaponStoragePatch
{
    [StaticConstructorOnStartup]
    class Main
    {
        static Main()
        {
            var harmony = HarmonyInstance.Create("com.mendingweaponstoragepatch.rimworld.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("MendingWeaponStoragePatch: Adding Harmony Postfix to WorkGiver_DoBill.TryFindBestBillIngredients");
            Log.Message("MendingWeaponStoragePatch: Adding Harmony Postfix to Game.Game_FinalizeInit");
        }

        private static LinkedList<Building_WeaponStorage> weaponStorage = null;
        private static bool initialized = false;
        public static IEnumerable<Building_WeaponStorage> GetWeaponStorage()
        {
            if (!initialized)
            {
                bool mendingFound = false;
                foreach (ModContentPack pack in LoadedModManager.RunningMods)
                {
                    foreach (Assembly assembly in pack.assemblies.loadedAssemblies)
                    {
                        if (!mendingFound &&
                            assembly.GetName().Name.Equals("Mending"))
                        {
                            mendingFound = true;
                        }
                        else if (
                            weaponStorage == null &&
                            assembly.GetName().Name.Equals("WeaponStorage"))
                        {
                            System.Type type = assembly.GetType("WeaponStorage.WorldComp");
                            PropertyInfo fi = type?.GetProperty("WeaponStoragesToUse", BindingFlags.Public | BindingFlags.Static);
                            weaponStorage = (LinkedList<Building_WeaponStorage>)fi?.GetValue(null, null);
                        }
                    }
                    if (mendingFound && weaponStorage != null)
                    {
                        break;
                    }
                }
                if (!mendingFound)
                {
                    Log.Error("Failed to initialize MendingWeaponStoragePatch, mending not found.");
                }
                else if (weaponStorage == null)
                {
                    Log.Error("Failed to initialize MendingWeaponStoragePatch, WeaponStorage could not be initialized.");
                }
                initialized = true;
            }
            return weaponStorage;
        }
    }

    [HarmonyPatch(typeof(Verse.Game), "FinalizeInit")]
    static class Patch_Game_FinalizeInit
    {
        static void Postfix()
        {
            Main.GetWeaponStorage();
        }
    }

    [HarmonyPatch(typeof(Mending.WorkGiver_DoBill), "TryFindBestBillIngredients")]
    static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
    {
        static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, bool ignoreHitPoints, ref Thing chosen)
        {
            if (__result == false)
            {
                IEnumerable<Building_WeaponStorage> storages = Main.GetWeaponStorage();
                if (storages == null)
                {
                    Log.Warning("MendingWeaponStoragePatch failed to retrieve WeaponStorage");
                    return;
                }

                foreach (Building_WeaponStorage storage in storages)
                {
                    if (storage.Spawned && storage.Map == pawn.Map)
                    {
                        foreach (ThingWithComps weapon in storage.StoredWeapons)
                        {
                            if ((ignoreHitPoints || weapon.HitPoints < weapon.MaxHitPoints && weapon.HitPoints > 0) &&
                                bill.recipe.fixedIngredientFilter.Allows(weapon) &&
                                bill.ingredientFilter.Allows(weapon) &&
                                bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(weapon)) &&
                                pawn.CanReserve(weapon, 1) &&
                                (!bill.CheckIngredientsIfSociallyProper || weapon.IsSociallyProper(pawn)))
                            {
                                if (!storage.Remove(weapon, false) || weapon.Spawned == false)
                                {
                                    Log.Error("Failed to spawn weapon-to-mend [" + weapon.Label + "] from storage [" + storage.Label + "].");
                                    __result = false;
                                    chosen = null;
                                }
                                else
                                {
                                    __result = true;
                                    chosen = weapon;
                                }
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}