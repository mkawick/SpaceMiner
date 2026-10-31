using Unity.Collections;
using Unity.Entities;

// Ask for the local player to join the game in a ship of this type (index into the Game Library's
// ship types). Made by GameSession. In multiplayer this becomes the client's "go in game" request,
// handled on the server.
public struct JoinRequest : IComponentData
{
    public int shipType;
}

// A ship prefab the game can spawn, in the order listed on GameLibrary_Authoring.
public struct ShipPrefab : IBufferElementData
{
    public FixedString64Bytes name;
    public Entity prefab;
}

// On the ship the local player is flying. Later: the network id of the owning connection.
public struct PlayerShip : IComponentData
{
    public int playerId;
}
