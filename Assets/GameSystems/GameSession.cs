using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

// Runs the start-up of a play session, one stage at a time:
//
//   Boot → (Login → Connect) → LoadWorld → Join → Playing
//
// Login and Connect are placeholders until there is a server. Lives on a GameObject at the root
// of the game scene (not in a subscene). Systems do the world work: AsteroidFieldSpawnSystem
// builds the field, PlayerSpawnSystem answers the JoinRequest made here.
public class GameSession : MonoBehaviour
{
    public enum Stage { Boot, Login, Connect, LoadWorld, Join, Playing, Failed }

    [Tooltip("Which entry in the Game Library's Ship Types list the player starts in")]
    public int startingShipType = 0;
    [Tooltip("Seconds to wait for the player's ship before giving up with an error")]
    public float joinTimeout = 10f;
    [Tooltip("The player's colour (later chosen on the join screen). Tints the TeamColour parts of units and buildings, and the home hex on the grid")]
    public Color playerColour = Color.white;
    [Tooltip("Empty = Assets/Models/Grix/Materials/TeamColour.mat. Tinted for the session, restored when play stops. Single-player shortcut: every TeamColour part gets this colour")]
    public Material teamColourMaterial;

    public static Color PlayerColour => Instance != null ? Instance.playerColour : Color.white;

    public static GameSession Instance { get; private set; }
    public Stage CurrentStage { get; private set; } = Stage.Boot;
    public string Status { get; private set; } = "Starting";

    private float stageStarted;
    private Color savedTeamColour;
    private bool tinted;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private EntityQuery playerQuery;
    private EntityQuery fieldReadyQuery;
    private World queryWorld;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        TintTeamColour();
    }

    private void OnDestroy()
    {
        RestoreTeamColour();
        if (Instance == this)
            Instance = null;
    }

    // Single player: the shared TeamColour material takes the player's colour for this session.
    // (Multiplayer will need a per-unit colour instead.)
    private void TintTeamColour()
    {
#if UNITY_EDITOR
        if (teamColourMaterial == null)
            teamColourMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Grix/Materials/TeamColour.mat");
#endif
        if (teamColourMaterial == null || !teamColourMaterial.HasProperty(BaseColorId))
            return;
        savedTeamColour = teamColourMaterial.GetColor(BaseColorId);
        teamColourMaterial.SetColor(BaseColorId, playerColour);
        if (teamColourMaterial.HasProperty(ColorId))
            teamColourMaterial.SetColor(ColorId, playerColour);
        tinted = true;
    }

    private void RestoreTeamColour()
    {
        if (!tinted || teamColourMaterial == null)
            return;
        teamColourMaterial.SetColor(BaseColorId, savedTeamColour);
        if (teamColourMaterial.HasProperty(ColorId))
            teamColourMaterial.SetColor(ColorId, savedTeamColour);
        tinted = false;
    }

    private void Update()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;
        if (queryWorld != world)
        {
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<InputData_Dots>());
            fieldReadyQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<AsteroidFieldReady>());
            queryWorld = world;
        }

        switch (CurrentStage)
        {
            case Stage.Boot:
                GoTo(Stage.Login, "Logging in");
                break;

            case Stage.Login:
                // Later: authenticate with the account service.
                GoTo(Stage.Connect, "Connecting");
                break;

            case Stage.Connect:
                // Later: pick a server instance and connect (Netcode for Entities).
                GoTo(Stage.LoadWorld, "Loading the Keldar");
                break;

            case Stage.LoadWorld:
                if (AllSubScenesLoaded(world) && !fieldReadyQuery.IsEmpty)
                    GoTo(Stage.Join, "Launching");
                break;

            case Stage.Join:
                if (!playerQuery.IsEmpty)
                {
                    GoTo(Stage.Playing, "");
                }
                else if (Time.time - stageStarted > joinTimeout)
                {
                    Debug.LogError("GameSession: the player's ship never arrived. Is a ship prefab listed in GameLibrary_Authoring's Ship Types?");
                    GoTo(Stage.Failed, "Couldn't launch a ship");
                }
                break;
        }
    }

    private void GoTo(Stage stage, string status)
    {
        CurrentStage = stage;
        Status = status;
        stageStarted = Time.time;

        if (stage == Stage.Join && playerQuery.IsEmpty)
        {
            EntityManager em = queryWorld.EntityManager;
            Entity request = em.CreateEntity();
            em.AddComponentData(request, new JoinRequest { shipType = startingShipType });
        }
    }

    private static bool AllSubScenesLoaded(World world)
    {
        foreach (SubScene subScene in SubScene.AllSubScenes)
        {
            if (!subScene.AutoLoadScene)
                continue;
            Entity sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, subScene.SceneGUID);
            if (sceneEntity == Entity.Null || !SceneSystem.IsSceneLoaded(world.Unmanaged, sceneEntity))
                return false;
        }
        return true;
    }
}
