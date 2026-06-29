import net from "net";

import { SendPacket }
    from "../lib/PacketSender";

import { ProtocolPersistence }
    from "../../Shared/protocol.persistence";

import { P2WConnectResponse }
    from "../../Shared/Packets/Response/P2WConnectResponse";

export function SendP2WConnectResponse(
    socket: net.Socket)
{
    const packet:
        P2WConnectResponse =
        {
            Message:
                "Connected to Persistence Server"
        };

    SendPacket(
        socket,
        ProtocolPersistence.P2WConnectResponse,
        packet);
}