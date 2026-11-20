using System.Collections.Frozen;

namespace Ares.Editor.Core.GameData;

/// <summary>
/// Readable names for the game's data-layer instance names (<c>DataLayer_</c> plus 32 hex digits), which the collision file
/// and the loot files use. The game data holds no link from an instance name to its data-layer asset, so the Dam's layers
/// are listed here, from the map's <c>WorldDataLayers</c> actor (game build 26439197); other names stay as they are.
/// </summary>
public static class DataLayerNames
{
    // Instance name -> readable name; the asset each one stands for is in the comment.
    private static readonly FrozenDictionary<string, string> s_labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DataLayer_FA3D89B449BBEB0AB9D5C5A8EB600487"] = "Locked doors: open", // RoundConfiguration_LockedDoors_Open
        ["DataLayer_5137D9F643D5555ADA866486A77BF077"] = "Locked doors: standard", // RoundConfiguration_LockedDoors_Standard
        ["DataLayer_6E02D05F44ACC9D32E71D09CCBA70D38"] = "Toxic swamp", // RoundConfiguration_ToxicSwamp
        ["DataLayer_62F93B724A8CCAA8F2498883691DD74C"] = "Toxic swamp: standard", // RoundConfiguration_ToxicSwamp_Standard
        ["DataLayer_EA86CCFC497AF4DCF6416E9C424A5E78"] = "Toxic swamp: air purifier", // RoundConfiguration_ToxicSwamp_AirPurifier
        ["DataLayer_A6C81DA64CD900E894AB9A954ECE501E"] = "Condition: Harvester", // RoundConfiguration_Harvester
        ["DataLayer_9898BDB44BC983B7FF05A29308C84D0A"] = "Condition: Electromagnetic storm", // RoundConfiguration_ElectromagneticStorm
        ["DataLayer_921215FA481904AE4423DA9210CABC3D"] = "Condition: Turbulence", // RoundConfiguration_Turbulence
        ["DataLayer_3E5D143841DB59A6407488B4ED6D9ACD"] = "Condition: Extractor", // RoundConfiguration_Extractor
        ["DataLayer_3D6B641041B18DF5CAFA5CBE822CC44A"] = "Condition: Cold snap", // RoundConfiguration_Coldsnap
        ["DataLayer_3C3EE7E64D63869AA76648BDD7C85FBB"] = "Condition: Night raid", // RoundConfiguration_NightRaid
        ["DataLayer_302DC7E64361781C2D1C2BB18B5F4785"] = "Condition: Frigate", // RoundConfiguration_Frigate
        ["DataLayer_6FA0DAED4834136F9FD6009206D7376E"] = "Lighting: Default", // TheDam_01_Lighting_DefaultL
        ["DataLayer_F76AF601465EAE9DF47AD999FEE36240"] = "Lighting: Thunderstorm", // TheDam_01_Lighting_Thunderstorm
        ["DataLayer_E73D033B4DA6A55A73CD8E92DD235AF7"] = "Lighting: Rainstorm", // TheDam_01_Lighting_Rainstorm
        ["DataLayer_CE4553134CE8DB192BFFA0A525909E9E"] = "Lighting: Forest fire", // TheDam_01_Lighting_ForestFire
        ["DataLayer_C48C0CBE4C6D88F2E505F3A172B056B3"] = "Lighting: Frigate overcast", // TheDam_02_Lighting_Frigate_Overcast
        ["DataLayer_B2C4865D406F6C778C38AA8A8BBC3F51"] = "Lighting: Hazy day", // TheDam_01_Lighting_HazyDay
        ["DataLayer_A48B2B494FFE7484C2F0E5B6EFF4B656"] = "Lighting: Winter snow", // TheDam_01_Lighting_WinterSnowDam
        ["DataLayer_9F7DA4C64C3079CAD8B2F39EC4901AB3"] = "Lighting: Pink sunrise", // TheDam_01_Lighting_PinkSunrise
        ["DataLayer_7EBAC4774598B90B562D4E881DFCB4FB"] = "Lighting: Windy storm", // TheDam_01_Lighting_WindyStorm
        ["DataLayer_7565040548B66AF257C03ABCED75785B"] = "Lighting: Grey morning", // TheDam_01_Lighting_GreyMorning
        ["DataLayer_58CEBF0045446258FBD4CCAC72405CA7"] = "Lighting: Foggy", // TheDam_01_Lighting_Foggy
        ["DataLayer_52C006434D304F6D91245392A866367F"] = "Lighting: Night", // TheDam_01_Lighting_Night
        ["DataLayer_8F6F03E14CD5062ED5B8CB9C87BCE96E"] = "Salvage", // Gameplay_Salvage
        ["DataLayer_37D0D12649E565F171954BADF3819818"] = "Salvage navmesh", // Navmesh_Salvage
        ["DataLayer_8D013B6346A2B53C40A1CABB0E580BB5"] = "Smart objects", // SmartObjects_Default
        ["DataLayer_6AE80371423C97978420B0B058A75D1A"] = "Birds and tumbleweeds", // BirdsAndTumbleweeds
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The readable name of a data layer, or <paramref name="name"/> itself when it is not a known layer.</summary>
    /// <param name="name">The data-layer instance name.</param>
    public static string Label(string name) => s_labels.GetValueOrDefault(name ?? throw new ArgumentNullException(nameof(name)), name);
}
