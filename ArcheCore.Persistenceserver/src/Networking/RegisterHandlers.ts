import { RegisterHandler }
    from "./lib/PacketDispatcher";

import { ProtocolPersistence }
    from "../Shared/protocol.persistence";

import { W2PConnectHandler }
    from "./W2P/W2PConnectHandler";

import { W2PCharacterSaveHandler }
    from "./W2P/W2PCharacterSaveHandler";

import { W2PCharacterLoadHandler }
    from "./W2P/W2PCharacterLoadHandler";
import {W2PHelloWorldHandler} from "./W2P/W2PHelloWorldHandler";

export function RegisterHandlers()
{
    RegisterHandler(
        ProtocolPersistence.CharacterSave,
        W2PCharacterSaveHandler);

    RegisterHandler(
        ProtocolPersistence.CharacterLoad,
        W2PCharacterLoadHandler);
    RegisterHandler(
        ProtocolPersistence.W2PConnectRequest,
        W2PConnectHandler);
    RegisterHandler(
        ProtocolPersistence.HelloWorld,
        W2PHelloWorldHandler
    )
}