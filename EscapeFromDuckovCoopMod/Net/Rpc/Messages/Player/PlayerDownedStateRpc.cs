using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_DOWNED_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerDownedStateRpc : IRpcMessage
{
    public string PlayerId;
    public bool IsDowned;
    public float BleedingHp;
    public const float MaxBleedingHp = 50f;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId);
        writer.Put(IsDowned);
        writer.Put(BleedingHp);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        IsDowned = reader.GetBool();
        BleedingHp = reader.GetFloat();
    }
}

[Rpc(Op.REVIVE_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ReviveRequestRpc : IRpcMessage
{
    public string DownedPlayerId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(DownedPlayerId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        DownedPlayerId = reader.GetString();
    }
}
