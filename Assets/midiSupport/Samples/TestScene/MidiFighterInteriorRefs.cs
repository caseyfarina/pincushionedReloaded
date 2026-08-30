using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Holds references to all Basic Asset Pack Interior prefabs.
    /// Lives in Resources so it loads in builds.
    /// Auto-populated by MidiFighterInteriorRefsInit.
    /// </summary>
    public class MidiFighterInteriorRefs : ScriptableObject
    {
        public GameObject[] prefabs;

        public const string ResourceName = "MidiFighterInteriorRefs";

        internal static readonly string[] Paths =
        {
            // Floor
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/Floor1m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/Floor2m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/Floor2x4m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/Floor3m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/Floor4m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/RugDoorMat.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/RugRectangleLong.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/RugRectangleMedium.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Floor/RugRectangleSmall.prefab",
            // Furniture
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/BedDouble.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/BenchDinning.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/ChairDinningA.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/ChairDinningB.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/ShelfWallSmall.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/ShelvesMediumA.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/ShelvesTallA.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/SofaArmChair.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/SofaDouble.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/SofaFootRest.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/TableNarrowDoubleDraw.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/TableNarrowSingleDraw.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/TableRectangleMedium.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/TableRectangleShort.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Furniture/TableSquareMedium.prefab",
            // Lights
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Lights/LampSmall.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Lights/LampTall.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Lights/LightCeliing.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Lights/LightCeliingDome.prefab",
            // Props
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/Books.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/Mug.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/PlantPotMedium.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/PlantPotRoundMedium.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/Switch.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/VaseRoundBottom.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/VaseSmall.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/VaseTall.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Props/VaseTallBottle.prefab",
            // Walls
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/Wall1m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/Wall2m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/Wall3m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/Wall4m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallCornerInner.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallCornerOuter.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallDoor2m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallDoorFrame2m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallWindow2m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallWindow4m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallWindowBay4m.prefab",
            "Assets/UnityTechnologies/Basic Asset Pack Interior/Prefabs/Walls/WallWindowTall4m.prefab",
        };
    }
}
